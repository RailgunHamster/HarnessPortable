using System.Text.Json;
using System.Text.Json.Nodes;

namespace HarnessPortable.Windows.Automation;

public sealed record UiActionContract(string Name, string Description, string[] Required, string[] Optional)
{
    public static IReadOnlyList<UiActionContract> All { get; } = [
        new("invoke","Invoke the real control automation provider, including its command.",[],[]),
        new("focus","Focus a focusable control.",[],[]),
        new("setValue","Set editable text/password or a numeric range value; text bindings commit by default.",["value"],["commit"]),
        new("setChecked","Set checked state without breaking its binding; null is supported for three-state controls.",["value"],[]),
        new("setExpanded","Set menu, combo box or expander expansion state.",["value"],[]),
        new("selectText","Set text selection by start and length.",["start","length"],[]),
        new("replaceSelection","Replace selected text through the native editing/undo path.",["value"],["commit"]),
        new("edit","Execute copy/cut/paste/undo/redo/selectAll/delete through the real routed command.",["value"],["commit"]),
        new("items","List selector items and selected index.",[],[]),
        new("select","Select an item by its current index.",["index"],[]),
        new("selectItem","Select/add/remove an item using its actual selection provider.",[],["mode"]),
        new("realize","Bring a virtualized item into view or open a selector popup.",["index"],[]),
        new("scroll","Set scroll offsets.",[],["vertical","horizontal"]),
        new("contextMenu","Open the control's actual context menu.",[],[]),
        new("dismiss","Dismiss a context menu.",[],[]),
        new("window","Show/hide/activate/close/minimize/maximize/restore/resize a window.",["value"],["widthPixels","heightPixels"]),
        new("key","Route an application key through the control's WPF input handlers.",["value"],[])
    ];
    public static JsonNode Describe() => JsonSerializer.SerializeToNode(All,AutomationApi.JsonOptions)!;
    public static void Validate(string action,JsonObject args)
    {
        var contract=All.SingleOrDefault(c=>c.Name==action) ?? throw new AutomationFault("unsupported","UI action is not registered.");
        if(contract.Required.Any(p=>!args.ContainsKey(p))) throw new AutomationFault("invalid_arguments","Missing an action parameter. Inspect ui.actions.");
        if(args.Any(p=>p.Key is not ("target" or "action") && !contract.Required.Contains(p.Key) && !contract.Optional.Contains(p.Key)))
            throw new AutomationFault("invalid_arguments","Parameter does not belong to this UI action. Inspect ui.actions.");
    }
}
