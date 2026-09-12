using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Obtains the current "dsh web" launch-token URL from the SSH peer when the
/// service there runs under NSSM. dsh prints its authenticated root URL to
/// stdout once at startup; NSSM's configured stdout redirection persists that
/// line to a log file, and <c>nssm get &#8249;service&#8250; AppStdout</c> names the file —
/// no path guessing, no registry access. The script below discovers the dsh
/// service, extracts the newest printed URL, and verifies it against the live
/// server (expecting the 303 token-redirect) so only a currently-valid URL is
/// ever returned. Failures return null and the caller falls back to the plain
/// host:port navigation (whose 401 page triggers the manual paste overlay).
/// </summary>
public static partial class NssmAuthUrlFetcher
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlPattern();

    /// <summary>
    /// PowerShell run on the server. Exit codes: 0 = verified URL on stdout,
    /// 3 = no matching service/log line, 4 = token rejected (stale).
    /// Delivered via -EncodedCommand to sidestep cmd/PowerShell quoting.
    /// </summary>
    public static string BuildScript() => """
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
foreach ($c in (Get-CimInstance Win32_Service | Where-Object { $_.State -eq "Running" -and $_.PathName -match "nssm\.exe" })) {
  $exe = [regex]::Match($c.PathName, "^""?(.+?nssm\.exe)").Groups[1].Value
  if (-not $exe) { continue }
  $params = & $exe get $c.Name AppParameters 2>$null
  if ("$params" -notmatch "dsh") { continue }
  $log = & $exe get $c.Name AppStdout 2>$null
  if (-not $log) { continue }
  $m = Select-String -Path "$log" -Pattern "dsh web: (http\S+)" | Select-Object -Last 1
  if (-not $m) { continue }
  $u = $m.Matches[0].Groups[1].Value
  $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time 3 $u
  if ("$code" -eq "303") { Write-Output $u; exit 0 }
  exit 4
}
exit 3
""";

    /// <summary>
    /// Runs the discovery script over an established SSH connection. Returns
    /// the verified absolute URL, or null on any failure/timeout. Never
    /// throws: token retrieval must not take the tunnel down.
    /// </summary>
    public static async Task<string?> TryFetchAsync(
        SshClient client, int expectedRemotePort, CancellationToken token)
    {
        string? url = null;

        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildScript()));
            using var cmd = client.CreateCommand("powershell -NoProfile -EncodedCommand " + encoded);

            var output = await Task.Run(() =>
            {
                var ar = cmd.BeginExecute(null, null);
                if (!ar.AsyncWaitHandle.WaitOne(CommandTimeout))
                {
                    try
                    {
                        cmd.CancelAsync();
                    }
                    catch
                    {
                        // The channel dies with the session either way.
                    }

                    return null;
                }

                return cmd.EndExecute(ar);
            }, token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                return null;
            }

            if (cmd.ExitStatus != 0)
            {
                FlickerLog.Log("webview-auth", "nssm fetch exit=" + cmd.ExitStatus);
                return null;
            }

            url = ExtractUrl(output ?? "");
        }
        catch (Exception ex)
        {
            FlickerLog.Log("webview-auth", "nssm fetch failed: " + ex.Message);
            return null;
        }

        if (url is null)
        {
            FlickerLog.Log("webview-auth", "nssm fetch produced no URL");
            return null;
        }

        // Guard against picking up a stale instance listening elsewhere.
        if (expectedRemotePort > 0 && Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
            parsed.Port != expectedRemotePort)
        {
            FlickerLog.Log("webview-auth", "nssm fetch port mismatch, ignoring");
            return null;
        }

        return url;
    }

    /// <summary>Last absolute http(s) URL appearing in the output, if any.</summary>
    public static string? ExtractUrl(string output)
    {
        var matches = UrlPattern().Matches(output);
        return matches.Count > 0 ? matches[^1].Value : null;
    }

    /// <summary>
    /// Rewrites an authenticated URL so the embedded browser opens it through
    /// the local forwarded port: keeps path + token query, replaces the
    /// authority with 127.0.0.1:localPort. Pass 0 as expectedRemotePort to
    /// skip the source-port sanity check.
    /// </summary>
    public static string? RewriteToLocal(string? authUrl, int localPort, int expectedRemotePort)
    {
        if (string.IsNullOrWhiteSpace(authUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(authUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        if (expectedRemotePort > 0 && uri.Port != expectedRemotePort)
        {
            return null;
        }

        return $"http://127.0.0.1:{localPort}{uri.PathAndQuery}";
    }
}
