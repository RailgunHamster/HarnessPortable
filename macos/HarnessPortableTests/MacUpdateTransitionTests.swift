import XCTest
@testable import HarnessPortable

final class MacUpdateTransitionTests: XCTestCase {
    func testOnlyVerifiedOfficialReleaseFeedsEnableAutomaticReplacement() throws {
        let hash = String(repeating: "a", count: 64)
        let cnb = try XCTUnwrap(URL(string: "https://cnb.cool/railgunhamster/HarnessPortable/-/git/raw/updates"))
        let dmg = try XCTUnwrap(URL(string: "https://cnb.cool/railgunhamster/HarnessPortable/-/releases/download/v2.8.8/HarnessPortable-2.8.8.dmg"))
        let github = try XCTUnwrap(URL(string: "https://github.com/RailgunHamster/HarnessPortable/releases/latest/download"))
        XCTAssertTrue(MacUpdateTransition.officialPackage(base: cnb, package: dmg, version: "2.8.8", sha256: hash))
        XCTAssertTrue(MacUpdateTransition.officialPackage(base: github,
            package: github.appendingPathComponent("HarnessPortable-2.8.8.dmg"), version: "2.8.8", sha256: hash))
        XCTAssertFalse(MacUpdateTransition.officialPackage(base: cnb, package: dmg, version: "2.8.8", sha256: nil))
        XCTAssertFalse(MacUpdateTransition.officialPackage(base: cnb, package: dmg, version: "2.8.9", sha256: hash))
        XCTAssertFalse(MacUpdateTransition.officialPackage(base: github, package: dmg, version: "2.8.8", sha256: hash))
        let mirror = try XCTUnwrap(URL(string: "https://github.com/Other/HarnessPortable/releases/latest/download"))
        XCTAssertFalse(MacUpdateTransition.officialPackage(base: mirror,
            package: mirror.appendingPathComponent("HarnessPortable-2.8.8.dmg"), version: "2.8.8", sha256: hash))
    }
}
