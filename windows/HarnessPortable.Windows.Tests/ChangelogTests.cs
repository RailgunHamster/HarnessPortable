using HarnessPortable.Windows.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HarnessPortable.Windows.Tests;

public sealed class ChangelogTests
{
    [Fact]
    public void SectionFor_ReturnsRequestedVersionBlock()
    {
        const string changelog = """
            # Changelog

            ## 2.5.0

            发布时间：2026-09-13 18:31（北京时间，UTC+08:00）

            - newest

            ## 2.4.0

            - older
            """;

        var section = Changelog.SectionFor(changelog, "2.5.0");
        Assert.Contains("newest", section);
        Assert.DoesNotContain("older", section);
        Assert.Contains("## 2.5.0", section);
        Assert.Contains("发布时间：2026-09-13 18:31（北京时间，UTC+08:00）", section);
    }

    [Fact]
    public void ReadEmbedded_ContainsCurrentVersion()
    {
        var text = Changelog.ReadEmbedded();
        Assert.Contains(AppVersion.Current, text);
        Assert.NotNull(Changelog.SectionFor(text, AppVersion.Current));
    }

    [Fact]
    public void ReadEmbedded_EachVersionHasPublicationTime()
    {
        // These historical versions have no surviving public release record.
        // New versions must carry a real timestamp; this is not a general exemption.
        string[] unrecordedVersions = ["2.7.10", "2.7.0", "2.6.13", "2.5.2"];
        var text = Changelog.ReadEmbedded();
        var versions = Regex.Matches(text, @"(?m)^##\s+(\d+\.\d+\.\d+)\s*$");
        Assert.NotEmpty(versions);
        foreach (Match match in versions)
        {
            var version = match.Groups[1].Value;
            var section = Changelog.SectionFor(text, version)!;
            var line = Assert.Single(section.Split('\n'), line => line.StartsWith("发布时间："));
            if (line.TrimEnd() == "发布时间：未记录")
            {
                Assert.Contains(version, unrecordedVersions);
                Assert.NotEqual(AppVersion.Current, version);
                continue;
            }

            var timestamp = Regex.Match(line.TrimEnd(),
                @"^发布时间：(\d{4}-\d{2}-\d{2} \d{2}:\d{2})（北京时间，UTC\+08:00）$");
            Assert.True(timestamp.Success, $"Invalid publication time for {version}: {line}");
            Assert.True(DateTimeOffset.TryParseExact(timestamp.Groups[1].Value + " +08:00",
                "yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                $"Invalid publication date for {version}: {line}");
        }
    }
}
