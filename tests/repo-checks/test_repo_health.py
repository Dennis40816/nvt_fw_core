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
        source = source.replace("                Write-LedgerWarningIds $baseline", "                # fixture warning generator")
        schema_path = (REPOSITORY / "tools/repo-checks/csharp/schema.json").as_posix()
        source = source.replace("Join-Path $PSScriptRoot 'csharp/schema.json'", "'" + schema_path + "'")
        self.fixture_checker.write_text(source, encoding="utf-8")
        self.write("global.json", (REPOSITORY / "global.json").read_text(encoding="utf-8"))
        self.write("src/Fixture/Fixture.csproj",
                   '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                   '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        self.write("src/Fixture/One.cs", "namespace N; class One {}\n")

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

    def test_unresolved_async_lambda_is_retained_as_candidate(self) -> None:
        self.write("src/Fixture/One.cs", "class One { void Run() { External(async () => {}); } }")
        f = self.measure()["findings"]
        self.assertEqual(1, len(f))
        self.assertEqual("asyncVoid", f[0]["rule"])
        self.assertTrue(f[0]["candidate"])

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
        self.set_today("2026-11-01")
        self.assert_exit(1, self.run_health("Verify", "-BaseRef", self.base), "removeBy: 2026-10-31 -> 2026-11-01")

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
