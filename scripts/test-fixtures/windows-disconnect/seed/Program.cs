using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

var root = Path.GetFullPath(args[0]);
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new InvalidOperationException("Use a new empty fixture directory.");
Directory.CreateDirectory(root);
var profiles = new ProfileStore(Path.Combine(root, "profiles.json"));
profiles.Save(new() { Tunnels = [
    new() { Id="disconnect-a", Name="Disconnect test A", SshHost="127.0.0.1", SshPort=22241, User="fixture", RemotePort=45891, LocalPort=48191, AuthMode="manual", IdentityFile=Path.Combine(root,"no-fixture-key") },
    new() { Id="disconnect-b", Name="Disconnect test B", SshHost="127.0.0.1", SshPort=22241, User="fixture", RemotePort=45891, LocalPort=48192, AuthMode="manual", IdentityFile=Path.Combine(root,"no-fixture-key") },
], Directs = ["http://127.0.0.1:45891/"] });
var secrets = new SecureStore(Path.Combine(root,"secrets.json"));
secrets.SetPassword("disconnect-a","fixture-password");
secrets.SetPassword("disconnect-b","fixture-password");
new AppSettingsStore(Path.Combine(root,"settings.json")).Save(new() { RestoreLastLayoutOnStartup=true, StartupReconnectPreference=true, CloseBehavior="exit" });
new WorkspaceSnapshotStore(Path.Combine(root,"workspace.json")).Save(new() { Name="Disconnect regression", Root=new() { Tabs=[
    new() {Kind="tunnel",ProfileId="disconnect-a",Label="A1",Selected=true,Active=true},
    new() {Kind="tunnel",ProfileId="disconnect-a",Label="A2"},
    new() {Kind="tunnel",ProfileId="disconnect-b",Label="B"},
    new() {Kind="direct",Url="http://127.0.0.1:45891/",Label="Direct"},
] } });
Console.WriteLine("Isolated fixture configuration prepared: " + root);
