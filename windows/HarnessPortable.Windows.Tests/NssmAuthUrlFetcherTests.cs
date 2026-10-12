using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public class NssmAuthUrlFetcherTests
{
    [Fact]
    public void BuildScript_UsesNssmConfigAndVerifiesToken()
    {
        var script = NssmAuthUrlFetcher.BuildScript();

        Assert.Contains("nssm\\.exe", script);
        // Reads the service's Parameters registry key — never calls nssm.exe,
        // whose `get` opens LSA policy and is denied under the UAC-filtered
        // token of an interactively launched app (2.6.22).
        Assert.DoesNotContain("nssm get", script);
        Assert.Contains(@"HKLM:\SYSTEM\CurrentControlSet\Services\$($c.Name)\Parameters", script);
        Assert.Contains(".Application $.AppParameters", script.Replace("$($item.Application) $($item.AppParameters)", ".Application $.AppParameters"));
        Assert.Contains(".AppStdout", script);
        Assert.Contains("dsh web: (http\\S+)", script);
        Assert.Contains("-eq \"303\"", script);
    }

    [Fact]
    public void ExtractUrl_PicksLastUrlFromOutput()
    {
        var output = "#< CLIXML\r\nsome noise\r\ndsh web: http://127.0.0.1:3080/?token=first\r\n";

        Assert.Equal("http://127.0.0.1:3080/?token=first", NssmAuthUrlFetcher.ExtractUrl(output));
    }

    [Fact]
    public void ExtractUrl_ReturnsLastWhenMultiple()
    {
        var output = "http://127.0.0.1:1/?t=a\nhttp://127.0.0.1:2/?t=b\n";

        Assert.Equal("http://127.0.0.1:2/?t=b", NssmAuthUrlFetcher.ExtractUrl(output));
    }

    [Fact]
    public void ExtractUrl_ReturnsNullWithoutUrl()
    {
        Assert.Null(NssmAuthUrlFetcher.ExtractUrl(""));
        Assert.Null(NssmAuthUrlFetcher.ExtractUrl("no url here"));
    }

    [Fact]
    public void RewriteToLocal_ReplacesAuthorityAndKeepsQuery()
    {
        var result = NssmAuthUrlFetcher.RewriteToLocal(
            "http://127.0.0.1:3080/?token=abc", 4080, 0);

        Assert.Equal("http://127.0.0.1:4080/?token=abc", result);
    }

    [Fact]
    public void RewriteToLocal_ChecksRemotePortWhenGiven()
    {
        Assert.Null(NssmAuthUrlFetcher.RewriteToLocal(
            "http://127.0.0.1:3080/?token=abc", 4080, 9999));
        Assert.NotNull(NssmAuthUrlFetcher.RewriteToLocal(
            "http://127.0.0.1:3080/?token=abc", 4080, 3080));
    }

    [Fact]
    public void RewriteToLocal_RejectsInvalidInput()
    {
        Assert.Null(NssmAuthUrlFetcher.RewriteToLocal(null, 4080, 0));
        Assert.Null(NssmAuthUrlFetcher.RewriteToLocal("", 4080, 0));
        Assert.Null(NssmAuthUrlFetcher.RewriteToLocal("not a url", 4080, 0));
        Assert.Null(NssmAuthUrlFetcher.RewriteToLocal("ftp://127.0.0.1:3080/", 4080, 0));
    }
}
