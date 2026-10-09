# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Offline provider contracts, plus an opt-out host build/mutation acceptance test."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
CHECKER = ROOT / 'tools/repo-checks/repo-health.ps1'


class EnforcementTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(dir=Path(__file__).parent, prefix='.h03-fixture-')
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name).resolve()
        self.write('global.json', (ROOT / 'global.json').read_text())
        self.write('src/Fixture/Fixture.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        shutil.copytree(ROOT / 'eng/core-health', self.root / 'eng/core-health')
        self.source()

    def write(self, path, text):
        p = self.root / path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding='utf-8', newline='\n')

    def source(self, calls=('File.WriteAllText("a", "b");',), path='src/Fixture/One.cs'):
        self.write(path, 'using System.IO;\nclass Fixture {\n void Save() {\n' + ''.join('  ' + c + '\n' for c in calls) + ' }\n}\n')

    def sarif(self, lines=(4,), path='src/Fixture/One.cs', symbol='File.WriteAllText(string, string?)'):
        self.write('fixture.sarif', json.dumps({'version': '2.1.0', 'runs': [{'results': [
            {'ruleId': 'RS0030', 'level': 'warning', 'message': {'text': f"The symbol '{symbol}' is banned in this project"},
             'locations': [{'physicalLocation': {'artifactLocation': {'uri': path}, 'region': {'startLine': line, 'startColumn': 3}}}]}
            for line in lines]}]}))

    def run_ps(self, body, *, measure=False):
        # Function-only loading avoids executing the production entry point. Only
        # reference evaluation is stubbed: parser/compiler execution is real and offline.
        setup = f"""
. '{CHECKER.as_posix()}' -FunctionsOnly
$Root = '{self.root.as_posix()}'
$Mode = 'Verify'; $BaseRef = ''; $BaselinePath = 'eng/code-health/baseline.json'
$script:Seams = @{{entries=@()}}
$script:Failures = 0; $script:ChangedPaths = @(); $script:Snapshot = 'abcdef0'
$script:Inventory = @('src/Fixture/One.cs', 'src/Fixture/Fixture.csproj')
$script:DiagnosticRequests = [Collections.Generic.List[hashtable]]::new()
$script:ResolvedDiagnostics = @()
$script:HealthProjects = @(@{{ name='Fixture'; path='src/Fixture/Fixture.csproj'; layer='infrastructure'; evaluation=@{{ Properties=@{{ DefineConstants='TRACE' }}; Items=@{{ Compile=@(@{{FullPath=(Join-Path $Root 'src/Fixture/One.cs')}}) }} }} }})
$checked = ${{function:Invoke-Checked}}
function Invoke-Checked([string]$Exe, [string[]]$Arguments) {{
    if ($Arguments -contains '-target:ResolveReferences') {{ return '{{"Items":{{"ReferencePath":[]}}}}' }}
    & $checked $Exe $Arguments
}}
Push-Location $Root
try {{
{body}
}} finally {{ Pop-Location }}
"""
        self.write('runner.ps1', setup)
        return subprocess.run(['pwsh', '-NoProfile', '-File', str(self.root / 'runner.ps1')], capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=90)

    def fingerprints(self, lines=(4,), path='src/Fixture/One.cs'):
        self.sarif(lines, path)
        extra = ''
        if path != 'src/Fixture/One.cs':
            extra = f"$script:Inventory[0] = '{path}'; $script:HealthProjects[0].evaluation.Items.Compile[0].FullPath = Join-Path $Root '{path}'\n"
        result = self.run_ps(extra + "Read-ProjectSarif (Join-Path $Root 'fixture.sarif') 'Fixture' 'src/Fixture/Fixture.csproj'\n[void](Measure-Syntax)\nConvertTo-Json -Depth 100 -InputObject $script:ResolvedDiagnostics")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        return json.loads(result.stdout)

    def compare(self, old, new):
        self.write('old.json', json.dumps({'limits': {}, 'entities': [], 'findings': old}))
        self.write('new.json', json.dumps({'limits': {}, 'entities': [], 'findings': new}))
        result = self.run_ps("$old = ConvertFrom-Json -AsHashtable (Get-Content old.json -Raw); $new = ConvertFrom-Json -AsHashtable (Get-Content new.json -Raw)\nCompare-Debt $old $new 'values' $false\nexit ([int]($script:Failures -gt 0))")
        return result

    def test_new_rs0030_fails_with_symbol_and_member(self):
        f = self.fingerprints()
        self.assertEqual('M:System.IO.File.WriteAllText(System.String,System.String)', f[0]['symbol'])
        self.assertEqual('Fixture.Save()', f[0]['member'])
        r = self.compare([], f)
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertIn('HC_DIAGNOSTIC src/Fixture/One.cs:4', r.stdout)
        self.assertIn('Fixture.Save()', r.stdout)

    def test_new_occurrence_inside_indebted_file_fails(self):
        f = self.fingerprints()
        self.source(('File.WriteAllText("a", "b");', 'File.WriteAllText("a", "b");'))
        added = self.fingerprints((4, 5))
        self.assertEqual(f[0]['syntaxHash'], added[1]['syntaxHash'])
        added[0]['count'] = 2
        self.assertEqual(1, self.compare(f, added[:1]).returncode)

    def test_deleting_another_finding_cannot_offset_new_one(self):
        f = self.fingerprints()
        self.source(('File.WriteAllText("new", "b");',))
        new = self.fingerprints()
        self.assertEqual(1, self.compare(f, new).returncode)

    def test_rename_preserves_identity_and_trivia_normalizes(self):
        f = self.fingerprints()
        self.source(('File.WriteAllText( /* trivia */ "a", "b");',), 'src/Fixture/Renamed.cs')
        new = self.fingerprints(path='src/Fixture/Renamed.cs')
        self.assertEqual(f[0]['syntaxHash'], new[0]['syntaxHash'])
        self.assertEqual(0, self.compare(f, new).returncode)

    def test_exact_seam_owner_and_file_capability(self):
        f = self.fingerprints()[0]
        self.write('finding.json', json.dumps(f))
        for member in ('Fixture.Save()', '*'):
            entry = {'schemaVersion': 1, 'entries': [{'path': f['path'], 'member': member, 'symbol': f['symbol'], 'owner': 'NVT CORE', 'reason': 'protected writer', 'kind': 'permanent-seam', 'review': 'owner-approved PR'}]}
            self.write('seams.json', json.dumps(entry))
            r = self.run_ps("$s = Read-HealthDocument (Get-Content seams.json -Raw) 'seam-owners'; $f = ConvertFrom-Json -AsHashtable (Get-Content finding.json -Raw)\nif (-not (Test-SeamOwner $f $s.entries)) { exit 1 }\n$f.symbol += '.other'; if (Test-SeamOwner $f $s.entries) { exit 1 }")
            self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_missing_unreadable_and_failed_sarif_fail(self):
        for value in (None, 'not JSON', json.dumps({'version': '2.1.0', 'runs': [{'results': [], 'invocations': [{'executionSuccessful': False}]}]})):
            with self.subTest(value=value):
                if value is not None: self.write('fixture.sarif', value)
                r = self.run_ps("Read-ProjectSarif (Join-Path $Root 'fixture.sarif') 'Fixture' 'Fixture.csproj'")
                self.assertNotEqual(0, r.returncode)

    def test_format_nonzero_grandfathered_passes_new_fails(self):
        self.write('format.json', json.dumps([{'FilePath': 'src/Fixture/One.cs', 'FileChanges': [{'LineNumber': 4, 'CharNumber': 3, 'DiagnosticId': 'WHITESPACE', 'FormatDescription': 'Fix spacing'}]}]))
        r = self.run_ps("Read-FormatReport (Join-Path $Root 'format.json') 2 'format'\n[void](Measure-Syntax)\nConvertTo-Json -Depth 100 -InputObject $script:ResolvedDiagnostics")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        findings = json.loads(r.stdout)
        self.write('findings.json', json.dumps(findings))
        for grandfathered, expected in ((True, 0), (False, 1)):
            self.write('baseline.json', json.dumps({'findings': findings if grandfathered else []}))
            r = self.run_ps("$f = @(ConvertFrom-Json -AsHashtable (Get-Content findings.json -Raw)); $b = ConvertFrom-Json -AsHashtable (Get-Content baseline.json -Raw)\nTest-FormatExit 2 $f $b\nexit ([int]($script:Failures -gt 0))")
            self.assertEqual(expected, r.returncode, r.stdout + r.stderr)

    def test_format_crash_missing_malformed_and_empty_nonzero_fail(self):
        for code, content in ((1, '[]'), (2, '[]'), (0, '{}'), (0, None)):
            with self.subTest(code=code, content=content):
                p=self.root/'format.json'
                if p.exists(): p.unlink()
                if content is not None: self.write('format.json', content)
                r=self.run_ps(f"Read-FormatReport (Join-Path $Root 'format.json') {code} 'format'")
                self.assertNotEqual(0,r.returncode)

    def test_managed_block_and_weaker_descendant_override_fail(self):
        common=(ROOT/'eng/core-health/.editorconfig').read_text()
        self.write('common.txt',common)
        good='# BEGIN CORE HEALTH MANAGED BLOCK\n'+common+'# END CORE HEALTH MANAGED BLOCK\n'
        for text, expected in ((good,0),(good.replace('indent_size = 4','indent_size = 2'),1),(good+'\n[*.cs]\ndotnet_diagnostic.RS0030.severity = none\n',1)):
            self.write('editor.txt',text)
            r=self.run_ps("Test-ManagedBlock (Get-Content common.txt -Raw) (Get-Content editor.txt -Raw)")
            self.assertEqual(expected,int(r.returncode!=0),r.stdout+r.stderr)
        for section in ('*.cs', '*.{cs,vb}', '*'):
            with self.subTest(section=section):
                r=self.run_ps(f"Test-EditorOverrides (Get-Content common.txt -Raw) \"[{section}]`ndotnet_diagnostic.RS0030.severity = none\" 'src/.editorconfig'")
                self.assertNotEqual(0,r.returncode)

    def check_editor(self, text, path='src/.editorconfig'):
        self.write('editor.txt', text)
        return self.run_ps(f"Test-EditorOverrides (Get-Content eng/core-health/.editorconfig -Raw) (Get-Content editor.txt -Raw) '{path}'")

    def test_descendant_editorconfig_root_true_fails(self):
        for setting in ('root = true', '  RoOt = TrUe  '):
            with self.subTest(setting=setting):
                r = self.check_editor('# subtree\n' + setting + '\n[*.cs]\n')
                self.assertNotEqual(0, r.returncode)
                self.assertIn('HC_DRIFT src/.editorconfig:2 descendant root=true', r.stdout + r.stderr)

    def test_descendant_editorconfig_without_root_passes(self):
        r = self.check_editor('[*.cs]\ndotnet_diagnostic.RS0030.severity = warning\n')
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_descendant_editorconfig_root_false_passes(self):
        r = self.check_editor('root = false\n[*.cs]\n')
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_repository_editorconfig_root_true_passes(self):
        r = self.check_editor('root = true\n[*.cs]\n', '.editorconfig')
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_descendant_editorconfig_commented_root_true_passes(self):
        r = self.check_editor('# root = true\n  ; ROOT = TRUE\n[*.cs]\n')
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_descendant_editorconfig_root_in_section_is_not_preamble(self):
        r = self.check_editor('  [*.cs]  \nroot = true\n')
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_bundle_file_drift_fails_even_with_changed_hash(self):
        # A self-consistent PR hash cannot approve altered canonical bytes.
        subprocess.run(['git','init','-q',str(self.root)],check=True)
        shutil.copytree(ROOT/'tools/repo-checks/csharp',self.root/'tools/repo-checks/csharp')
        env=os.environ|{'GIT_AUTHOR_NAME':'fixture','GIT_AUTHOR_EMAIL':'fixture@example.test','GIT_COMMITTER_NAME':'fixture','GIT_COMMITTER_EMAIL':'fixture@example.test'}
        subprocess.run(['git','-C',str(self.root),'add','.'],check=True,env=env)
        subprocess.run(['git','-C',str(self.root),'commit','-qm','fixture'],check=True,env=env)
        commit=subprocess.check_output(['git','-C',str(self.root),'rev-parse','HEAD'],env=env,text=True).strip()
        import hashlib
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (self.root/'eng/core-health').iterdir()}
        self.write('eng/core-health.lock.json',json.dumps({'schemaVersion':1,'coreCommit':commit,'files':files}))
        self.write('.editorconfig',(ROOT/'.editorconfig').read_text())
        self.write('eng/core-health/BannedSymbols.Common.txt','P:System.DateTime.Now\n')
        files['BannedSymbols.Common.txt']=hashlib.sha256((self.root/'eng/core-health/BannedSymbols.Common.txt').read_bytes()).hexdigest()
        self.write('eng/core-health.lock.json',json.dumps({'schemaVersion':1,'coreCommit':commit,'files':files}))
        r=self.run_ps("$Mode='Enroll'; Test-BundleDrift")
        self.assertNotEqual(0,r.returncode)
        self.assertIn('approved Core revision',r.stderr)

    def test_unclassified_project_and_evaluated_overrides_fail(self):
        props={'MSBuildProjectName':'Fixture','HealthLayer':'infrastructure','HealthPublicApi':'','HealthBaselineWarningIds':'',
               'TreatWarningsAsErrors':'true','Nullable':'enable','AnalysisLevel':'latest-recommended','LangVersion':'14.0',
               'EnableNETAnalyzers':'true','EnforceCodeStyleInBuild':'true','RunAnalyzers':'true','RunAnalyzersDuringBuild':'true',
               'Deterministic':'true','RestorePackagesWithLockFile':'true','ImplicitUsings':'enable',
               'NoWarn':'','WarningsAsErrors':';NU1605;SYSLIB0011','WarningsNotAsErrors':'','ErrorLog':str(self.root/'artifacts/code-health/Fixture.sarif')+',version=2.1'}
        names={'Microsoft.CodeAnalysis.BannedApiAnalyzers':'3.3.4','Microsoft.VisualStudio.Threading.Analyzers':'17.14.15'}
        items={'AdditionalFiles':[{'FullPath':str(self.root/'eng/core-health'/n)} for n in ('BannedSymbols.Common.txt','BannedSymbols.Files.txt')], 'PackageReference':[{'Identity':n,'PrivateAssets':'all','IncludeAssets':'runtime;build;native;contentfiles;analyzers;buildtransitive'} for n in names], 'PackageVersion':[{'Identity':n,'Version':v} for n,v in names.items()]}
        for key,value in ((None,None),('HealthLayer',''),('HealthLayer','unknown'),('TreatWarningsAsErrors','false'),('Nullable','disable'),('AnalysisLevel','none'),('WarningsAsErrors',''),('HealthBaselineWarningIds','RS0030'),('WarningsNotAsErrors','RS0030')):
            p=props|({key:value} if key else {})
            self.write('evaluated.json',json.dumps({'Properties':p,'Items':items}))
            r=self.run_ps("$e=ConvertFrom-Json -AsHashtable (Get-Content evaluated.json -Raw); Test-ProjectProperties $e 'Fixture.csproj' @{findings=@()}")
            self.assertEqual(0 if key is None else 1,int(r.returncode!=0),r.stdout+r.stderr)
        items['PackageReference'][0]['ExcludeAssets']='analyzers'
        self.write('evaluated.json',json.dumps({'Properties':props,'Items':items}))
        self.assertNotEqual(0,self.run_ps("$e=ConvertFrom-Json -AsHashtable (Get-Content evaluated.json -Raw); Test-ProjectProperties $e 'Fixture.csproj' $null").returncode)

    def test_format_warning_with_prospective_coordinates_reuses_sarif(self):
        self.sarif()
        self.write('format.json',json.dumps([{'FilePath':'src/Fixture/One.cs','FileChanges':[{'LineNumber':100,'CharNumber':100,'DiagnosticId':'RS0030','FormatDescription':"warning RS0030: banned symbol"}]}]))
        r=self.run_ps("Read-ProjectSarif (Join-Path $Root 'fixture.sarif') 'Fixture' 'Fixture.csproj'\nRead-FormatReport (Join-Path $Root 'format.json') 2 'format'\n[void](Measure-Syntax)\nConvertTo-Json -Depth 100 -InputObject $script:ResolvedDiagnostics")
        self.assertEqual(0,r.returncode,r.stdout+r.stderr)
        findings=json.loads(r.stdout)
        self.assertEqual(findings[0]['syntaxHash'],findings[1]['syntaxHash'])
        self.assertTrue(findings[1]['reuse'])

    def test_suppression_scope_growth_and_error_suppression_fail(self):
        self.source(('File.WriteAllText("a", "b");',))
        self.sarif()
        base=(self.root/'src/Fixture/One.cs').read_text()
        self.write('src/Fixture/One.cs',base.replace('  File.','#pragma warning disable CA1031\n  File.').replace(' }\n','#pragma warning restore CA1031\n }\n'))
        self.sarif((5,))
        body="Read-ProjectSarif (Join-Path $Root 'fixture.sarif') 'Fixture' 'Fixture.csproj'\n[void](Measure-Syntax)\nConvertTo-Json -Depth 100 -InputObject @($script:ResolvedDiagnostics | Where-Object { $_.rule -eq 'suppressionScopes' })"
        r=self.run_ps(body); self.assertEqual(0,r.returncode,r.stdout+r.stderr); old=json.loads(r.stdout)
        self.write('src/Fixture/One.cs',(self.root/'src/Fixture/One.cs').read_text().replace('#pragma warning restore','  System.Console.WriteLine("new covered code");\n#pragma warning restore'))
        r=self.run_ps(body); self.assertEqual(0,r.returncode,r.stdout+r.stderr)
        self.assertEqual(1,self.compare(old,json.loads(r.stdout)).returncode)
        self.write('src/Fixture/One.cs',(self.root/'src/Fixture/One.cs').read_text().replace('CA1031','VSTHRD100'))
        r=self.run_ps(body); self.assertNotEqual(0,r.returncode); self.assertIn('protected error',r.stderr)

    def suppression_scopes(self, source):
        self.write('src/Fixture/One.cs', source)
        # The provider drops suppressed warnings; scope fingerprints must still
        # protect code with no surviving RS0030 diagnostic requests.
        self.write('fixture.sarif', json.dumps({'version': '2.1.0', 'runs': [{'results': [
            {'ruleId': 'RS0030', 'level': 'warning', 'suppressions': [{'kind': 'inSource'}]}
        ]}]}))
        r = self.run_ps("Read-ProjectSarif (Join-Path $Root 'fixture.sarif') 'Fixture' 'Fixture.csproj'\n[void](Measure-Syntax)\nConvertTo-Json -Depth 100 -InputObject $script:ResolvedDiagnostics")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        scopes = json.loads(r.stdout)
        self.assertTrue(scopes)
        self.assertTrue(all(f['rule'] == 'suppressionScopes' for f in scopes))
        return scopes

    def assert_expression_scope_growth_fails_verify(self, source, normalized, member, line):
        old = self.suppression_scopes(source)
        self.assertEqual(1, len(old))
        self.assertEqual((member, 'pragma:RS0030', line, 1, 'suppression', False),
                         tuple(old[0][k] for k in ('member', 'symbol', 'line', 'count', 'stage', 'reuse')))
        self.assertEqual(hashlib.sha256(normalized.encode()).hexdigest(), old[0]['syntaxHash'])
        added = self.suppression_scopes(source.replace('File.ReadAllText("a")', 'File.ReadAllText("a") + File.ReadAllText("b")'))
        self.assertEqual(1, len(added))
        self.assertNotEqual(old[0]['syntaxHash'], added[0]['syntaxHash'])
        self.write('old.json', json.dumps({'entities': [], 'findings': old}))
        self.write('new.json', json.dumps({'entities': [], 'findings': added}))
        r = self.run_ps("""
function Write-LedgerWarningIds($Baseline) {} # fixture has no generated project map
$old = ConvertFrom-Json -AsHashtable (Get-Content old.json -Raw)
Enroll-Baseline $old (Join-Path $Root $BaselinePath)
$enrolled = Read-Baseline (Get-Content $BaselinePath -Raw) 'fixture enrollment'
Compare-Debt $enrolled $old 'values' $true
if ($script:Failures) { throw 'Unchanged enrolled scope failed Verify' }
$new = ConvertFrom-Json -AsHashtable (Get-Content new.json -Raw)
Compare-Debt $enrolled $new 'values' $true
exit ([int]($script:Failures -gt 0))
""")
        self.assertEqual(1, r.returncode, r.stdout + r.stderr)
        self.assertIn('Enroll passed', r.stdout)
        self.assertIn('HC_DIAGNOSTIC src/Fixture/One.cs:', r.stdout)
        self.assertIn('suppressionScopes:', r.stdout)
        self.assertIn('New fingerprint;', r.stdout)

    def test_expression_bodied_member_scope_growth_fails_verify(self):
        self.assert_expression_scope_growth_fails_verify('''using System.IO;
class Fixture {
#pragma warning disable RS0030
 string Read() => File.ReadAllText("a");
#pragma warning restore RS0030
}
''', '=> File . ReadAllText ( "a" )', 'Fixture.Read()', 4)

    def test_field_initializer_scope_growth_fails_verify(self):
        self.assert_expression_scope_growth_fails_verify('''using System.IO;
class Fixture {
#pragma warning disable RS0030
 string text = File.ReadAllText("a");
#pragma warning restore RS0030
}
''', '= File . ReadAllText ( "a" )', 'Fixture', 4)

    def test_property_initializer_scope_growth_fails_verify(self):
        self.assert_expression_scope_growth_fails_verify('''using System.IO;
class Fixture {
#pragma warning disable RS0030
 string Text { get; } = File.ReadAllText("a");
#pragma warning restore RS0030
}
''', '= File . ReadAllText ( "a" )', 'Fixture', 4)

    def test_expression_lambda_scope_growth_fails_verify(self):
        # The initializer starts before the span, so only the lambda body fits.
        self.assert_expression_scope_growth_fails_verify('''using System.IO;
class Fixture {
 System.Func<string> read =
  () =>
#pragma warning disable RS0030
   File.ReadAllText("a")
#pragma warning restore RS0030
  ;
}
''', 'File . ReadAllText ( "a" )', 'Fixture', 6)

    def test_expressions_inside_covered_statements_are_not_counted_twice(self):
        statements = ['System.Func<string> read = () => File.ReadAllText("a");',
                      'System.Func<string, string> readOne = path => File.ReadAllText(path);',
                      'string Local() => File.ReadAllText("b");']
        source = 'using System.IO;\nclass Fixture {\n void Read() {\n#pragma warning disable RS0030\n' + '\n'.join(statements) + '\n#pragma warning restore RS0030\n }\n}\n'
        scopes = self.suppression_scopes(source)
        self.assertEqual(3, len(scopes))
        self.assertEqual({'pragma:RS0030'}, {f['symbol'] for f in scopes})
        # These remain statement hashes, with no lambda/arrow/initializer extras.
        normalized = ['System . Func < string > read = ( ) => File . ReadAllText ( "a" ) ;',
                      'System . Func < string , string > readOne = path => File . ReadAllText ( path ) ;',
                      'string Local ( ) => File . ReadAllText ( "b" ) ;']
        self.assertEqual({hashlib.sha256(n.encode()).hexdigest() for n in normalized},
                         {f['syntaxHash'] for f in scopes})

    def test_suppressed_warning_as_error_and_required_analyzer_metadata(self):
        report={'version':'2.1.0','runs':[{'results':[{'ruleId':'CS0618','level':'error','suppressions':[{'kind':'inSource'}]}], 'tool':{'driver':{'rules':[{'id':'RS0030'},{'id':'VSTHRD100'}]}}}]}
        self.write('fixture.sarif',json.dumps(report))
        body="Read-ProjectSarif (Join-Path $Root 'fixture.sarif') 'Fixture' 'Fixture.csproj' -RequireAnalyzerMetadata"
        r=self.run_ps(body); self.assertEqual(0,r.returncode,r.stdout+r.stderr)
        report['runs'][0]['results'][0]['ruleId']='VSTHRD100'
        self.write('fixture.sarif',json.dumps(report))
        self.assertNotEqual(0,self.run_ps(body).returncode)
        report['runs'][0]['results']=[]
        report['runs'][0]['tool']['driver']['rules']=[]
        self.write('fixture.sarif',json.dumps(report))
        r=self.run_ps(body); self.assertNotEqual(0,r.returncode); self.assertIn('did not load',r.stderr)

    def test_many_compliant_entities_without_allowances_do_not_scan_quadratically(self):
        r=self.run_ps("$new=@(1..5000 | ForEach-Object { @{project='Fixture';kind='member';symbol=\"M$_\";locations=@('One.cs');values=@{methodLines=1}} }); $matches=Match-Entities @() $new; if ($matches.Count) { exit 1 }")
        self.assertEqual(0,r.returncode,r.stdout+r.stderr)

    def test_enroll_refuses_existing_baseline_before_build(self):
        self.write('eng/code-health/baseline.json','{}')
        r=self.run_ps("Enroll-Baseline @{} (Join-Path $Root 'eng/code-health/baseline.json')")
        self.assertNotEqual(0,r.returncode)
        self.assertIn('baseline already exists',r.stderr)
        self.assertEqual('{}',(self.root/'eng/code-health/baseline.json').read_text())


@unittest.skipIf(os.environ.get('HEALTH_SKIP_HOST_TESTS') == '1', 'HEALTH_SKIP_HOST_TESTS=1: requires host cache/build')
class HostMutationTests(unittest.TestCase):
    def test_whole_verify_and_banned_call_in_copy(self):
        args=['pwsh','-NoProfile','-File',str(CHECKER),'-Mode','Verify','-Repo','core','-Solution','Nvt.Core.sln']
        r=subprocess.run(args+['-Root',str(ROOT)],cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=600)
        self.assertEqual(0,r.returncode,r.stdout+r.stderr)
        # Keep package/obj assets; use the existing committed tree read-only via
        # GIT_WORK_TREE. No checkout, branch, restore, or shared index mutation.
        with tempfile.TemporaryDirectory(dir=ROOT/'artifacts',prefix='h03-mutation-') as directory:
            copy=Path(directory)
            shutil.copytree(ROOT,copy,dirs_exist_ok=True,ignore=shutil.ignore_patterns('.git','artifacts','__pycache__','.h03-fixture-*'))
            path=copy/'src/Nvt.Core/IO/AtomicOutput.cs'
            original=path.read_text(encoding='utf-8')
            # A tracked new type/member ensures a genuinely new fingerprint.
            path.write_text(original+'\ninternal static class HealthMutation { internal static void Banned() { System.IO.File.WriteAllText("health-mutation", "x"); } }\n',encoding='utf-8')
            gitdir=os.environ.get('GIT_DIR') or subprocess.check_output(['git','rev-parse','--absolute-git-dir'],cwd=ROOT,text=True).strip()
            env=os.environ|{'GIT_DIR':gitdir,'GIT_WORK_TREE':str(copy)}
            r=subprocess.run(args+['-Root',str(copy)],cwd=copy,env=env,capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=600)
            self.assertEqual(1,r.returncode,r.stdout+r.stderr)
            self.assertIn('RS0030',r.stdout)


if __name__ == '__main__':
    unittest.main()
