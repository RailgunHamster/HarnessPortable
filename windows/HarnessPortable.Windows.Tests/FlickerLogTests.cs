using System.Text;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

/// <summary>
/// The flicker log is process-wide static state, so these tests run without
/// parallelization and point it at a private directory.
/// </summary>
[CollectionDefinition("FlickerLog", DisableParallelization = true)]
public sealed class FlickerLogCollection;

[Collection("FlickerLog")]
public sealed class FlickerLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());

    public FlickerLogTests()
    {
        FlickerLog.ConfigureForTests(_dir, TimeSpan.FromMilliseconds(20));
        FlickerLog.ResetForTests();
    }

    [Fact]
    public void FormatBatch_FoldsRunsOfIdenticalEvents()
    {
        var lines = new List<string>
        {
            "10:00:00.001 #1 [winevent-focus] A class=Chrome pid=1",
            "10:00:00.002 #2 [winevent-focus] A class=Chrome pid=1",
            "10:00:00.003 #3 [winevent-focus] A class=Chrome pid=1",
            "10:00:00.004 #4 [window] Activated",
        };

        var text = FlickerLog.FormatBatch(FlickerLog.EntriesFromLines(lines), suppressed: 0);
        var outLines = Split(text);

        Assert.Equal(2, outLines.Length);
        Assert.EndsWith("(x3)", outLines[0], StringComparison.Ordinal);
        Assert.Contains("[window] Activated", outLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void FormatBatch_KeepsDistinctEventsAndReportsSuppressed()
    {
        var lines = new List<string>
        {
            "10:00:00.001 #1 [window] Activated",
            "10:00:00.002 #2 [window] Deactivated",
            "10:00:00.003 #3 [window] Activated",
        };

        var text = FlickerLog.FormatBatch(FlickerLog.EntriesFromLines(lines), suppressed: 1234);
        var outLines = Split(text);

        Assert.Equal(4, outLines.Length);
        Assert.Contains("1234 entries suppressed", outLines[0], StringComparison.Ordinal);
        Assert.DoesNotContain(" (x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendBounded_RotatesAndNeverLetsAGenerationExceedTheCap()
    {
        var chunk = new string('x', 128 * 1024) + Environment.NewLine;

        for (var i = 0; i < 60; i++)
        {
            FlickerLog.AppendBounded(chunk);
        }

        Assert.True(File.Exists(FlickerLog.LogPath), "live log should exist");
        Assert.True(
            new FileInfo(FlickerLog.LogPath).Length <= FlickerLog.MaxFileBytes,
            "live generation must stay within the cap");

        // Two generations at most: the live file plus one rotated backup.
        Assert.True(
            new FileInfo(FlickerLog.BackupLogPath).Length <= FlickerLog.MaxFileBytes * 2,
            "rotated generation must stay within the cap");
    }

    [Fact]
    public void AppendBounded_ShrinksALegacyOversizedFile()
    {
        // 9.9 MB, the shape the old tail-trim left behind (146 MB was observed).
        SeedLegacyLog(250000);
        var before = new FileInfo(FlickerLog.LogPath).Length;
        Assert.True(before > 8 * 1024 * 1024, $"seed should be oversized, was {before}");

        FlickerLog.AppendBounded("10:00:01.000 #999999 [boot] after rewrite\n");

        // The oversized original is retired into the backup, so after this
        // call neither generation can hold more than the cap allows.
        Assert.True(File.Exists(FlickerLog.LogPath), "a live log must exist after the legacy rewrite");
        Assert.True(File.Exists(FlickerLog.BackupLogPath), "the old tail should be kept as the backup");
        Assert.False(File.Exists(FlickerLog.LogPath + ".trim"), "staging file must not be left behind");

        Assert.True(
            new FileInfo(FlickerLog.BackupLogPath).Length <= FlickerLog.MaxFileBytes * 2,
            "the retired tail must be bounded");
        Assert.True(
            new FileInfo(FlickerLog.LogPath).Length <= FlickerLog.MaxFileBytes * 2,
            "the live generation must be bounded");

        var content = File.ReadAllText(FlickerLog.LogPath);
        Assert.Contains("after rewrite", content, StringComparison.Ordinal);

        var backup = File.ReadAllText(FlickerLog.BackupLogPath);
        Assert.Contains("#249999 [window] Activated", backup, StringComparison.Ordinal);
        Assert.DoesNotContain("#1000 [window] Activated", backup, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteTail_KeepsOnlyTheNewestChunk()
    {
        SeedLegacyLog(250000);
        var before = new FileInfo(FlickerLog.LogPath).Length;

        FlickerLog.RewriteTail(FlickerLog.LogPath, FlickerLog.BackupLogPath, FlickerLog.MaxFileBytes);

        Assert.False(File.Exists(FlickerLog.LogPath), "the oversized original is retired");
        Assert.False(File.Exists(FlickerLog.BackupLogPath + ".trim"), "staging file must not be left behind");

        var after = new FileInfo(FlickerLog.BackupLogPath).Length;
        Assert.True(after <= FlickerLog.MaxFileBytes, $"tail must land within the requested size, was {after}");
        Assert.True(after < before / 4, $"tail must drop the old bulk, {before} -> {after}");
    }

    [Fact]
    public void DrainOnce_WritesQueuedEntriesAndClearsTheQueue()
    {
        for (var i = 0; i < 25; i++)
        {
            FlickerLog.EnqueueForTests("winevent-focus", "window=" + i);
        }

        FlickerLog.DrainOnceForTests();

        var content = File.ReadAllText(FlickerLog.LogPath);
        Assert.Contains("[winevent-focus] window=0", content, StringComparison.Ordinal);
        Assert.Contains("[winevent-focus] window=24", content, StringComparison.Ordinal);

        // A second drain has nothing left to write.
        var sizeAfterFirst = new FileInfo(FlickerLog.LogPath).Length;
        FlickerLog.DrainOnceForTests();
        Assert.Equal(sizeAfterFirst, new FileInfo(FlickerLog.LogPath).Length);
    }

    private void SeedLegacyLog(int lines)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < lines; i++)
        {
            sb.Append("10:00:00.000 #").Append(i).Append(" [window] Activated").Append('\n');
        }

        File.WriteAllText(FlickerLog.LogPath, sb.ToString());
    }

    private static string[] Split(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
