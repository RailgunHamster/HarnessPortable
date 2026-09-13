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

    func testKeychainRoundTrip() throws {
        let store = KeychainStore()
        let account = "test-\(UUID().uuidString)"
        defer { store.deletePassword(for: account) }

        try store.setPassword("secret", for: account)
        let reader = KeychainStore()
        XCTAssertEqual(reader.password(for: account), "secret")
    }

    @MainActor
    func testClosingTunnelTabsRemovesAllSessions() {
        let workspace = WorkspaceStore()
        workspace.openTunnelTab(profileID: "one")
        workspace.openTunnelTab(profileID: "one")

        let closed = workspace.closeTunnelTabs(profileID: "one")

        XCTAssertEqual(closed.count, 2)
        XCTAssertFalse(workspace.allTabs().contains { $0.tab.profileID == "one" })
        XCTAssertTrue(workspace.allTabs().contains { $0.tab.kind == .management })
    }

    @MainActor
    func testClosingLastTunnelCollapsesEmptySplitAndKeepsManagement() {
        let workspace = WorkspaceStore()
        workspace.openTunnelTab(profileID: "one")
        guard let tunnelID = workspace.allTabs().first(where: { $0.tab.kind == .tunnel })?.tab.id else {
            return XCTFail("expected a tunnel tab")
        }
        _ = workspace.splitPane(workspace.root.id, direction: .right, duplicateTabID: tunnelID)

        _ = workspace.closeTunnelTabs(profileID: "one")

        XCTAssertEqual(workspace.root.kind, .pane)
        XCTAssertEqual(workspace.allTabs().filter { $0.tab.kind == .management }.count, 1)
        XCTAssertFalse(workspace.allTabs().contains { $0.tab.kind == .tunnel })
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
