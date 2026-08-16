using System.Windows;
using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows;

public partial class PasswordPromptWindow : Window
{
    public string Password => PasswordInput.Password;

    public PasswordPromptWindow(TunnelProfile profile)
    {
        InitializeComponent();
        ProfileSummary.Text = $"{profile.User}@{profile.SshHost}:{profile.SshPort}";
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Password.Length == 0)
        {
            return;
        }

        DialogResult = true;
    }
}
