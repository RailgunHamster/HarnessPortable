import XCTest
@testable import HarnessPortable

@MainActor
final class WorkspaceUXTests: XCTestCase {
    func testLegacySettingsKeepDefaultsAndReconnectOnStartup() throws {
        let settings = try JSONDecoder().decode(AppSettings.self, from: Data(#"{"closeBehavior":"tray","updateSourceSelected":"home"}"#.utf8))
        XCTAssertEqual(settings.closeBehavior, "tray")
        XCTAssertEqual(settings.updateSourceSelected, "home")
        XCTAssertTrue(settings.reconnectRestoredWorkspace)
        XCTAssertTrue(settings.connections.isEmpty)
    }
    func testExplicitPauseOnStartupIsPreserved() throws {
        let settings = try JSONDecoder().decode(AppSettings.self, from: Data(#"{"reconnectRestoredWorkspace":false,"startupReconnectPreference":false}"#.utf8))
        XCTAssertFalse(settings.reconnectRestoredWorkspace)
    }
    func testVersion271SavedDefaultMigratesToReconnect() throws {
        let settings = try JSONDecoder().decode(AppSettings.self, from: Data(#"{"reconnectRestoredWorkspace":false,"lastLayoutName":"Work"}"#.utf8))
        XCTAssertTrue(settings.reconnectRestoredWorkspace)
        let restored = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertTrue(restored.reconnectRestoredWorkspace)
        XCTAssertEqual(restored.lastLayoutName, "Work")
    }
    func testConnectionOrderMovesAcrossKindsAndKeepsHiddenEntries() throws {
        let keys = ["tunnel:a", "direct:b", "tunnel:c", "direct:d"]
        let moved = try XCTUnwrap(ConnectionOrdering.moving("direct:d", to: "direct:b", after: false, keys: keys, saved: ["deleted", "tunnel:a", "tunnel:a"]))
        XCTAssertEqual(moved, ["tunnel:a", "direct:d", "direct:b", "tunnel:c"])
        var settings = AppSettings(); settings.connectionOrder = moved
        let restored = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertEqual(restored.connectionOrder, moved)
        XCTAssertNil(ConnectionOrdering.moving("external", to: "tunnel:a", after: true, keys: keys, saved: moved))
    }
    func testConnectionPreferencesRoundTrip() throws {
        var settings = AppSettings()
        var preference = ConnectionPreference(); preference.favorite = true; preference.group = "家庭"; preference.lastOpened = "2026-10-12T12:00:00Z"
        settings.connections["tunnel:one"] = preference
        let restored = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertEqual(restored.connections["tunnel:one"]?.group, "家庭")
        XCTAssertEqual(restored.connections["tunnel:one"]?.favorite, true)
    }
    func testSnapshotRestoresSelectedTabAndActivePane() throws {
        let workspace = WorkspaceStore()
        workspace.openDirect("https://one.test")
        workspace.openDirect("https://two.test")
        let selected = try XCTUnwrap(workspace.selectedTab(in: workspace.activePaneID))
        XCTAssertEqual(selected.url, "https://two.test")
        let layout = WorkspaceLayout(name: "上次工作区", root: workspace.captureLayout())
        let roundTrip = try JSONDecoder().decode(WorkspaceLayout.self, from: JSONEncoder().encode(layout))
        let restored = WorkspaceStore(); restored.restore(roundTrip)
        XCTAssertEqual(restored.selectedTab(in: restored.activePaneID)?.url, "https://two.test")
    }
    func testSnapshotFileDoesNotReplaceNamedPresets() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let snapshot = WorkspaceSnapshotStore(url: directory.appendingPathComponent("workspace.json"))
        snapshot.save(WorkspaceLayout(name: "上次工作区", root: .pane([WorkspaceTab(kind: .direct, url: "https://one.test")])) )
        XCTAssertEqual(snapshot.load()?.name, "上次工作区")
        XCTAssertEqual(snapshot.load()?.root.tabs.first?.url, "https://one.test")
    }
}
