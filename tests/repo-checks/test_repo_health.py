# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Offline H02 syntax and baseline regressions, using temporary C# Git trees."""
from __future__ import annotations

import copy
import json
import subprocess
import unittest
from pathlib import Path

from test_doc_sync import GitRepositoryTests

REPOSITORY = Path(__file__).resolve().parents[2]
CHECKER = REPOSITORY / "tools/repo-checks/repo-health.ps1"
BASELINE = "eng/code-health/baseline.json"
TEST_DEBT = "eng/code-health/test-debt.json"
TEST_RULES = ("timeWaitsInTests", "elapsedAssertionsInTests", "tempPathInTests", "sourceTextReadsInTests",
              "headlessSessionSetups", "childProcessInTests", "sameNameFakes")


GENERATED_BLOCK = ("  <!-- BEGIN GENERATED HEALTH WARNING IDS -->\n{}"
                   "  <!-- END GENERATED HEALTH WARNING IDS -->\n")


class RepoHealthTests(GitRepositoryTests):
    def setUp(self) -> None:
        super().setUp()
        self.fixture_checker = self.root / "fixture-checker.ps1"
        source = CHECKER.read_text(encoding="utf-8")
        source = source.replace("if ($Mode -notin @('Measure', 'EnrollTestDebt')) { Initialize-Enforcement $baseline }",
            "if ($baseline -and @($baseline.findings + $allowed.findings | Where-Object { $_.rule -notin $script:SyntaxRules }).Count) { throw 'HC_NOT_IMPLEMENTED: fixture has no diagnostic provider' }")
        source = source.replace("else { Measure-Health }", "else { Measure-Syntax }")
        # Pin the removeBy clock, so the fixtures do not expire with the real date.
        clock = "function Get-HealthToday { return [DateTime]::UtcNow.Date }"
        self.assertEqual(1, source.count(clock))
        source = source.replace(clock, "function Get-HealthToday { return [DateTime]'2026-10-10' }")
        schema_path = (REPOSITORY / "tools/repo-checks/csharp/schema.json").as_posix()
        source = source.replace("Join-Path $PSScriptRoot 'csharp/schema.json'", "'" + schema_path + "'")
        self.fixture_checker.write_text(source, encoding="utf-8")
        self.write("global.json", (REPOSITORY / "global.json").read_text(encoding="utf-8"))
        self.write("src/Fixture/Fixture.csproj",
                   '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                   '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        self.write("src/Fixture/One.cs", "namespace N; class One {}\n")
        self.write("eng/code-health/projects.props", "<Project>\n" + GENERATED_BLOCK.format("") + "</Project>\n")

    def set_today(self, date: str) -> None:
        source = self.fixture_checker.read_text(encoding="utf-8")
        start = source.index("function Get-HealthToday { return [DateTime]'")
        end = source.index("' }", start)
        head = "function Get-HealthToday { return [DateTime]'"
        self.fixture_checker.write_text(source[:start] + head + date + source[end:], encoding="utf-8")

    def run_health(self, mode: str = "Measure", *args: str) -> subprocess.CompletedProcess[str]:
        # Ordinary subprocesses use the current console; no detached window.
        return subprocess.run(["pwsh", "-NoProfile", "-File", str(self.fixture_checker), "-Mode", mode,
                               "-Repo", "core", "-Root", str(self.root), *args],
                              env=self.env, capture_output=True, encoding="utf-8", timeout=90)

    def measure(self) -> dict:
        self.git("add", "-A")
        result = self.run_health()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        return json.loads(result.stdout)

    def enroll(self) -> dict:
        # Syntax fixture enrollment is hand-built; production Enroll requires all providers.
        m = self.measure()
        b = {key: copy.deepcopy(m[key]) for key in
             ("schemaVersion", "measurementVersion", "snapshotCommit", "limits")}
        b["entities"] = []
        for e in m["entities"]:
            ceilings = {key: value for key, value in e["values"].items()
                        if value > b["limits"][key]}
            if ceilings:
                b["entities"].append({key: copy.deepcopy(e[key]) for key in
                                      ("project", "kind", "symbol", "locations", "contentHash")}
                                     | {"ceilings": ceilings, "owner": "fixture", "issue": "fixture/1"})
        b["findings"] = [{key: copy.deepcopy(f[key]) for key in
                          ("rule", "project", "path", "member", "symbol", "syntaxHash", "count")}
                         | {"owner": "fixture", "removeBy": "2026-10-14"}
                         for f in m["findings"] if f["rule"] not in TEST_RULES]
        self.write_baseline(b)
        self.base = self.commit()
        return b

    def write_baseline(self, b: dict) -> None:
        self.write(BASELINE, json.dumps(b, indent=2) + "\n")

    def hot(self, lines: int = 808, name: str = "Hot", path: str = "src/Fixture/One.cs") -> None:
        self.write(path, f"namespace N; class {name} {{\n" + "// blank/comment debt\n" * (lines - 2) + "}\n")

    def assert_exit(self, code: int, result: subprocess.CompletedProcess[str], text: str = "") -> None:
        self.assertEqual(code, result.returncode, result.stdout + result.stderr)
        if text:
            self.assertIn(text, result.stdout)

    def test_async_methods_local_functions_and_void_delegate_lambdas(self) -> None:
        self.write("src/Fixture/One.cs", '''using System; using System.Threading.Tasks;
namespace N;
delegate void Sink();
class One {
    string words = "async void Fake() {} Action a = async () => {};";
    // async void Comment() {}
    async void Declared() { async void Local() { await Task.Yield(); } await Task.Yield(); }
    void Run() {
        Action a = async () => { await Task.Yield(); };
        Sink s = async delegate { await Task.Yield(); };
        Func<Task> task = async () => { await Task.Yield(); };
        Task.Run(async () => { await Task.Yield(); });
    }
}
''')
        m = self.measure()
        self.assertEqual("roslyn-physical-v1", m["measurementVersion"])
        self.assertEqual(4, m["summary"]["asyncVoid"])
        f = [f for f in m["findings"] if f["rule"] == "asyncVoid"]
        self.assertTrue(any("local:Local" in x["member"] for x in f))
        self.assertTrue(any("N.Sink" in x["symbol"] for x in f))

    def test_async_lambda_argument_of_unresolved_call_is_not_a_finding(self) -> None:
        self.write("src/Fixture/One.cs", "class One { void Run() { External(async () => {}); } }")
        self.assertEqual([], self.measure()["findings"])

    def test_async_lambda_subscription_with_unresolved_delegate_is_candidate(self) -> None:
        self.write("src/Fixture/One.cs", "class One { void Run(Window w) { w.Opened += async (_, _) => {}; } }")
        f = self.measure()["findings"]
        self.assertEqual(1, len(f))
        self.assertEqual("asyncVoid", f[0]["rule"])
        self.assertTrue(f[0]["candidate"])

    def test_async_lambda_resolves_through_sdk_implicit_usings(self) -> None:
        # No using directive: Task, Func and SendOrPostCallback resolve only through the implicit usings.
        self.write("src/Fixture/One.cs", '''namespace N;
class One {
    void Run(SynchronizationContext context) {
        Task.Run(async () => { await Task.Yield(); });
        Func<Task> work = async () => { await Task.Yield(); };
        context.Post(async _ => { await Task.Yield(); }, null);
    }
}
''')
        f = [f for f in self.measure()["findings"] if f["rule"] == "asyncVoid"]
        self.assertEqual(1, len(f))
        self.assertIn("SendOrPostCallback", f[0]["symbol"])
        self.assertFalse(f[0]["candidate"])

    def test_blocking_candidates_ignore_comments_and_literals(self) -> None:
        self.write("src/Fixture/One.cs", '''class One {
    void M(dynamic task) {
        // task.Result; task.Wait(); task.GetAwaiter().GetResult();
        string s = "Task.WaitAll(); task.Result";
        var a = task.Result; var b = task?.Result;
        task.Wait(); Task.WaitAll(); Task.WaitAny(); task.GetAwaiter().GetResult();
        task.GetResult();
    }
}''')
        m = self.measure()
        self.assertEqual(6, m["summary"]["blockingWait"])
        self.assertTrue(all(f["candidate"] for f in m["findings"]))

    def test_state_partials_nested_generics_and_generated_observable_backing(self) -> None:
        self.write("src/Fixture/One.cs", '''namespace N;
partial class One<T> {
    [ObservableProperty] int _value;
    int a, b; readonly int r; const int c = 1;
    int Auto { get; set; } int Init { get; init; }
    int Manual { get { return 1; } set {} }
    partial int Partial { get; set; }
    class Nested { int own; }
    string text => "int fake; class Nested {}";
}
class One { readonly System.Collections.Generic.List<int> values = new(); }
record Position(int X, int Y);
''')
        self.write("src/Fixture/Part.cs", '''namespace N; partial class One<T> {
    int other;
    partial int Partial { get => other; set => other = value; }
}''')
        types = {e["symbol"]: e for e in self.measure()["entities"] if e["kind"] == "type"}
        self.assertEqual(7, types["N.One`1"]["values"]["stateMembers"])
        self.assertEqual(2, types["N.One`1"]["values"]["partialFiles"])
        self.assertEqual(1, types["N.One`1.Nested"]["values"]["stateMembers"])
        self.assertEqual(0, types["N.One"]["values"]["stateMembers"])
        self.assertEqual(2, types["N.Position"]["values"]["stateMembers"])

    def test_physical_lines_include_blank_comment_and_raw_string_lines(self) -> None:
        source = 'class One { string s = """\nclass Fake {\n\n}\n"""; }\n\n// final\n'
        self.write("src/Fixture/One.cs", source)
        m = self.measure()
        self.assertEqual(len(source.splitlines()), m["summary"]["fileLines"]["total"])
        self.assertEqual(1, len([e for e in m["entities"] if e["kind"] == "type"]))

    def test_callable_spans_cover_attributes_accessors_operators_and_local_functions(self) -> None:
        self.write("src/Fixture/One.cs", '''class One {
    [Marker]
    void M() {
        string s = "} void Fake() {";
        void Local() {
            // body
        }
    }
    One() {}
    ~One() {}
    int Value { get { return 1; } set {} }
    public static One operator +(One a, One b) => a;
    public static implicit operator int(One a) => 0;
}''')
        members = {e["symbol"]: e["values"]["methodLines"] for e in self.measure()["entities"] if e["kind"] == "member"}
        self.assertEqual(7, members["One.M()"])
        self.assertEqual(3, members["One.M().local:Local()"])
        self.assertEqual(8, len(members))

    def test_partial_files_are_distinct_and_project_qualified(self) -> None:
        self.write("src/Fixture/One.cs", "namespace N; partial class One {} partial class One {}")
        self.write("src/Fixture/Part.cs", "namespace N; partial class One {}")
        self.write("tests/Second/Second.csproj", '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        self.write("tests/Second/One.cs", "namespace N; partial class One {}")
        m = self.measure()
        types = {e["project"]: e["values"]["partialFiles"] for e in m["entities"] if e["kind"] == "type"}
        self.assertEqual({"Fixture": 2, "Second": 1}, types)

    def test_axaml_view_spans_aggregate_partials_not_other_types(self) -> None:
        self.write("src/Fixture/One.cs", "namespace N; class Other {}")
        self.write("src/Fixture/View.axaml.cs", "namespace N;\npartial class View {\n void A() {}\n}\n// extra\n")
        self.write("src/Fixture/View.Output.cs", "namespace N;\npartial class View {\n void B() {}\n}\nclass OtherTwo {}\n")
        self.write("src/Fixture/View.axaml", '<View xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="N.View"/>')
        m = self.measure()
        view = next(e for e in m["entities"] if e["kind"] == "type" and e["symbol"] == "N.View")
        self.assertEqual(6, view["values"]["viewTypeLines"])
        self.assertEqual(5, m["summary"]["axamlCodeBehindLines"]["total"])
        self.assertEqual(1, len([e for e in m["entities"] if "viewTypeLines" in e["values"]]))

    def test_suppressions_count_directives_disabled_ids_and_attributes(self) -> None:
        self.write("src/Fixture/One.cs", '''using System.Diagnostics.CodeAnalysis;
#pragma warning disable CS1234, CA5678
#pragma warning restore CS1234
[SuppressMessage("Category", "CA9999:Example")]
class One { string text = "#pragma warning disable CS0001 [SuppressMessage]"; }
''')
        m = self.measure()
        self.assertEqual(4, m["summary"]["suppressions"])
        symbols = {f["symbol"] for f in m["findings"]}
        self.assertIn("pragma:CA5678", symbols)
        self.assertIn("SuppressMessage:CA9999:Example", symbols)

    def test_axaml_association_stays_in_its_own_project(self) -> None:
        self.write("src/Fixture/One.cs", "namespace N; partial class View {}")
        self.write("src/Fixture/View.axaml", '<View xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="N.View"/>')
        self.write("tests/Second/Second.csproj", '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        self.write("tests/Second/One.cs", "namespace N; partial class View {}")
        views = [e["project"] for e in self.measure()["entities"] if "viewTypeLines" in e["values"]]
        self.assertEqual(["Fixture"], views)

    def code_view(self, lines: int, base: str = "Avalonia.Controls.Control", name: str = "Plain",
                  path: str = "src/Fixture/Plain.cs", using: str = "") -> None:
        self.assertGreaterEqual(lines, 2)
        self.write(path, using + f"namespace N;\nclass {name} : {base} {{\n" + "// body\n" * (lines - 2) + "}\n")

    def view_lines(self) -> dict:
        return {e["symbol"]: e["values"]["viewTypeLines"] for e in self.measure()["entities"]
                if e["kind"] == "type" and "viewTypeLines" in e["values"]}

    def test_code_only_view_above_the_budget_is_measured(self) -> None:
        self.code_view(805)
        self.assertEqual({"N.Plain": 805}, self.view_lines())

    def test_code_only_view_below_the_budget_adds_no_entity(self) -> None:
        self.code_view(790)
        self.assertEqual({}, self.view_lines())

    def test_code_only_view_is_found_through_a_base_type_of_the_same_project(self) -> None:
        self.write("src/Fixture/Base.cs", "namespace N;\nabstract class Base : Avalonia.Controls.UserControl {}\n")
        self.code_view(810, base="Base")
        self.assertEqual({"N.Plain": 810}, self.view_lines())

    def test_large_type_that_is_not_a_view_is_not_a_view(self) -> None:
        self.code_view(900, base="System.Object")
        self.assertEqual({}, self.view_lines())

    def test_code_only_view_result_does_not_depend_on_the_file_name(self) -> None:
        self.code_view(805, path="src/Fixture/Anything.Else.cs")
        self.assertEqual({"N.Plain": 805}, self.view_lines())

    def test_code_only_view_crossing_the_budget_fails_verify(self) -> None:
        self.code_view(790)
        self.enroll()
        self.code_view(805)
        self.assert_exit(1, self.run_health("Verify"), "viewTypeLines")

    def test_budget_boundary_is_strictly_above_800_lines(self) -> None:
        self.code_view(800)
        self.assertEqual({}, self.view_lines())
        self.code_view(801)
        self.assertEqual({"N.Plain": 801}, self.view_lines())

    def test_code_only_view_sums_its_partial_files(self) -> None:
        self.write("src/Fixture/Plain.cs", "namespace N;\npartial class Plain : Avalonia.Controls.Panel {\n" + "// a\n" * 448 + "}\n")
        self.write("src/Fixture/Plain.Part.cs", "namespace N;\npartial class Plain {\n" + "// b\n" * 398 + "}\n")
        self.assertEqual({"N.Plain": 850}, self.view_lines())

    def test_view_base_names_cover_window_panel_decorator_and_common_containers(self) -> None:
        for base in ("Window", "Panel", "Decorator", "Grid", "Border", "ContentControl"):
            with self.subTest(base=base):
                self.code_view(805, base=base, using="using Avalonia.Controls;\n")
                self.assertEqual({"N.Plain": 805}, self.view_lines())

    def test_a_global_alias_qualified_avalonia_name_counts_as_a_mention(self) -> None:
        self.code_view(805, base="global::Avalonia.Controls.Control")
        self.assertEqual({"N.Plain": 805}, self.view_lines())
        self.code_view(805, base="Control", using="using global::Avalonia.Controls;\n")
        self.assertEqual({"N.Plain": 805}, self.view_lines())

    def test_a_using_alias_of_an_avalonia_view_base_is_found(self) -> None:
        self.code_view(805, base="V", using="using V = Avalonia.Controls.UserControl;\n")
        self.assertEqual({"N.Plain": 805}, self.view_lines())

    def test_scroll_viewer_is_a_view_base(self) -> None:
        self.code_view(805, base="ScrollViewer", using="using Avalonia.Controls;\n")
        self.assertEqual({"N.Plain": 805}, self.view_lines())

    def test_an_unresolved_base_named_control_without_avalonia_is_not_a_view(self) -> None:
        self.code_view(805, base="Control")
        self.assertEqual({}, self.view_lines())

    def test_a_base_named_control_that_the_project_declares_is_not_an_avalonia_view(self) -> None:
        self.write("src/Fixture/Control.cs", "namespace N;\nclass Control {}\n")
        self.code_view(805, base="Control", using="using Avalonia;\n")
        self.assertEqual({}, self.view_lines())

    def test_lower_baseline_removes_a_code_only_view_that_shrinks_below_the_budget(self) -> None:
        self.code_view(805)
        self.enroll()
        self.code_view(790)
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "run LowerBaseline")
        self.assert_exit(0, self.run_health("LowerBaseline", "-BaseRef", self.base))
        entities = json.loads((self.root / BASELINE).read_text())["entities"]
        self.assertEqual([], [e for e in entities if "viewTypeLines" in e["ceilings"]])

    def test_pragma_disable_all_retains_disabled_all_id(self) -> None:
        self.write("src/Fixture/One.cs", "#pragma warning disable\nclass One {}\n")
        self.assertEqual(2, self.measure()["summary"]["suppressions"])

    def test_evaluated_nowarn_and_warning_exemptions_honor_conditions_and_imports(self) -> None:
        self.write("Directory.Build.props", '''<Project><PropertyGroup>
<Disabled>CA1111;CA2222</Disabled><NoWarn>$(NoWarn);$(Disabled)</NoWarn>
<NoWarn Condition="'$(Configuration)' == 'Never'">CA9999</NoWarn>
<WarningsNotAsErrors>CS1234</WarningsNotAsErrors>
</PropertyGroup></Project>''')
        symbols = {f["symbol"] for f in self.measure()["findings"] if f["rule"] == "suppressions"}
        self.assertTrue({"NoWarn:CA1111", "NoWarn:CA2222", "WarningsNotAsErrors:CS1234"}.issubset(symbols))
        self.assertNotIn("NoWarn:CA9999", symbols)
        self.assertFalse(any("$(" in s for s in symbols))

    def test_project_only_nowarn_is_detected(self) -> None:
        self.write("src/Fixture/Fixture.csproj",
                   '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                   '<TargetFramework>net10.0</TargetFramework><NoWarn>CA1031</NoWarn></PropertyGroup></Project>')
        symbols = {f["symbol"] for f in self.measure()["findings"] if f["rule"] == "suppressions"}
        self.assertIn("NoWarn:CA1031", symbols)

    def test_finding_is_due_on_its_removeby_day_and_fails_after(self) -> None:
        self.write("src/Fixture/One.cs", "#pragma warning disable CA1234\nclass One {}\n")
        self.enroll()
        self.set_today("2026-10-14")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))
        self.set_today("2026-10-15")
        result = self.run_health("Verify", "-BaseRef", self.base)
        self.assert_exit(1, result, "removeBy: 2026-10-14 -> 2026-10-15")
        self.assertIn("src/Fixture/One.cs", result.stdout)

    def test_expired_test_debt_finding_fails_verify(self) -> None:
        self.seed_test_debt("class One { void M() { System.Threading.Thread.Sleep(1); } }\n")
        path = self.root / TEST_DEBT
        ledger = json.loads(path.read_text())
        ledger["findings"][0]["removeBy"] = "2026-10-31"
        path.write_text(json.dumps(ledger, indent=2) + "\n", encoding="utf-8")
        self.set_today("2026-11-01")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "removeBy: 2026-10-31 -> 2026-11-01")

    def strip_removeby(self, path: str) -> None:
        ledger = json.loads((self.root / path).read_text())
        for finding in ledger["findings"]:
            finding.pop("removeBy", None)
        self.write(path, json.dumps(ledger, indent=2) + "\n")

    def test_finding_without_removeby_never_expires(self) -> None:
        self.write("src/Fixture/One.cs", "#pragma warning disable CA1234\nclass One {}\n")
        self.enroll()
        self.strip_removeby(BASELINE)
        self.set_today("2030-01-01")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_only_the_dated_finding_of_a_mixed_ledger_expires(self) -> None:
        self.write("src/Fixture/One.cs", "#pragma warning disable CA1234\nclass One {}\n")
        self.write("src/Fixture/Two.cs", "#pragma warning disable CA1235\nclass Two {}\n")
        b = self.enroll()
        undated = [f for f in b["findings"] if f["path"].endswith("One.cs")]
        dated = [f for f in b["findings"] if f["path"].endswith("Two.cs")]
        self.assertTrue(undated and dated)
        for finding in undated:
            del finding["removeBy"]
        self.write_baseline(b)
        self.set_today("2026-10-15")
        result = self.run_health("Verify", "-BaseRef", self.base)
        self.assert_exit(1, result)
        self.assertEqual(len(dated), result.stdout.count("removeBy: 2026-10-14 -> 2026-10-15"))
        self.assertNotIn("One.cs", result.stdout)
        self.assertNotRegex(result.stdout, r"removeBy: \d+ more findings")

    def test_new_debt_is_still_rejected_when_the_ledger_has_no_dates(self) -> None:
        self.write("src/Fixture/One.cs", "#pragma warning disable CA1234\nclass One {}\n")
        self.enroll()
        self.strip_removeby(BASELINE)
        self.base = self.commit()
        self.write("src/Fixture/Two.cs", "#pragma warning disable CA1235\nclass Two {}\n")
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "New fingerprint")

    def test_a_ledger_without_dates_passes_against_a_base_with_dates(self) -> None:
        self.write("src/Fixture/One.cs", "#pragma warning disable CA1234\nclass One {}\n")
        self.enroll()
        self.strip_removeby(BASELINE)
        self.git("add", "-A")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_a_null_removeby_is_a_bad_ledger(self) -> None:
        self.write("src/Fixture/One.cs", "#pragma warning disable CA1234\nclass One {}\n")
        b = self.enroll()
        b["findings"][0]["removeBy"] = None
        self.write_baseline(b)
        self.assert_exit(2, self.run_health("Verify", "-BaseRef", self.base), "invalid finding count/hash/date")

    def test_enrolled_test_debt_has_no_removeby_and_never_expires(self) -> None:
        self.seed_test_debt("class One { void M() { System.Threading.Thread.Sleep(1); } }\n")
        ledger = json.loads((self.root / TEST_DEBT).read_text())
        self.assertEqual(1, len(ledger["findings"]))
        self.assertNotIn("removeBy", ledger["findings"][0])
        self.set_today("2030-01-01")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_lower_baseline_is_not_blocked_by_an_expired_date(self) -> None:
        self.write("src/Fixture/One.cs", "#pragma warning disable CA1234\nclass One {}\n")
        self.enroll()
        self.set_today("2026-12-01")
        self.write("src/Fixture/One.cs", "class One {}\n")
        self.git("add", "-A")
        self.assert_exit(0, self.run_health("LowerBaseline", "-BaseRef", self.base))
        self.assertEqual([], json.loads((self.root / BASELINE).read_text())["findings"])

    def test_many_expired_findings_print_a_summary_line(self) -> None:
        for i in range(23):
            self.write(f"src/Fixture/S{i}.cs", f"#pragma warning disable CA{1000 + i}\nclass S{i} {{}}\n")
        self.enroll()
        self.set_today("2026-10-15")
        result = self.run_health("Verify", "-BaseRef", self.base)
        self.assert_exit(1, result)
        self.assertEqual(20, result.stdout.count("removeBy: 2026-10-14 -> 2026-10-15"))
        self.assertRegex(result.stdout, r"removeBy: \d+ more findings of this ledger")

    def test_impossible_calendar_date_in_test_debt_is_a_bad_ledger(self) -> None:
        self.seed_test_debt("class One { void M() { System.Threading.Thread.Sleep(1); } }\n")
        path = self.root / TEST_DEBT
        ledger = json.loads(path.read_text())
        ledger["findings"][0]["removeBy"] = "2026-02-30"
        path.write_text(json.dumps(ledger, indent=2) + "\n", encoding="utf-8")
        self.assert_exit(2, self.run_health("Verify", "-BaseRef", self.base), "invalid finding count/hash/date")

    def test_impossible_calendar_date_is_a_bad_ledger(self) -> None:
        self.write("src/Fixture/One.cs", "#pragma warning disable CA1234\nclass One {}\n")
        b = self.enroll()
        b["findings"][0]["removeBy"] = "2026-13-45"
        self.write_baseline(b)
        self.assert_exit(2, self.run_health("Verify", "-BaseRef", self.base), "invalid finding count/hash/date")

    def test_removing_a_tightened_optional_limit_is_a_raised_limit(self) -> None:
        b = self.enroll()
        b["limits"]["viewTypeLines"] = 700
        self.write_baseline(b)
        self.base = self.commit()
        del b["limits"]["viewTypeLines"]
        self.write_baseline(b)
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "Raised limit is forbidden")

    def test_native_import_identity_handles_alias_attributes_and_const_entrypoint(self) -> None:
        self.write("src/Fixture/One.cs", '''using Import = System.Runtime.InteropServices.DllImportAttribute;
using System.Runtime.InteropServices;
class One {
 const string Entry = "Open";
 [Import("native", EntryPoint=Entry)] static extern void First();
 [LibraryImport("native", EntryPoint="Open")] static partial void Second();
 [DllImport("other", EntryPoint="Open")] static extern void Third();
 string text = "[DllImport(native)]"; // [DllImport("native", EntryPoint="Open")]
}''')
        m = self.measure()
        self.assertEqual({"groups": 1, "excess": 1}, m["nativeImportDuplicates"])
        self.assertEqual(2, len([f for f in m["findings"] if f["rule"] == "nativeImportDuplicates"]))

    def test_generation_fields_use_numeric_mutable_variables_not_properties_or_words(self) -> None:
        self.write("src/Fixture/One.cs", '''class One {
 int generation, nextRevision; System.Int64 RequestId;
 readonly long Revision; const int Generation = 1;
 string RevisionText; double RequestIdDouble; int RequestIdProperty { get; set; }
 string words = "long GenerationFake;"; // int RevisionFake;
 class Nested { uint revision; }
}''')
        self.assertEqual(4, self.measure()["summary"]["generationFields"])

    def test_clock_candidates_include_timeprovider_and_clockstate_not_comment_clones(self) -> None:
        self.write("src/Fixture/One.cs", '''using Provider = System.TimeProvider;
class One : Provider {}
class Derived : One {}
class ManualClock {}
class ClockState {}
class NotClock { string text = "class FakeClock : TimeProvider {}"; }
''')
        self.assertEqual(4, self.measure()["summary"]["fakeClockDuplicates"])

    def test_workspace_candidates_have_both_lifecycle_operations_and_temp_path(self) -> None:
        self.write("src/Fixture/One.cs", '''using System.IO;
class TestWorkspace {}
class Other { void M() { var p=Path.GetTempPath(); Directory.CreateDirectory(p); Directory.Delete(p); } }
class Incomplete { void M() { Directory.CreateDirectory("x"); } }
class Words { string s="class TestWorkspace {} Path.GetTempPath Directory.Delete"; }
''')
        self.assertEqual(2, self.measure()["summary"]["workspaceDuplicates"])

    def test_source_assertion_discovery_retained_outside_architecture_folder(self) -> None:
        self.write("src/Fixture/One.cs", '''class One { void M() {
 var s = System.IO.File.ReadAllText("One.cs"); Assert.Contains("class", s);
 Assert.DoesNotContain("bad", s); Assert.Matches("good", s); Assert.DoesNotMatch("bad", s);
 string words="ReadText() Contains()"; // ReadText(); Matches();
} }''')
        c = self.measure()["sourceTextAssertions"]["candidates"]
        self.assertEqual(1, len(c))
        self.assertEqual((1, 4, False), (c[0]["readCalls"], c[0]["assertions"], c[0]["planFilter"]))

    def test_generated_suffix_is_included_build_outputs_are_excluded(self) -> None:
        self.write("src/Fixture/Generated.g.cs", "class Generated {}")
        self.write("src/Fixture/generated/Kept.cs", "class Kept {}")
        for directory in ("bin", "obj", "artifacts"):
            self.write(f"src/Fixture/{directory}/Broken.cs", "class Broken {")
        symbols = {e["symbol"] for e in self.measure()["entities"] if e["kind"] == "type"}
        self.assertEqual({"N.One", "Generated", "Kept"}, symbols)

    def test_grow_fails_with_location_old_new_entity_and_contributing_change(self) -> None:
        self.hot(808)
        self.enroll()
        self.hot(809)
        result = self.run_health("Verify", "-BaseRef", self.base)
        self.assert_exit(1, result, "808 -> 809")
        self.assertIn("HC_STATE src/Fixture/One.cs:1", result.stdout)
        self.assertIn("entity Fixture:file:N.Hot", result.stdout)
        self.assertIn("Contributing change:", result.stdout)

    def test_shrink_lowers_in_same_change_and_passes(self) -> None:
        self.hot(808)
        self.enroll()
        self.hot(807)
        self.assert_exit(0, self.run_health("LowerBaseline", "-BaseRef", self.base))
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))
        b = json.loads((self.root / BASELINE).read_text())
        self.assertEqual(807, b["entities"][0]["ceilings"]["fileLines"])

    def test_stale_baseline_after_fix_fails(self) -> None:
        self.hot(808)
        self.enroll()
        self.hot(807)
        self.assert_exit(1, self.run_health("Verify"), "Fixes must lower/delete")

    def test_size_swap_with_equal_totals_fails(self) -> None:
        self.hot(808)
        self.enroll()
        self.git("rm", "src/Fixture/One.cs")
        self.hot(808, "Replacement", "src/Fixture/New.cs")
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "New/growing excess")

    def test_finding_swap_with_equal_totals_fails(self) -> None:
        self.write("src/Fixture/One.cs", "class One { async void Old() {} }")
        self.enroll()
        self.write("src/Fixture/One.cs", "class One { async void Replacement() {} }")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "New fingerprint")

    def test_file_rename_preserves_size_and_finding_identity(self) -> None:
        self.hot(808)
        with (self.root / "src/Fixture/One.cs").open("a") as file:
            file.write("class Other { async void Go() {} }\n")
        self.enroll()
        self.git("mv", "src/Fixture/One.cs", "src/Fixture/Renamed.cs")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_oversized_partial_rename_cannot_lose_identity_to_small_partial(self) -> None:
        self.hot(808)
        source = (self.root / "src/Fixture/One.cs").read_text()
        self.write("src/Fixture/One.cs", source.replace("class Hot", "partial class Hot"))
        self.write("src/Fixture/A.cs", "namespace N; partial class Hot {}\n")
        self.enroll()
        self.git("mv", "src/Fixture/One.cs", "src/Fixture/Z.cs")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_raised_edited_ceiling_fails_even_when_source_fits(self) -> None:
        self.hot(808)
        b = self.enroll()
        self.hot(809)
        b["entities"][0]["ceilings"]["fileLines"] = 809
        self.write_baseline(b)
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "808 -> 809")

    def test_lower_baseline_refuses_new_debt_without_writing(self) -> None:
        self.enroll()
        self.write("src/Fixture/One.cs", "class One { async void Added() {} }")
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("LowerBaseline"), "New fingerprint")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_lower_baseline_deletes_fixed_findings_without_enrolling_source(self) -> None:
        self.write("src/Fixture/One.cs", "class One { async void Fixed() {} }")
        b = self.enroll()
        self.write("src/Fixture/One.cs", "class One { void Fixed() {} }")
        self.assert_exit(0, self.run_health("LowerBaseline"))
        after = json.loads((self.root / BASELINE).read_text())
        self.assertEqual([], after["findings"])
        self.assertEqual(b["snapshotCommit"], after["snapshotCommit"])
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    # --- Adopt: debt that a rule change reveals, in a change that is synchronization-only ---

    def revealed_excess(self, lines: int = 808) -> None:
        """The ledger is enrolled on a clean tree. An excess then appears in the base commit, as a new rule reveals old debt."""
        self.enroll()
        self.hot(lines)
        self.base = self.commit()

    def test_adopt_registers_excess_in_a_synchronization_only_change(self) -> None:
        self.revealed_excess()
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "800 -> 808")
        self.assert_exit(0, self.run_health("Adopt", "-BaseRef", self.base), "Adopt passed; 1 entities")
        after = json.loads((self.root / BASELINE).read_text())
        self.assertEqual(1, len(after["entities"]))
        self.assertEqual({"fileLines": 808}, after["entities"][0]["ceilings"])
        self.assertEqual(["src/Fixture/One.cs"], after["entities"][0]["locations"])
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_adopt_refuses_excess_when_this_change_edits_a_source_file(self) -> None:
        self.revealed_excess()
        self.hot(809)
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "800 -> 809")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_adopt_refuses_excess_in_a_new_file(self) -> None:
        self.revealed_excess()
        self.hot(808, "Other", "src/Fixture/Other.cs")
        self.git("add", "-A")
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "800 -> 808")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_adopt_requires_a_base_ref(self) -> None:
        self.revealed_excess()
        self.assert_exit(2, self.run_health("Adopt"), "Adopt requires -BaseRef")

    def test_adopt_registers_a_finding_in_a_synchronization_only_change(self) -> None:
        self.enroll()
        self.write("src/Fixture/One.cs", "class One { async void Revealed() {} }")
        self.base = self.commit()
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "New fingerprint")
        self.assert_exit(0, self.run_health("Adopt", "-BaseRef", self.base), "0 entities and 1 finding")
        after = json.loads((self.root / BASELINE).read_text())
        self.assertEqual(["asyncVoid"], [f["rule"] for f in after["findings"]])
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_adopt_refuses_a_finding_when_this_change_edits_a_source_file(self) -> None:
        self.enroll()
        self.write("src/Fixture/One.cs", "class One { async void Revealed() {} }")
        self.base = self.commit()
        self.write("src/Fixture/One.cs", "class One { async void Revealed() {} int edited; }")
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "New fingerprint")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_adopt_does_not_let_growth_in_another_file_pass(self) -> None:
        self.revealed_excess()
        self.write("src/Fixture/Grown.cs", "namespace N; class Grown { async void Added() {} }")
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "New fingerprint")
        self.assertEqual([], json.loads((self.root / BASELINE).read_text())["entities"])

    def test_adopt_refuses_a_type_when_one_of_its_partial_files_is_edited(self) -> None:
        self.enroll()

        def wide(first: int, count: int, tail: str = "") -> str:
            return "namespace N;\npartial class Wide { " + "".join(f"int f{n}; " for n in range(first, first + count)) + "}\n" + tail

        self.write("src/Fixture/A.cs", wide(0, 20))
        self.write("src/Fixture/B.cs", wide(20, 15))
        self.base = self.commit()
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "30 -> 35")
        self.write("src/Fixture/B.cs", wide(20, 15, "// edited\n"))
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "30 -> 35")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())
        self.write("src/Fixture/B.cs", wide(20, 15))
        self.assert_exit(0, self.run_health("Adopt", "-BaseRef", self.base), "Adopt passed; 1 entities")

    def test_a_ledger_entry_cannot_name_an_unchanged_file_for_a_changed_one(self) -> None:
        self.write("src/Fixture/Calm.cs", "namespace N; class Calm {}\n")
        self.enroll()
        self.hot(900)
        grown = next(e for e in self.measure()["entities"] if e["kind"] == "file" and e["values"].get("fileLines") == 900)
        b = json.loads((self.root / BASELINE).read_text())
        b["entities"].append({key: copy.deepcopy(grown[key]) for key in ("project", "kind", "symbol", "contentHash")}
                             | {"locations": ["src/Fixture/Calm.cs"], "ceilings": {"fileLines": 900},
                                "owner": "fixture", "issue": "fixture/1"})
        self.write_baseline(b)
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "800 -> 900")

    def test_a_ledger_finding_cannot_name_an_unchanged_file_for_a_changed_one(self) -> None:
        self.write("src/Fixture/Calm.cs", "namespace N; class Calm {}\n")
        self.enroll()
        self.write("src/Fixture/One.cs", "class One { async void Added() {} }")
        finding = next(f for f in self.measure()["findings"] if f["rule"] == "asyncVoid")
        b = json.loads((self.root / BASELINE).read_text())
        b["findings"].append({key: copy.deepcopy(finding[key]) for key in
                              ("rule", "project", "member", "symbol", "syntaxHash", "count")}
                             | {"path": "src/Fixture/Calm.cs", "owner": "fixture"})
        self.write_baseline(b)
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "New fingerprint")

    def test_adopt_refuses_a_file_with_a_non_ascii_name_that_this_change_edits(self) -> None:
        self.enroll()
        self.hot(808, "Wide", "src/Fixture/\u00dcn\u00ef.cs")
        self.base = self.commit()
        self.hot(809, "Wide", "src/Fixture/\u00dcn\u00ef.cs")
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "800 -> 809")

    def test_adopt_refuses_a_file_that_this_change_renames(self) -> None:
        self.revealed_excess()
        self.git("mv", "src/Fixture/One.cs", "src/Fixture/Moved.cs")
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "800 -> 808")

    def test_adopt_refuses_a_view_whose_base_type_this_change_edits(self) -> None:
        self.enroll()
        self.write("src/Fixture/Base.cs", "namespace N;\nabstract class Base {}\n")
        self.write("src/Fixture/Derived.cs", "namespace N;\nclass Derived : Base {\n" + "// body\n" * 808 + "}\n")
        self.base = self.commit()
        self.write("src/Fixture/Base.cs", "namespace N;\nabstract class Base : Avalonia.Controls.UserControl {}\n")
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "viewTypeLines")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_adopt_refuses_a_finding_whose_fingerprint_a_new_file_of_the_project_extends(self) -> None:
        self.enroll()
        attribute = 'using System.Diagnostics.CodeAnalysis;\nnamespace N;\n[SuppressMessage("X", "CA1234")] partial class Foo {}\n'
        self.write("src/Fixture/A.cs", attribute)
        self.base = self.commit()
        self.write("src/Fixture/B.cs", attribute)
        self.git("add", "-A")
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "New fingerprint")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_adopt_registers_the_finding_when_the_project_is_untouched(self) -> None:
        self.enroll()
        attribute = 'using System.Diagnostics.CodeAnalysis;\nnamespace N;\n[SuppressMessage("X", "CA1234")] partial class Foo {}\n'
        self.write("src/Fixture/A.cs", attribute)
        self.base = self.commit()
        self.assert_exit(0, self.run_health("Adopt", "-BaseRef", self.base), "0 entities and 1 finding")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_adopt_refuses_a_view_when_a_file_that_declares_its_base_type_leaves_the_project(self) -> None:
        self.enroll()
        project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
        self.write("other/Other.csproj", project)
        self.write("src/Fixture/Control.cs", "namespace N;\nclass Control {}\n")
        self.write("src/Fixture/Uses.cs", "using Avalonia.Controls;\nnamespace N;\nclass Uses {}\n")
        self.write("src/Fixture/Derived.cs", "namespace N;\nclass Derived : Control {\n" + "// body\n" * 808 + "}\n")
        self.base = self.commit()
        self.git("mv", "src/Fixture/Control.cs", "other/Control.cs")
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "viewTypeLines")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_adopt_refuses_a_view_when_an_axaml_file_of_the_project_is_added(self) -> None:
        self.enroll()
        self.write("src/Fixture/Derived.cs", "namespace N;\npartial class Derived {\n" + "// body\n" * 808 + "}\n")
        self.base = self.commit()
        self.write("src/Fixture/Derived.axaml",
                   '<UserControl xmlns="https://github.com/avaloniaui" '
                   'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="N.Derived"/>\n')
        self.git("add", "-A")
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "Adopt refused")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_adopt_refuses_when_only_a_project_file_changes(self) -> None:
        self.revealed_excess()
        project = (self.root / "src/Fixture/Fixture.csproj").read_text()
        self.write("src/Fixture/Fixture.csproj", project.replace("</Project>", "<ItemGroup><Compile Include=\"..\\Shared\\*.cs\" /></ItemGroup></Project>"))
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "Adopt refused")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_adopt_refuses_a_csharp_file_in_a_documents_folder(self) -> None:
        self.revealed_excess()
        self.write("docs/Evil.cs", "namespace N; class Evil {}\n")
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "Adopt refused")

    def test_adopt_accepts_a_change_of_synchronization_files_only(self) -> None:
        self.revealed_excess()
        self.write("docs/note.md", "# note\n")
        self.write("eng/core-health/BannedSymbols.Common.txt", "T:System.Foo\n")
        self.write("tools/repo-checks/repo-health.md", "copy\n")
        self.git("add", "-A")
        self.assert_exit(0, self.run_health("Adopt", "-BaseRef", self.base), "Adopt passed; 1 entities")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_adopt_accepts_only_the_generated_block_of_projects_props(self) -> None:
        self.revealed_excess()
        props = "eng/code-health/projects.props"
        # The writer regenerates the block after Adopt, so a hand-written group inside it does not survive.
        self.write(props, "<Project>\n" + GENERATED_BLOCK.format("  <PropertyGroup Condition=\"'$(MSBuildProjectName)' == 'Fixture'\">\n"
                   "    <HealthBaselineWarningIds>CA1031</HealthBaselineWarningIds>\n  </PropertyGroup>\n") + "</Project>\n")
        self.assert_exit(0, self.run_health("Adopt", "-BaseRef", self.base), "Adopt passed; 1 entities")
        self.assertEqual("<Project>\n" + GENERATED_BLOCK.format("") + "</Project>\n", (self.root / props).read_text().replace("\r\n", "\n"))

    def test_adopt_refuses_an_item_inside_the_generated_block(self) -> None:
        self.revealed_excess()
        self.write("eng/code-health/projects.props", "<Project>\n" + GENERATED_BLOCK.format(
            "  <ItemGroup><Compile Include=\"x.cs\" /></ItemGroup>\n") + "</Project>\n")
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "Adopt refused")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_adopt_refuses_a_moved_generated_block(self) -> None:
        block = GENERATED_BLOCK.format("")
        self.write("eng/code-health/projects.props", "<Project>\n<!-- a -->\n" + block + "<!-- b -->\n</Project>\n")
        self.revealed_excess()
        self.write("eng/code-health/projects.props", "<Project>\n" + block + "<!-- a -->\n<!-- b -->\n</Project>\n")
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "Adopt refused")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_a_hand_raised_finding_count_fails_in_a_synchronization_only_change(self) -> None:
        self.enroll()
        attribute = 'using System.Diagnostics.CodeAnalysis;\nnamespace N;\n[SuppressMessage("X", "CA1234")] partial class Foo {}\n'
        self.write("src/Fixture/A.cs", attribute)
        self.base = self.commit()
        self.assert_exit(0, self.run_health("Adopt", "-BaseRef", self.base), "0 entities and 1 finding")
        self.commit()
        self.write("src/Fixture/B.cs", attribute)
        self.base = self.commit()
        b = json.loads((self.root / BASELINE).read_text())
        self.assertEqual(1, len(b["findings"]))
        self.assertEqual(1, b["findings"][0]["count"])
        b["findings"][0]["count"] = 2
        self.write_baseline(b)
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base))
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base))

    def test_adopt_refuses_a_second_generated_block(self) -> None:
        self.revealed_excess()
        block = GENERATED_BLOCK.format("")
        self.write("eng/code-health/projects.props", "<Project>\n" + block + "<ItemGroup><Compile Include=\"x.cs\" /></ItemGroup>\n" + block + "</Project>\n")
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "Adopt refused")

    def test_adopt_refuses_a_change_outside_the_edited_text_of_projects_props(self) -> None:
        self.revealed_excess()
        self.write("eng/code-health/projects.props", "<Project>\n<!-- note -->\n" + GENERATED_BLOCK.format("") + "</Project>\n")
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "Adopt refused")

    def test_adopt_refuses_when_the_root_is_a_sub_directory_of_the_repository(self) -> None:
        self.enroll()
        for name in ("global.json", BASELINE, "eng/code-health/projects.props", "src/Fixture/Fixture.csproj", "src/Fixture/One.cs"):
            self.write("sub/" + name, (self.root / name).read_text())
        self.base = self.commit()
        result = subprocess.run(["pwsh", "-NoProfile", "-File", str(self.fixture_checker), "-Mode", "Adopt", "-Repo", "core",
                                 "-Root", str(self.root / "sub"), "-BaseRef", self.base], env=self.env, capture_output=True, encoding="utf-8", timeout=90)
        self.assert_exit(1, result, "-Root is not the repository root")

    def test_adopt_accepts_a_markdown_file_with_a_non_ascii_name(self) -> None:
        self.revealed_excess()
        self.write("docs/\u00dcn\u00ef.md", "# note\n")
        self.git("add", "-A")
        self.assert_exit(0, self.run_health("Adopt", "-BaseRef", self.base), "Adopt passed; 1 entities")

    def test_adopt_refuses_a_path_with_a_line_break(self) -> None:
        # Git lists the deleted path "docs/a<LF>b.md". With the line break dropped it would read "docs/ab.md", a document.
        # The name cannot exist in a Windows working tree, so the path goes into the base commit through the index.
        self.revealed_excess()
        blob = self.git("rev-parse", "HEAD:global.json").strip()
        name = "docs/a\nb.md"
        who = ("-c", "user.name=t", "-c", "user.email=t@example.invalid", "-c", "commit.gpgsign=false")
        self.git("-c", "core.protectNTFS=false", "update-index", "--add", "--cacheinfo", f"100644,{blob},{name}")
        self.git(*who, "commit", "-qm", "path with a line break")
        base = self.git("rev-parse", "HEAD").strip()
        self.git("-c", "core.protectNTFS=false", "update-index", "--force-remove", "--", name)
        self.git(*who, "commit", "-qm", "remove it")
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", base), "Adopt refused")

    def test_adopt_refuses_a_csharp_file_in_the_checker_folder(self) -> None:
        self.revealed_excess()
        self.write("tools/repo-checks/Gen/Big.cs", "namespace N; class Big {}\n")
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Adopt", "-BaseRef", self.base), "Adopt refused")

    def test_adopt_leaves_existing_ceilings_and_counts_alone(self) -> None:
        self.hot(808, "Known", "src/Fixture/One.cs")
        b = self.enroll()
        self.hot(808, "Known", "src/Fixture/One.cs")
        self.write("src/Fixture/Two.cs", "namespace N; class Two { async void Revealed() {} }\n")
        self.base = self.commit()
        self.assert_exit(0, self.run_health("Adopt", "-BaseRef", self.base), "0 entities and 1 finding")
        after = json.loads((self.root / BASELINE).read_text())
        self.assertEqual(b["entities"], after["entities"])
        self.assertEqual(b["findings"], after["findings"][:len(b["findings"])])
        self.assertEqual(len(b["findings"]) + 1, len(after["findings"]))

    def test_a_hand_raised_ceiling_in_an_unchanged_file_still_fails(self) -> None:
        self.hot(808)
        b = self.enroll()
        b["entities"][0]["ceilings"]["fileLines"] = 900
        self.write_baseline(b)
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "808 -> 900")

    def test_example_baseline_without_view_limit_is_not_expanded_by_lowering(self) -> None:
        b = self.enroll()
        del b["limits"]["viewTypeLines"]
        self.write_baseline(b)
        self.base = self.commit()
        self.assert_exit(0, self.run_health("LowerBaseline"))
        after = json.loads((self.root / BASELINE).read_text())
        self.assertEqual(b, after)

    def test_state_growth_across_partials_reports_contributing_file_line(self) -> None:
        self.write("src/Fixture/One.cs", "namespace N; partial class One { " + " ".join(f"int f{i};" for i in range(31)) + " }")
        self.write("src/Fixture/Part.cs", "// header\n\nnamespace N; partial class One {}\n")
        self.enroll()
        self.write("src/Fixture/Part.cs", "// header\n\nnamespace N; partial class One { int extra; }\n")
        result = self.run_health("Verify")
        self.assert_exit(1, result, "stateMembers: 31 -> 32")
        self.assertIn("Contributing change: src/Fixture/Part.cs:3", result.stdout)

    def test_method_and_axaml_growth_ratchet_independent_of_file_budget(self) -> None:
        self.write("src/Fixture/One.cs", "namespace N; class One {\nvoid M() {\n" + "// body\n" * 79 + "}\n}\n")
        self.write("src/Fixture/View.axaml.cs", "namespace N; partial class View {\n" + "// body\n" * 149 + "}\n")
        self.enroll()
        self.write("src/Fixture/One.cs", "namespace N; class One {\nvoid M() {\n" + "// body\n" * 80 + "}\n}\n")
        self.write("src/Fixture/View.axaml.cs", "namespace N; partial class View {\n" + "// body\n" * 150 + "}\n")
        result = self.run_health("Verify")
        self.assert_exit(1, result, "methodLines: 81 -> 82")
        self.assertIn("axamlCodeBehindLines: 151 -> 152", result.stdout)

    def test_view_aggregate_growth_cannot_hide_in_compliant_partial_files(self) -> None:
        self.write("src/Fixture/View.axaml.cs", "namespace N; partial class View {\n" + "// body\n" * 149 + "}\n")
        self.write("src/Fixture/View.Output.cs", "namespace N; partial class View {\n" + "// body\n" * 649 + "}\n")
        self.enroll()
        self.write("src/Fixture/View.Output.cs", "namespace N; partial class View {\n" + "// body\n" * 650 + "}\n")
        self.assert_exit(1, self.run_health("Verify"), "viewTypeLines: 802 -> 803")

    def test_new_ninth_partial_file_fails(self) -> None:
        for i in range(8): self.write(f"src/Fixture/P{i}.cs", "namespace N; partial class One {}")
        # One.cs is already the ninth declaration; start with eight total.
        self.git("add", "-A")
        self.git("rm", "-f", "src/Fixture/P7.cs")
        self.enroll()
        self.write("src/Fixture/P7.cs", "namespace N; partial class One {}")
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Verify"), "partialFiles: 8 -> 9")

    def test_empty_input_and_bad_baseline_counts_are_errors(self) -> None:
        self.git("add", "-A")
        self.git("rm", "-f", "src/Fixture/One.cs")
        self.assert_exit(2, self.run_health(), "No tracked C# input")
        self.write("src/Fixture/One.cs", "class One { async void M() {} }")
        b = self.enroll()
        b["findings"][0]["count"] = -1
        self.write_baseline(b)
        self.assert_exit(2, self.run_health("Verify"), "Bad baseline")

    def test_lower_baseline_cannot_use_an_edited_raised_ceiling(self) -> None:
        self.hot(808)
        b = self.enroll()
        self.hot(809)
        b["entities"][0]["ceilings"]["fileLines"] = 809
        self.write_baseline(b)
        before = (self.root / BASELINE).read_bytes()
        self.assert_exit(1, self.run_health("LowerBaseline"), "808 -> 809")
        self.assertEqual(before, (self.root / BASELINE).read_bytes())

    def test_new_compliant_file_is_allowed_by_excess_ratchet(self) -> None:
        self.enroll()
        self.hot(100, "New", "src/Fixture/New.cs")
        self.git("add", "-A")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_merge_base_is_used_not_the_tip_of_base_ref(self) -> None:
        self.enroll()
        original = self.base
        self.git("checkout", "-qb", "base-side")
        self.write("separate.txt", "unrelated\n")
        self.write(BASELINE, "not a baseline")
        self.commit()
        self.git("checkout", "-q", "--detach", original)
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", "base-side"))

    def test_push_uses_committed_allowances_not_empty_base_or_edited_baseline(self) -> None:
        self.hot(808)
        self.enroll()
        self.assert_exit(0, self.run_health("Verify"))
        b = json.loads((self.root / BASELINE).read_text())
        b["entities"][0]["ceilings"]["fileLines"] = 809
        self.write_baseline(b)
        self.hot(809)
        self.assert_exit(1, self.run_health("Verify"), "808 -> 809")

    def test_parse_error_exits_two(self) -> None:
        self.write("src/Fixture/One.cs", "class One { void Broken( { }")
        self.git("add", "-A")
        self.assert_exit(2, self.run_health(), "parse error")

    def test_missing_parser_exits_two(self) -> None:
        self.git("add", "-A")
        self.assert_exit(2, self.run_health("Measure", "-ParserDirectory", str(self.root / "missing")), "Parser cannot be loaded")

    def test_bad_missing_baseline_and_invalid_base_are_errors(self) -> None:
        self.enroll()
        for change, args in (("bad", ()), ("missing", ()), ("base", ("-BaseRef", "not-a-ref"))):
            with self.subTest(change=change):
                target = self.root / BASELINE
                old = target.read_bytes()
                if change == "bad": target.write_text('{}')
                if change == "missing": target.unlink()
                self.assert_exit(2, self.run_health("Verify", *args))
                target.write_bytes(old)

    def test_diagnostic_slots_are_reserved_and_cannot_be_silently_dropped(self) -> None:
        b = self.enroll()
        b["findings"] = [{"rule": "RS0030", "project": "Fixture", "path": "src/Fixture/One.cs",
                          "member": "N.One.M()", "symbol": "M:System.IO.File.WriteAllText",
                          "syntaxHash": "a" * 64, "count": 1, "owner": "fixture", "removeBy": "2026-10-14"}]
        self.write_baseline(b)
        self.base = self.commit()
        b["findings"] = []
        self.write_baseline(b)
        self.assert_exit(2, self.run_health("LowerBaseline"), "HC_NOT_IMPLEMENTED")

    def test_h03_modes_and_invalid_mode_exit_two(self) -> None:
        for mode in ("Build", "Sarif", "Format", "Unknown"):
            with self.subTest(mode=mode):
                self.assert_exit(2, self.run_health(mode))

    def test_output_path_matches_stdout_contract(self) -> None:
        self.git("add", "-A")
        output = self.root / "measurement.json"
        result = self.run_health("Measure", "-OutputPath", str(output))
        self.assert_exit(0, result)
        self.assertEqual("", result.stdout)
        self.assertEqual("roslyn-physical-v1", json.loads(output.read_text())["measurementVersion"])

    SAMPLE = """using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
class Sample {
    async Task A() { await Task.Delay(1); Thread.Sleep(1); SpinWait.SpinUntil(() => true); }
    void B() { var w = Stopwatch.StartNew(); Assert.True(w.Elapsed.Ticks > 0); Assert.Equal(1, elapsedMs); }
    void C() { Path.GetTempPath(); Path.GetTempFileName(); Directory.CreateTempSubdirectory(); }
    void D() { File.ReadAllText("x"); File.ReadAllLines("y"); }
    void E() { HeadlessUnitTestSession.StartNew(typeof(object)); builder.UseHeadless(); }
    void F() { new Process(); new ProcessStartInfo("x"); Process.Start("x"); }
}
"""

    def rule_counts(self) -> dict[str, int]:
        summary = self.measure()["summary"]
        return {rule: summary[rule] for rule in TEST_RULES}

    def test_test_duplication_rules_count_each_pattern_in_a_test_project(self) -> None:
        self.write("tests/Fixture.Tests/One.cs", self.SAMPLE)
        self.assertEqual({"timeWaitsInTests": 3, "elapsedAssertionsInTests": 2, "tempPathInTests": 3,
                          "sourceTextReadsInTests": 2, "headlessSessionSetups": 2, "childProcessInTests": 3,
                          "sameNameFakes": 0}, self.rule_counts())

    def test_test_duplication_rules_ignore_product_code_and_the_shared_helper_project(self) -> None:
        self.write("src/Fixture/Two.cs", self.SAMPLE)
        self.write("tests/Nvt.Core.TestSupport/Helper.cs", self.SAMPLE.replace("Sample", "Helper"))
        self.assertEqual(dict.fromkeys(TEST_RULES, 0), self.rule_counts())

    SHARED = ("using System;\nusing System.Threading.Tasks;\n"
              "class FakeClock : TimeProvider { }\n"
              "class TestWorkspace { void M() { Task.Run(() => { }).Wait(); } }\n")
    SHARED_RULES = ("fakeClockDuplicates", "workspaceDuplicates", "blockingWait")

    def test_shared_helper_project_owns_the_clock_the_workspace_and_the_blocking_wait(self) -> None:
        self.write("tests/Nvt.Core.TestSupport/Helper.cs", self.SHARED)
        summary = self.measure()["summary"]
        self.assertEqual({rule: 0 for rule in self.SHARED_RULES}, {rule: summary[rule] for rule in self.SHARED_RULES})

    def test_the_helper_tests_project_and_other_projects_are_not_exempt(self) -> None:
        self.write("tests/Nvt.Core.TestSupport.Tests/Helper.cs", self.SHARED)
        self.write("tests/Fixture.Tests/Other.cs", self.SHARED.replace("FakeClock", "OtherClock").replace("TestWorkspace", "OtherWorkspace"))
        summary = self.measure()["summary"]
        self.assertGreaterEqual(summary["fakeClockDuplicates"], 2)
        self.assertGreaterEqual(summary["workspaceDuplicates"], 1)
        self.assertGreaterEqual(summary["blockingWait"], 2)

    def test_test_duplication_rules_ignore_comments_and_strings(self) -> None:
        self.write("tests/Fixture.Tests/One.cs", 'class One { string s = "Task.Delay(1) Process.Start(x)"; // Thread.Sleep(1);\n}\n')
        self.assertEqual(dict.fromkeys(TEST_RULES, 0), self.rule_counts())

    def test_architecture_files_may_read_source_text(self) -> None:
        self.write("tests/Fixture.Tests/ArchitectureRules.cs", 'class R { void M() { System.IO.File.ReadAllText("x"); } }\n')
        self.assertEqual(0, self.measure()["summary"]["sourceTextReadsInTests"])

    def test_assembly_attribute_counts_as_a_headless_setup(self) -> None:
        self.write("tests/Fixture.Tests/One.cs", "using System;\n[assembly: AvaloniaTestApplication(typeof(One))]\nclass One {}\n")
        self.assertEqual(1, self.measure()["summary"]["headlessSessionSetups"])

    def test_same_name_fakes_need_two_files_and_ignore_partial_types(self) -> None:
        self.write("tests/Fixture.Tests/A.cs", "namespace A; class FakeStore {}\nclass Faker {}\npartial class FakeSplit {}\n")
        self.write("tests/Fixture.Tests/B.cs", "namespace B; class FakeStore {}\npartial class FakeSplit {}\n")
        self.assertEqual(2, self.measure()["summary"]["sameNameFakes"])
        self.write("tests/Fixture.Tests/B.cs", "namespace B; class FakeOther {}\n")
        self.assertEqual(0, self.measure()["summary"]["sameNameFakes"])

    def seed_test_debt(self, source: str) -> None:
        self.write("tests/Fixture.Tests/One.cs", source)
        self.enroll()
        self.assert_exit(0, self.run_health("EnrollTestDebt"), "EnrollTestDebt passed")
        self.base = self.commit()

    def test_enrolled_test_debt_passes_and_new_duplication_fails(self) -> None:
        self.seed_test_debt("class One { async System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.Delay(1); } }\n")
        ledger = json.loads((self.root / TEST_DEBT).read_text())
        self.assertEqual(["timeWaitsInTests"], [f["rule"] for f in ledger["findings"]])
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))
        self.write("tests/Fixture.Tests/Two.cs", "class Two { void M() { System.Threading.Thread.Sleep(1); } }\n")
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "New test duplication")

    def test_fixed_test_debt_must_be_lowered_in_the_same_change(self) -> None:
        self.seed_test_debt("class One { void M() { System.Threading.Thread.Sleep(1); } }\n")
        self.write("tests/Fixture.Tests/One.cs", "class One { void M() { } }\n")
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "run LowerBaseline")
        self.assert_exit(0, self.run_health("LowerBaseline", "-BaseRef", self.base))
        self.assertEqual([], json.loads((self.root / TEST_DEBT).read_text())["findings"])
        self.git("add", "-A")
        self.assert_exit(0, self.run_health("Verify", "-BaseRef", self.base))

    def test_test_debt_ledger_cannot_be_raised_or_deleted_after_enrollment(self) -> None:
        self.seed_test_debt("class One { void M() { System.Threading.Thread.Sleep(1); } }\n")
        path = self.root / TEST_DEBT
        ledger = json.loads(path.read_text())
        ledger["findings"][0]["count"] += 1
        path.write_text(json.dumps(ledger, indent=2) + "\n")
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "may only shrink")
        path.unlink()
        self.git("add", "-A")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "cannot be deleted")

    def test_missing_test_debt_ledger_fails_when_test_duplication_exists(self) -> None:
        self.write("tests/Fixture.Tests/One.cs", "class One { void M() { System.Threading.Thread.Sleep(1); } }\n")
        self.enroll()
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "Run -Mode EnrollTestDebt")

    def test_enroll_test_debt_refuses_an_existing_ledger_and_rejects_bad_ledgers(self) -> None:
        self.seed_test_debt("class One { void M() { System.Threading.Thread.Sleep(1); } }\n")
        self.assert_exit(2, self.run_health("EnrollTestDebt"), "already exists")
        path = self.root / TEST_DEBT
        ledger = json.loads(path.read_text())
        ledger["findings"][0]["rule"] = "asyncVoid"
        path.write_text(json.dumps(ledger, indent=2) + "\n")
        self.assert_exit(2, self.run_health("Verify", "-BaseRef", self.base), "not a test-duplication rule")


class CheckerHostSettingsTests(unittest.TestCase):
    """The checker fixes the tool language and the MSBuild host limits for every host."""

    def test_script_sets_language_and_msbuild_limits_once(self) -> None:
        source = CHECKER.read_text(encoding="utf-8")
        for line in ("$env:DOTNET_CLI_UI_LANGUAGE = 'en'", "$env:VSLANG = '1033'", "$env:PreferredUILang = 'en-US'",
                     "$env:MSBUILDDISABLENODEREUSE = '1'", "$script:MsbuildLimits = @('-m:4', '-nodeReuse:false')"):
            self.assertEqual(1, source.count(line), line)
        self.assertIn("$Arguments[0] -in @('build', 'msbuild')", source)


if __name__ == "__main__":
    unittest.main()
