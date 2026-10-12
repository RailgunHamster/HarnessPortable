using System.Diagnostics;
using System.IO;
using System.Text;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Auto-login for **direct** tabs pointing at a `dsh web` that runs on this
/// very Windows machine under NSSM. The tunnel NSSM mode reaches the log over
/// SSH, which does not exist for a direct URL — but the same discovery script
/// works locally: it finds the NSSM-hosted dsh service, reads its stdout log,
/// and verifies the newest printed URL against the live server (expecting the
/// 303 token-redirect). The verified token query is then grafted onto the
/// direct destination, whose host/path stay untouched. Any failure returns
/// null and the caller opens the plain URL, where the 401 paste overlay takes
/// over — the same fallback chain as every other auth mode.
/// </summary>
public static class LocalNssmAuth
{
    public const string ModeNone = "none";
    public const string ModeNssm = "nssm";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs the NSSM discovery script on this machine and, on success, grafts
    /// the verified token query onto <paramref name="destination"/>. Returns
    /// null on any failure/timeout — never throws, token retrieval must not
    /// break the direct tab's navigation.
    /// </summary>
    public static async Task<string?> TryBuildAuthTargetAsync(
        string destination, CancellationToken token)
    {
        string? authUrl;

        if (!Uri.TryCreate(destination, UriKind.Absolute, out var target)
            || !LocalServiceIdentity.IsLocalHost(target.Host)) return null;

        try
        {
            authUrl = await RunDiscoveryScriptAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FlickerLog.Log("webview-auth", "local nssm fetch failed: " + ex.Message);
            return null;
        }

        if (authUrl is null)
        {
            return null;
        }

        return GraftToken(destination, authUrl);
    }

    /// <summary>
    /// PowerShell run locally; identical to the tunnel mode's server-side
    /// script. Exit 0 with the verified URL on stdout, anything else = miss.
    /// </summary>
    private static async Task<string?> RunDiscoveryScriptAsync(CancellationToken token)
    {
        var encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(NssmAuthUrlFetcher.BuildScript()));

        // Full path, not "powershell": the app process is often relaunched by
        // the Velopack updater, whose PATH may differ from an interactive
        // shell's, and a resolved-but-wrong powershell surfaces as a bare
        // exit=1 with no other clue.
        var powershell = Path.Combine(
            Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        if (!process.Start())
        {
            FlickerLog.Log("webview-auth", "local nssm fetch: process not started");
            return null;
        }

        using (token.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }
        }))
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var errorTask = process.StandardError.ReadToEndAsync(token);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(CommandTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already gone.
                }

                FlickerLog.Log("webview-auth", "local nssm fetch timeout");
                return null;
            }

            if (process.ExitCode != 0)
            {
                // The script's own misses exit 3/4; anything else (typically 1)
                // is a terminating error whose stderr names the culprit —
                // e.g. a stripped PSModulePath breaking Get-CimInstance module
                // autoload in updater-relaunched processes.
                var error = (await errorTask.ConfigureAwait(false))
                    .Replace("\r", "").Replace("\n", " ").Trim();
                if (error.Length > 300)
                {
                    error = error[..300];
                }

                FlickerLog.Log("webview-auth", $"local nssm fetch exit={process.ExitCode}" +
                    (error.Length > 0 ? " stderr=" + error : ""));
                return null;
            }

            var url = NssmAuthUrlFetcher.ExtractUrl(await outputTask.ConfigureAwait(false));

            if (url is null)
            {
                FlickerLog.Log("webview-auth", "local nssm fetch produced no URL");
            }

            return url;
        }
    }

    /// <summary>
    /// Grafts the token query of a verified <paramref name="authUrl"/> onto
    /// <paramref name="destination"/>: the destination's scheme, host, port,
    /// path and any non-token parameters are preserved; a stale token item is
    /// replaced. Returns null when the auth URL carries no token, or when the
    /// two ports disagree — the log then belongs to a different instance and
    /// grafting would navigate this tab into someone else's session.
    /// </summary>
    public static string? GraftToken(string destination, string authUrl)
    {
        if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(authUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(authUrl.Trim(), UriKind.Absolute, out var auth) ||
            (auth.Scheme != Uri.UriSchemeHttp && auth.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var query = auth.Query.TrimStart('?');
        if (query.Length == 0 || !query.Contains("token=", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Uri.TryCreate(destination.Trim(), UriKind.Absolute, out var dest) ||
            (dest.Scheme != Uri.UriSchemeHttp && dest.Scheme != Uri.UriSchemeHttps) ||
            dest.Port != auth.Port || !LocalServiceIdentity.IsLocalHost(dest.Host)
            || !LocalServiceIdentity.IsLocalHost(auth.Host))
        {
            return null;
        }

        // Keep the destination's own parameters, minus any stale token: the
        // verified query carries the full set dsh printed at startup.
        var kept = dest.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("token=", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var merged = string.Join("&", kept.Concat([query]));
        var builder = new UriBuilder(dest) { Query = merged };
        return builder.Uri.ToString();
    }
}
