using System.IO;
using HarnessPortable.Windows.Models;
using Renci.SshNet;

namespace HarnessPortable.Windows.Services;

/// <summary>Read the Linux peer's current dsh web launch URL without changing its services.</summary>
public static class SystemdAuthUrlFetcher
{
    public static string BuildCommand(TunnelProfile profile)
    {
        using var stream = typeof(SystemdAuthUrlFetcher).Assembly
            .GetManifestResourceStream("systemd-auth.sh")
            ?? throw new InvalidOperationException("Missing systemd-auth.sh resource.");
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd().Replace("\r\n", "\n");
        var unit = string.IsNullOrWhiteSpace(profile.SystemdUnit) ? "dsh-web.service" : profile.SystemdUnit.Trim();
        return "sh -c " + ShellQuote(script) + " sh " + ShellQuote(unit) + " " +
            ShellQuote(profile.SystemdScope) + " " + profile.RemotePort + " " +
            ShellQuote(profile.SystemdLogPath.Trim());
    }

    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

    public static async Task<string?> TryFetchAsync(
        SshClient client, TunnelProfile profile, CancellationToken token)
    {
        try
        {
            using var command = client.CreateCommand(BuildCommand(profile));
            command.CommandTimeout = TimeSpan.FromSeconds(10);
            await command.ExecuteAsync(token).ConfigureAwait(false);
            if (token.IsCancellationRequested || command.ExitStatus != 0)
            {
                FlickerLog.Log("webview-auth", "systemd fetch exit=" + command.ExitStatus);
                return null;
            }
            var url = NssmAuthUrlFetcher.ExtractUrl(command.Result);
            return NssmAuthUrlFetcher.RewriteToLocal(url, profile.LocalPort, profile.RemotePort) != null
                ? url : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Do not log commands, output or URLs: the URL contains a credential.
            FlickerLog.Log("webview-auth", "systemd fetch failed (" + ex.GetType().Name + ")");
            return null;
        }
    }
}
