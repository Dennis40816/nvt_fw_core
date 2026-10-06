# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Offline parser, host-boundary and refill regressions; fixtures supplied by test-p0.sh."""
import contextlib
import io
import json
import os
import re
from pathlib import Path
import runpy
import subprocess
import sys
import unittest
from unittest.mock import patch



QPY, ROOT = map(Path, sys.argv[1:3])
sys.argv[1:] = []
q = runpy.run_path(str(QPY))
globals_q = q["validate"].__globals__
BRIEF = "Title: synthetic\nRisk: R0\nScope: tracked.txt\nAccept:\n$ git diff --check\n"


class P0(unittest.TestCase):
    def test_cleanup_pending_reserves_live_trees_across_sibling_queues(self):
        parent = ROOT / "cleanup-reserve"
        queue, sibling = parent / "q1", parent / "q2"
        for path in (queue, sibling):
            (path / "running").mkdir(parents=True)
            (path / "work").mkdir()
        (queue / "queue.env").write_text("TASK_RESERVE_GB=20\n", encoding="utf-8")
        (sibling / "queue.env").write_text("TASK_RESERVE_GB=40\n", encoding="utf-8")
        (queue / "running/transition.md").touch()
        retained = parent / "retained"
        retained.mkdir()
        other = parent / "other"
        other.mkdir()
        log = queue / "work/cleanup-pending.log"
        entry = f"transition\t{retained.as_posix()}\tcleanup pending: retained\n"
        log.write_text(entry + entry, encoding="utf-8")
        (sibling / "work/cleanup-pending.log").write_text(
            f"done-tree\t{other.as_posix()}\tcleanup pending: remove failed\n"
            f"stale\t{(parent / 'gone').as_posix()}\tcleanup pending: already removed\n", encoding="utf-8")
        self.assertEqual(q["required_free"](queue, "215", "20"), 280)
        (queue / "running/transition.md").unlink()
        self.assertEqual(q["required_free"](queue, "215", "20"), 280)
        retained.rmdir()
        self.assertEqual(q["required_free"](queue, "215", "20"), 260)
        other.rmdir()
        self.assertEqual(q["required_free"](queue, "215", "20"), 220)

    def test_cleanup_pending_malformed_log_blocks_disk_gate(self):
        queue = ROOT / "cleanup-malformed/q1"
        (queue / "running").mkdir(parents=True)
        (queue / "work").mkdir()
        (queue / "work/cleanup-pending.log").write_text("incomplete pending record\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "invalid cleanup-pending.log"):
            q["required_free"](queue, "215", "20")

    def test_risk_rules_missing_keeps_every_legacy_protection(self):
        queue = ROOT / "risk-missing"
        queue.mkdir()
        self.assertIsNone(q["risk_rules"](queue))
        protected = (".git", ".github", ".agents", ".codex", "governance", "firmware", "externaltools",
                     "profiles", "release", "releases", "signing", "permissions", "contracts", "schemas", "eng", "scripts")
        paths = [f"nested/{name}/help.md" for name in protected]
        paths += ["nested/" + name for name in ("AGENTS.md", "CLAUDE.md", "SPEC.md", "product-spec.md")]
        paths += [f"src/Project{suffix}/help.md" for suffix in (".Domain", ".Profiles")]
        paths += ["nested/file" + ext for ext in (".bin", ".hex", ".elf", ".srec", ".pfx", ".pem")]
        for path in paths:
            with self.subTest(path=path):
                self.assertEqual(q["risk_floor"](path, q["risk_rules"](queue)), 3)
        for path, expected in (("src/Logic.cs", 2), ("packages.lock.json", 2), ("docs/help.md", 0),
                               ("scripts-other/help.txt", 0), ("src/Project.DomainExtra/a.cs", 2)):
            self.assertEqual(q["risk_floor"](path, q["risk_rules"](queue)), expected)

    def test_risk_rule_forms_boundaries_and_queue_local_configs(self):
        queue = ROOT / "risk-forms"
        queue.mkdir()
        config = queue / "risk-floor.txt"
        config.write_text("# four supported forms\nR3 src/secret/\nR2 scripts\nR3 *.zip\n"
                          "R3 name-suffix:.Sensitive\nR2 .github\n", encoding="utf-8-sig")
        rules = q["risk_rules"](queue)
        for path, expected in (("src/secret/a.md", 3), ("src/secret", 3), ("src/secrets/a.md", 0),
                ("nested/src/secret/a.md", 0), ("nested/scripts/help.md", 2), ("scripts2/help.md", 0),
                ("a/ARCHIVE.ZIP", 3), ("a/archive.zip.md", 0), ("src/Foo.SENSITIVE/help.md", 3),
                ("src/Foo.SensitiveExtra/help.md", 0), (".github/workflows/build.yml", 2),
                (".github/file.pem", 3), (".github/firmware/help.md", 3), ("docs/product-spec.md", 0)):
            self.assertEqual(q["risk_floor"](path, rules), expected, path)
        config.write_text("# empty config deliberately opts into common rules only\n", encoding="utf-8")
        common = q["risk_rules"](queue)
        for name in ("profiles", "contracts", "schemas", "eng", "scripts", "Project.Domain", "Project.Profiles"):
            self.assertEqual(q["risk_floor"](f"src/{name}/help.md", common), 0)
        example = QPY.parent / "example-queue"
        self.assertEqual(q["accept_templates"](example), [])
        self.assertIsNotNone(q["risk_rules"](example))
        self.assertEqual(q["risk_floor"]("firmware/file.bin", q["risk_rules"](example)), 3)

    def test_risk_invalid_config_fails_all_gate_entries_without_writes(self):
        queue = ROOT / "risk-invalid"
        for state in (*q["STATES"], "work"):
            (queue / state).mkdir(parents=True)
        config = queue / "risk-floor.txt"
        for rule in ("R1 src/", "R4 src/", "r3 src/", "R3", "R3 ../src", "R3 ./src", "R3 /src",
                     "R3 " + ROOT.anchor.replace("\\", "/") + "src", "R3 src\\secret", "R3 src//secret", "R3 src/**", "R3 *.cs?",
                     "R3 *", "R3 name-suffix:", "R3 name-suffix:x/y", "R3 name-suffix:..",
                     "R3 src (note)", "R2 src/ # inline comment", "R2 src/\nR3 SRC/"):
            config.write_text("# line one\n" + rule + "\n", encoding="utf-8")
            with self.subTest(rule=rule), self.assertRaisesRegex(ValueError, r"risk-floor.txt line [23]"):
                q["risk_rules"](queue)
        config.write_bytes(b"R3 \xffPRIVATE_MARKER\n")
        with self.assertRaises(UnicodeError):
            q["risk_rules"](queue)
        config.write_text("# line one\nR9 src/\n", encoding="utf-8")
        brief = queue / "ready/invalid.md"
        brief.write_text(BRIEF, encoding="utf-8")
        refill = ROOT / "risk-invalid-out.md"
        refill.write_text(f"=====BRIEF valid=====\n{BRIEF}=====REPORT=====\nreport\n", encoding="utf-8")
        for policy in ("warn", "enforce"):
            (queue / "queue.env").write_text(f"ACCEPT_POLICY={policy}\n", encoding="utf-8")
            for cmd, args in (("check", []), ("check-diff", ["a" * 40]),
                              ("run-accept", [str(queue / "work/log")]), ("run-prebuild", [str(queue / "work/log")])):
                result = subprocess.run([sys.executable, "-I", "-B", str(QPY), cmd, str(brief), *args], capture_output=True)
                self.assertEqual(result.returncode, 1)
                self.assertIn(b"risk-floor.txt line 2", result.stderr)
            result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "split", str(refill),
                                     str(queue / "proposed")], capture_output=True)
            self.assertEqual(result.returncode, 1)
            self.assertIn(b"risk-floor.txt line 2", result.stderr)
            self.assertEqual(list((queue / "proposed").iterdir()), [])
            self.assertEqual(list((queue / "work").iterdir()), [])
            self.assertFalse((queue.parent / ".qworker-claim.lock").exists())

    def test_scope_validation_reports_source_lines_in_both_policies_and_split(self):
        queue = ROOT / "scope-early"
        (queue / "ready").mkdir(parents=True)
        (queue / "proposed").mkdir()
        brief = queue / "ready/scope.md"
        refill = ROOT / "scope-early-out.md"
        for policy in ("warn", "enforce"):
            (queue / "queue.env").write_text(f"ACCEPT_POLICY={policy}\n", encoding="utf-8")
            for scope in ("src/A.cs (only if PRIVATE_MARKER)", "src/A B.cs", "src/A\tB.cs", "src/*.cs",
                          "src/A?.cs", "src/[AB].cs", "src/{a,b}.cs", " src/A.cs", "src/A.cs ",
                          "../src", "./src", "src//a", "src/a//", "/src", ROOT.anchor.replace("\\", "/") + "src", "src\\a"):
                text = BRIEF.replace("Scope: tracked.txt", "Scope:\ntracked.txt\n" + scope)
                with self.subTest(policy=policy, scope=scope), self.assertRaisesRegex(ValueError, "Scope line 5"):
                    q["validate"](q["parse"](text), policy=policy)
                brief.write_text(text, encoding="utf-8")
                result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "check", str(brief)], capture_output=True)
                self.assertEqual(result.returncode, 1)
                self.assertIn(b"Scope line 5", result.stderr)
                self.assertNotIn(b"PRIVATE_MARKER", result.stderr)
                refill.write_text(f"=====BRIEF valid=====\n{BRIEF}=====BRIEF invalid=====\n{text}"
                                  "=====REPORT=====\nreport\n", encoding="utf-8")
                result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "split", str(refill),
                                         str(queue / "proposed")], capture_output=True)
                self.assertEqual(result.returncode, 1)
                self.assertIn(b"brief invalid: Scope line 5", result.stderr)
                self.assertEqual(list((queue / "proposed").iterdir()), [])
                self.assertFalse((queue / "work").exists())
                self.assertFalse((queue.parent / ".qworker-claim.lock").exists())
            for scope in ("", "  \t"):
                with self.assertRaisesRegex(ValueError, "Scope line 3: missing or empty"):
                    q["validate"](q["parse"](BRIEF.replace("Scope: tracked.txt", "Scope: " + scope)), policy=policy)
            with self.assertRaisesRegex(ValueError, "Scope: missing or empty"):
                q["validate"](q["parse"](BRIEF.replace("Scope: tracked.txt\n", "")), policy=policy)
            for scope in ("src/", "src/Project.Domain/Logic.cs", "docs/說明.md"):
                q["validate"](q["parse"](BRIEF.replace("Scope: tracked.txt", "Scope: " + scope)), policy=policy)

    def test_check_diff_uses_queue_rules_without_bypassing_scope_or_r3(self):
        queue = ROOT / "risk-diff"
        queue.mkdir()
        (queue / "risk-floor.txt").write_text("R2 src/Project.Domain/\n", encoding="utf-8")
        rules = q["risk_rules"](queue)
        path = "src/Project.Domain/Logic.cs"
        brief = q["parse"](BRIEF.replace("Risk: R0", "Risk: R2").replace("Scope: tracked.txt", "Scope: src/"))
        output = subprocess.CompletedProcess([], 0, stdout=(path + "\0").encode())
        with patch("subprocess.run", return_value=output):
            q["check_diff"](brief, "a" * 40, rules=rules)
            with self.assertRaisesRegex(ValueError, "host risk floor"):
                q["check_diff"](brief, "a" * 40)
        for path, error in (("src/Project.Domain/new.bin", "host risk floor"),
                            ("docs/outside.md", "outside Scope")):
            with patch("subprocess.run", return_value=subprocess.CompletedProcess([], 0, stdout=(path + "\0").encode())):
                with self.assertRaisesRegex(ValueError, error):
                    q["check_diff"](brief, "a" * 40, rules=rules)
        with patch("subprocess.run") as run, self.assertRaisesRegex(ValueError, "Scope line 3"):
            q["check_diff"](q["parse"](BRIEF.replace("tracked.txt", "src/A.cs (note)")), "a" * 40, rules=rules)
        run.assert_not_called()

    def test_r5_policy_defaults_literals_and_queue_local_split(self):
        queue = ROOT / "policy-queue"
        for state in (*q["STATES"], "work"):
            (queue / state).mkdir(parents=True)
        self.assertEqual(q["accept_policy"](queue), "warn")
        config = queue / "queue.env"
        for setting, expected in (("", "warn"), ("ACCEPT_POLICY=\n", "warn"),
                ("export ACCEPT_POLICY='enforce' # comment\n", "enforce"),
                ('ACCEPT_POLICY="warn"\n', "warn")):
            config.write_text(setting, encoding="utf-8-sig")
            self.assertEqual(q["accept_policy"](queue), expected)
        for setting in ("ACCEPT_POLICY=bad\n", "ACCEPT_POLICY=$MODE\n", "ACCEPT_POLICY=warn enforce\n",
                        "ACCEPT_POLICY='unclosed\n"):
            config.write_text(setting, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "ACCEPT_POLICY must"):
                q["accept_policy"](queue)
        text = BRIEF.replace("$ git diff --check", '$ python -c "pass" && true')
        brief = queue / "ready/policy.md"
        brief.write_text(text, encoding="utf-8")
        refill = ROOT / "policy-refill.md"
        refill.write_text(f"=====BRIEF policy-split=====\n{text}", encoding="utf-8")
        for policy, expected in (("warn", 0), ("enforce", 1)):
            config.write_text(f"ACCEPT_POLICY={policy}\n", encoding="utf-8")
            result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "check", str(brief)], capture_output=True)
            self.assertEqual(result.returncode, expected)
            result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "split", str(refill),
                                     str(queue / "proposed")], capture_output=True)
            self.assertEqual(result.returncode, expected)
            target = queue / "proposed/policy-split.md"
            self.assertEqual(target.exists(), policy == "warn")
            if target.exists():
                target.unlink()
        self.assertFalse((queue / "work/accept-violations.log").exists())

    def test_r5_multiple_test_invocations_cannot_hide_failures(self):
        queue = ROOT / "shell-tests"
        (queue / "work").mkdir(parents=True)
        (queue / "running").mkdir()
        (queue / "queue.env").write_text("ACCEPT_POLICY=warn\n", encoding="utf-8")
        mock = queue / "mock"
        mock.mkdir()
        executable = mock / "dotnet"
        executable.write_text('''#!/usr/bin/env bash
case "$2" in
  good.csproj) echo 'Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2'; exit 0;;
  zero.csproj) echo 'Passed! - Failed: 0, Passed: 0, Skipped: 0, Total: 0'; exit 0;;
  unknown.csproj) echo 'Build succeeded.'; exit 0;;
  error.csproj) echo 'Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2'; exit 7;;
esac
exit 91
''', encoding="utf-8", newline="\n")
        executable.chmod(0o700)
        env = dict(os.environ, PATH=str(mock) + os.pathsep + os.environ["PATH"])
        for index, (second, expected) in enumerate((("good", 0), ("zero", 1), ("unknown", 1), ("error", 1))):
            brief_path = queue / "running" / f"multiple-{index}.md"
            brief_path.write_text(BRIEF.replace("$ git diff --check",
                f"$ dotnet test good.csproj >/dev/null; dotnet test {second}.csproj | cat >/dev/null; true"), encoding="utf-8")
            prefix = queue / "work" / brief_path.stem
            result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "run-accept", str(brief_path),
                                     str(prefix)], env=env, capture_output=True)
            self.assertEqual(result.returncode, expected, result.stderr.decode("utf-8"))
            logs = list(Path(f"{prefix}-accept-1-tests").glob("test.*"))
            self.assertEqual(len(logs), 2)
            self.assertIn(b"tests=4" if expected == 0 else b"tests=0", result.stdout)
        # A skipped invocation cannot reuse another invocation's positive evidence.
        for index, command in enumerate(("false && dotnet test good.csproj; true",
                "dotnet test good.csproj; false && dotnet test good.csproj; true")):
            brief_path = queue / "running" / f"skipped-{index}.md"
            brief_path.write_text(BRIEF.replace("$ git diff --check", "$ " + command), encoding="utf-8")
            result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "run-accept", str(brief_path),
                                     str(queue / "work" / brief_path.stem)], env=env, capture_output=True)
            self.assertEqual(result.returncode, 1)
        # Concurrent workers append whole records without a long queue lock.
        runs = []
        for index in range(2):
            brief_path = queue / "running" / f"concurrent-{index}.md"
            brief_path.write_text(BRIEF.replace("$ git diff --check", '$ python -B -c "pass"'), encoding="utf-8")
            runs.append(subprocess.Popen([sys.executable, "-I", "-B", str(QPY), "run-accept", str(brief_path),
                                          str(queue / "work" / brief_path.stem)], env=env,
                                         stdout=subprocess.PIPE, stderr=subprocess.PIPE))
        for run in runs:
            run.communicate(timeout=20)
            self.assertEqual(run.returncode, 0)
        records = (queue / "work/accept-violations.log").read_text(encoding="utf-8").splitlines()
        self.assertEqual(len(records), 8)
        self.assertEqual(len({row.split("\t")[1] for row in records}), 8)
        self.assertTrue(all(len(row.split("\t")) == 4 for row in records))
        self.assertTrue(all("Accept line=5" in row for row in records))
        self.assertTrue(all("csproj" not in row for row in records))

    def test_r4_queue_templates_argv_and_positive_evidence(self):
        queue = ROOT / "extra-templates"
        queue.mkdir()
        config = queue / "accept-templates.txt"
        config.write_text("# synthetic only\n\n"
            "python -I scripts/check.py --structure-only\n"
            "bash scripts/check.sh\n"
            "test -f {path}\n"
            "test -s {path}\n"
            "pwsh -NoProfile -File ./scripts/check.ps1 -ValidateOnly\n"
            "dotnet test {project} -c Release --filter {filter}\n"
            "dotnet format {project} style --include {paths}\n", encoding="utf-8-sig")
        forms = q["accept_templates"](queue)
        commands = ["python scripts/check.py --structure-only", "bash scripts/check.sh",
                    "test -f docs/check.md", "test -s docs/check.md",
                    "pwsh -NoProfile -File ./scripts/check.ps1 -ValidateOnly",
                    'dotnet test tests/a.csproj -c Release --filter "Name=has space"',
                    "dotnet format a.csproj style --include src/a.cs src/b.cs"]
        calls = []
        def fake_run(args, **kwargs):
            self.assertFalse(kwargs["shell"])
            calls.append(args)
            return subprocess.CompletedProcess(args, 0,
                stdout=b"Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2\n")
        brief = dict(Risk="R0", Scope="tracked.txt", Accept="\n".join("$ " + c for c in commands), Prebuild="Desktop")
        with patch("subprocess.run", fake_run), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(q["run_accept"](brief, ROOT / "extra-log", templates=forms), 0)
            self.assertEqual(q["run_prebuild"](brief, ROOT / "extra-log", templates=forms), 0)
        self.assertEqual(calls[0], ["python", "-I", "-B", "scripts/check.py", "--structure-only"])
        self.assertEqual(calls[-2][-2:], ["src/a.cs", "src/b.cs"])
        self.assertEqual(calls[-1][:2], ["dotnet", "build"])
        repo = ROOT / "isolated-script"
        (repo / "scripts").mkdir(parents=True)
        (repo / "scripts/check.py").write_text(
            "import sys\nassert sys.flags.isolated == 1\nassert sys.dont_write_bytecode\n"
            "print('isolated script passed')\n", encoding="utf-8")
        previous = Path.cwd()
        try:
            os.chdir(repo)
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(q["run_accept"]({"Risk": "R0", "Scope": "tracked.txt", "Accept": "$ " + commands[0]},
                                               ROOT / "isolated-real", templates=forms), 0)
        finally:
            os.chdir(previous)
        with patch("subprocess.run", return_value=subprocess.CompletedProcess([], 0, stdout=b"No tests found")), \
                contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(q["run_accept"]({"Risk": "R0", "Scope": "tracked.txt", "Accept": "$ " + commands[5]},
                                           ROOT / "extra-zero", templates=forms), 1)
        # Whole-list validation must prevent even the first harmless command from running.
        with patch("subprocess.run") as run, self.assertRaises(ValueError):
            q["run_accept"]({"Risk": "R0", "Scope": "tracked.txt", "Accept": "$ git diff --check\n$ test -f ../bad"},
                            ROOT / "extra-bad", templates=forms)
        run.assert_not_called()

    def test_r4_templates_reject_injection_paths_and_bad_configs(self):
        queue = ROOT / "unsafe-templates"
        queue.mkdir()
        config = queue / "accept-templates.txt"
        config.write_text("test -f {path}\ndotnet test {project} -c Release --filter {filter}\n", encoding="utf-8")
        forms = q["accept_templates"](queue)
        for command in ("test -f a;echo bad", "test -f a|echo bad", "test -f a&&echo bad",
                        "test -f a > bad", "test -f a 2>>bad", "test -f `bad`", 'test -f "$(bad)"',
                        "test -f ../bad", "test -f docs/a..b", "test -f /absolute", "test -f " + ROOT.anchor.replace("\\", "/") + "absolute",
                        "test -f " + ROOT.anchor + "absolute", "test -f //server/share", "test -f -option", "test -f $TEMP/file",
                        "test -f 'docs/a;bad'", 'dotnet test a.csproj -c Release --filter "Name~a|Name~b"',
                        'dotnet test a.csproj -c Release --filter "$(bad)"', "python -c pass", "bash -c bad",
                        "pwsh -NoProfile -Command bad", "test -f a extra",
                        "dotnet test a..b.csproj --no-restore", "pwsh -NoProfile -File ./scripts/a..b.ps1"):
            with self.subTest(command=command), self.assertRaises(ValueError):
                q["accept_argv"](command, forms)
        for form in ("python -I -c pass", "python -I -m os", "bash -c bad",
                     "pwsh -NoProfile -Command bad", "evil {path}", "git add -A", "test -f ../bad",
                     "test -f /absolute", "test -f " + ROOT.anchor.replace("\\", "/") + "absolute", "test -f $TEMP/a",
                     "python -I ../check.py", "bash scripts/a.sh -c bad", "test -f {unknown}",
                     "dotnet format {project} --include {paths} extra", "test -f a;bad", 'test -f "a|b"'):
            config.write_text(form + "\n", encoding="utf-8")
            with self.subTest(form=form), self.assertRaisesRegex(ValueError, "accept-templates.txt line 1"):
                q["accept_templates"](queue)
        config.write_bytes(b"test -f \xff")
        with self.assertRaises(UnicodeError):
            q["accept_templates"](queue)

    def test_r4_missing_config_defaults_and_queue_local_cli_split(self):
        queue = ROOT / "template-cli"
        other = ROOT / "template-other"
        for parent in (queue, other):
            for state in (*q["STATES"], "work"):
                (parent / state).mkdir(parents=True)
            (parent / "queue.env").write_text("ACCEPT_POLICY=enforce\n", encoding="utf-8")
        self.assertEqual(q["accept_templates"](queue), [])
        self.assertEqual(q["accept_argv"]("git diff --check", q["accept_templates"](queue)), ["git", "diff", "--check"])
        for command in ("test -f tracked.txt", "dotnet test a.csproj -c Release --filter Name=a"):
            with self.assertRaises(ValueError):
                q["accept_argv"](command, q["accept_templates"](queue))
        text = BRIEF.replace("$ git diff --check", "$ test -f tracked.txt")
        for parent in (queue, other):
            (parent / "ready/task.md").write_text(text, encoding="utf-8")
        (queue / "accept-templates.txt").write_text("test -f {path}\n", encoding="utf-8")
        for parent, expected in ((queue, 0), (other, 1)):
            result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "check", str(parent / "ready/task.md")],
                                    capture_output=True)
            self.assertEqual(result.returncode, expected)
        refill = ROOT / "template-refill.md"
        refill.write_text(f"=====BRIEF custom=====\n{text}", encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            q["split"](refill, queue / "proposed")
        self.assertTrue((queue / "proposed/custom.md").is_file())
        with self.assertRaises(ValueError):
            q["split"](refill, other / "proposed")
        self.assertFalse((other / "proposed/custom.md").exists())

    def test_u1_u5_templates_and_host_execution(self):
        for command in ("echo injected", "git diff --check; echo bad", "python -c pass",
                        "dotnet test -evil.csproj --no-restore",
                        "dotnet test a.csproj --no-restore -p:CustomAfterMicrosoftCommonTargets=evil",
                        "pwsh -NoProfile -Command bad", "pwsh -NoProfile -File ./scripts/../evil.ps1",
                        "pwsh -NoProfile -File ./scripts/test.ps1 -Command bad",
                        "pwsh -NoProfile -File ./scripts/test.ps1; echo injected"):
            with self.subTest(command=command), self.assertRaises(ValueError):
                q["accept_argv"](command)
        commands = q["validate"](q["parse"](BRIEF + "$ dotnet test a.csproj --no-restore\n"))
        self.assertEqual(commands[1][0], "dotnet")
        self.assertEqual(q["accept_argv"]("pwsh -NoProfile -File ./scripts/verify.ps1 -StructureOnly"),
                         ["pwsh", "-NoProfile", "-File", "./scripts/verify.ps1", "-StructureOnly"])
        for name in ("../evil", "-option", "Desktop;echo", "a/b", "a.b", "$(bad)"):
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "Prebuild"):
                q["validate"](q["parse"](BRIEF + "Prebuild: " + name + "\n"))
        prebuild = q["prebuild_commands"](q["parse"](BRIEF + "Prebuild: Desktop Bootstrap Architecture UiSmoke\n"))
        self.assertEqual(len(prebuild), 4)
        self.assertEqual(prebuild[0][2], "src/Project.Desktop/Project.Desktop.csproj")
        self.assertEqual(prebuild[1][2], "tests/Project.Bootstrap.Tests/Project.Bootstrap.Tests.csproj")

    def test_r3_host_children_remove_six_environment_variables(self):
        real_run = subprocess.run
        calls = []
        child = ("import os,sys; "
                 f"assert not any(k.upper() in {q['HOST_SECRET_VARS']!r} for k in os.environ); "
                 "print('Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2')")
        def fake_run(args, **kwargs):
            self.assertFalse(kwargs["shell"])
            self.assertFalse(q["HOST_SECRET_VARS"].intersection(k.upper() for k in kwargs["env"]))
            calls.append(args)
            return real_run([sys.executable, "-I", "-B", "-c", child], **kwargs)
        text = (BRIEF + "$ dotnet build a.csproj --no-restore\n$ dotnet test a.csproj --no-restore\n"
                "$ pwsh -NoProfile -File ./scripts/check.ps1\nPrebuild: Desktop Bootstrap\n")
        synthetic = dict.fromkeys(q["HOST_SECRET_VARS"], "SYNTHETIC_TEST_VALUE")
        with patch.dict(os.environ, synthetic), patch("subprocess.run", fake_run):
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(q["run_accept"](q["parse"](text), ROOT / "host-env"), 0)
            self.assertEqual(q["run_prebuild"](q["parse"](text), ROOT / "host-env"), 0)
            self.assertTrue(all(os.environ[k] == v for k, v in synthetic.items()))
        self.assertEqual([a[0] for a in calls], ["git", "dotnet", "dotnet", "pwsh", "dotnet", "dotnet"])
        with patch("subprocess.run", return_value=subprocess.CompletedProcess([], 9)) as run:
            self.assertEqual(q["run_prebuild"](q["parse"](text), ROOT / "prebuild-failure"), 9)
        self.assertEqual(run.call_count, 1)

    def test_r3_quota_json_boundary(self):
        import json
        for used, age, expected in ((100, 10, 3), (101, 0, 3), (100, 10.01, 0), (99.9, 0, 0)):
            with self.subTest(used=used, age=age):
                self.assertEqual(q["quota_result"](json.dumps(dict(used_percent=used, sample_age_min=age,
                                                                  status="EXHAUSTED"))), expected)
        for text in ("", "not JSON", "{}", '{"status":"NO_DATA"}',
                     '{"status":"OK","used_percent":100,"sample_age_min":null}',
                     '{"status":"OK","used_percent":true,"sample_age_min":0}',
                     '{"status":"OK","used_percent":100,"sample_age_min":-1}',
                     '{"status":"OK","used_percent":NaN,"sample_age_min":0}',
                     '{"status":"OK","used_percent":100,"sample_age_min":Infinity}',
                     '{"status":"OK","used_percent":"100","sample_age_min":0}',
                     '{}\n{}'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                q["quota_result"](text)

    def test_u2_risk_syntax_and_host_floors(self):
        for risk in ("", "r3", "R3", "R4", "R1\nRisk: R0", "R0\nR3"):
            with self.subTest(risk=risk), self.assertRaises(ValueError):
                q["validate"](q["parse"](BRIEF.replace("Risk: R0", "Risk: " + risk)))
        for path in (".github/workflows/build.yml", "AGENTS.md", "docs/governance/rule.md",
                     "src/Project.Infrastructure/ExternalTools/a.cs", "firmware/new.bin",
                     "src/Project.Domain/a.cs", "docs/product-spec.md"):
            self.assertEqual(q["risk_floor"](path), 3, path)
        self.assertEqual(q["risk_floor"]("src/Logic.cs"), 2)
        self.assertEqual(q["risk_floor"]("packages.lock.json"), 2)
        self.assertEqual(q["risk_floor"]("docs/help.md"), 0)

    def test_u3_isolated_python_u12_utf8_cli(self):
        poison = ROOT / "poison"
        poison.mkdir()
        marker = poison / "executed"
        (poison / "sitecustomize.py").write_text(
            f"from pathlib import Path\nPath({str(marker)!r}).touch()\n", encoding="utf-8")
        (poison / "subprocess.py").write_text("raise RuntimeError('injected import')\n", encoding="utf-8")
        brief = ROOT / "utf8.md"
        brief.write_text(BRIEF.replace("tracked.txt", "文件.txt"), encoding="utf-8")
        env = dict(os.environ, PYTHONPATH=str(poison), PYTHONIOENCODING="cp950")
        result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "get", str(brief), "Scope"],
                                env=env, cwd=poison, capture_output=True, check=True)
        self.assertEqual(result.stdout.decode("utf-8").strip(), "文件.txt")
        self.assertFalse(marker.exists())
        self.assertFalse((poison / "__pycache__").exists())
        # Also force cp950 without -I to exercise q.py's explicit stream encoding.
        env.pop("PYTHONPATH")
        result = subprocess.run([sys.executable, "-B", str(QPY), "get", str(brief), "Scope"],
                                env=env, cwd=ROOT, capture_output=True, check=True)
        self.assertEqual(result.stdout.decode("utf-8").strip(), "文件.txt")

    def test_u6_local_prompt_preserves_owner_authority(self):
        self.assertIn("Only the owner can authorize R3", q["COMMON"])
        self.assertIn("queue briefs never grant those permissions", q["COMMON"])

    def test_u7_u12_utf8_output_and_safe_summary(self):
        real_run = subprocess.run
        def fake_run(args, **kwargs):
            self.assertFalse(kwargs["shell"])
            self.assertNotIn("encoding", kwargs)  # Decode bytes in the main thread, also on Windows.
            return real_run([sys.executable, "-I", "-B", "-c",
                             "import sys;sys.stdout.buffer.write('合成診斷 SENTINEL_PRIVATE\\n'.encode('utf-8'))"], **kwargs)
        summary = io.StringIO()
        with patch("subprocess.run", fake_run), contextlib.redirect_stdout(summary):
            self.assertEqual(q["run_accept"](q["parse"](BRIEF), ROOT / "utf8-log"), 0)
        self.assertIn("合成診斷", (ROOT / "utf8-log-accept-1.log").read_text(encoding="utf-8"))
        self.assertIn("template=diff-check, exit=0", summary.getvalue())
        self.assertNotIn("SENTINEL_PRIVATE", summary.getvalue())
        self.assertNotIn("git diff", summary.getvalue())
        def bad_encoding(args, **kwargs):
            return real_run([sys.executable, "-I", "-B", "-c", "import sys;sys.stdout.buffer.write(b'\\xff')"], **kwargs)
        with patch("subprocess.run", bad_encoding), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(q["run_accept"](q["parse"](BRIEF), ROOT / "bad-encoding"), 1)

    def test_split_report_separates_all_briefs(self):
        queue = ROOT / "report-queue"
        (queue / "proposed").mkdir(parents=True)
        refill = ROOT / "refill-1005-0300-out.md"
        body = BRIEF + "Goal: synthetic goal\nNot in scope: unrelated changes\n"
        report = "盤點：2 件可派\nneeds a dependency：none\n\n"
        refill.write_text(f"=====BRIEF first=====\n{body}\n=====BRIEF last=====\n{body}\n"
                          f"=====REPORT=====\n{report}", encoding="utf-8")
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            q["split"](refill, queue / "proposed")
        self.assertIn("2 brief(s)", output.getvalue())
        self.assertEqual(sorted(p.name for p in (queue / "proposed").iterdir()), ["first.md", "last.md"])
        for slug in ("first", "last"):
            self.assertEqual((queue / "proposed" / f"{slug}.md").read_text(encoding="utf-8"), body)
        self.assertEqual((queue / "work/refill-1005-0300-report.md").read_text(encoding="utf-8"), report)
        self.assertNotIn(report, output.getvalue())

    def test_split_without_report_preserves_legacy_behavior(self):
        queue = ROOT / "legacy-report-queue"
        (queue / "proposed").mkdir(parents=True)
        refill = ROOT / "legacy-refill.md"
        body = BRIEF + "Not in scope: unrelated changes\n盤點：legacy report\n"
        refill.write_text(f"=====BRIEF legacy=====\n{body}", encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            q["split"](refill, queue / "proposed")
        self.assertEqual((queue / "proposed/legacy.md").read_text(encoding="utf-8"), body)
        self.assertEqual(q["parse"](body)["Not in scope"], "unrelated changes\n盤點：legacy report")
        self.assertFalse((queue / "work").exists())
        for text in ("", "盤點：before brief\n=====BRIEF rejected=====\n" + BRIEF):
            refill.write_text(text, encoding="utf-8")
            with self.subTest(text=text), self.assertRaises(ValueError):
                q["split"](refill, queue / "proposed")
        self.assertFalse((queue / "proposed/rejected.md").exists())

    def test_split_report_only_cli_returns_zero(self):
        queue = ROOT / "report-only-queue"
        (queue / "proposed").mkdir(parents=True)
        refill = ROOT / "refill-only-out.md"
        report = "盤點：無可派工作\nGoal: report goal\nRisk: R3\nAccept:\n"
        refill.write_text("=====REPORT=====\n" + report, encoding="utf-8")
        result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "split", str(refill),
                                 str(queue / "proposed")], capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(b"0 brief(s)", result.stdout)
        self.assertEqual(list((queue / "proposed").iterdir()), [])
        self.assertEqual((queue / "work/refill-only-report.md").read_text(encoding="utf-8"), report)

    def test_split_report_fields_and_markers_do_not_enter_prompt(self):
        queue = ROOT / "report-fields-queue"
        (queue / "proposed").mkdir(parents=True)
        refill = ROOT / "refill-fields.md"
        body = BRIEF + "Goal: synthetic goal\nNot in scope: unrelated changes\n"
        report = ("Goal: report-only goal\nTitle: report-only title\nRisk: R3\nScope: report-only path\n"
                  "Accept:\n$ report-only command\nNot in scope: report-only prose\nMystery: report-only field\n"
                  "## Result (done)\n=====BRIEF report-only=====\n=====REPORT=====\nreport-only tail\n")
        refill.write_text(f"=====BRIEF clean=====\n{body}=====REPORT=====\n{report}", encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            q["split"](refill, queue / "proposed")
        saved = (queue / "proposed/clean.md").read_text(encoding="utf-8")
        self.assertEqual(saved, body)
        self.assertEqual(list((queue / "proposed").iterdir()), [queue / "proposed/clean.md"])
        self.assertEqual((queue / "work/refill-fields-report.md").read_text(encoding="utf-8"), report)
        self.assertNotIn("report-only", q["prompt"](q["parse"](saved), "worktree", "branch", "base"))

    def test_split_report_requires_standalone_line_and_valid_briefs(self):
        queue = ROOT / "report-boundary-queue"
        (queue / "proposed").mkdir(parents=True)
        refill = ROOT / "refill-boundary-out.md"
        body = BRIEF + "Not in scope: inline =====REPORT=====\n  =====REPORT=====\n"
        refill.write_text(f"=====BRIEF literal=====\n{body}", encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            q["split"](refill, queue / "proposed")
        self.assertEqual((queue / "proposed/literal.md").read_text(encoding="utf-8"), body)
        self.assertFalse((queue / "work").exists())
        refill.write_text(f"=====BRIEF good=====\n{BRIEF}=====BRIEF bad=====\n"
                          "Risk: R3\n=====REPORT=====\nGoal: report prose\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            q["split"](refill, queue / "proposed")
        self.assertFalse((queue / "proposed/good.md").exists())
        self.assertFalse((queue / "work").exists())
        refill.write_text("=====REPORT=====", encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            q["split"](refill, queue / "proposed")
        self.assertEqual((queue / "work/refill-boundary-report.md").read_text(encoding="utf-8"), "")

    def test_u9_u16_atomic_prevalidation_and_all_state_duplicates(self):
        queue = ROOT / "split-queue"
        for state in (*q["STATES"], "work"):
            (queue / state).mkdir(parents=True)
        refill = ROOT / "refill.md"
        for bad in ("CON", "aux.log", "COM1", "a" * 303, "a..b", "trailing.", "../escape", "has space"):
            refill.write_text(f"=====BRIEF good=====\n{BRIEF}=====BRIEF {bad}=====\n{BRIEF}", encoding="utf-8")
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                q["split"](refill, queue / "proposed")
            self.assertEqual(list((queue / "proposed").iterdir()), [])
        for state in q["STATES"]:
            existing = queue / state / "Old.md"
            existing.write_text("preserve", encoding="utf-8")
            refill.write_text(f"=====BRIEF good=====\n{BRIEF}=====BRIEF old=====\n{BRIEF}", encoding="utf-8")
            with self.subTest(state=state), self.assertRaises(ValueError):
                q["split"](refill, queue / "proposed")
            self.assertFalse((queue / "proposed/good.md").exists())
            self.assertEqual(existing.read_text(), "preserve")
            existing.unlink()
        refill.write_text(f"=====BRIEF twin=====\n{BRIEF}=====BRIEF TWIN=====\n{BRIEF}", encoding="utf-8")
        with self.assertRaises(ValueError):
            q["split"](refill, queue / "proposed")
        with self.assertRaises(ValueError):
            q["task_name"]("valid", queue / ("x" * 240))
        refill.write_text(f"=====BRIEF first=====\n{BRIEF}=====BRIEF second=====\n{BRIEF}", encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            q["split"](refill, queue / "proposed")
        self.assertEqual(sorted(p.name for p in (queue / "proposed").iterdir()), ["first.md", "second.md"])
        refill.write_text(f"=====BRIEF concurrent=====\n{BRIEF}", encoding="utf-8")
        argv = [sys.executable, "-I", "-B", str(QPY), "split", str(refill), str(queue / "proposed")]
        runs = [subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=subprocess.PIPE) for _ in range(2)]
        for run in runs:
            run.communicate(timeout=20)
        self.assertEqual(sorted(run.returncode for run in runs), [0, 1])
        self.assertEqual((queue / "proposed/concurrent.md").read_text(encoding="utf-8"), BRIEF)

    def test_u12_u42_parse_failures_and_document_checks(self):
        for text in ("SchemaVersion: 99\n" + BRIEF, "Mystery: yes\n" + BRIEF,
                     BRIEF + "Mystery: yes\n", BRIEF + "Risk: R1\n", "preamble\n" + BRIEF,
                     BRIEF + "## Result (done)\n"):
            with self.assertRaises(ValueError):
                q["parse"](text)
        self.assertEqual(q["parse"]("Goal: example\n  Body: an indented prose line\n")["Goal"],
                         "example\n  Body: an indented prose line")
        self.assertEqual(q["validate"](q["parse"](BRIEF)), [["git", "diff", "--check"]])
        for accept in ("", "$", "prose only"):
            with self.assertRaises(ValueError):
                q["validate"]({"Risk": "R0", "Accept": accept})
        bad = ROOT / "invalid-utf8.md"
        bad.write_bytes(b"Title: \xffSENTINEL_PRIVATE")
        result = subprocess.run([sys.executable, "-I", "-B", str(QPY), "check", str(bad)], capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn(b"SENTINEL_PRIVATE", result.stderr)


unittest.main(verbosity=2)
