namespace HarnessPortable.Windows.Models;

/// <summary>
/// A tunnel profile is the equivalent of
///   ssh -N -L localPort:remoteHost:remotePort user@sshHost:sshPort
/// </summary>
public sealed record TunnelProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = "";
    public string SshHost { get; init; } = "";
    public int SshPort { get; init; } = 22;
    public string User { get; init; } = "";
    public string RemoteHost { get; init; } = "127.0.0.1";
    public int RemotePort { get; init; } = 3080;
    public int LocalPort { get; init; } = 3080;

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
