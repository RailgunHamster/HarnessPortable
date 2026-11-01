using System.Text.Json;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class UpdateSelectionTests
{
    [Theory]
    [InlineData("")][InlineData("home")][InlineData("gitee")][InlineData("nonsense")]
    public void FreshAndRetiredSelectionsUseCnb(string selected)
    {
        var settings=JsonSerializer.Deserialize<AppSettings>("""{"UpdateSourceSelected":"SELECTED","UpdateHomeUrl":"\\\\old\\share","UpdateGiteeUrl":"https://gitee.com/example/repo","CloseBehavior":"tray"}""".Replace("SELECTED",selected))!;
        var selection=UpdateSelection.From(settings);
        Assert.Equal(UpdateSourceSlot.Cnb,selection.Slot);Assert.Equal(UpdateSourceFactory.CnbRepoUrl,selection.Url());
        selection.Store(settings);var encoded=JsonSerializer.Serialize(settings);
        Assert.DoesNotContain("UpdateHomeUrl",encoded);Assert.DoesNotContain("UpdateGiteeUrl",encoded);Assert.Equal("tray",settings.CloseBehavior);
    }
    [Fact]
    public void MigrationPreservesCustomGithubAndLaterExplicitChoices()
    {
        var settings=new AppSettings{UpdateSourceSelected="github",UpdateGitHubUrl=UpdateSelection.GitHub};
        var initial=UpdateSelection.From(settings);Assert.Equal(UpdateSourceSlot.Cnb,initial.Slot);initial.Store(settings);
        settings.UpdateSourceSelected="github";Assert.Equal(UpdateSourceSlot.GitHub,UpdateSelection.From(settings).Slot);
        Assert.Equal(UpdateSourceSlot.GitHub,UpdateSelection.From(new(){UpdateSourceSelected="github",UpdateGitHubUrl="https://github.com/example/custom"}).Slot);
    }
    [Fact]
    public void SwitchingKeepsIndependentDrafts()
    {
        var selection=UpdateSelection.MergeTyped(new(),"cnb","https://cnb.cool/example/custom","github");
        selection=UpdateSelection.MergeTyped(selection,"github","https://github.com/example/custom","cnb");
        Assert.Equal("https://cnb.cool/example/custom",selection.Url());Assert.Equal("https://github.com/example/custom",selection.GitHubUrl);
    }
    [Fact]
    public void LoadingOldSettingsPersistsMigrationWithoutChangingUnrelatedPreferences()
    {
        var file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try{
            File.WriteAllText(file,"""{"UpdateSourceSelected":"home","UpdateHomeUrl":"\\\\unreachable\\share","Appearance":"dark","ConnectionOrder":["one"]}""");
            var store=new AppSettingsStore(file);var settings=store.Load();
            Assert.Equal("cnb",settings.UpdateSourceSelected);Assert.Equal("dark",settings.Appearance);Assert.Equal(["one"],settings.ConnectionOrder);
            Assert.DoesNotContain("UpdateHomeUrl",File.ReadAllText(file));
            settings.UpdateSourceSelected="github";store.Save(settings);Assert.Equal("github",store.Load().UpdateSourceSelected);
        }finally{File.Delete(file);}
    }
    [Theory]
    [InlineData("https://cnb.cool/group/repo",true)]
    [InlineData("https://cnb.cool/group/subgroup/repo.git",true)]
    [InlineData("https://cnb.cool",false)]
    [InlineData("https://cnb.cool/group/-/git/raw/main/file",false)]
    [InlineData("https://cnb.cool.evil.example/group/repo",false)]
    [InlineData("https://secret@cnb.cool/group/repo",false)]
    [InlineData("http://cnb.cool/group/repo",false)]
    [InlineData("https://gitee.com/group/repo",false)]
    [InlineData(@"\\server\share",false)]
    public void CnbSlotRejectsRetiredAndMalformedAddresses(string value,bool valid)=>Assert.Equal(valid,UpdateSourceFactory.IsValid(value,cnbOnly:true));
}
