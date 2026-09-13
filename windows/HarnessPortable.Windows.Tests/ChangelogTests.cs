using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class ChangelogTests
{
    [Fact]
    public void SectionFor_ReturnsRequestedVersionBlock()
    {
        const string changelog = """
            # Changelog

            ## 2.5.0

            - newest

            ## 2.4.0

            - older
            """;

        var section = Changelog.SectionFor(changelog, "2.5.0");
        Assert.Contains("newest", section);
        Assert.DoesNotContain("older", section);
        Assert.Contains("## 2.5.0", section);
    }

    [Fact]
    public void ReadEmbedded_ContainsCurrentVersion()
    {
        var text = Changelog.ReadEmbedded();
        Assert.Contains("2.5.0", text);
        Assert.NotNull(Changelog.SectionFor(text, AppVersion.Current));
    }
}
