"""Run the actual shared SSH helper against fake commands, without a Linux host.

Usage: python -m unittest discover -s tests -v
Requires POSIX sh (Git for Windows is supported); no third-party dependencies.
"""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = (ROOT / "shared/systemd-auth.sh").read_text(encoding="utf-8")
SHELL = os.environ.get("SYSTEMD_TEST_SHELL") or (
    "C:/Program Files/Git/usr/bin/sh.exe" if os.name == "nt" else shutil.which("sh")
)
URL = "http://127.0.0.1:3080/?token=current"


def shell_path(path):
    value = str(path).replace("\\", "/")
    if len(value) > 2 and value[1] == ":":
        return "/" + value[0].lower() + value[2:]
    return value


def quote(value):
    return "'" + value.replace("'", "'\"'\"'") + "'"


class SystemdAuthTests(unittest.TestCase):
    def setUp(self):
        (ROOT / "temp").mkdir(exist_ok=True)
        self.directory = tempfile.TemporaryDirectory(dir=ROOT / "temp")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.trace = self.root / "trace"
        self.journal = self.root / "journal"
        self.journal.write_text("dsh web: " + URL + "\n", encoding="utf-8")
        self.env = dict(os.environ, MOCK_DIR=shell_path(self.root),
                        MOCK_TRACE=shell_path(self.trace), MOCK_JOURNAL=shell_path(self.journal),
                        MOCK_ACTIVE="active", MOCK_INVOCATION="current-invocation",
                        MOCK_NEXT_INVOCATION="current-invocation",
                        MOCK_HTTP_CODE="303", MOCK_CURL_EXIT="0", MOCK_JOURNAL_EXIT="0")
        self.mock("systemctl", """
printf 'systemctl %s\\n' "$*" >> "$MOCK_TRACE"
case "$*" in
  *--value*) printf '%s\\n' "$MOCK_NEXT_INVOCATION" ;;
  *) printf 'ActiveState=%s\\nInvocationID=%s\\n' "$MOCK_ACTIVE" "$MOCK_INVOCATION" ;;
esac
""")
        self.mock("journalctl", """
printf 'journalctl %s\\n' "$*" >> "$MOCK_TRACE"
[ "$MOCK_JOURNAL_EXIT" = 0 ] || exit "$MOCK_JOURNAL_EXIT"
case "$*" in
  *"_SYSTEMD_INVOCATION_ID=$MOCK_INVOCATION"*) cat "$MOCK_JOURNAL" ;;
  *) printf 'dsh web: http://127.0.0.1:3080/?token=old-instance\\n' ;;
esac
""")
        self.mock("curl", """
printf 'curl %s\\n' "$*" >> "$MOCK_TRACE"
printf '%s' "$MOCK_HTTP_CODE"
exit "$MOCK_CURL_EXIT"
""")

    def mock(self, name, body):
        path = self.root / name
        path.write_text("#!/bin/sh\n" + body, encoding="utf-8", newline="\n")
        path.chmod(0o755)

    def run_helper(self, unit="dsh-web.service", scope="user", port=3080, log=""):
        command = "export PATH=\"$MOCK_DIR:/usr/bin:/bin:$PATH\"; exec sh -c " + quote(SCRIPT) + " sh " + " ".join(
            quote(str(value)) for value in (unit, scope, port, log)
        )
        result = subprocess.run([SHELL, "-c", command], env=self.env, capture_output=True,
                                text=True, timeout=15, cwd=ROOT)
        self.assertEqual("", result.stderr)
        return result.returncode, result.stdout.strip()

    def commands(self):
        return self.trace.read_text(encoding="utf-8") if self.trace.exists() else ""

    def test_user_journal_is_filtered_to_current_invocation(self):
        self.assertEqual((0, URL), self.run_helper())
        self.assertIn("systemctl --user show", self.commands())
        self.assertIn("journalctl --user --unit=dsh-web.service", self.commands())
        self.assertIn("_SYSTEMD_INVOCATION_ID=current-invocation", self.commands())
        self.assertIn("--noproxy *", self.commands())

    def test_system_service_does_not_use_user_manager(self):
        self.assertEqual((0, URL), self.run_helper(scope="system", unit="dsh-web@3080.service"))
        self.assertNotIn("--user", self.commands())
        self.assertIn("--unit=dsh-web@3080.service", self.commands())

    def test_latest_startup_url_ignores_unrelated_urls(self):
        self.journal.write_text("dsh web: http://127.0.0.1:3080/?token=old\n"
                                "dsh web: " + URL + "\nhttps://example.com/\n", encoding="utf-8")
        self.assertEqual((0, URL), self.run_helper())

    def test_startup_url_survives_many_later_log_lines(self):
        with self.journal.open("a", encoding="utf-8") as stream:
            stream.write("ordinary request\n" * 12000)
        self.assertEqual((0, URL), self.run_helper())

    def test_stdout_file_uses_configured_path(self):
        self.assertEqual((0, URL), self.run_helper(log=shell_path(self.journal)))
        self.assertNotIn("journalctl", self.commands())

    def test_explicit_path_with_spaces_and_quotes(self):
        path = self.root / "dsh's stdout.log"
        path.write_text("dsh web: " + URL + "\n", encoding="utf-8")
        self.assertEqual((0, URL), self.run_helper(log=shell_path(path)))
        self.assertNotIn("journalctl", self.commands())

    def test_inactive_service_returns_no_credential(self):
        self.env["MOCK_ACTIVE"] = "failed"
        self.assertEqual((3, ""), self.run_helper())
        self.assertNotIn("curl", self.commands())

    def test_missing_invocation_does_not_read_old_logs(self):
        self.env["MOCK_INVOCATION"] = ""
        self.assertEqual((3, ""), self.run_helper())
        self.assertNotIn("journalctl", self.commands())

    def test_unreadable_journal_returns_no_credential(self):
        self.env["MOCK_JOURNAL_EXIT"] = "1"
        self.assertEqual((3, ""), self.run_helper())

    def test_missing_log_file_returns_no_credential(self):
        self.assertEqual((3, ""), self.run_helper(log=shell_path(self.root / "missing")))

    def test_url_without_token_returns_no_credential(self):
        self.journal.write_text("dsh web: http://127.0.0.1:3080/\n", encoding="utf-8")
        self.assertEqual((3, ""), self.run_helper())

    def test_port_mismatch_is_rejected_before_http_request(self):
        self.assertEqual((5, ""), self.run_helper(port=4080))
        self.assertNotIn("curl", self.commands())

    def test_rejected_token_returns_no_credential(self):
        self.env["MOCK_HTTP_CODE"] = "401"
        self.assertEqual((4, ""), self.run_helper())

    def test_http_failure_returns_no_credential(self):
        self.env["MOCK_CURL_EXIT"] = "28"
        self.assertEqual((4, ""), self.run_helper())

    def test_service_restart_during_fetch_returns_no_credential(self):
        self.env["MOCK_NEXT_INVOCATION"] = "restarted-invocation"
        self.assertEqual((6, ""), self.run_helper())

    def test_invalid_arguments_cannot_inject_shell_commands(self):
        self.assertEqual((5, ""), self.run_helper(unit="bad'; echo injected; echo '.service"))
        self.assertEqual((5, ""), self.run_helper(scope="user; echo injected"))
        self.assertEqual((5, ""), self.run_helper(port="3080; echo injected"))
        self.assertEqual("", self.commands())


if __name__ == "__main__":
    unittest.main()
