#!/usr/bin/env python3
# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Summarize test durations from TRX files and check the slow-test ratchet.

Python 3.9+, standard library only.
It reads TRX files, converts each duration to reference seconds, lists the slowest tests, and
checks them against ``tests/slow-tests-baseline.json``.

    python tools/repo-checks/duration_report.py --trx artifacts/test-results --baseline tests/slow-tests-baseline.json \
        --github-summary --check

Exit code: 0 = fine, 1 = ratchet violation (only with --check), 2 = usage error or no TRX (with --require-trx).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import statistics
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Optional

try:  # TRX files come from our own test runs, but prefer the hardened parser when it is installed.
    from defusedxml import ElementTree as ET  # type: ignore[import-not-found]
except ImportError:  # pragma: no cover
    import xml.etree.ElementTree as ET  # noqa: S405

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
UNIT_RX = re.compile(r"NVT_CALIBRATION_UNIT_SECONDS=([0-9]+(?:\.[0-9]+)?(?:[eE][-+]?[0-9]+)?)")
REASONS = ("real-process", "large-input", "real-io", "ui-render")
UNCLASSIFIED = "unclassified"
DEFAULTS = {"slowRefSeconds": 5.0, "overRefSeconds": 30.0, "warnRefSeconds": 1.0}


@dataclass(frozen=True)
class Timing:
    name: str      # full name with parameters, as the TRX shows it
    method: str    # name without parameters: the key the baseline uses
    project: str
    seconds: float
    outcome: str


def parse_duration(text: str) -> float:
    h, m, s = text.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def method_key(name: str) -> str:
    cut = name.find("(")
    return (name if cut < 0 else name[:cut]).strip()


def parse_trx(path: Path) -> tuple[list[Timing], list[float]]:
    """Return the executed tests of one TRX file and every calibration unit found in test output."""
    root = ET.parse(str(path)).getroot()
    assert root is not None
    project_of: dict[str, str] = {}
    for unit in root.iter(NS + "UnitTest"):
        method = unit.find(NS + "TestMethod")
        if method is not None:
            code_base = (method.get("codeBase") or "").replace("\\", "/")
            project_of[unit.get("id") or ""] = Path(code_base).stem or path.stem
    tests: list[Timing] = []
    units: list[float] = []
    for result in root.iter(NS + "UnitTestResult"):
        out = result.find(NS + "Output/" + NS + "StdOut")
        if out is not None and out.text:
            units.extend(float(m) for m in UNIT_RX.findall(out.text))
        duration = result.get("duration")
        outcome = result.get("outcome") or ""
        if not duration or outcome == "NotExecuted":
            continue
        name = result.get("testName") or ""
        tests.append(Timing(name, method_key(name), project_of.get(result.get("testId") or "", path.stem),
                              parse_duration(duration), outcome))
    return tests, units


def find_trx(paths: Iterable[str]) -> list[Path]:
    found: list[Path] = []
    for raw in paths:
        p = Path(raw)
        if p.is_dir():
            found.extend(sorted(p.rglob("*.trx")))
        elif p.is_file():
            found.append(p)
    return found


@dataclass
class Report:
    markdown: str
    violations: list[str]
    warnings: list[str]
    unit_seconds: Optional[float]
    slow_methods: dict[str, float]  # method -> max reference seconds


def load_baseline(path: Optional[Path]) -> dict:
    data: dict = {}
    if path is not None and path.is_file():
        data = json.loads(path.read_text(encoding="utf-8"))
    merged: dict = dict(DEFAULTS)
    merged.update({k: v for k, v in data.items() if k in DEFAULTS})
    merged["referenceUnitSeconds"] = data.get("referenceUnitSeconds")
    merged["slow"] = {entry["test"]: entry for entry in data.get("slow", [])}
    return merged


def build_report(tests: list[Timing], units: list[float], baseline: dict, *, unit_seconds: Optional[float] = None,
                 top: int = 20) -> Report:
    warnings: list[str] = []
    violations: list[str] = []
    unit = unit_seconds if unit_seconds else (statistics.median(units) if units else None)
    reference_unit = baseline.get("referenceUnitSeconds")
    if unit and reference_unit:
        factor = reference_unit / unit  # reference seconds = measured seconds x (reference unit / this machine's unit)
    else:
        factor = 1.0
        warnings.append("No calibration unit or no reference unit: raw seconds are used as reference seconds.")

    def ref(seconds: float) -> float:
        return seconds * factor

    slow_limit = float(baseline["slowRefSeconds"])
    over_limit = float(baseline["overRefSeconds"])
    warn_limit = float(baseline["warnRefSeconds"])

    worst: dict[str, float] = {}
    for t in tests:
        worst[t.method] = max(worst.get(t.method, 0.0), ref(t.seconds))
    slow_methods = {m: s for m, s in worst.items() if s > slow_limit}
    listed: dict = baseline["slow"]

    for method, seconds in sorted(slow_methods.items(), key=lambda kv: -kv[1]):
        entry = listed.get(method)
        if entry is None:
            violations.append(f"Slow test is not in the baseline ({seconds:.1f} ref s): {method}")
            continue
        reason = (entry.get("reason") or "").strip()
        if reason == UNCLASSIFIED:
            warnings.append(f"Slow test has no reason yet: {method}")
        elif reason not in REASONS:
            violations.append(f"Slow test has an invalid reason '{reason}': {method}")
        if seconds > over_limit and not entry.get("approved"):
            violations.append(f"Test is over {over_limit:g} ref s ({seconds:.1f}) and is not approved: {method}")
    for method in sorted(set(listed) - set(slow_methods)):
        if method in worst:
            warnings.append(f"Listed as slow but now {worst[method]:.1f} ref s. Remove it from the baseline: {method}")

    ranked = sorted(tests, key=lambda t: -t.seconds)[:top]
    over_warn = sum(1 for s in worst.values() if s > warn_limit)
    slow_total = sum(t.seconds for t in tests if t.method in slow_methods)
    lines = ["### Slowest tests", ""]
    unit_text = f"{unit * 1000:.3f} ms" if unit else "unknown"
    lines.append(f"Calibration unit: {unit_text}. Reference unit: "
                 f"{(reference_unit * 1000):.3f} ms." if reference_unit else f"Calibration unit: {unit_text}.")
    lines.append(f"Tests: {len(tests)}. Over {warn_limit:g} ref s: {over_warn}. "
                 f"Slow (over {slow_limit:g} ref s): {len(slow_methods)}, together {slow_total:.0f} s.")
    lines += ["", "| # | seconds | ref s | project | test | slow |", "|---:|---:|---:|---|---|---|"]
    for i, t in enumerate(ranked, 1):
        mark = "slow" if t.method in slow_methods else ""
        shown = t.name if len(t.name) <= 110 else t.name[:107] + "..."
        lines.append(f"| {i} | {t.seconds:.1f} | {ref(t.seconds):.1f} | {t.project} | `{shown}` | {mark} |")
    if violations:
        lines += ["", "### Ratchet violations", ""] + [f"- {v}" for v in violations]
    if warnings:
        lines += ["", "### Notes", ""] + [f"- {w}" for w in warnings]
    return Report("\n".join(lines) + "\n", violations, warnings, unit, slow_methods)


def seed_baseline(report: Report, baseline: dict, reference_unit: Optional[float]) -> dict:
    """A first baseline: every test that is slow now, with reason 'unclassified'. Owners fill in the reasons."""
    entries = [{"test": m, "reason": baseline["slow"].get(m, {}).get("reason", UNCLASSIFIED)}
               for m in sorted(report.slow_methods)]
    return {"schema": 1, "referenceUnitSeconds": reference_unit or baseline.get("referenceUnitSeconds"),
            "slowRefSeconds": baseline["slowRefSeconds"], "overRefSeconds": baseline["overRefSeconds"],
            "warnRefSeconds": baseline["warnRefSeconds"], "slow": entries}


def main(argv: Optional[list[str]] = None) -> int:
    ap = argparse.ArgumentParser(description=(__doc__ or "").splitlines()[0])
    ap.add_argument("--trx", nargs="+", required=True, help="TRX files or directories (searched recursively)")
    ap.add_argument("--baseline", type=Path, help="tests/slow-tests-baseline.json")
    ap.add_argument("--unit-seconds", type=float,
                    default=float(os.environ["NVT_CALIBRATION_UNIT_SECONDS"]) if os.environ.get(
                        "NVT_CALIBRATION_UNIT_SECONDS") else None,
                    help="Calibration unit of this machine in seconds (default: read from test output)")
    ap.add_argument("--top", type=int, default=20)
    ap.add_argument("--github-summary", action="store_true", help="append to $GITHUB_STEP_SUMMARY when it is set")
    ap.add_argument("--check", action="store_true", help="exit 1 on a ratchet violation")
    ap.add_argument("--require-trx", action="store_true", help="exit 2 when no TRX file is found")
    ap.add_argument("--seed-baseline", type=Path, help="write a first baseline with every slow test as 'unclassified'")
    args = ap.parse_args(argv)

    files = find_trx(args.trx)
    if not files:
        print("No TRX file found.", file=sys.stderr)
        return 2 if args.require_trx else 0
    tests: list[Timing] = []
    units: list[float] = []
    for f in files:
        t, u = parse_trx(f)
        tests += t
        units += u
    baseline = load_baseline(args.baseline)
    report = build_report(tests, units, baseline, unit_seconds=args.unit_seconds, top=args.top)
    print(report.markdown)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if args.github_summary and summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(report.markdown + "\n")
    if args.seed_baseline:
        seeded = seed_baseline(report, baseline, baseline.get("referenceUnitSeconds") or report.unit_seconds)
        args.seed_baseline.write_text(json.dumps(seeded, indent=2) + "\n", encoding="utf-8")
        print(f"Wrote {args.seed_baseline} ({len(seeded['slow'])} slow tests).")
    return 1 if (args.check and report.violations) else 0


if __name__ == "__main__":
    sys.exit(main())
