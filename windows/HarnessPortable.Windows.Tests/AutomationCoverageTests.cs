using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HarnessPortable.Windows.Tests;

public sealed class AutomationCoverageTests
{
    private static readonly HashSet<string> Interactive = ["Window","Button","ToggleButton","RadioButton","CheckBox","TextBox","PasswordBox","ComboBox","ListBox","ListView","ListBoxItem","ListViewItem","ComboBoxItem","TabItem","TabControl","Expander","Slider","ScrollViewer","GridSplitter","MenuItem","RepeatButton","ContextMenu"];
    private static readonly HashSet<string> ObservationalOrContainer = ["Separator","ContentControl","ProgressBar","UserControl","ItemsControl","GroupBox","Label","ToolTip","Menu"];
    private static string Root => Path.Combine(AppContext.BaseDirectory,"AutomationSources");

    [Fact]
    public void EveryDeclaredInteractiveControlHasAnExplicitStableApiId()
    {
        var files=Directory.GetFiles(Root,"*.xaml",SearchOption.AllDirectories);
        Assert.True(files.Length>=14,"Application XAML sources must be available to the coverage gate.");
        var missing=new List<string>();
        foreach(var file in files)
        {
            var xml=XDocument.Load(file,LoadOptions.SetLineInfo);
            if(xml.Root?.Attribute(XName.Get("Class","http://schemas.microsoft.com/winfx/2006/xaml")) is null)continue;
            foreach(var node in xml.Descendants().Where(n=>Interactive.Contains(n.Name.LocalName) || IsCustomPrimitive(n)))
                if(string.IsNullOrWhiteSpace(node.Attribute("AutomationProperties.AutomationId")?.Value))missing.Add(Path.GetFileName(file)+":"+node.Name.LocalName+" "+node.Attribute(XName.Get("Name","http://schemas.microsoft.com/winfx/2006/xaml"))?.Value);
        }
        Assert.True(missing.Count==0,"New UI controls must expose a stable AutomationProperties.AutomationId:\n"+string.Join('\n',missing));
    }

    private static bool IsCustomPrimitive(XElement node)
    {
        var uri=node.Name.NamespaceName;
        if(!uri.StartsWith("clr-namespace:",StringComparison.Ordinal))return false;
        var parts=uri["clr-namespace:".Length..].Split(';');
        var assembly=parts.Skip(1).FirstOrDefault(p=>p.StartsWith("assembly="))?["assembly=".Length..];
        var type=assembly is null ? typeof(MainWindow).Assembly.GetType(parts[0]+"."+node.Name.LocalName) : Type.GetType(parts[0]+"."+node.Name.LocalName+", "+assembly);
        return type is not null && (typeof(AvalonDock.Controls.LayoutDocumentTabItem).IsAssignableFrom(type) || typeof(AvalonDock.Controls.DropDownControlArea).IsAssignableFrom(type) || typeof(System.Windows.Controls.Primitives.ButtonBase).IsAssignableFrom(type) ||
            typeof(System.Windows.Controls.Primitives.Selector).IsAssignableFrom(type) || typeof(System.Windows.Controls.Primitives.TextBoxBase).IsAssignableFrom(type));
    }

    [Fact]
    public void CustomPointerGesturesMustDeclareARegisteredSemanticApi()
    {
        var mapping=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/windows-ui-api-gestures.json")))!.AsObject();
        var source=string.Join('\n',Directory.GetFiles(Root,"*.cs",SearchOption.AllDirectories).Select(File.ReadAllText));
        var registered=Regex.Matches(source,"(?:Command|RegisterCommand|RegisterQuery)\\(\"([^\"]+)\"").Select(m=>m.Groups[1].Value).ToHashSet();
        var observed=new HashSet<string>();
        foreach(var file in Directory.GetFiles(Root,"*.xaml",SearchOption.AllDirectories))
        {
            var xml=XDocument.Load(file); var className=xml.Root?.Attribute(XName.Get("Class","http://schemas.microsoft.com/winfx/2006/xaml"))?.Value.Split('.').Last();
            if(className is null)continue;
            foreach(var attribute in xml.Descendants().Attributes().Where(a=>Regex.IsMatch(a.Name.LocalName,"^(Preview)?(Mouse[A-Za-z]+|DragEnter|DragOver|DragLeave|Drop)$") && Regex.IsMatch(a.Value,"^[A-Za-z_][A-Za-z0-9_]*$")))
            {
                var key=className+"."+attribute.Value; observed.Add(key);
                Assert.True(mapping[key] is JsonArray {Count:>0},"Custom UI gesture has no API mapping: "+key);
                foreach(var operation in mapping[key]!.AsArray())Assert.Contains(operation!.GetValue<string>(),registered);
            }
        }
        Assert.All(mapping,p=>Assert.Contains(p.Key,observed));
    }

    [Fact]
    public void NativeOsDialogsCannotBypassTheAutomationDialogAdapter()
    {
        foreach(var file in Directory.GetFiles(Root,"*.cs",SearchOption.AllDirectories))
        {
            if(Path.GetFileName(file) is "AutomationDialogs.cs" or "App.xaml.cs")continue;
            var text=File.ReadAllText(file);
            Assert.DoesNotContain("System.Windows.MessageBox.Show(",text);
            Assert.DoesNotContain("System.Windows.Forms.MessageBox.Show(",text);
            if(text.Contains("OpenFileDialog")||text.Contains("SaveFileDialog"))Assert.DoesNotContain("dialog.ShowDialog(this)",text);
        }
    }

    [Fact]
    public void DynamicButtonsAndMenusMustUseTheApiAwareFactory()
    {
        foreach(var file in Directory.GetFiles(Root,"*.cs",SearchOption.AllDirectories))
        {
            if(Path.GetFileName(file)=="AutomationControls.cs")continue;
            var source=File.ReadAllText(file);
            Assert.DoesNotMatch(@"new\s+(?:System\.Windows\.Controls\.)?(?:Button|MenuItem)\b",source);
            if(Path.GetFileName(file)=="TrayIconService.cs")Assert.DoesNotMatch(@"\.Items\.Add\(\s*""",source);
        }
    }

    [Fact]
    public void NewNativeInputControlKindsRequireAnExplicitAdapterReview()
    {
        var unknown=new HashSet<string>();
        foreach(var file in Directory.GetFiles(Root,"*.xaml",SearchOption.AllDirectories))
        {
            var xml=XDocument.Load(file);
            if(xml.Root?.Attribute(XName.Get("Class","http://schemas.microsoft.com/winfx/2006/xaml")) is null)continue;
            foreach(var element in xml.Descendants().Where(e=>e.Name.NamespaceName=="http://schemas.microsoft.com/winfx/2006/xaml/presentation"))
            {
                var name=element.Name.LocalName;
                if(name is "Hyperlink" or "InkCanvas") {unknown.Add(name);continue;}
                var type=Type.GetType("System.Windows.Controls."+name+", PresentationFramework");
                if(type is null || !typeof(System.Windows.Controls.Control).IsAssignableFrom(type))continue;
                if(!Interactive.Contains(name)&&!ObservationalOrContainer.Contains(name))unknown.Add(name);
            }
        }
        Assert.True(unknown.Count==0,"New input-control kinds need an API adapter/contract test before they are added to the supported set: "+string.Join(", ",unknown));
    }

    [Fact]
    public void AuthenticationTextInputsMustExplicitlyRemainSensitive()
    {
        foreach(var file in Directory.GetFiles(Root,"*.xaml",SearchOption.AllDirectories))
        {
            var xml=XDocument.Load(file);
            foreach(var element in xml.Descendants().Where(e=>e.Name.LocalName=="TextBox"))
            {
                var name=element.Attribute(XName.Get("Name","http://schemas.microsoft.com/winfx/2006/xaml"))?.Value??"";
                if(!Regex.IsMatch(name,"Password|Token|AuthInput|AuthUrl",RegexOptions.IgnoreCase))continue;
                Assert.Contains(element.Attributes(),a=>a.Name.LocalName=="AutomationMetadata.Sensitive"&&a.Value=="True");
            }
        }
    }
}
