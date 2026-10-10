# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Verify the shared policy contract and the manifest's drift gate."""

from __future__ import annotations

import configparser
import hashlib
import json
import re
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BUNDLE = ROOT / "tools" / "repo-checks" / "csharp"
FILES = (
    "Directory.Build.props", ".editorconfig", "BannedSymbols.Common.txt",
    "BannedSymbols.Files.txt", "BannedSymbols.Tests.txt", "schema.json",
)
ENGINES = [engine for name in ("pwsh", "powershell") if (engine := shutil.which(name))]


class HealthBundleTests(unittest.TestCase):
    def test_manifest_covers_exactly_the_six_distribution_files(self) -> None:
        manifest = json.loads((BUNDLE / "bundle-manifest.json").read_bytes())
        self.assertEqual(1, manifest["version"])
        self.assertEqual(list(FILES), manifest["copyFiles"])
        self.assertEqual(list(FILES), [entry["path"] for entry in manifest["files"]])
        for entry in manifest["files"]:
            with self.subTest(path=entry["path"]):
                content = (BUNDLE / entry["path"]).read_bytes()
                self.assertEqual(len(content), entry["byteLength"])
                self.assertEqual(hashlib.sha256(content).hexdigest(), entry["sha256"])

    def test_all_bundle_files_are_utf8_without_bom_and_use_lf(self) -> None:
        for path in BUNDLE.iterdir():
            if path.is_file():
                with self.subTest(path=path.name):
                    content = path.read_bytes()
                    content.decode("utf-8")
                    self.assertFalse(content.startswith(b"\xef\xbb\xbf"))
                    self.assertNotIn(b"\r", content)
                    self.assertTrue(content.endswith(b"\n"))

    def test_editorconfig_parses_with_valid_unique_keys_and_exact_severities(self) -> None:
        parser = configparser.ConfigParser(interpolation=None, strict=True)
        parser.optionxform = str
        source = (BUNDLE / ".editorconfig").read_text(encoding="utf-8")
        parser.read_string("[editorconfig]\n" + source)
        self.assertEqual(["editorconfig", "*.cs", "tests/**/*.cs"], parser.sections())
        self.assertEqual({"root": "true"}, dict(parser["editorconfig"]))
        for section in parser.values():
            for key in section:
                self.assertRegex(key, r"^[A-Za-z_][A-Za-z0-9_.]*$")
        severities = {
            "warning": "IDE0005 IDE0051 IDE0052 IDE0059 IDE0060 IDE0079 IDE1006 IDE0330 "
                       "CA1031 CA1307 CA1309 CA1310 CA1822 CA1862 CA2000 CA2012 CA2016 CA2201 CA5392 "
                       "RS0030 VSTHRD002 VSTHRD110 RS0022 RS0026 RS0027 RS0041",
            "error": "VSTHRD100 CS4014 RS0016 RS0017 RS0024 RS0025 RS0036 RS0037",
            "none": "CA2007",
        }
        expected = {f"dotnet_diagnostic.{rule}.severity": severity
                    for severity, rules in severities.items() for rule in rules.split()}
        actual = {key: value for key, value in parser["*.cs"].items()
                  if key.startswith("dotnet_diagnostic.")}
        self.assertEqual(expected, actual)
        self.assertEqual("true", parser["*.cs"]["csharp_prefer_system_threading_lock"])
        self.assertEqual("non_public", parser["*.cs"]["dotnet_code_quality_unused_parameters"])
        self.assertEqual({"dotnet_diagnostic.CA1707.severity": "none",
                          "dotnet_diagnostic.xUnit1031.severity": "warning",
                          "dotnet_diagnostic.xUnit1051.severity": "warning"},
                         dict(parser["tests/**/*.cs"]))
        self.assertNotRegex(source, r"(?i)(begin|end).*managed|managed.*(begin|end)")

    def test_props_has_exact_package_assets_and_public_api_condition(self) -> None:
        root = ET.parse(BUNDLE / "Directory.Build.props").getroot()
        self.assertEqual("Project", root.tag)
        references = [(group.get("Condition"), reference.attrib)
                      for group in root.findall("ItemGroup")
                      for reference in group.findall("PackageReference")]
        assets = {"PrivateAssets": "all",
                  "IncludeAssets": "runtime;build;native;contentfiles;analyzers;buildtransitive"}
        self.assertEqual([
            (None, {"Include": "Microsoft.CodeAnalysis.BannedApiAnalyzers", **assets}),
            (None, {"Include": "Microsoft.VisualStudio.Threading.Analyzers", **assets}),
            ("'$(HealthPublicApi)' == 'true'",
             {"Include": "Microsoft.CodeAnalysis.PublicApiAnalyzers", **assets}),
        ], references)
        self.assertEqual(3, len(root.findall(".//PackageReference")))
        groups = {group.get("Condition"): [item.get("Include")
                  for item in group.findall("AdditionalFiles")]
                  for group in root.findall("ItemGroup")}
        self.assertEqual({
            None: ["$(RepoHealthRoot)eng/core-health/BannedSymbols.Common.txt",
                   "$(RepoHealthRoot)eng/core-health/BannedSymbols.Files.txt",
                   "$(RepoHealthRoot)eng/code-health/$(HealthLayer)/BannedSymbols.txt"],
            "'$(HealthLayer)' == 'tests'": ["$(RepoHealthRoot)eng/core-health/BannedSymbols.Tests.txt"],
            "'$(HealthPublicApi)' == 'true'": ["$(MSBuildProjectDirectory)/PublicAPI.Shipped.txt",
                                            "$(MSBuildProjectDirectory)/PublicAPI.Unshipped.txt"],
        }, groups)

    def test_props_preserves_enforcement_and_baseline_settings(self) -> None:
        properties = ET.parse(BUNDLE / "Directory.Build.props").getroot().find("PropertyGroup")
        self.assertEqual([
            ("Nullable", "enable", None), ("LangVersion", "14.0", None),
            ("ImplicitUsings", "enable", None), ("EnableNETAnalyzers", "true", None),
            ("AnalysisLevel", "latest-recommended", None), ("EnforceCodeStyleInBuild", "true", None),
            ("TreatWarningsAsErrors", "true", None), ("Deterministic", "true", None),
            ("ContinuousIntegrationBuild", "true", "'$(CI)' == 'true'"),
            ("RestorePackagesWithLockFile", "true", None),
            ("GenerateDocumentationFile", "false", "'$(HealthLayer)' == 'tests'"),
            ("GenerateDocumentationFile", "true", "'$(HealthPublicApi)' == 'true'"),
            ("WarningsNotAsErrors", "$(WarningsNotAsErrors);$(HealthBaselineWarningIds)", None),
            ("ErrorLog", "$(RepoHealthRoot)artifacts/code-health/$(MSBuildProjectName).sarif,version=2.1",
             "'$(HealthCollectDiagnostics)' == 'true'"),
        ], [(element.tag, element.text, element.get("Condition")) for element in properties])

    def test_banned_symbols_are_documentation_ids_without_wildcards_or_duplicates(self) -> None:
        seen = set()
        for name, count in zip(FILES[2:5], (29, 32, 3)):
            lines = (BUNDLE / name).read_text(encoding="utf-8").splitlines()
            self.assertEqual(count, len(lines))
            for line in lines:
                with self.subTest(path=name, symbol=line):
                    self.assertRegex(line, r"^[TMPF]:[A-Za-z_][A-Za-z0-9_.`]*(?:\([A-Za-z0-9_.`,{}\[\]]+\))?$")
                    self.assertNotIn("*", line)
                    self.assertNotIn("?", line)
                    self.assertNotIn(line, seen)
                    stack = []
                    pairs = {"}": "{", "]": "[", ")": "("}
                    for character in line:
                        if character in "{[(":
                            stack.append(character)
                        elif character in pairs:
                            self.assertTrue(stack)
                            self.assertEqual(pairs[character], stack.pop())
                    self.assertEqual([], stack)
                    seen.add(line)
        self.assertEqual(64, len(seen))

    def test_schema_uses_defined_fields_and_marks_unspecified_documents(self) -> None:
        schema = json.loads((BUNDLE / "schema.json").read_bytes())
        definitions = schema["$defs"]
        for name in ("policy",):
            self.assertEqual({}, definitions[name]["not"])
            self.assertNotIn("properties", definitions[name])
        for name, fields in {
            "baseline": "schemaVersion measurementVersion snapshotCommit limits entities findings",
            "baseline-entity": "project kind symbol locations ceilings owner issue contentHash",
            "baseline-finding": "rule project path member symbol syntaxHash count owner removeBy",
            "seam-owner-entry": "path member symbol owner reason kind review",
        }.items():
            self.assertEqual(set(fields.split()), set(definitions[name]["properties"]))
            self.assertEqual(set(fields.split()) - ({"contentHash"} if name == "baseline-entity" else set()), set(definitions[name]["required"]))
            self.assertFalse(definitions[name]["additionalProperties"])


@unittest.skipUnless(ENGINES, "PowerShell is required to run the manifest CLI regressions")
class ManifestCommandTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parent,
                                               prefix=".csharp-bundle-test-")
        self.addCleanup(temporary.cleanup)
        self.bundle = Path(temporary.name)
        shutil.copytree(BUNDLE, self.bundle, dirs_exist_ok=True)
        self.manifest = self.bundle / "bundle-manifest.json"

    def run_script(self, *arguments: str, engine: str | None = None) -> subprocess.CompletedProcess:
        return subprocess.run([engine or ENGINES[0], "-NoLogo", "-NoProfile", "-NonInteractive",
                               "-ExecutionPolicy", "Bypass",
                               "-File", str(self.bundle / "New-HealthBundle.ps1"), *arguments],
                              cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                              errors="replace", timeout=30)

    def assert_success(self, result: subprocess.CompletedProcess) -> None:
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("6 files", result.stdout)

    def test_check_is_read_only_in_each_available_powershell(self) -> None:
        original = {path.name: path.read_bytes() for path in self.bundle.iterdir()}
        for engine in ENGINES:
            with self.subTest(engine=engine):
                self.assert_success(self.run_script("-Check", engine=engine))
                self.assertEqual(original, {path.name: path.read_bytes() for path in self.bundle.iterdir()})

    def test_regeneration_is_deterministic_across_powershell_versions(self) -> None:
        expected = self.manifest.read_bytes()
        for engine in ENGINES:
            with self.subTest(engine=engine):
                self.manifest.unlink()
                self.assert_success(self.run_script(engine=engine))
                self.assertEqual(expected, self.manifest.read_bytes())

    def test_check_rejects_payload_drift_without_rewriting_manifest(self) -> None:
        original = self.manifest.read_bytes()
        path = self.bundle / "BannedSymbols.Common.txt"
        path.write_bytes(path.read_bytes() + b"P:System.Environment.TickCount\n")
        self.assertEqual(1, self.run_script("-Check").returncode)
        self.assertEqual(original, self.manifest.read_bytes())
        self.assert_success(self.run_script())
        self.assert_success(self.run_script("-Check"))
        entry = next(entry for entry in json.loads(self.manifest.read_bytes())["files"]
                     if entry["path"] == path.name)
        self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), entry["sha256"])
        self.assertEqual(path.stat().st_size, entry["byteLength"])

    def test_check_rejects_missing_payload_or_manifest(self) -> None:
        for name in (*FILES, "bundle-manifest.json"):
            with self.subTest(path=name):
                path = self.bundle / name
                original = path.read_bytes()
                path.unlink()
                self.assertEqual(1, self.run_script("-Check").returncode)
                self.assertFalse(path.exists())
                path.write_bytes(original)

    def test_check_rejects_manifest_tampering(self) -> None:
        original = self.manifest.read_bytes()
        for field, value in (("sha256", "0" * 64), ("byteLength", 0), ("path", "unknown.txt")):
            document = json.loads(original)
            document["files"][0][field] = value
            self.manifest.write_text(json.dumps(document), encoding="utf-8")
            self.assertEqual(1, self.run_script("-Check").returncode)
        for field, value in (("version", 2), ("copyFiles", []), ("files", [])):
            document = json.loads(original)
            document[field] = value
            self.manifest.write_text(json.dumps(document), encoding="utf-8")
            self.assertEqual(1, self.run_script("-Check").returncode)
        for content in (b"not JSON\n", b"\xef\xbb\xbf" + original,
                        original.replace(b"\n", b"\r\n")):
            self.manifest.write_bytes(content)
            self.assertEqual(1, self.run_script("-Check").returncode)
            self.assertEqual(content, self.manifest.read_bytes())

    def test_generation_and_check_reject_crlf_and_bom(self) -> None:
        path = self.bundle / ".editorconfig"
        original = path.read_bytes()
        for content in (original.replace(b"\n", b"\r\n"), b"\xef\xbb\xbf" + original):
            path.write_bytes(content)
            for arguments in ((), ("-Check",)):
                with self.subTest(arguments=arguments, prefix=content[:5]):
                    self.assertEqual(1, self.run_script(*arguments).returncode)


if __name__ == "__main__":
    unittest.main()
