using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AvalonDock.Controls;
using AvalonDock.Layout;
using HarnessPortable.Windows.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using ListBox = System.Windows.Controls.ListBox;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using RadioButton = System.Windows.Controls.RadioButton;

namespace HarnessPortable.Windows.Automation;

/// <summary>Semantic operations on this application's own controls; no screen coordinates or private-handler reflection.</summary>
public sealed class WpfAutomationTree(Func<IEnumerable<Window>> windows)
{
    private sealed record Identity(string Value);
    private readonly ConditionalWeakTable<DependencyObject, Identity> _identities = new();
    private readonly Dictionary<string, WeakReference<FrameworkElement>> _targets = [];
    private long _nextIdentity;
    private string IdentityOf(DependencyObject value) => _identities.GetValue(value, _ => new("ui-" + ++_nextIdentity)).Value;

    public JsonArray Snapshot(bool includeHidden = false)
    {
        _targets.Clear();
        var result = new JsonArray();
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        foreach (var window in windows().ToArray()) Walk(window, null, window, includeHidden, visited, result);
        // ContextMenu and ComboBox popups have their own WPF presentation source.
        // CurrentSources contains only this process's managed presentation sources.
        foreach (PresentationSource source in PresentationSource.CurrentSources)
            if (source.CheckAccess() && source.RootVisual is FrameworkElement root && root.IsVisible) Walk(root, null, null, includeHidden, visited, result);
        return result;
    }

    private void Walk(DependencyObject node, string? parent, Window? window, bool hidden,
        HashSet<DependencyObject> visited, JsonArray output)
    {
        if (!visited.Add(node)) return;
        if (node is FrameworkElement element)
        {
            if (!hidden && !element.IsVisible) return;
            var id = IdentityOf(element);
            _targets[id] = new(element);
            var actions = Actions(element);
            var sensitive = Sensitive(element);
            var properties = new JsonObject();
            switch (element)
            {
                case PasswordBox password: properties["hasValue"] = password.Password.Length > 0; break;
                case TextBox text: properties["value"] = sensitive ? null : text.Text; properties["hasValue"] = text.Text.Length > 0;
                    properties["readOnly"] = text.IsReadOnly; properties["selectionStart"] = text.SelectionStart; properties["selectionLength"] = text.SelectionLength; break;
                case TextBlock text: properties["text"] = sensitive ? null : text.Text; break;
                case ToggleButton toggle: properties["checked"] = toggle.IsChecked; properties["threeState"]=toggle.IsThreeState; break;
                case Selector selector:
                    properties["selectedIndex"] = selector.SelectedIndex; properties["itemCount"] = selector.Items.Count;
                    if(selector is ComboBox {IsEditable:true} editable)properties["value"]=sensitive ? null : editable.Text;
                    break;
                case Expander expander: properties["expanded"] = expander.IsExpanded; break;
                case RangeBase range: properties["value"] = range.Value; properties["minimum"] = range.Minimum; properties["maximum"] = range.Maximum; break;
                case ScrollViewer scroll: properties["verticalOffset"] = scroll.VerticalOffset; properties["horizontalOffset"] = scroll.HorizontalOffset; break;
            }
            if (element is Window w) { properties["state"] = w.WindowState.ToString().ToLowerInvariant(); properties["active"] = w.IsActive; properties["title"] = w.Title; properties["width"]=w.ActualWidth; properties["height"]=w.ActualHeight; }
            if(Pattern<ISelectionItemProvider>(element,PatternInterface.SelectionItem) is { } selection)properties["selected"]=selection.IsSelected;
            output.Add(new JsonObject
            {
                ["id"] = id, ["parentId"] = parent, ["windowId"] = window is null ? null : IdentityOf(window),
                ["automationId"] = AutomationProperties.GetAutomationId(element), ["elementName"] = element.Name,
                ["type"] = element.GetType().Name, ["name"] = Label(element), ["dataKey"] = DataKey(element),
                ["tabId"] = TabKey(element),
                ["visible"] = element.IsVisible, ["enabled"] = element.IsEnabled, ["sensitive"] = sensitive,
                ["actions"] = new JsonArray(actions.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["properties"] = properties
            });
            parent = id;
            // The browser's native accessibility tree belongs to page automation, not the WPF tree.
            if (element is Microsoft.Web.WebView2.Wpf.WebView2) return;
        }
        if (node is Visual or System.Windows.Media.Media3D.Visual3D)
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                Walk(VisualTreeHelper.GetChild(node, index), parent, window, hidden, visited, output);
    }

    private static string? DataKey(FrameworkElement element) => (element.Tag ?? element.DataContext) switch
    {
        ConnectionRow row => AutomationKeys.Connection(row.Entry),
        LayoutDocument document => document.ContentId,
        CollectionViewGroup group => group.Name?.ToString(),
        _ => null
    };
    private static string? TabKey(FrameworkElement element)
    {
        var tab = Ancestor<LayoutDocumentTabItem>(element)?.Model?.ContentId ?? Ancestor<SessionView>(element)?.AutomationTabId
            ?? Ancestor<PaneActions>(element)?.Session?.AutomationTabId;
        if(tab is not null)return tab;
        var menu=Ancestor<ContextMenu>(element);
        return menu?.PlacementTarget is FrameworkElement placement && !ReferenceEquals(placement,element) ? TabKey(placement) : null;
    }
    private static string Label(FrameworkElement element)
    {
        var explicitName = AutomationProperties.GetName(element);
        if (!string.IsNullOrEmpty(explicitName)) return explicitName;
        return element switch
        {
            Window window => window.Title,
            MenuItem menu => menu.Header as string ?? "",
            HeaderedContentControl header => header.Header as string ?? "",
            ContentControl control => control.Content as string ?? "",
            _ => ""
        };
    }
    private static bool Sensitive(FrameworkElement element) => element is PasswordBox || AutomationMetadata.GetSensitive(element);
    private static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (DependencyObject? node = element; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : null)
            if (node is T match) return match;
        return null;
    }

    private static AutomationPeer? Peer(FrameworkElement element) => UIElementAutomationPeer.CreatePeerForElement(element);
    private static T? Pattern<T>(FrameworkElement element, PatternInterface pattern) where T : class => Peer(element)?.GetPattern(pattern) as T;
    private static ContextMenu? Menu(FrameworkElement element) => element.ContextMenu ?? (element as DropDownControlArea)?.DropDownContextMenu;
    public static IReadOnlyList<string> Actions(FrameworkElement element)
    {
        var result = new List<string>();
        if (element.Focusable) result.Add("focus");
        if (Pattern<IInvokeProvider>(element, PatternInterface.Invoke) is not null) result.Add("invoke");
        if (Pattern<ISelectionItemProvider>(element,PatternInterface.SelectionItem) is not null)result.Add("selectItem");
        if (element is TextBox or PasswordBox or ComboBox {IsEditable:true}) result.Add("setValue");
        if (element is TextBox) {result.Add("selectText");result.Add("replaceSelection");}
        if (element is TextBox or PasswordBox)result.Add("edit");
        if (element is ToggleButton) result.Add("setChecked");
        if (element is Selector) { result.Add("items"); result.Add("select"); result.Add("realize"); }
        if (element is Expander || Pattern<IExpandCollapseProvider>(element, PatternInterface.ExpandCollapse) is not null) result.Add("setExpanded");
        if (element is Slider or System.Windows.Controls.Primitives.ScrollBar) result.Add("setValue");
        if (element is ScrollViewer) result.Add("scroll");
        if (Menu(element) is not null) result.Add("contextMenu");
        if (element is ContextMenu) result.Add("dismiss");
        if (element is Window) { result.Add("window"); result.Add("key"); }
        else if (element is System.Windows.Controls.Control {Focusable:true} or SessionView || AutomationMetadata.GetKeyTarget(element)) result.Add("key");
        return result;
    }

    public FrameworkElement Resolve(JsonObject selector)
    {
        var snapshot=Snapshot(includeHidden: true);
        FrameworkElement FindId(string id)=>_targets.TryGetValue(id,out var reference)&&reference.TryGetTarget(out var target)
            ? target : throw new AutomationFault("not_found","Target was removed. Query the UI tree again.");
        if (selector["id"] is not null)
        {
            if (selector.Count != 1) throw new AutomationFault("invalid_arguments", "An id selector cannot contain additional fields.");
            return FindId(selector.String("id"));
        }
        var criteria = new[] { "automationId", "elementName", "type", "name", "dataKey", "tabId", "windowId" };
        if (!selector.Any() || selector.Any(p => !criteria.Contains(p.Key))) throw new AutomationFault("invalid_arguments", "Use an id or documented selector fields.");
        var nodes = snapshot.OfType<JsonObject>().Where(node => selector.All(p => JsonNode.DeepEquals(node[p.Key], p.Value))).ToArray();
        if (nodes.Length == 0) throw new AutomationFault("not_found", "No control matches the selector.");
        if (nodes.Length != 1) throw new AutomationFault("ambiguous", "Multiple controls match. Use a concrete id from ui.tree.");
        return FindId(nodes[0]["id"]!.GetValue<string>());
    }

    public JsonNode? Act(JsonObject args)
    {
        var selector = args["target"] as JsonObject ?? throw new AutomationFault("invalid_arguments", "Target selector is required.");
        var target = Resolve(selector);
        var action = args.String("action");
        UiActionContract.Validate(action,args);
        if (!Actions(target).Contains(action)) throw new AutomationFault("unsupported", "Control does not expose that action.");
        if (action is not ("items" or "window") && (!target.IsVisible || !target.IsEnabled)) throw new AutomationFault("disabled", "Control is hidden or disabled.");
        switch (action)
        {
            case "invoke": Pattern<IInvokeProvider>(target, PatternInterface.Invoke)!.Invoke(); break;
            case "focus": if (!target.Focus()) throw new AutomationFault("unavailable", "Control cannot receive focus."); break;
            case "setValue" when target is PasswordBox password: password.Password = args.String("value"); break;
            case "setValue" when target is TextBox text:
                if (text.IsReadOnly) throw new AutomationFault("disabled", "Text field is read-only.");
                text.SetCurrentValue(TextBox.TextProperty,args.String("value"));
                if(args.Boolean("commit",true))text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                break;
            case "setValue" when target is ComboBox editable:
                if(editable.IsReadOnly)throw new AutomationFault("disabled","Selector text is read-only.");
                editable.SetCurrentValue(ComboBox.TextProperty,args.String("value"));
                if(args.Boolean("commit",true))editable.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                break;
            case "setValue" when target is RangeBase range:
                var value = args.Number("value");
                if (!double.IsFinite(value) || value < range.Minimum || value > range.Maximum) throw new AutomationFault("invalid_arguments", "Value is outside the control range.");
                range.SetCurrentValue(RangeBase.ValueProperty,value); break;
            case "selectText":
                var input = (TextBox)target; var start = args.Integer("start", -1); var length = args.Integer("length", -1);
                if (start < 0 || length < 0 || start > input.Text.Length || length > input.Text.Length - start) throw new AutomationFault("invalid_arguments", "Invalid text selection range.");
                input.Select(start, length); break;
            case "replaceSelection":
                var editor=(TextBox)target;
                if(editor.IsReadOnly)throw new AutomationFault("disabled","Text field is read-only.");
                editor.SelectedText=args.String("value");
                if(args.Boolean("commit",true))editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                break;
            case "edit":
                var command=args.String("value") switch
                {
                    "copy"=>ApplicationCommands.Copy,"cut"=>ApplicationCommands.Cut,"paste"=>ApplicationCommands.Paste,
                    "undo"=>ApplicationCommands.Undo,"redo"=>ApplicationCommands.Redo,"selectAll"=>ApplicationCommands.SelectAll,"delete"=>ApplicationCommands.Delete,
                    _=>throw new AutomationFault("invalid_arguments","Unknown editing command.")
                };
                if(!command.CanExecute(null,target))throw new AutomationFault("disabled","Editing command is not available in the current state.");
                command.Execute(null,target);
                if(target is TextBox edited && args.Boolean("commit",true))edited.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                break;
            case "setChecked":
                var toggle=(ToggleButton)target;
                if(args["value"] is null)
                {
                    if(!toggle.IsThreeState||toggle is RadioButton)throw new AutomationFault("invalid_arguments","This control does not support an indeterminate state.");
                    toggle.SetCurrentValue(ToggleButton.IsCheckedProperty,null);
                }
                else toggle.SetCurrentValue(ToggleButton.IsCheckedProperty,args.Boolean("value"));
                break;
            case "setExpanded":
                if (args["value"] is null) throw new AutomationFault("invalid_arguments", "Boolean value is required.");
                if (target is Expander expander) expander.SetCurrentValue(Expander.IsExpandedProperty, args.Boolean("value"));
                else if (args.Boolean("value")) Pattern<IExpandCollapseProvider>(target, PatternInterface.ExpandCollapse)!.Expand();
                else Pattern<IExpandCollapseProvider>(target, PatternInterface.ExpandCollapse)!.Collapse(); break;
            case "items":
                var items = (Selector)target;
                return new JsonArray(items.Items.Cast<object>().Select((item, index) => (JsonNode?)new JsonObject
                {
                    ["index"] = index, ["name"] = item is ComboBoxItem combo ? combo.Content?.ToString() : item is string name ? name : null,
                    ["tag"] = (item as FrameworkElement)?.Tag as string, ["selected"] = index == items.SelectedIndex
                }).ToArray());
            case "select":
            case "realize":
                var selectorControl = (Selector)target; var selected = args.Integer("index", -1);
                if (selected < 0 || selected >= selectorControl.Items.Count) throw new AutomationFault("invalid_arguments", "Item index is out of range.");
                if (action == "select") selectorControl.SetCurrentValue(Selector.SelectedIndexProperty, selected);
                if (target is ListBox list) list.ScrollIntoView(list.Items[selected]);
                else if (target is ComboBox combo) combo.IsDropDownOpen = true;
                target.UpdateLayout(); break;
            case "scroll":
                var viewer = (ScrollViewer)target;
                if (args["vertical"] is not null) viewer.ScrollToVerticalOffset(args.Number("vertical"));
                if (args["horizontal"] is not null) viewer.ScrollToHorizontalOffset(args.Number("horizontal")); break;
            case "selectItem":
                var selectionItem=Pattern<ISelectionItemProvider>(target,PatternInterface.SelectionItem)!;
                switch(args.OptionalString("mode")??"replace")
                {
                    case "replace":selectionItem.Select();break;
                    case "add":selectionItem.AddToSelection();break;
                    case "remove":selectionItem.RemoveFromSelection();break;
                    default:throw new AutomationFault("invalid_arguments","mode must be replace, add or remove.");
                }
                break;
            case "contextMenu":
                var contextMenu=Menu(target)!;
                contextMenu.PlacementTarget=target;
                if(target is DropDownControlArea area)contextMenu.DataContext=area.DropDownContextMenuDataContext;
                contextMenu.IsOpen=true;
                break;
            case "dismiss": ((ContextMenu)target).IsOpen = false; break;
            case "window":
                var window = (Window)target;
                switch (args.String("value"))
                {
                    case "close": window.Close(); break;
                    case "show": window.Show(); break;
                    case "hide": window.Hide(); break;
                    case "activate": window.Activate(); break;
                    case "normal": window.WindowState = WindowState.Normal; break;
                    case "maximized": window.WindowState = WindowState.Maximized; break;
                    case "minimized": window.WindowState = WindowState.Minimized; break;
                    case "resize":
                        if(args["widthPixels"] is null && args["heightPixels"] is null)throw new AutomationFault("invalid_arguments","Provide widthPixels or heightPixels.");
                        var width=args["widthPixels"] is null ? window.ActualWidth : args.Number("widthPixels");
                        var height=args["heightPixels"] is null ? window.ActualHeight : args.Number("heightPixels");
                        if(!double.IsFinite(width)||!double.IsFinite(height)||width<window.MinWidth||height<window.MinHeight||width>window.MaxWidth||height>window.MaxHeight)
                            throw new AutomationFault("invalid_arguments","Window size is outside its allowed range.");
                        window.SetCurrentValue(Window.WidthProperty,width);window.SetCurrentValue(Window.HeightProperty,height);break;
                    default: throw new AutomationFault("invalid_arguments", "Unknown window operation.");
                }
                break;
            case "key":
                if (!Enum.TryParse<Key>(args.String("value"), true, out var key) || key is Key.None or Key.System)
                    throw new AutomationFault("invalid_arguments", "Unknown WPF key.");
                var source = PresentationSource.FromVisual(target) ?? throw new AutomationFault("unavailable", "Target has no presentation source.");
                var inputEvent = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                target.RaiseEvent(inputEvent);
                if (!inputEvent.Handled) { inputEvent.RoutedEvent = Keyboard.KeyDownEvent; target.RaiseEvent(inputEvent); }
                break;
        }
        return new JsonObject { ["dispatched"] = true };
    }
}
