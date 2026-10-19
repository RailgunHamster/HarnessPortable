# Windows disconnect regression

This opt-in test uses a loopback SSH server and four real WebView2 controls. It checks repeated disconnect/reconnect, two tabs sharing one tunnel, an unrelated tunnel and direct page, port release, cancelled delayed callbacks, stop-all, beforeunload, and a script alert already open at disconnect. It never uses the installed app's configuration.

Requirements: Windows, .NET 10 SDK, WebView2 Runtime, Python with `paramiko` installed. Reserve loopback ports 22241, 45891, 48191 and 48192 for this fixture.

Start `python scripts/test-fixtures/windows-disconnect/ssh-server.py` in a separate terminal. In PowerShell from the repository root:

```powershell
$fixtureData = Join-Path $env:TEMP ('HarnessPortable-disconnect-' + [guid]::NewGuid())
dotnet run --project scripts/test-fixtures/windows-disconnect/seed/Seed.csproj -- $fixtureData
dotnet run --project scripts/test-fixtures/windows-disconnect/runner/Regression.csproj -- $fixtureData --beforeunload --alert
```

The seed refuses a nonempty directory. All credentials are fixture values. A successful run prints three cycle results and `PASS: stop all`. Stop the loopback server afterward. To exercise the ordinary page without script dialogs, omit the two flags.
