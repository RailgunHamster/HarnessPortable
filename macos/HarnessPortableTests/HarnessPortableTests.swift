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

    func testHostClassification() {
        XCTAssertTrue(HostResolver.isTailscaleAddress("100.101.4.83"))
        XCTAssertFalse(HostResolver.isTailscaleAddress("192.168.1.10"))
        XCTAssertTrue(HostResolver.isIPLiteral("127.0.0.1"))
        XCTAssertFalse(HostResolver.isIPLiteral("macair"))
    }
}
