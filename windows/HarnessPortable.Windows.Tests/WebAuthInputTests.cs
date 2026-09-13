using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public class WebAuthInputTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ReturnsNullForEmptyInput(string? input)
    {
        Assert.Null(WebAuthInput.Normalize(input, "127.0.0.1", 3080));
    }

    [Fact]
    public void Normalize_TreatsBareValueAsToken()
    {
        Assert.Equal(
            "http://127.0.0.1:3080/?token=abc-123_X",
            WebAuthInput.Normalize("abc-123_X", "127.0.0.1", 3080));
    }

    [Fact]
    public void Normalize_UsesRemoteAuthority()
    {
        Assert.Equal(
            "http://10.0.0.5:4096/?token=tok",
            WebAuthInput.Normalize("tok", "10.0.0.5", 4096));
    }

    [Fact]
    public void Normalize_FallsBackToLoopbackWithoutHost()
    {
        Assert.Equal(
            "http://127.0.0.1:3080/?token=tok",
            WebAuthInput.Normalize("tok", "  ", 3080));
    }

    [Fact]
    public void Normalize_AcceptsKeyValuePairAndBareQuery()
    {
        Assert.Equal(
            "http://127.0.0.1:3080/?token=abc",
            WebAuthInput.Normalize("token=abc", "127.0.0.1", 3080));
        // A pasted bare query keeps its shape; the session's rewriter turns it
        // into "http://127.0.0.1:<localPort>/?token=abc" when it navigates.
        Assert.Equal(
            "http://127.0.0.1:3080?token=abc",
            WebAuthInput.Normalize("?token=abc", "127.0.0.1", 3080));
    }

    [Fact]
    public void Normalize_KeepsFullUrlWithQuery()
    {
        Assert.Equal(
            "http://192.168.0.10:3080/?token=abc",
            WebAuthInput.Normalize("http://192.168.0.10:3080/?token=abc", "127.0.0.1", 3080));
    }

    [Fact]
    public void Normalize_RejectsUrlWithoutQuery()
    {
        Assert.Null(WebAuthInput.Normalize("http://192.168.0.10:3080/", "127.0.0.1", 3080));
    }

    [Fact]
    public void Normalize_RejectsUnusableInput()
    {
        Assert.Null(WebAuthInput.Normalize("not a url", "127.0.0.1", 3080));
        Assert.Null(WebAuthInput.Normalize("ftp://host/?token=a", "127.0.0.1", 3080));
        Assert.Null(WebAuthInput.Normalize("http://host:3080/x", "127.0.0.1", 3080));
    }

    [Fact]
    public void Normalize_EscapesTheToken()
    {
        Assert.Equal(
            "http://127.0.0.1:3080/?token=a%26b",
            WebAuthInput.Normalize("a&b", "127.0.0.1", 3080));
    }
}
