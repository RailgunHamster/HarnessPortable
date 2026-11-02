using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HarnessPortable.Windows.Services;

/// <summary>Extracts and compiles the embedded helper outside the installation being replaced.</summary>
internal static class UpdateTransitionSplash
{
    private static readonly Lazy<Task<string?>> Prepared = new(() => PrepareSafelyAsync());
    internal static string CacheDirectory => Path.Combine(Path.GetTempPath(), "HarnessPortable", "update-splash");

    internal static void WarmUp() => _ = Prepared.Value;

    private static async Task<string?> PrepareSafelyAsync()
    {
        try { return await Task.Run(() => EnsureBinaryAsync(CacheDirectory)).ConfigureAwait(false); }
        catch (Exception error)
        {
            FlickerLog.Log("update-splash", "prepare failed: " + error.GetType().Name);
            return null;
        }
    }

    internal static async Task<string> EnsureBinaryAsync(string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var source = ReadResource("UpdateSplash.Source");
        var hash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant()[..16];
        var executable = Path.Combine(directory, $"HarnessPortable.UpdateSplash-{hash}.exe");
        if (File.Exists(executable)) return executable;
        var unique = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        var sourcePath = unique + ".cs";
        var temporary = unique + ".exe";
        await File.WriteAllBytesAsync(sourcePath, source).ConfigureAwait(false);
        try
        {
            var framework = Environment.Is64BitOperatingSystem ? "Framework64" : "Framework";
            var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Microsoft.NET", framework, "v4.0.30319", "csc.exe");
            var start = new ProcessStartInfo(compiler)
            { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "/nologo", "/optimize+", "/codepage:65001", "/target:winexe", "/out:" + temporary,
                "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll", sourcePath })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Splash compiler did not start.");
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            if (process.ExitCode != 0 || !File.Exists(temporary)) throw new IOException("Splash compilation failed.");
            try { File.Move(temporary, executable); }
            catch (IOException) when (File.Exists(executable)) { } // Another instance completed the same build.
            return executable;
        }
        finally
        {
            TryDelete(sourcePath);
            TryDelete(temporary);
        }
    }

    internal static async Task<IUpdateSplashSession?> LaunchAsync(string? fromVersion, string? toVersion,
        string rootDirectory, bool dark, CancellationToken token)
    {
        SplashSession? session = null;
        try
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new IOException("The update installation root is unavailable.");
            var executable = await Prepared.Value.WaitAsync(token).ConfigureAwait(false);
            if (executable is null) return null;
            var directory = Path.Combine(CacheDirectory, "sessions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var logo = Path.Combine(directory, "logo.png");
            await File.WriteAllBytesAsync(logo, ReadResource("UpdateSplash.Logo"), token).ConfigureAwait(false);
            var start = CreateStartInfo(executable, directory, rootDirectory, fromVersion, toVersion, dark, Environment.ProcessId);
            if (Environment.GetCommandLineArgs().Contains("--automation-offscreen")) start.ArgumentList.Add("--offscreen");
            var process = Process.Start(start) ?? throw new IOException("Splash did not start.");
            session = new SplashSession(process, directory);
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(directory, "ready")))
            {
                token.ThrowIfCancellationRequested();
                if (process.HasExited || timer.Elapsed > TimeSpan.FromSeconds(5)) throw new IOException("Splash did not render.");
                await Task.Delay(25, token).ConfigureAwait(false);
            }
            FlickerLog.Log("update-splash", "transition window ready");
            return session;
        }
        catch (OperationCanceledException) { session?.Dispose(); throw; }
        catch (Exception error)
        {
            session?.Dispose();
            FlickerLog.Log("update-splash", "using native progress: " + error.GetType().Name);
            return null;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, string sessionDirectory, string rootDirectory,
        string? fromVersion, string? toVersion, bool dark, int waitPid)
    {
        var start = new ProcessStartInfo(executable)
        { WorkingDirectory = sessionDirectory, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--title", "Harness Portable", "--theme", dark ? "Dark" : "Light",
            "--main-exe", "HarnessPortable.exe", "--wait-pid", waitPid.ToString(), "--root-dir", Path.GetFullPath(rootDirectory),
            "--session-dir", sessionDirectory, "--logo", Path.Combine(sessionDirectory, "logo.png"),
            "--from-version", fromVersion ?? "", "--to-version", toVersion ?? "" }) start.ArgumentList.Add(argument);
        return start;
    }

    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(UpdateTransitionSplash).Assembly.GetManifestResourceStream(name)
            ?? throw new IOException("Embedded splash resource is missing.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    private sealed class SplashSession(Process process, string directory) : IUpdateSplashSession
    {
        private bool _detached;
        public void Detach() => _detached = true;
        public void Dispose()
        {
            if (!_detached)
            {
                try { File.WriteAllText(Path.Combine(directory, "cancel"), ""); } catch { }
                try { if (!process.HasExited && !process.WaitForExit(500)) process.Kill(); } catch { }
            }
            process.Dispose();
        }
    }
}
