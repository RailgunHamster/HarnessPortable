using System.Text.Json.Nodes;
using HarnessPortable.Windows.Automation;

namespace HarnessPortable.Windows.Tests;

public sealed class AutomationEventsTests
{
    [Fact]
    public void CursorBatchesDoNotSkipUnreadEventsAndReportExpiredHistory()
    {
        var events=new AutomationEvents();for(var i=0;i<1100;i++)events.Publish("fixture.changed");
        var first=events.Read(0,10);Assert.True(first["truncated"]!.GetValue<bool>());Assert.Equal(10,first["events"]!.AsArray().Count);
        var next=events.Read(first["cursor"]!.GetValue<long>(),10);
        Assert.Equal(first["cursor"]!.GetValue<long>()+1,next["events"]![0]!["sequence"]!.GetValue<long>());
        Assert.False(next["truncated"]!.GetValue<bool>());
    }
    [Fact]
    public async Task WaitingObservesChangesPublishedBeforeOrAfterTheWaitAndSupportsCancellation()
    {
        var events=new AutomationEvents();var pending=events.WaitAsync(new(){["cursor"]=0,["timeoutMs"]=2000},CancellationToken.None);
        Assert.False(pending.IsCompleted);events.Publish("fixture.done",new(){["state"]="ready"});
        Assert.Equal("fixture.done",(await pending)!["events"]![0]!["kind"]!.GetValue<string>());
        Assert.Single((await events.WaitAsync(new(){["cursor"]=0},CancellationToken.None))!["events"]!.AsArray());
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>events.WaitAsync(new(){["cursor"]=1},cancelled.Token));
    }
}
