using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using FileDialog = Microsoft.Win32.FileDialog;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace HarnessPortable.Windows.Automation;

/// <summary>
/// OS-owned dialogs have no WPF surface. API mode supplies equivalent app-owned prompts,
/// while ordinary launches retain the native Windows dialogs.
/// </summary>
public static class AutomationDialogs
{
    public static bool Enabled { get; set; }

    public static MessageBoxResult Show(Window? owner, string message, string caption = "Harness Portable",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        if (!Enabled) return System.Windows.MessageBox.Show(owner, message, caption, buttons, image, defaultResult);
        var (dialog, content) = Create(owner, caption, "dialog.message");
        var text = new TextBlock { Text=message, TextWrapping=TextWrapping.Wrap, Margin=new(0,0,0,18), MaxWidth=570 };
        AutomationProperties.SetAutomationId(text, "dialog.message.text"); content.Children.Add(text);
        var choices = buttons switch
        {
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
            MessageBoxButton.OKCancel => [MessageBoxResult.OK, MessageBoxResult.Cancel],
            _ => [MessageBoxResult.OK]
        };
        var result = choices.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel : buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
        var row = new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
        foreach (var choice in choices)
        {
            var label = choice switch { MessageBoxResult.Yes => "是", MessageBoxResult.No => "否", MessageBoxResult.Cancel => "取消", _ => "确定" };
            var button = AutomationControls.Button("dialog.choice."+choice.ToString().ToLowerInvariant(),label);
            button.Margin=new(8,0,0,0); button.IsDefault=choice == (defaultResult == MessageBoxResult.None ? choices[0] : defaultResult);
            button.IsCancel=choice is MessageBoxResult.Cancel or MessageBoxResult.No;
            button.Click += (_, _) => { result=choice; dialog.DialogResult=true; };
            row.Children.Add(button);
        }
        content.Children.Add(row); dialog.ShowDialog(); return result;
    }

    public static MessageBoxResult Show(string message, string caption = "Harness Portable", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None) => Show(null,message,caption,buttons,image,defaultResult);

    public static bool? ChooseFile(Window owner, OpenFileDialog picker) => Enabled
        ? ChoosePath(owner,picker,false,picker.CheckFileExists,false) : picker.ShowDialog(owner);
    public static bool? ChooseFile(Window owner, SaveFileDialog picker) => Enabled
        ? ChoosePath(owner,picker,true,false,picker.OverwritePrompt) : picker.ShowDialog(owner);

    private static bool? ChoosePath(Window owner, FileDialog picker, bool saving, bool fileMustExist, bool overwritePrompt)
    {
        var (dialog, content) = Create(owner,string.IsNullOrWhiteSpace(picker.Title) ? saving ? "保存文件" : "选择文件" : picker.Title,"dialog.file");
        content.Children.Add(new TextBlock { Text="文件的完整路径", Margin=new(0,0,0,8) });
        var input = new TextBox { Text=picker.FileName, MinWidth=480, Padding=new(8), Margin=new(0,0,0,8) };
        AutomationProperties.SetAutomationId(input,"dialog.file.path"); content.Children.Add(input);
        var hint = new TextBlock { TextWrapping=TextWrapping.Wrap, MaxWidth=570 }; AutomationProperties.SetAutomationId(hint,"dialog.file.validation"); content.Children.Add(hint);
        var row = new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right, Margin=new(0,16,0,0) };
        var cancel = AutomationControls.Button("dialog.file.cancel","取消"); cancel.IsCancel=true;
        var accept = AutomationControls.Button("dialog.file.accept",saving ? "保存" : "选择"); accept.IsDefault=true; accept.Margin=new(8,0,0,0);
        accept.Click += (_, _) =>
        {
            try
            {
                var path=input.Text.Trim();
                if (!Path.IsPathFullyQualified(path)) { hint.Text="请输入完整的绝对路径。"; return; }
                path=Path.GetFullPath(path);
                if (picker.AddExtension && Path.GetExtension(path).Length == 0 && !string.IsNullOrEmpty(picker.DefaultExt)) path += picker.DefaultExt.StartsWith('.') ? picker.DefaultExt : "."+picker.DefaultExt;
                if (fileMustExist && !File.Exists(path)) { hint.Text="文件不存在。"; return; }
                if (picker.CheckPathExists && !Directory.Exists(Path.GetDirectoryName(path))) { hint.Text="文件夹不存在。"; return; }
                if (saving && overwritePrompt && File.Exists(path) && Show(dialog,"该文件已存在，要覆盖吗？","确认覆盖",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No) != MessageBoxResult.Yes) return;
                picker.FileName=path; dialog.DialogResult=true;
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { hint.Text="无法使用此文件路径。"; }
        };
        row.Children.Add(cancel); row.Children.Add(accept); content.Children.Add(row);
        return dialog.ShowDialog();
    }

    private static (Window Dialog, StackPanel Content) Create(Window? owner, string title, string id)
    {
        var content = new StackPanel { Margin=new(22) };
        var dialog = new Window { Title=title, Content=content, SizeToContent=SizeToContent.WidthAndHeight,
            ResizeMode=ResizeMode.NoResize, ShowInTaskbar=false, MaxWidth=680, WindowStartupLocation=owner?.IsVisible == true ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
        if (owner?.IsVisible == true) dialog.Owner=owner;
        AutomationProperties.SetAutomationId(dialog,id);
        Services.AppTheme.Track(dialog);
        return (dialog,content);
    }
}
