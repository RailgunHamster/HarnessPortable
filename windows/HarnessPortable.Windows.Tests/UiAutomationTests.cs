using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HarnessPortable.Windows.Automation;

namespace HarnessPortable.Windows.Tests;

public sealed class UiAutomationTests
{
    private sealed class BoundText : System.ComponentModel.INotifyPropertyChanged
    {
        private string _value="initial";
        public string Value {get=>_value;set{_value=value;PropertyChanged?.Invoke(this,new(nameof(Value)));}}
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
    private static void Sta(Func<Task> action)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async()=>
            {
                try {await action();} catch(Exception error){failure=error;}
                finally{Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(25)));
        if(failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static async Task<JsonObject> Call(UiAutomationService service,string method,JsonObject? args=null)
    {
        var reply=await service.Api.CallAsync(new(){["version"]=1,["id"]=Guid.NewGuid().ToString("N"),["method"]=method,["params"]=args??new()});
        Assert.Null(reply["error"]); return reply;
    }
    private sealed class CounterCommand(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add{} remove{} }
        public bool CanExecute(object? parameter)=>true;
        public void Execute(object? parameter)=>run();
    }

    [Fact]
    public void InvokeRunsTheRealButtonCommandAndFutureControlsAppearAutomatically()=>Sta(async()=>
    {
        var clicks=0; var commands=0;
        var button=new Button{Content="Fixture",Command=new CounterCommand(()=>commands++)}; button.Click+=(_,_)=>clicks++;
        AutomationProperties.SetAutomationId(button,"fixture.button");
        var panel=new StackPanel(); panel.Children.Add(button);
        var window=new Window{Content=panel,Width=400,Height=300,Left=-25000,Top=0,ShowActivated=false}; window.Show();
        try
        {
            var surface=new UiAutomationService(Dispatcher.CurrentDispatcher,()=>[window]);
            var found=await Call(surface,"ui.find",new(){["selector"]=new JsonObject{["automationId"]="fixture.button"}});
            var id=found["result"]![0]!["id"]!.GetValue<string>();
            Assert.Contains(found["result"]![0]!["actions"]!.AsArray(),n=>n!.GetValue<string>()=="invoke");
            await Call(surface,"ui.act",new(){["target"]=new JsonObject{["id"]=id},["action"]="invoke"});
            for(var i=0;i<30&&commands==0;i++)await Task.Delay(10);
            Assert.Equal(1,clicks); Assert.Equal(1,commands);
            var later=new TextBox{Name="AddedLater"}; panel.Children.Add(later); window.UpdateLayout();
            var tree=await Call(surface,"ui.tree");
            Assert.Contains(tree["result"]!.AsArray(),n=>n!["elementName"]?.GetValue<string>()=="AddedLater" && n["actions"]!.AsArray().Any(a=>a!.GetValue<string>()=="setValue"));
        }
        finally{window.Close();}
    });

    [Fact]
    public void PasswordAndMarkedAuthenticationFieldsAreWriteOnlyInSnapshots()=>Sta(async()=>
    {
        var password=new PasswordBox{Name="PasswordInput",Password="secret-one"};
        var auth=new TextBox{Name="AuthInput",Text="secret-two"}; AutomationMetadata.SetSensitive(auth,true);
        var panel=new StackPanel(); panel.Children.Add(password); panel.Children.Add(auth);
        var window=new Window{Content=panel,Width=400,Height=300,Left=-25000,ShowActivated=false}; window.Show();
        try
        {
            var surface=new UiAutomationService(Dispatcher.CurrentDispatcher,()=>[window]);
            var tree=await Call(surface,"ui.tree");
            Assert.DoesNotContain("secret-one",tree.ToJsonString()); Assert.DoesNotContain("secret-two",tree.ToJsonString());
            var row=tree["result"]!.AsArray().Single(n=>n!["elementName"]?.GetValue<string>()=="PasswordInput")!;
            await Call(surface,"ui.act",new(){["target"]=new JsonObject{["id"]=row["id"]!.DeepClone()},["action"]="setValue",["value"]="replacement"});
            for(var i=0;i<30&&password.Password!="replacement";i++)await Task.Delay(10);
            Assert.Equal("replacement",password.Password);
        }
        finally{window.Close();}
    });

    [Fact]
    public void WaitObservesStateAndReportsTimeoutWithoutBlockingDispatcher()=>Sta(async()=>
    {
        var text=new TextBox{Name="Status",Text="starting"};
        var window=new Window{Content=text,Width=400,Height=300,Left=-25000,ShowActivated=false}; window.Show();
        try
        {
            var surface=new UiAutomationService(Dispatcher.CurrentDispatcher,()=>[window]);
            _=Task.Delay(80).ContinueWith(_=>window.Dispatcher.BeginInvoke(()=>text.Text="ready"));
            var result=await Call(surface,"ui.wait",new(){["selector"]=new JsonObject{["elementName"]="Status"},["property"]="properties.value",["equals"]="ready",["timeoutMs"]=2000});
            Assert.Equal("ready",result["result"]!["properties"]!["value"]!.GetValue<string>());
            var timeout=await surface.Api.CallAsync(new(){["version"]=1,["id"]="timeout",["method"]="ui.wait",["params"]=new JsonObject{
                ["selector"]=new JsonObject{["elementName"]="missing"},["timeoutMs"]=80}});
            Assert.Equal("timeout",timeout["error"]!["code"]!.GetValue<string>());
        }
        finally{window.Close();}
    });

    [Fact]
    public void ValueEditingPreservesTwoWayBindingsAndReadonlyIndicatorsAreNotWritable()=>Sta(async()=>
    {
        var model=new BoundText();var text=new TextBox{Name="BoundInput"};
        text.SetBinding(TextBox.TextProperty,new System.Windows.Data.Binding(nameof(BoundText.Value)){Source=model,Mode=System.Windows.Data.BindingMode.TwoWay});
        var window=new Window{Content=text,Width=400,Height=300,Left=-25000,ShowActivated=false};window.Show();
        try
        {
            var surface=new UiAutomationService(Dispatcher.CurrentDispatcher,()=>[window]);
            await Call(surface,"ui.act",new(){["target"]=new JsonObject{["elementName"]="BoundInput"},["action"]="setValue",["value"]="committed"});
            for(var i=0;i<40&&model.Value!="committed";i++)await Task.Delay(10);
            Assert.Equal("committed",model.Value);Assert.True(System.Windows.Data.BindingOperations.IsDataBound(text,TextBox.TextProperty));
            model.Value="model update";Assert.Equal("model update",text.Text);
            Assert.DoesNotContain("setValue",WpfAutomationTree.Actions(new ProgressBar()));
            Assert.Contains("setValue",WpfAutomationTree.Actions(new Slider()));
        }
        finally{window.Close();}
    });
}
