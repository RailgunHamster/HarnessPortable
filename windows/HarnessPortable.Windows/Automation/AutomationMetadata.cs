using System.Windows;

namespace HarnessPortable.Windows.Automation;

public static class AutomationMetadata
{
    public static readonly DependencyProperty SensitiveProperty = DependencyProperty.RegisterAttached(
        "Sensitive", typeof(bool), typeof(AutomationMetadata), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetSensitive(DependencyObject target) => (bool)target.GetValue(SensitiveProperty);
    public static void SetSensitive(DependencyObject target, bool value) => target.SetValue(SensitiveProperty, value);
    public static readonly DependencyProperty KeyTargetProperty = DependencyProperty.RegisterAttached(
        "KeyTarget",typeof(bool),typeof(AutomationMetadata),new PropertyMetadata(false));
    public static bool GetKeyTarget(DependencyObject target)=>(bool)target.GetValue(KeyTargetProperty);
    public static void SetKeyTarget(DependencyObject target,bool value)=>target.SetValue(KeyTargetProperty,value);
}
