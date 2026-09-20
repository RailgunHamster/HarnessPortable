using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class ViewStateUrlsTests
{
    [Fact]
    public void Extract_ReadsAllThreeParameters()
    {
        var state = ViewStateUrls.Extract(
            "http://127.0.0.1:3080/?token=secret&dsh_session=session-abc&dsh_sidebar=0&dsh_rightbar=320");

        Assert.NotNull(state);
        Assert.Equal("session-abc", state!.SessionId);
        Assert.Equal(0, state.Sidebar);
        Assert.Equal(320, state.Rightbar);
    }

    [Fact]
    public void Extract_IgnoresUrlsWithoutOurParameters()
    {
        Assert.Null(ViewStateUrls.Extract("http://127.0.0.1:3080/?token=secret"));
        Assert.Null(ViewStateUrls.Extract("http://127.0.0.1:3080/"));
        Assert.Null(ViewStateUrls.Extract("about:blank"));
        Assert.Null(ViewStateUrls.Extract(null));
        Assert.Null(ViewStateUrls.Extract(""));
    }

    [Fact]
    public void Extract_KeepsSessionWithoutWidths()
    {
        var state = ViewStateUrls.Extract("http://127.0.0.1:3080/s/session-abc?dsh_session=session-abc");

        Assert.NotNull(state);
        Assert.Equal("session-abc", state!.SessionId);
        Assert.Null(state.Sidebar);
        Assert.Null(state.Rightbar);
    }

    [Fact]
    public void Extract_DropsUnparsableWidthsButKeepsTheRest()
    {
        var state = ViewStateUrls.Extract(
            "http://127.0.0.1:3080/?dsh_session=session-abc&dsh_sidebar=nonsense&dsh_rightbar=-5");

        Assert.NotNull(state);
        Assert.Equal("session-abc", state!.SessionId);
        Assert.Null(state.Sidebar);
        Assert.Null(state.Rightbar);
    }

    [Fact]
    public void Extract_ReturnsNullWhenNothingIsUsable()
    {
        // Present but junk: storing an all-null blob in the preset would be noise.
        Assert.Null(ViewStateUrls.Extract("http://127.0.0.1:3080/?dsh_sidebar=nonsense&dsh_rightbar=-5"));
    }

    [Fact]
    public void Merge_PreservesTokenPathAndFragment()
    {
        var merged = ViewStateUrls.Merge(
            "http://127.0.0.1:3080/s/session-abc?token=secret#anchor",
            new LayoutWebState { SessionId = "session-abc", Sidebar = 0, Rightbar = 320 });

        // Order: untouched parameters first, ours appended.
        Assert.Equal(
            "http://127.0.0.1:3080/s/session-abc?token=secret&dsh_session=session-abc&dsh_sidebar=0&dsh_rightbar=320#anchor",
            merged);
    }

    [Fact]
    public void Merge_ReplacesExistingValuesWithoutDuplicating()
    {
        var merged = ViewStateUrls.Merge(
            "http://127.0.0.1:3080/?dsh_session=session-old&dsh_sidebar=280&token=t",
            new LayoutWebState { SessionId = "session-new", Sidebar = 300, Rightbar = 0 });

        Assert.Equal(
            "http://127.0.0.1:3080/?dsh_session=session-new&dsh_sidebar=300&token=t&dsh_rightbar=0",
            merged);
    }

    [Fact]
    public void Merge_LeavesUnrecordedFieldsAlone()
    {
        var merged = ViewStateUrls.Merge(
            "http://127.0.0.1:3080/?dsh_sidebar=280",
            new LayoutWebState { SessionId = "session-abc" });

        Assert.Equal("http://127.0.0.1:3080/?dsh_sidebar=280&dsh_session=session-abc", merged);
    }

    [Fact]
    public void Merge_IsANoOpWithoutStateOrOnForeignUrls()
    {
        Assert.Equal("http://127.0.0.1:3080/?token=t", ViewStateUrls.Merge("http://127.0.0.1:3080/?token=t", null));
        Assert.Equal("about:blank", ViewStateUrls.Merge("about:blank", new LayoutWebState { SessionId = "s", Sidebar = 1, Rightbar = 1 }));
    }

    [Fact]
    public void Merge_ThenExtract_RoundTrips()
    {
        var merged = ViewStateUrls.Merge(
            "http://127.0.0.1:3080/s/session-abc?token=secret",
            new LayoutWebState { SessionId = "session-abc", Sidebar = 264, Rightbar = 0 });

        var state = ViewStateUrls.Extract(merged);

        Assert.NotNull(state);
        Assert.Equal("session-abc", state!.SessionId);
        Assert.Equal(264, state.Sidebar);
        Assert.Equal(0, state.Rightbar);
    }

    [Fact]
    public void Merge_EscapesTheSessionId()
    {
        var merged = ViewStateUrls.Merge(
            "http://127.0.0.1:3080/",
            new LayoutWebState { SessionId = "session-a b&c" });

        Assert.Contains("dsh_session=session-a%20b%26c", merged);
        Assert.Equal("session-a b&c", ViewStateUrls.Extract(merged)!.SessionId);
    }

    [Fact]
    public void NeedsReapply_IsTrueForAPlainPage()
    {
        // The token redirect lands here with the query stripped: our parameters
        // never arrived, so the tab has to put them back.
        Assert.True(ViewStateUrls.NeedsReapply("http://127.0.0.1:3080/"));
        Assert.True(ViewStateUrls.NeedsReapply("http://127.0.0.1:3080/?token=x"));
        Assert.True(ViewStateUrls.NeedsReapply("http://127.0.0.1:3080/s/session-abc"));
    }

    [Fact]
    public void NeedsReapply_IsFalseOnceThePageCarriesState()
    {
        Assert.False(ViewStateUrls.NeedsReapply("http://127.0.0.1:3080/?dsh_session=session-abc"));
        Assert.False(ViewStateUrls.NeedsReapply("http://127.0.0.1:3080/?dsh_sidebar=0"));
        Assert.False(ViewStateUrls.NeedsReapply("about:blank"));
        Assert.False(ViewStateUrls.NeedsReapply(null));
    }
}
