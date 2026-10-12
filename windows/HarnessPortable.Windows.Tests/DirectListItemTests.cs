using System.ComponentModel;
using HarnessPortable.Windows.Controls;

namespace HarnessPortable.Windows.Tests;

/// <summary>
/// WPF only listens for property changes when the source casts to
/// INotifyPropertyChanged. The auth-mode toggle button binds its label to
/// DirectListItem.AuthLabel; before 2.6.20 the class raised the event without
/// declaring the interface, so the label never refreshed after a click and
/// users kept clicking — flipping the saved mode back off (2.6.19 on
/// railgunhamster: "只有个无，我点了没用").
/// </summary>
public class DirectListItemTests
{
    [Fact]
    public void DirectListItem_IsNotifyPropertyChanged()
    {
        var item = new ManagementView.DirectListItem
        {
            Url = "http://localhost:3080",
        };

        string? raised = null;
        ((INotifyPropertyChanged)item).PropertyChanged += (_, e) => raised = e.PropertyName;

        item.AuthLabel = "NSSM 自动";

        Assert.Equal(nameof(ManagementView.DirectListItem.AuthLabel), raised);
    }
}
