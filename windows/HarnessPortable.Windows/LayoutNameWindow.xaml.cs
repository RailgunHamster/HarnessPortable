using System.Windows;

namespace HarnessPortable.Windows;

public partial class LayoutNameWindow : Window
{
    public string LayoutName => NameBox.Text.Trim();

    public LayoutNameWindow(string? initialName = null)
    {
        InitializeComponent();
        NameBox.Text = initialName ?? $"布局 {DateTime.Now:MM-dd HH:mm}";
        Loaded += (_, _) => NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (LayoutName.Length == 0)
        {
            return;
        }

        DialogResult = true;
    }
}
