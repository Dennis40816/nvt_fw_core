# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Tests for duration_report.py."""
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "repo-checks"))
import duration_report as td

NS = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


def make_trx(path: Path, tests, project="Nvt.Core.Tests", stdout=None):
    """tests: list of (name, seconds, outcome). stdout: {test index: text}."""
    units, results = [], []
    for i, (name, seconds, outcome) in enumerate(tests):
        tid = f"id-{i}"
        units.append(f'<UnitTest name="{name}" id="{tid}"><TestMethod codeBase="C:\\\\b\\\\{project}.dll" '
                     f'className="X" name="Y" /></UnitTest>')
        h, rem = divmod(seconds, 3600)
        m, s = divmod(rem, 60)
        out = ""
        if stdout and i in stdout:
            out = f"<Output><StdOut>{stdout[i]}</StdOut></Output>"
        results.append(f'<UnitTestResult testId="{tid}" testName="{name}" outcome="{outcome}" '
                       f'duration="{int(h):02d}:{int(m):02d}:{s:09.6f}">{out}</UnitTestResult>')
    path.write_text(f'<?xml version="1.0"?><TestRun xmlns="{NS}"><Results>{"".join(results)}</Results>'
                    f'<TestDefinitions>{"".join(units)}</TestDefinitions></TestRun>', encoding="utf-8")


def _load(data):
    with tempfile.TemporaryDirectory() as d:
        p = Path(d) / "b.json"
        p.write_text(json.dumps(data), encoding="utf-8")
        return td.load_baseline(p)


class ParseTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def test_method_key_strips_parameters(self):
        self.assertEqual("A.B.C", td.method_key('A.B.C(x: "(1)", y: 2)'))
        self.assertEqual("A.B.C", td.method_key("A.B.C"))

    def test_parse_reads_project_duration_and_skips_not_executed(self):
        trx = self.dir / "a.trx"
        make_trx(trx, [("N.C.Fast", 0.25, "Passed"), ("N.C.Slow", 61.5, "Failed"), ("N.C.Skip", 9, "NotExecuted")])
        tests, units = td.parse_trx(trx)
        self.assertEqual(["N.C.Fast", "N.C.Slow"], [t.name for t in tests])
        self.assertAlmostEqual(61.5, tests[1].seconds)
        self.assertEqual("Nvt.Core.Tests", tests[0].project)
        self.assertEqual([], units)

    def test_parse_finds_calibration_unit_in_test_output(self):
        trx = self.dir / "a.trx"
        make_trx(trx, [("N.Calibration.Run", 0.1, "Passed")], stdout={0: "NVT_CALIBRATION_UNIT_SECONDS=0.00123"})
        _, units = td.parse_trx(trx)
        self.assertEqual([0.00123], units)

    def test_find_trx_walks_directories(self):
        sub = self.dir / "shard" / "results"
        sub.mkdir(parents=True)
        make_trx(sub / "x.trx", [("N.C.M", 1, "Passed")])
        self.assertEqual([sub / "x.trx"], td.find_trx([str(self.dir)]))


class ReportTests(unittest.TestCase):
    @staticmethod
    def tt(name, seconds, outcome="Passed"):
        return td.Timing(name, td.method_key(name), "P", seconds, outcome)

    def test_calibration_converts_to_reference_seconds(self):
        # This machine is twice as slow as the reference (unit 2 ms vs 1 ms): 8 s is 4 reference seconds.
        report = td.build_report([self.tt("N.C.M", 8.0)], [], _load({"referenceUnitSeconds": 0.001}),
                                 unit_seconds=0.002)
        self.assertEqual({}, report.slow_methods)
        self.assertEqual([], report.violations)
        report = td.build_report([self.tt("N.C.M", 8.0)], [], _load({"referenceUnitSeconds": 0.001}),
                                 unit_seconds=0.001)
        self.assertIn("N.C.M", report.slow_methods)

    def test_unit_from_test_output_uses_the_median(self):
        report = td.build_report([self.tt("N.C.M", 8.0)], [0.002, 0.002, 0.010], _load({"referenceUnitSeconds": 0.001}))
        self.assertIsNotNone(report.unit_seconds)
        self.assertAlmostEqual(0.002, report.unit_seconds or 0.0)

    def test_unlisted_slow_test_is_a_violation(self):
        report = td.build_report([self.tt("N.C.M", 6.0)], [], _load({}))
        self.assertEqual(1, len(report.violations))
        self.assertIn("not in the baseline", report.violations[0])

    def test_listed_slow_test_with_valid_reason_passes(self):
        data = {"slow": [{"test": "N.C.M", "reason": "real-process"}]}
        report = td.build_report([self.tt("N.C.M", 6.0)], [], _load(data))
        self.assertEqual([], report.violations)

    def test_unclassified_reason_is_a_warning_and_invalid_reason_fails(self):
        data = {"slow": [{"test": "N.C.A", "reason": "unclassified"}, {"test": "N.C.B", "reason": "waits"}]}
        report = td.build_report([self.tt("N.C.A", 6.0), self.tt("N.C.B", 6.0)], [], _load(data))
        self.assertEqual(1, len(report.violations))
        self.assertIn("N.C.B", report.violations[0])
        self.assertTrue(any("N.C.A" in w for w in report.warnings))

    def test_over_limit_needs_approval(self):
        data = {"slow": [{"test": "N.C.M", "reason": "large-input"}]}
        report = td.build_report([self.tt("N.C.M", 40.0)], [], _load(data))
        self.assertEqual(1, len(report.violations))
        data = {"slow": [{"test": "N.C.M", "reason": "large-input", "approved": True}]}
        self.assertEqual([], td.build_report([self.tt("N.C.M", 40.0)], [], _load(data)).violations)

    def test_theory_rows_use_the_slowest_row(self):
        rows = [self.tt('N.C.M(a: 1)', 1.0), self.tt('N.C.M(a: 2)', 7.0)]
        report = td.build_report(rows, [], _load({}))
        self.assertEqual(["N.C.M"], list(report.slow_methods))

    def test_test_that_got_fast_is_a_note_not_a_failure(self):
        data = {"slow": [{"test": "N.C.M", "reason": "real-io"}]}
        report = td.build_report([self.tt("N.C.M", 0.5)], [], _load(data))
        self.assertEqual([], report.violations)
        self.assertTrue(any("Remove it from the baseline" in w for w in report.warnings))

    def test_markdown_lists_the_slowest_first_and_respects_top(self):
        rows = [self.tt(f"N.C.T{i}", float(i)) for i in range(1, 6)]
        report = td.build_report(rows, [], _load({}), top=2)
        table = [l for l in report.markdown.splitlines() if l.startswith("| ") and l[2].isdigit()]
        self.assertEqual(2, len(table))
        self.assertIn("N.C.T5", table[0])

    def test_missing_calibration_falls_back_with_a_note(self):
        report = td.build_report([self.tt("N.C.M", 1.0)], [], _load({}))
        self.assertTrue(any("No calibration unit" in w for w in report.warnings))


class MainTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        make_trx(self.dir / "a.trx", [("N.C.Fast", 0.2, "Passed"), ("N.C.Slow", 9.0, "Passed")])

    def tearDown(self):
        self.tmp.cleanup()

    def test_check_fails_on_violation_and_passes_without_check(self):
        self.assertEqual(1, td.main(["--trx", str(self.dir), "--check"]))
        self.assertEqual(0, td.main(["--trx", str(self.dir)]))

    def test_no_trx_exit_codes(self):
        empty = self.dir / "empty"
        empty.mkdir()
        self.assertEqual(0, td.main(["--trx", str(empty)]))
        self.assertEqual(2, td.main(["--trx", str(empty), "--require-trx"]))

    def test_seed_baseline_then_check_passes_with_a_warning(self):
        seed = self.dir / "slow-tests-baseline.json"
        self.assertEqual(0, td.main(["--trx", str(self.dir), "--seed-baseline", str(seed)]))
        data = json.loads(seed.read_text(encoding="utf-8"))
        self.assertEqual([{"test": "N.C.Slow", "reason": "unclassified"}], data["slow"])
        self.assertEqual(0, td.main(["--trx", str(self.dir), "--baseline", str(seed), "--check"]))

    def test_github_summary_is_appended(self):
        summary = self.dir / "summary.md"
        summary.write_text("before\n", encoding="utf-8")
        old = os.environ.get("GITHUB_STEP_SUMMARY")
        os.environ["GITHUB_STEP_SUMMARY"] = str(summary)
        try:
            td.main(["--trx", str(self.dir), "--github-summary"])
        finally:
            if old is None:
                del os.environ["GITHUB_STEP_SUMMARY"]
            else:
                os.environ["GITHUB_STEP_SUMMARY"] = old
        text = summary.read_text(encoding="utf-8")
        self.assertTrue(text.startswith("before\n"))
        self.assertIn("### Slowest tests", text)


class RealFileSmokeTest(unittest.TestCase):
    """Uses the NFC TRX downloaded for the audit when it is present on this machine."""
    ROOT = Path("C:/Users/liusx/source/toolchain-helper/drafts/nfc-trx")

    @unittest.skipUnless(ROOT.is_dir(), "NFC TRX not downloaded")
    def test_nfc_trx_parses_and_ranks(self):
        files = td.find_trx([str(self.ROOT)])
        tests = [t for f in files for t in td.parse_trx(f)[0]]
        self.assertGreater(len(tests), 8000)
        report = td.build_report(tests, [], _load({}))
        self.assertIn("AcceptedCatalogNormalizationFailureStaysPrebuiltAndMatchesJsonFailure", report.markdown)


if __name__ == "__main__":
    unittest.main()
