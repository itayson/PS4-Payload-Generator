"""Scope and static UI contract gates; uses only Python standard library."""
from pathlib import Path
import re
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "src"
BASE = SOURCE / "UiStateStore.cs"
HISTORY = SOURCE / "UiHistoryScreen.cs"

class UiSourceContract(unittest.TestCase):
    def test_all_loaded_history_records_are_rendered(self):
        ui = HISTORY.read_text(encoding="utf-8")
        self.assertRegex(ui, r"int\s+shown\s*=\s*history\.Count\s*;")
        self.assertNotRegex(ui, r"history\.Count\s*<\s*10")

    def test_history_format_and_capacity_stable(self):
        store = BASE.read_text(encoding="utf-8")
        self.assertIn('const string HistoryKey = "tayson.ui.history.v2";', store)
        self.assertIn("const int MaxItems = 20;", store)
        self.assertIn('if (p.Length != 8) continue;', store)
        self.assertIn('Encode(x.Key)', store)

    def test_whitespace_ids_get_filtered(self):
        store = BASE.read_text(encoding="utf-8")
        for field in ("ContentId", "TitleId", "DisplayName"):
            self.assertIn(f"string.IsNullOrWhiteSpace(meta.{field})", store)
        self.assertNotIn(
            '=> meta.ContentId ?? meta.TitleId ?? meta.DisplayName ?? "pkg";',
            store,
        )

def scope_check():
    root = subprocess.check_output(
        ["git", "rev-parse", "--show-toplevel"], text=True).strip()
    subprocess.check_call(["git", "fetch", "--no-tags", "origin", "main"])
    base = subprocess.check_output(
        ["git", "merge-base", "HEAD", "origin/main"], text=True).strip()
    diff = subprocess.check_output(
        ["git", "diff", "--name-only", base, "HEAD"], text=True, cwd=root)
    allowed_exact = {
        "payload-generator/3.2.0/UPDATE/src/UiHistoryScreen.cs",
        "payload-generator/3.2.0/UPDATE/src/UiStateStore.cs",
        ".github/workflows/android-ui-regression.yml",
    }
    allowed_prefix = "payload-generator/3.2.0/UPDATE/tests/UiStateRegression/"
    changed = [p for p in diff.splitlines() if p]
    disallowed = [p for p in changed if p not in allowed_exact
                  and not p.startswith(allowed_prefix)]
    if disallowed:
        raise SystemExit("REFUSED: out-of-scope paths: " + repr(disallowed))
    print("SCOPE PASS: " + repr(changed))

if __name__ == "__main__":
    if sys.argv[1:] == ["--scope"]:
        scope_check()
    else:
        unittest.main()
