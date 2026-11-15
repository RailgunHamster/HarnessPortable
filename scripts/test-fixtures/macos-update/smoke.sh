#!/bin/bash
# Exercise the shipped helper process against disposable, signed bundles.
set -euo pipefail
helper_source=${1:?Pass the path to the built macOS executable}
export TMPDIR=$(/usr/bin/getconf DARWIN_USER_TEMP_DIR)
fixture=$(/usr/bin/mktemp -d "${TMPDIR%/}/hp-update-qa.XXXXXXXX")
export HARNESS_PORTABLE_DATA_DIRECTORY="$fixture/isolated-data"
parent=
helper=
session=
cleanup() {
    if [ -n "$parent" ]; then kill "$parent" 2>/dev/null || true; fi
    if [ -n "$helper" ]; then kill "$helper" 2>/dev/null || true; fi
    if [ -n "$session" ]; then /bin/rm -rf -- "$session"; fi
    /bin/rm -rf -- "$fixture"
}
trap cleanup EXIT

cat > "$fixture/main.m" <<'C'
#import <AppKit/AppKit.h>
#include <stdio.h>

@interface FixtureDelegate : NSObject <NSApplicationDelegate>
@end
@implementation FixtureDelegate
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    if (FAILS == 1) exit(3);
    FILE *out = fopen(REPORT, "w");
    if (!out) exit(4);
    fputs("launched\n", out);
    fclose(out);
    [NSApp performSelector:@selector(terminate:) withObject:nil afterDelay:3];
}
@end

int main(void) {
    @autoreleasepool {
        NSApplication *app = [NSApplication sharedApplication];
        [app setActivationPolicy:NSApplicationActivationPolicyProhibited];
        FixtureDelegate *delegate = [[FixtureDelegate alloc] init];
        app.delegate = delegate;
        if (FAILS == 2) for (;;) [NSThread sleepForTimeInterval:10];
        [app run];
    }
    return 0;
}
C

make_app() {
    local path=$1 version=$2 label=$3 fails=$4
    /bin/mkdir -p "$path/Contents/MacOS"
    /usr/bin/clang "-DREPORT=\"$fixture/$label\"" "-DFAILS=$fails" -framework AppKit "$fixture/main.m" -o "$path/Contents/MacOS/Fixture"
    cat > "$path/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>Fixture</string>
<key>CFBundleName</key><string>Harness Portable Update Fixture</string>
<key>CFBundleIdentifier</key><string>com.harness.portable</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>CFBundleVersion</key><string>$version</string>
<key>LSBackgroundOnly</key><true/>
</dict></plist>
PLIST
    /usr/bin/codesign --force --sign - "$path"
}

version_of() { /usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$1/Contents/Info.plist"; }

run_case() {
    local mode=$1 failures=$2
    local case_path="$fixture/$mode"
    local installed="$case_path/installed/HarnessPortable.app"
    local candidate="$case_path/dmg/HarnessPortable.app"
    local directory="${TMPDIR%/}/hp-update-$(/usr/bin/uuidgen)"
    make_app "$installed" 2.8.7 "$mode-old" 0
    make_app "$candidate" 2.8.8 "$mode-new" "$failures"
    /bin/mkdir -m 700 "$directory"
    session=$directory
    /usr/bin/hdiutil create -quiet -volname "Harness Update Fixture" -srcfolder "$case_path/dmg" -format UDZO "$directory/update.dmg"
    local digest
    digest=$(/usr/bin/shasum -a 256 "$directory/update.dmg")
    digest=${digest%% *}
    if [ "$mode" = corrupt ]; then digest=$(printf '%064d' 0); fi
    /bin/sleep 50 & parent=$!
    cat > "$directory/request.json" <<JSON
{"parent":$parent,"app":"file://$installed","from":"2.8.7","to":"2.8.8","sha256":"$digest","appearance":"light"}
JSON
    /bin/cp "$helper_source" "$directory/HarnessPortableUpdater"
    "$directory/HarnessPortableUpdater" --harness-install-update "$directory" > "$case_path/helper.log" 2>&1 & helper=$!
    local limit=$((SECONDS + 45))
    if [ "$mode" != corrupt ]; then
        while [ ! -e "$directory/ready" ] && kill -0 "$helper" 2>/dev/null && [ "$SECONDS" -lt "$limit" ]; do sleep .1; done
        if [ ! -e "$directory/ready" ]; then cat "$case_path/helper.log"; return 1; fi
        [ "$(version_of "$installed")" = 2.8.7 ]
        if [ "$mode" != uncommitted ]; then /usr/bin/touch "$directory/commit"; fi
    fi
    kill "$parent" 2>/dev/null || true
    wait "$parent" 2>/dev/null || true; parent=
    while kill -0 "$helper" 2>/dev/null && [ "$SECONDS" -lt "$limit" ]; do sleep .1; done
    if kill -0 "$helper" 2>/dev/null; then echo "Helper timed out in $mode"; return 1; fi
    local result=0
    wait "$helper" || result=$?; helper=
    case "$mode" in
        success)
            [ "$result" = 0 ] && [ "$(version_of "$installed")" = 2.8.8 ] && [ -f "$fixture/$mode-new" ];;
        rollback|stalled)
            local launch_limit=$((SECONDS + 5))
            while [ ! -f "$fixture/$mode-old" ] && [ "$SECONDS" -lt "$launch_limit" ]; do sleep .1; done
            [ "$result" = 1 ] && [ "$(version_of "$installed")" = 2.8.7 ] && [ -f "$fixture/$mode-old" ];;
        *)
            [ "$result" = 1 ] && [ "$(version_of "$installed")" = 2.8.7 ] && [ ! -f "$fixture/$mode-new" ];;
    esac || {
        cat "$case_path/helper.log" "$HARNESS_PORTABLE_DATA_DIRECTORY/update.log"
        echo "Unexpected result in $mode ($result)"
        return 1
    }
    [ ! -e "$directory" ]
    session=
    ! /usr/bin/find "$case_path/installed" -maxdepth 1 -name '.harness-update-*' | /usr/bin/grep -q .
    printf 'PASS: %s\n' "$mode"
}

run_case success 0
run_case rollback 1
run_case stalled 2
run_case corrupt 0
run_case uncommitted 0
/usr/bin/grep -q 'helper.relaunched version=2.8.8' "$HARNESS_PORTABLE_DATA_DIRECTORY/update.log"
/usr/bin/grep -q 'helper.rolled_back' "$HARNESS_PORTABLE_DATA_DIRECTORY/update.log"
/usr/bin/grep -q 'helper.launch_timed_out' "$HARNESS_PORTABLE_DATA_DIRECTORY/update.log"
