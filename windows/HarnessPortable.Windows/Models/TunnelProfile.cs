namespace HarnessPortable.Windows.Models;

/// <summary>
/// A tunnel profile is the equivalent of
///   ssh -N -L localPort:remoteHost:remotePort user@sshHost:sshPort
/// </summary>
public sealed record TunnelProfile
{
    /// <summary>On connect, locate the dsh web launch-token URL via NSSM on the server (default).</summary>
    public const string AuthModeNssm = "nssm";

    /// <summary>
    /// Use the web login input stored in the credential store (token URL or
    /// the token itself); when it is empty, paste the token URL into the 401
    /// overlay instead.
    /// </summary>
    public const string AuthModeManual = "manual";

    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = "";
    public string SshHost { get; init; } = "";
    public int SshPort { get; init; } = 22;
    public string User { get; init; } = "";
    public string RemoteHost { get; init; } = "127.0.0.1";
    public int RemotePort { get; init; } = 3080;
    public int LocalPort { get; init; } = 3080;
    public string AuthMode { get; init; } = AuthModeNssm;

    /// <summary>
    /// Optional OpenSSH private key path. Empty: try ~/.ssh/id_ed25519,
    /// id_ecdsa, id_rsa. Key login does not require a saved password.
    /// </summary>
    public string IdentityFile { get; init; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? SshHost : Name;

    public string Summary => $"{User}@{SshHost}:{SshPort} → {RemoteHost}:{RemotePort}";

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(SshHost) &&
        !string.IsNullOrWhiteSpace(User) &&
        !string.IsNullOrWhiteSpace(RemoteHost) &&
        SshPort is >= 1 and <= 65535 &&
        RemotePort is >= 1 and <= 65535 &&
        LocalPort is >= 1 and <= 65535;
}
