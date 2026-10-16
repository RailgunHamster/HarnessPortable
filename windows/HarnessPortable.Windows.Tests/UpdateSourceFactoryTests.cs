using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class UpdateSourceFactoryTests
{
    [Fact]
    public void Normalize_Empty_UsesGiteeDefault()
    {
        Assert.Equal(UpdateSourceFactory.GiteeRepoUrl, UpdateSourceFactory.Normalize(""));
        Assert.Equal(UpdateSourceFactory.GiteeRepoUrl, UpdateSourceFactory.Normalize("  "));
        Assert.Equal(UpdateSourceKind.Gitee, UpdateSourceFactory.Classify(""));
    }

    [Theory]
    [InlineData("https://github.com/RailgunHamster/HarnessPortable", UpdateSourceKind.GitHub)]
    [InlineData("https://github.com/RailgunHamster/HarnessPortable/releases", UpdateSourceKind.GitHub)]
    [InlineData("https://gitee.com/railgunhamster/HarnessPortable", UpdateSourceKind.Gitee)]
    [InlineData("https://gitee.com/railgunhamster/HarnessPortable.git", UpdateSourceKind.Gitee)]
    [InlineData("https://example.test/updates", UpdateSourceKind.Web)]
    [InlineData(@"\\server-home\public\Software\HarnessPortable-Releases", UpdateSourceKind.File)]
    [InlineData(@"C:\Updates", UpdateSourceKind.File)]
    public void Classify_PicksSourceKind(string url, UpdateSourceKind expected)
    {
        Assert.Equal(expected, UpdateSourceFactory.Classify(url));
    }
}
