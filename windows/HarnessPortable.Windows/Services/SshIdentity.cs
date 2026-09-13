using System.IO;
using System.Text.RegularExpressions;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Resolves OpenSSH private-key paths and classifies SSH authentication
/// failures. Desktop key login uses a profile-specific file when set,
/// otherwise the first existing default identity under ~/.ssh.
/// </summary>
public static class SshIdentity
{
    public static readonly string[] DefaultFileNames = ["id_ed25519", "id_ecdsa", "id_rsa"];

    public static string SshDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh");

    public static string ExpandPath(string? path, string? sshDirectory = null)
    {
        var trimmed = path?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return "";
        }

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (expanded == "~")
        {
            expanded = home;
        }
        else if (expanded.StartsWith("~/", StringComparison.Ordinal) ||
                 expanded.StartsWith("~\\", StringComparison.Ordinal))
        {
            expanded = Path.Combine(home, expanded[2..]);
        }

        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.Combine(sshDirectory ?? SshDirectory, expanded);
        }

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }

    /// <summary>
    /// Files SSH.NET should offer. A configured identity is exclusive
    /// (IdentitiesOnly): a missing file is still returned so the caller can
    /// report it. With no identity, only default files that exist are listed.
    /// </summary>
    public static IReadOnlyList<string> CandidateFiles(string? identityFile, string? sshDirectory = null)
    {
        var specified = identityFile?.Trim() ?? "";
        if (specified.Length > 0)
        {
            var expanded = ExpandPath(specified, sshDirectory);
            return expanded.Length == 0 ? [] : [expanded];
        }

        var directory = sshDirectory ?? SshDirectory;
        var found = new List<string>(DefaultFileNames.Length);
        foreach (var name in DefaultFileNames)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                found.Add(candidate);
            }
        }

        return found;
    }

    public static bool HasUsableKey(string? identityFile, string? sshDirectory = null)
    {
        var specified = identityFile?.Trim() ?? "";
        if (specified.Length > 0)
        {
            return File.Exists(ExpandPath(specified, sshDirectory));
        }

        return CandidateFiles(null, sshDirectory).Count > 0;
    }

    public static string MissingCredentialsMessage(string? identityFile)
    {
        var specified = identityFile?.Trim() ?? "";
        if (specified.Length > 0)
        {
            var path = ExpandPath(specified);
            return File.Exists(path)
                ? "无法读取私钥（可能需要口令）：" + path
                : "私钥文件不存在：" + path;
        }

        return "未保存密码，也没有可用的 SSH 私钥";
    }

    public static bool LooksLikeAuthenticationFailure(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return Regex.IsMatch(
            message,
            @"auth|password|permission denied|too many authentication|keyboard-interactive|publickey|private key|identity file|no such identity|私钥|认证|用户名|密码",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
