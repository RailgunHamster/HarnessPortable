import XCTest
@testable import HarnessPortable

final class HarnessPortableTests: XCTestCase {
    func testProfileFixtureUsesCrossPlatformKeysAndDefaults() throws {
        let data = Data(#"""
{
          "Version": 1,
          "Tunnels": [{
            "Id": "one",
            "Name": "Home",
            "SshHost": "macair",
            "User": "RailgunHamster",
            "RemoteHost": "127.0.0.1",
            "RemotePort": 3080,
            "LocalPort": 3080
          }],
          "Directs": ["http://192.168.0.10:4096"]
        }
"""#.utf8)

        let config = try decodeJSON(ProfileConfig.self, from: data)
        XCTAssertEqual(config.tunnels.first?.sshPort, 22)
        XCTAssertEqual(config.tunnels.first?.displayName, "Home")
        XCTAssertEqual(config.directs, ["http://192.168.0.10:4096"])
    }

    func testURLNormalizationMatchesDesktopDefaults() {
        XCTAssertEqual(ProfileStore.normalizeURL("192.168.0.10"), "http://192.168.0.10:4096")
        XCTAssertEqual(ProfileStore.normalizeURL("host:3080"), "http://host:3080")
        XCTAssertEqual(ProfileStore.normalizeURL("https://example.test"), "https://example.test")
    }

    func testLayoutFixtureRestoresNestedSplitAndManagementTab() throws {
        let data = Data(#"""
{
          "name": "quad",
          "savedAt": "2026-01-01T00:00:00Z",
          "root": {
            "kind": "split",
            "orientation": "horizontal",
            "children": [
              { "kind": "pane", "tabs": [{ "kind": "management" }] },
              { "kind": "split", "orientation": "vertical", "children": [
                { "kind": "pane", "tabs": [{ "kind": "direct", "url": "http://127.0.0.1:4096" }] },
                { "kind": "pane", "tabs": [{ "kind": "tunnel", "profileID": "one", "label": "API" }] }
              ] }
            ]
          }
        }
"""#.utf8)

        let layout = try JSONDecoder().decode(WorkspaceLayout.self, from: data)
        XCTAssertEqual(layout.root.kind, .split)
        XCTAssertEqual(layout.root.children.count, 2)
        XCTAssertEqual(layout.root.children[1].children.count, 2)
        XCTAssertTrue(layout.root.children[0].tabs.contains { $0.kind == .management })
    }

    func testViewStateUrlsExtractReadsOnlyTheOwnedParameters() {
        let state = ViewStateUrls.extract(
            "http://127.0.0.1:3080/?token=secret&dsh_session=session-abc&dsh_sidebar=0&dsh_rightbar=320"
        )
        XCTAssertEqual(state?.sessionId, "session-abc")
        XCTAssertEqual(state?.sidebar, 0)
        XCTAssertEqual(state?.rightbar, 320)

        XCTAssertEqual(
            ViewStateUrls.extract("http://127.0.0.1:3080/s/session-abc?dsh_session=session-abc")?.sessionId,
            "session-abc"
        )
        XCTAssertNil(ViewStateUrls.extract("http://127.0.0.1:3080/?token=secret"))
        XCTAssertNil(ViewStateUrls.extract("http://127.0.0.1:3080/"))
        XCTAssertNil(ViewStateUrls.extract("about:blank"))
        XCTAssertNil(ViewStateUrls.extract(nil))
        XCTAssertNil(ViewStateUrls.extract(""))
    }

    func testViewStateUrlsExtractRejectsInvalidPixels() {
        let state = ViewStateUrls.extract(
            "http://127.0.0.1:3080/?dsh_session=session-abc&dsh_sidebar=nonsense&dsh_rightbar=-5"
        )
        XCTAssertEqual(state?.sessionId, "session-abc")
        XCTAssertNil(state?.sidebar)
        XCTAssertNil(state?.rightbar)
        XCTAssertNil(ViewStateUrls.extract("http://127.0.0.1:3080/?dsh_sidebar=nonsense&dsh_rightbar=-5"))
    }

    func testViewStateUrlsMergePreservesForeignParametersAndPath() {
        let merged = ViewStateUrls.merge(
            "http://127.0.0.1:3080/s/session-abc?token=secret#anchor",
            state: LayoutWebState(sessionId: "session-abc", sidebar: 0, rightbar: 320)
        )
        XCTAssertEqual(merged, "http://127.0.0.1:3080/s/session-abc?token=secret&dsh_session=session-abc&dsh_sidebar=0&dsh_rightbar=320#anchor")

        // Existing values are replaced in place, order otherwise preserved.
        let replaced = ViewStateUrls.merge(
            "http://127.0.0.1:3080/?dsh_session=session-old&dsh_sidebar=280&token=t",
            state: LayoutWebState(sessionId: "session-new", sidebar: 300, rightbar: 0)
        )
        XCTAssertEqual(replaced, "http://127.0.0.1:3080/?dsh_session=session-new&dsh_sidebar=300&token=t&dsh_rightbar=0")

        // Nil fields are left alone rather than cleared.
        let appended = ViewStateUrls.merge(
            "http://127.0.0.1:3080/?dsh_sidebar=280",
            state: LayoutWebState(sessionId: "session-abc")
        )
        XCTAssertEqual(appended, "http://127.0.0.1:3080/?dsh_sidebar=280&dsh_session=session-abc")

        XCTAssertEqual(
            ViewStateUrls.merge("http://127.0.0.1:3080/?token=t", state: nil),
            "http://127.0.0.1:3080/?token=t"
        )
        XCTAssertEqual(
            ViewStateUrls.merge("about:blank", state: LayoutWebState(sessionId: "s", sidebar: 1, rightbar: 1)),
            "about:blank"
        )
    }

    func testViewStateUrlsMergeExtractRoundTrip() {
        let merged = ViewStateUrls.merge(
            "http://127.0.0.1:3080/?token=t",
            state: LayoutWebState(sessionId: "session-abc", sidebar: 264, rightbar: 0)
        )
        let state = ViewStateUrls.extract(merged)
        XCTAssertEqual(state?.sessionId, "session-abc")
        XCTAssertEqual(state?.sidebar, 264)
        XCTAssertEqual(state?.rightbar, 0)

        // Values outside the unreserved set survive the round trip.
        let encoded = ViewStateUrls.merge(
            "http://127.0.0.1:3080/",
            state: LayoutWebState(sessionId: "session-a b&c")
        )
        XCTAssertTrue(encoded.contains("dsh_session=session-a%20b%26c"))
        XCTAssertEqual(ViewStateUrls.extract(encoded)?.sessionId, "session-a b&c")
    }

    func testViewStateUrlsNeedsReapply() {
        XCTAssertTrue(ViewStateUrls.needsReapply("http://127.0.0.1:3080/"))
        XCTAssertTrue(ViewStateUrls.needsReapply("http://127.0.0.1:3080/?token=x"))
        XCTAssertTrue(ViewStateUrls.needsReapply("http://127.0.0.1:3080/s/session-abc"))
        XCTAssertFalse(ViewStateUrls.needsReapply("http://127.0.0.1:3080/?dsh_session=session-abc"))
        XCTAssertFalse(ViewStateUrls.needsReapply("http://127.0.0.1:3080/?dsh_sidebar=0"))
        XCTAssertFalse(ViewStateUrls.needsReapply("about:blank"))
        XCTAssertFalse(ViewStateUrls.needsReapply(nil))
    }

    func testLayoutTabWebStateDecodesCamelAndPascalCase() throws {
        let camel = Data(#"""
        { "kind": "tunnel", "profileID": "one", "web": { "sessionId": "s1", "sidebar": 0, "rightbar": 320 } }
        """#.utf8)
        var tab = try JSONDecoder().decode(WorkspaceTab.self, from: camel)
        XCTAssertEqual(tab.web?.sessionId, "s1")
        XCTAssertEqual(tab.web?.sidebar, 0)
        XCTAssertEqual(tab.web?.rightbar, 320)

        // Windows presets serialize PascalCase members.
        let pascal = Data(#"""
        { "kind": "direct", "url": "http://127.0.0.1:3080", "web": { "SessionId": "s2", "Sidebar": 280, "Rightbar": 0 } }
        """#.utf8)
        tab = try JSONDecoder().decode(WorkspaceTab.self, from: pascal)
        XCTAssertEqual(tab.web?.sessionId, "s2")
        XCTAssertEqual(tab.web?.sidebar, 280)
        XCTAssertEqual(tab.web?.rightbar, 0)

        // Presets written before the field existed still load.
        let legacy = Data(#"""
        { "kind": "direct", "url": "http://127.0.0.1:3080" }
        """#.utf8)
        tab = try JSONDecoder().decode(WorkspaceTab.self, from: legacy)
        XCTAssertNil(tab.web)
    }

    func testUpdateVersionComparison() {
        XCTAssertTrue(UpdateService.isVersion("2.6.13", newerThan: "2.6.12"))
        XCTAssertTrue(UpdateService.isVersion("2.7", newerThan: "2.6.13"))
        XCTAssertTrue(UpdateService.isVersion("3.0.0", newerThan: "2.99.99"))
        XCTAssertTrue(UpdateService.isVersion("2.6.13.1", newerThan: "2.6.13"))
        XCTAssertFalse(UpdateService.isVersion("2.6.13", newerThan: "2.6.13"))
        XCTAssertFalse(UpdateService.isVersion("2.6.12", newerThan: "2.6.13"))
        XCTAssertFalse(UpdateService.isVersion("2.6.13.0", newerThan: "2.6.13"))
        // Non-numeric segments count as 0, matching a missing tail.
        XCTAssertFalse(UpdateService.isVersion("2.6.x", newerThan: "2.6.0"))
    }

    func testUpdateSourceURLResolution() {
        XCTAssertEqual(
            UpdateService.resolveReadableURL("https://example.com/releases")?.absoluteString,
            "https://example.com/releases"
        )
        XCTAssertEqual(
            UpdateService.resolveReadableURL("  https://example.com/releases  ")?.absoluteString,
            "https://example.com/releases"
        )
        // An smb share maps to its /Volumes mount when one exists; whether
        // this test host has any mount is environment-specific, so both
        // outcomes are valid for shares we did not mount ourselves.
        if let mapped = UpdateService.resolveReadableURL("smb://server-not-mounted.example/public/Releases") {
            XCTAssertTrue(
                mapped.path == "/Volumes/public/Releases" ||
                mapped.path.hasPrefix("/Volumes/public@") ||
                mapped.path.hasPrefix("/Volumes/public-"),
                "unexpected mapping: \(mapped.path)"
            )
        }
        XCTAssertNil(UpdateService.resolveReadableURL("ftp://example.com/x"))
        XCTAssertNil(UpdateService.resolveReadableURL("   "))
        // A plain path is accepted as-is (mount point or local folder).
        XCTAssertEqual(
            UpdateService.resolveReadableURL("/tmp")?.path,
            "/tmp"
        )
        if let mapped = UpdateService.resolveReadableURL("smb://server-home/public/Software/HarnessPortable-Releases") {
            // When the share is mounted on this machine the smb URL resolves
            // to one of the /Volumes spellings; otherwise the test host has no
            // mount and the call returns nil, which the guard below accepts.
            XCTAssertTrue(
                mapped.path == "/Volumes/public/Software/HarnessPortable-Releases" ||
                mapped.path.hasPrefix("/Volumes/public@") ||
                mapped.path.hasPrefix("/Volumes/public-"),
                "unexpected mapping: \(mapped.path)"
            )
        }
    }

    func testAppSettingsDecodeLegacyUpdateServerUrl() throws {
        // The pre-slot field folds into the matching slot instead of being lost.
        let legacySmb = try JSONDecoder().decode(
            AppSettings.self,
            from: Data(#"{ "updateServerUrl": "smb://nas/public/Releases" }"#.utf8)
        )
        XCTAssertEqual(legacySmb.updateHomeUrl, "smb://nas/public/Releases")
        XCTAssertEqual(legacySmb.updateSourceSelected, "home")

        let legacyHttp = try JSONDecoder().decode(
            AppSettings.self,
            from: Data(#"{ "updateServerUrl": "https://example.com/feed", "updateSourceSelected": "github" }"#.utf8)
        )
        XCTAssertEqual(legacyHttp.updateGithubUrl, "https://example.com/feed")
        XCTAssertEqual(legacyHttp.updateSourceSelected, "github")

        let defaults = try JSONDecoder().decode(AppSettings.self, from: Data("{}".utf8))
        XCTAssertEqual(defaults.updateSourceSelected, "home")
        XCTAssertTrue(defaults.updateGithubUrl.contains("github.com"))
    }

    func testProfileAuthModeSupportsLaunchdAndNormalizesUnknownValues() throws {
        let base = TunnelProfile(id: "p1", name: "n", sshHost: "h", user: "u")
        XCTAssertEqual(base.authMode, TunnelProfile.authModeNssm)

        let launchd = TunnelProfile(
            id: "p9", name: "n", sshHost: "h", user: "u",
            authMode: TunnelProfile.authModeLaunchd
        )
        let decoded = try JSONDecoder().decode(TunnelProfile.self, from: JSONEncoder().encode(launchd))
        XCTAssertEqual(decoded.authMode, TunnelProfile.authModeLaunchd)

        // Unknown values on disk fall back to the default.
        let legacy = try JSONDecoder().decode(
            TunnelProfile.self,
            from: Data(#"{ "id": "x", "sshHost": "h", "user": "u", "authMode": "whatever" }"#.utf8)
        )
        XCTAssertEqual(legacy.authMode, TunnelProfile.authModeNssm)
    }

    func testSystemdProfileDefaultsAndRoundTrip() throws {
        let legacy = try JSONDecoder().decode(TunnelProfile.self, from: Data("{}".utf8))
        XCTAssertEqual(legacy.authMode, TunnelProfile.authModeNssm)
        XCTAssertEqual(legacy.systemdUnit, "dsh-web.service")
        XCTAssertEqual(legacy.systemdScope, "user")
        XCTAssertEqual(legacy.systemdLogPath, "")
        let profile = TunnelProfile(
            sshHost: "linux", user: "me", authMode: TunnelProfile.authModeSystemd,
            systemdUnit: "dsh-web@4080.service", systemdScope: "system",
            systemdLogPath: "/var/log/dsh's stdout.log"
        )
        let decoded = try JSONDecoder().decode(TunnelProfile.self, from: JSONEncoder().encode(profile))
        XCTAssertEqual(decoded, profile)
        let command = try XCTUnwrap(SystemdAuthUrl.command(for: decoded))
        XCTAssertTrue(command.hasSuffix(" sh 'dsh-web@4080.service' 'system' 3080 '/var/log/dsh'\"'\"'s stdout.log'"))
    }

    func testLaunchdAuthUrlScriptExtractsNewestURL() {
        // The remote script greps the last http(s) URL from the launchd log;
        // extractUrl is the same parser used on its output.
        let log = """
        INFO dsh web listening
        dsh web: http://127.0.0.1:3080/?token=old
        INFO reloaded
        dsh web: http://127.0.0.1:3080/?token=new
        """
        XCTAssertEqual(
            NssmAuthUrl.extractUrl(from: log),
            "http://127.0.0.1:3080/?token=new"
        )
        XCTAssertNil(NssmAuthUrl.extractUrl(from: "no url here"))
    }

    func testLocalDshAuthGraftsNewestTokenOntoDirectURL() {
        let log = """
        INFO dsh web listening
        dsh web: http://127.0.0.1:3080/?token=old
        dsh web: http://127.0.0.1:3080/?token=new
        """
        XCTAssertEqual(
            LocalDshAuth.authTarget(destination: "http://127.0.0.1:3080", logText: log),
            "http://127.0.0.1:3080?token=new"
        )
        // Existing query parameters survive; a stale token is replaced.
        XCTAssertEqual(
            LocalDshAuth.authTarget(destination: "http://127.0.0.1:3080/?token=stale&x=1", logText: log),
            "http://127.0.0.1:3080/?x=1&token=new"
        )
        // No token in the log -> destination untouched (caller keeps the URL).
        XCTAssertNil(LocalDshAuth.authTarget(destination: "http://127.0.0.1:3080", logText: "no urls here"))
        XCTAssertNil(LocalDshAuth.authTarget(destination: "http://127.0.0.1:3080", logText: nil))
        // A log line without a token query is not usable.
        XCTAssertNil(
            LocalDshAuth.authTarget(destination: "http://127.0.0.1:3080", logText: "dsh web: http://127.0.0.1:3080/")
        )
    }

    func testKeychainRoundTrip() throws {
        let store = KeychainStore()
        let account = "test-\(UUID().uuidString)"
        defer { store.deletePassword(for: account) }

        try store.setPassword("secret", for: account)
        let reader = KeychainStore()
        XCTAssertEqual(reader.password(for: account), "secret")
    }

    func testLocalAuthNeverSendsTokensToOtherHostsOrPorts() {
        let log = "dsh web: http://127.0.0.1:3080/?token=synthetic"
        XCTAssertNil(LocalDshAuth.authTarget(destination: "http://unrelated.invalid:3080", logText: log))
        XCTAssertNil(LocalDshAuth.authTarget(destination: "http://localhost:4096", logText: log))
        XCTAssertNotNil(LocalDshAuth.authTarget(destination: "http://localhost:3080", logText: log))
    }

    func testCorruptTrustStoreDoesNotAcceptAChangedKey() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let path = directory.appendingPathComponent("known_hosts.json")
        let store = KnownHostsStore(path: path)
        XCTAssertEqual(store.check(host: "test.invalid", port: 22, keyType: "ssh-ed25519", keyBase64: "AQID"), .trustedNew)
        try Data("{truncated".utf8).write(to: path)
        XCTAssertEqual(store.check(host: "test.invalid", port: 22, keyType: "ssh-ed25519", keyBase64: "BAUG"), .unavailable)
        XCTAssertEqual(try String(contentsOf: path, encoding: .utf8), "{truncated")
    }

    @MainActor
    func testClosingTunnelTabsRemovesAllSessions() {
        let workspace = WorkspaceStore()
        workspace.openTunnelTab(profileID: "one")
        workspace.openTunnelTab(profileID: "one")

        let closed = workspace.closeTunnelTabs(profileID: "one")

        XCTAssertEqual(closed.count, 2)
        XCTAssertFalse(workspace.allTabs().contains { $0.tab.profileID == "one" })
    }

    @MainActor
    func testClosingLastTunnelCollapsesEmptySplit() {
        let workspace = WorkspaceStore()
        workspace.openTunnelTab(profileID: "one")
        guard let tunnelID = workspace.allTabs().first(where: { $0.tab.kind == .tunnel })?.tab.id else {
            return XCTFail("expected a tunnel tab")
        }
        _ = workspace.splitPane(workspace.root.id, direction: .right, duplicateTabID: tunnelID)

        _ = workspace.closeTunnelTabs(profileID: "one")

        XCTAssertEqual(workspace.root.kind, .pane)
        XCTAssertTrue(workspace.allTabs().filter { $0.tab.kind == .tunnel }.isEmpty)
    }

    @MainActor
    func testRestoreIgnoresLegacyManagementTabsAndKeepsAnEmptyPane() throws {
        let layout = try JSONDecoder().decode(WorkspaceLayout.self, from: Data(#"""
        {
          "name": "legacy",
          "savedAt": "2026-01-01T00:00:00Z",
          "root": { "kind": "pane", "tabs": [{ "kind": "management" }] }
        }
        """#.utf8))

        let workspace = WorkspaceStore()
        workspace.restore(layout)

        XCTAssertEqual(workspace.root.kind, .pane)
        XCTAssertTrue(workspace.allTabs().isEmpty)
    }

    func testHostClassification() {
        XCTAssertTrue(HostResolver.isTailscaleAddress("100.101.4.83"))
        XCTAssertFalse(HostResolver.isTailscaleAddress("192.168.1.10"))
        XCTAssertTrue(HostResolver.isIPLiteral("127.0.0.1"))
        XCTAssertFalse(HostResolver.isIPLiteral("macair"))
    }

    func testSSHConfigParserReadsAliasesAndIgnoresPatterns() {
        let text = """
        Host tcloud
          HostName 124.220.21.113
          User root
          Port 22
        Host nuc
          HostName nuc11atkc4.tail603dd.ts.net
          User wangyuxin
          Port 4322
        Host *
          ServerAliveInterval 60
        Host *.example
          HostName ignored.example
        """

        let hosts = SSHConfigReader.parse(text)

        XCTAssertEqual(hosts.map(\.alias), ["tcloud", "nuc"])
        XCTAssertEqual(hosts[0].hostName, "124.220.21.113")
        XCTAssertEqual(hosts[0].user, "root")
        XCTAssertEqual(hosts[0].port, 22)
        XCTAssertNil(hosts[0].identityFile)
        XCTAssertEqual(hosts[1].connectionLabel, "wangyuxin@nuc11atkc4.tail603dd.ts.net:4322")
    }

    func testSSHConfigParserUsesFirstMatchingValueAndDefaults() {
        let hosts = SSHConfigReader.parse("""
        Host invalid
          Port not-a-port
        Host *
          User global
          Port 2200
        Host target
          HostName target.internal
          User local
          Port 70000
        Host bare
        """)

        XCTAssertEqual(hosts.map(\.alias), ["target", "bare"])
        guard hosts.count == 2 else { return }
        XCTAssertEqual(hosts[0].hostName, "target.internal")
        XCTAssertEqual(hosts[0].user, "global")
        XCTAssertEqual(hosts[0].port, 2200)
        XCTAssertEqual(hosts[1].hostName, "bare")
        XCTAssertEqual(hosts[1].user, "global")
        XCTAssertEqual(hosts[1].port, 2200)
    }

    func testProfileAuthModeDefaultsToNssmAndDecodesManual() throws {
        let base = TunnelProfile(id: "p1", name: "n", sshHost: "h", user: "u")
        XCTAssertEqual(base.authMode, TunnelProfile.authModeNssm)

        let manual = TunnelProfile(
            id: "p2", name: "n", sshHost: "h", user: "u",
            authMode: TunnelProfile.authModeManual
        )
        let decoded = try JSONDecoder().decode(TunnelProfile.self, from: JSONEncoder().encode(manual))
        XCTAssertEqual(decoded.authMode, TunnelProfile.authModeManual)
        XCTAssertEqual(decoded.identityFile, "")

        let withKey = TunnelProfile(
            id: "p3", name: "n", sshHost: "h", user: "u",
            identityFile: "~/.ssh/id_ed25519"
        )
        let decodedKey = try JSONDecoder().decode(TunnelProfile.self, from: JSONEncoder().encode(withKey))
        XCTAssertEqual(decodedKey.identityFile, "~/.ssh/id_ed25519")
    }

    func testSSHConfigParserReadsIdentityFile() {
        let hosts = SSHConfigReader.parse("""
        Host tcloud
          HostName 124.220.21.113
          User root
          IdentityFile ~/.ssh/id_ed25519
        """)
        XCTAssertEqual(hosts.first?.identityFile, "~/.ssh/id_ed25519")
    }

    func testSSHIdentityExpandPathAndAuthFailure() {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        XCTAssertEqual(SSHIdentity.expandPath("~"), home)
        XCTAssertTrue(SSHIdentity.expandPath("~/.ssh/id_ed25519").hasSuffix("/.ssh/id_ed25519"))
        XCTAssertTrue(SSHIdentity.looksLikeAuthenticationFailure("Permission denied (publickey)."))
        XCTAssertTrue(SSHIdentity.looksLikeAuthenticationFailure("Too many authentication failures"))
        XCTAssertFalse(SSHIdentity.looksLikeAuthenticationFailure("Connection refused"))
    }

    func testNssmAuthUrlRewriteAndExtraction() {
        XCTAssertEqual(
            NssmAuthUrl.rewriteToLocal("http://127.0.0.1:3080/?token=abc", localPort: 4080, expectedRemotePort: 0),
            "http://127.0.0.1:4080/?token=abc"
        )
        XCTAssertNil(
            NssmAuthUrl.rewriteToLocal("http://127.0.0.1:3080/?token=abc", localPort: 4080, expectedRemotePort: 9999)
        )
        XCTAssertNil(NssmAuthUrl.rewriteToLocal("not a url", localPort: 4080, expectedRemotePort: 0))
        XCTAssertNil(NssmAuthUrl.rewriteToLocal(nil, localPort: 4080, expectedRemotePort: 0))

        XCTAssertEqual(
            NssmAuthUrl.extractUrl(from: "noise\nhttp://127.0.0.1:1/?t=a\nhttp://127.0.0.1:2/?t=b\n"),
            "http://127.0.0.1:2/?t=b"
        )
        XCTAssertNil(NssmAuthUrl.extractUrl(from: "no url here"))

        XCTAssertTrue(NssmAuthUrl.looksLikeAuthRequired("dsh web authentication required; reopen the URL printed by dsh web."))
        XCTAssertFalse(NssmAuthUrl.looksLikeAuthRequired("hello world"))
    }

    func testNssmAuthUrlBuildsTargetFromPastedInput() {
        XCTAssertEqual(
            NssmAuthUrl.buildAuthTarget(base: "http://127.0.0.1:4080", input: "http://127.0.0.1:3080/?token=abc"),
            "http://127.0.0.1:4080/?token=abc"
        )
        XCTAssertEqual(
            NssmAuthUrl.buildAuthTarget(base: "http://127.0.0.1:4080/?stale=1", input: "?token=abc"),
            "http://127.0.0.1:4080?token=abc"
        )
        XCTAssertEqual(
            NssmAuthUrl.buildAuthTarget(base: "http://127.0.0.1:4080", input: "token=abc"),
            "http://127.0.0.1:4080/?token=abc"
        )
        // A bare token is now accepted as the token value.
        XCTAssertEqual(
            NssmAuthUrl.buildAuthTarget(base: "http://127.0.0.1:4080", input: "abc-123_XYZ"),
            "http://127.0.0.1:4080/?token=abc-123_XYZ"
        )
        XCTAssertEqual(
            NssmAuthUrl.buildAuthTarget(base: "http://127.0.0.1:4080", input: "a+b&c"),
            "http://127.0.0.1:4080/?token=a%2Bb%26c"
        )
        // Whitespace or a path separator means it is not a token.
        XCTAssertNil(NssmAuthUrl.buildAuthTarget(base: "http://127.0.0.1:4080", input: "garbage token"))
        XCTAssertNil(NssmAuthUrl.buildAuthTarget(base: "http://127.0.0.1:4080", input: "abc/def"))
        XCTAssertNil(NssmAuthUrl.buildAuthTarget(base: "http://127.0.0.1:4080", input: "http://127.0.0.1:3080/"))
    }

    func testWebAuthInputNormalization() {
        // A bare base64url token becomes the token query.
        XCTAssertEqual(
            WebAuthInput.normalize("abc-123_XYZ", host: "127.0.0.1", port: 3080),
            "http://127.0.0.1:3080/?token=abc-123_XYZ"
        )
        // Characters outside base64url are escaped in the query value.
        XCTAssertEqual(
            WebAuthInput.normalize("a+b&c", host: "127.0.0.1", port: 3080),
            "http://127.0.0.1:3080/?token=a%2Bb%26c"
        )
        // A bare query is appended as-is.
        XCTAssertEqual(
            WebAuthInput.normalize("?token=abc", host: "127.0.0.1", port: 3080),
            "http://127.0.0.1:3080?token=abc"
        )
        // A key=value pair becomes the query.
        XCTAssertEqual(
            WebAuthInput.normalize("token=abc", host: "127.0.0.1", port: 3080),
            "http://127.0.0.1:3080/?token=abc"
        )
        // A full URL is kept verbatim, whatever host it names.
        XCTAssertEqual(
            WebAuthInput.normalize("http://127.0.0.1:3080/?token=abc", host: "10.0.0.5", port: 4096),
            "http://127.0.0.1:3080/?token=abc"
        )
        // An empty host falls back to loopback.
        XCTAssertEqual(
            WebAuthInput.normalize("?token=abc", host: "", port: 4096),
            "http://127.0.0.1:4096?token=abc"
        )
        // Nothing typed, or an unusable input.
        XCTAssertNil(WebAuthInput.normalize("", host: "127.0.0.1", port: 3080))
        XCTAssertNil(WebAuthInput.normalize("   ", host: "127.0.0.1", port: 3080))
        XCTAssertNil(WebAuthInput.normalize(nil, host: "127.0.0.1", port: 3080))
        XCTAssertNil(WebAuthInput.normalize("http://127.0.0.1:3080/", host: "127.0.0.1", port: 3080))
        XCTAssertNil(WebAuthInput.normalize("abc/def", host: "127.0.0.1", port: 3080))
        XCTAssertNil(WebAuthInput.normalize("not a token", host: "127.0.0.1", port: 3080))
    }

    func testKeychainAuthInputRoundTrip() throws {
        let store = KeychainStore()
        let profileID = "test-\(UUID().uuidString)"
        defer { store.deleteAuthInput(for: profileID) }

        XCTAssertFalse(store.hasAuthInput(for: profileID))
        try store.setAuthInput("http://127.0.0.1:3080/?token=abc", for: profileID)
        XCTAssertTrue(store.hasAuthInput(for: profileID))

        let reader = KeychainStore()
        XCTAssertEqual(reader.authInput(for: profileID), "http://127.0.0.1:3080/?token=abc")
        // The web-auth account never collides with the SSH password account.
        XCTAssertFalse(reader.hasPassword(for: profileID))

        store.deleteAuthInput(for: profileID)
        XCTAssertFalse(store.hasAuthInput(for: profileID))
    }
}
