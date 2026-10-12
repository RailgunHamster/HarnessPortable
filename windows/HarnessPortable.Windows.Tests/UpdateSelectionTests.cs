using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

/// <summary>
/// The update feed is chosen from a pair of slots; these lock the defaults,
/// the picker's memory of both addresses, and the one-time fold of the
/// single-URL setting that predates it.
/// </summary>
public sealed class UpdateSelectionTests
{
    [Fact]
    public void FreshSettings_DefaultsToHomeShare_AndFillsBothSlots()
    {
        var selection = UpdateSelection.From(new AppSettings());

        Assert.Equal(UpdateSourceSlot.Home, selection.Slot);
        Assert.Equal(UpdateSourceFactory.DefaultServerUrl, selection.Url(UpdateSourceSlot.Home));
        Assert.Equal(UpdateSourceFactory.GitHubRepoUrl, selection.Url(UpdateSourceSlot.GitHub));
        Assert.Equal(UpdateSourceFactory.DefaultServerUrl, selection.Url());
    }

    [Fact]
    public void SelectedSlot_DecidesWhichAddressIsUsed()
    {
        var selection = new UpdateSelection
        {
            Slot = UpdateSourceSlot.GitHub,
            HomeUrl = @"\\server-home\public\Software\HarnessPortable-Releases",
            GitHubUrl = "https://github.com/RailgunHamster/HarnessPortable",
        };

        Assert.Equal("https://github.com/RailgunHamster/HarnessPortable", selection.Url());
        Assert.Equal(@"\\server-home\public\Software\HarnessPortable-Releases",
            selection.Url(UpdateSourceSlot.Home));
    }

    [Theory]
    [InlineData(@"\\server-home\public\Software\HarnessPortable-Releases", UpdateSourceSlot.Home)]
    [InlineData(@"D:\releases", UpdateSourceSlot.Home)]
    [InlineData("https://github.com/RailgunHamster/HarnessPortable", UpdateSourceSlot.GitHub)]
    public void LegacySingleUrl_LandsInTheSlotItNames(string legacy, UpdateSourceSlot expected)
    {
        var selection = UpdateSelection.From(new AppSettings { UpdateServerUrl = legacy });

        Assert.Equal(expected, selection.Slot);
        Assert.Equal(legacy, selection.Url(expected));
    }

    [Fact]
    public void LegacyHttpDirectory_StaysInHomeSlot()
    {
        // An http mirror of the share is a home-slot address, not GitHub.
        var selection = UpdateSelection.From(
            new AppSettings { UpdateServerUrl = "http://server-home/releases" });

        Assert.Equal(UpdateSourceSlot.Home, selection.Slot);
        Assert.Equal("http://server-home/releases", selection.Url(UpdateSourceSlot.Home));
    }

    [Fact]
    public void SplitSettings_WinOverTheRetiredSingleUrl()
    {
        // Once both slots exist the retired field is only a mirror for older
        // builds; it must not drag the selection back.
        var selection = UpdateSelection.From(new AppSettings
        {
            UpdateServerUrl = @"\\server-home\public\Software\HarnessPortable-Releases",
            UpdateHomeUrl = @"\\other\releases",
            UpdateGitHubUrl = "https://github.com/RailgunHamster/HarnessPortable",
            UpdateSourceSelected = "github",
        });

        Assert.Equal(UpdateSourceSlot.GitHub, selection.Slot);
        Assert.Equal(@"\\other\releases", selection.Url(UpdateSourceSlot.Home));
    }

    [Fact]
    public void RoundTrip_KeepsBothAddresses()
    {
        var settings = new AppSettings();
        new UpdateSelection
        {
            Slot = UpdateSourceSlot.GitHub,
            HomeUrl = @"\\server-home\public\Software\HarnessPortable-Releases",
            GitHubUrl = "https://github.com/RailgunHamster/HarnessPortable",
        }.Store(settings);

        var back = UpdateSelection.From(settings);

        Assert.Equal(UpdateSourceSlot.GitHub, back.Slot);
        Assert.Equal(@"\\server-home\public\Software\HarnessPortable-Releases",
            back.Url(UpdateSourceSlot.Home));
        Assert.Equal("https://github.com/RailgunHamster/HarnessPortable",
            back.Url(UpdateSourceSlot.GitHub));
    }

    [Fact]
    public void UnknownSelectedValue_FallsBackToHome()
    {
        var selection = UpdateSelection.From(new AppSettings { UpdateSourceSelected = "nonsense" });

        Assert.Equal(UpdateSourceSlot.Home, selection.Slot);
    }

    /// <summary>
    /// Regression for 2.6.19–2.6.23: switching the picker read the address
    /// box AFTER the switch, so the leaving slot's address was written into
    /// the slot being entered and the box appeared to never change.
    /// </summary>
    [Fact]
    public void MergeTyped_FilesAddressWithSlotBeingLeft()
    {
        var stored = new UpdateSelection
        {
            Slot = UpdateSourceSlot.Home,
            HomeUrl = @"\\server-home\public\Software\HarnessPortable-Releases",
            GitHubUrl = "https://github.com/RailgunHamster/HarnessPortable",
        };

        // Leaving "home" with the UNC still in the box → select github, keep UNC in home.
        var switched = UpdateSelection.MergeTyped(
            stored, "home", @"\\server-home\public\Software\HarnessPortable-Releases");
        Assert.Equal(UpdateSourceSlot.GitHub, switched.Slot);
        Assert.Equal(@"\\server-home\public\Software\HarnessPortable-Releases", switched.HomeUrl);
        Assert.Equal("https://github.com/RailgunHamster/HarnessPortable", switched.GitHubUrl);
        Assert.Equal("https://github.com/RailgunHamster/HarnessPortable", switched.Url());

        // Leaving "github" with a custom repo in the box → select home, keep repo in github.
        var back = UpdateSelection.MergeTyped(switched, "github", "https://github.com/someone/Fork");
        Assert.Equal(UpdateSourceSlot.Home, back.Slot);
        Assert.Equal("https://github.com/someone/Fork", back.GitHubUrl);
        Assert.Equal(@"\\server-home\public\Software\HarnessPortable-Releases", back.HomeUrl);
        Assert.Equal(@"\\server-home\public\Software\HarnessPortable-Releases", back.Url());
    }
}
