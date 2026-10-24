using System.Windows.Automation;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace HarnessPortable.Windows.Automation;

/// <summary>Dynamic UI controls carry their API identity at construction time.</summary>
public static class AutomationControls
{
    public static MenuItem Menu(string id, object header, object? tag = null)
    {
        var item = new MenuItem { Header=header, Tag=tag };
        AutomationProperties.SetAutomationId(item,id); return item;
    }
    public static Button Button(string id, string label)
    {
        var button=new Button { Content=label, MinWidth=78, Padding=new(12,7,12,7) };
        AutomationProperties.SetAutomationId(button,id); return button;
    }
}
