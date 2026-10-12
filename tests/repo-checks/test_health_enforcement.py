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

    def run_ps(self, body, *, measure=False, checker=CHECKER):
        # Function-only loading avoids executing the production entry point. Only
        # reference evaluation is stubbed: parser/compiler execution is real and offline.
        setup = f"""
. '{checker.as_posix()}' -FunctionsOnly
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

    def test_only_synchronization_paths_allow_adoption(self):
        r = self.run_ps("""
$allowed = @('eng/core-health.lock.json', 'eng/core-health/Directory.Build.props', 'eng/core-health/.editorconfig',
    'eng/core-health/schema.json', 'eng/core-health/BannedSymbols.Common.txt', 'tools/repo-checks/repo-health.ps1',
    'tools/repo-checks/csharp/schema.json', 'tools/repo-checks/csharp/Directory.Build.props', 'tools/repo-checks/doc_sync.py',
    'tools/repo-checks/doc-sync.core.json', 'eng/code-health/baseline.json', 'eng/code-health/test-debt.json',
    'tests/repo-checks/test_x.py', 'docs/guide.md', 'README.md')
foreach ($p in $allowed) { if (-not (Test-SynchronizationPath $p)) { Write-Output "should allow $p"; exit 7 } }
$refused = @('src/Fixture/One.cs', 'src/Fixture/Fixture.csproj', 'Directory.Build.props', 'docs/Evil.cs', 'tests/repo-checks/Fixture.cs',
    'eng/code-health/other.json', 'global.json', '.editorconfig',
    'tools/repo-checks/Gen/Big.cs', 'tools/repo-checks/Gen/Gen.csproj', 'eng/core-health/X.cs', 'eng/core-health/Other.props',
    'eng/core-health/sub/Directory.Build.props', 'tools/repo-checks/csharp/sub/.editorconfig', 'eng/core-health/Dir.Targets',
    'eng/core-health/sub/global.json', 'tools/repo-checks/nuget.config', 'tools/repo-checks/packages.lock.json',
    'tools/repo-checks/stylecop.json', 'eng/core-health/other.json', 'tools/repo-checks/.globalconfig',
    "src/A.cs`nwarning: LF will be replaced by CRLF", "warning: x`nsrc/A.cs", "docs/a`nb.md", "eng/core-health/ok.txt`nsrc/Big.cs")
foreach ($p in $refused) { if (Test-SynchronizationPath $p) { Write-Output "should refuse [$p]"; exit 8 } }
$script:ChangedPaths = @('docs/guide.md', 'src/A.cs', "eng/core-health.lock.json`nwarning: LF will be replaced by CRLF")
if (@(Get-NonSynchronizationPaths).Count -ne 2) { exit 9 }
""")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_generated_block_must_hold_only_per_project_warning_ids(self):
        r = self.run_ps(r"""
$group = "  <PropertyGroup Condition=`"'`$(MSBuildProjectName)' == 'Nvt.Core'`">`n    <HealthBaselineWarningIds>CA1031;RS0030</HealthBaselineWarningIds>`n  </PropertyGroup>`n"
$begin = '  <!-- BEGIN GENERATED HEALTH WARNING IDS -->'
$end = '  <!-- END GENERATED HEALTH WARNING IDS -->'
function Block($inside) { return "<Project>`n$begin`n$inside$end`n</Project>`n" }
if ($null -eq (Get-GeneratedBlockRest (Block ''))) { Write-Output 'empty block refused'; exit 7 }
if ($null -eq (Get-GeneratedBlockRest (Block ($group + $group)))) { Write-Output 'groups refused'; exit 7 }
if ($null -eq (Get-GeneratedBlockRest ((Block $group) -replace "`n", "`r`n"))) { Write-Output 'CRLF refused'; exit 7 }
$refused = @{
    item = (Block "  <ItemGroup><Compile Include=`"x.cs`" /></ItemGroup>`n")
    property = (Block "  <PropertyGroup><DefineConstants>X</DefineConstants></PropertyGroup>`n")
    extraProperty = (Block ($group -replace '</PropertyGroup>', "    <NoWarn>CS0000</NoWarn>`n  </PropertyGroup>"))
    badIds = (Block ($group -replace 'CA1031;RS0030', 'CA1031</HealthBaselineWarningIds><Import Project="x.props" /><HealthBaselineWarningIds>'))
    secondBlock = ((Block $group) + (Block ''))
    wrapped = ("<Project>`n$begin`n$begin`n$group$end`n$end`n</Project>`n")
    noBlock = "<Project>`n</Project>`n"
}
foreach ($name in $refused.Keys) { if ($null -ne (Get-GeneratedBlockRest $refused[$name])) { Write-Output "should refuse $name"; exit 8 } }
foreach ($ignorable in @([string][char]0, [string][char]0xAD, [string][char]0x200B)) {
    # Characters that a culture comparison ignores must still make two files differ.
    $x = Get-GeneratedBlockRest (Block $group)
    $y = Get-GeneratedBlockRest ((Block $group) -replace '<Project>', ('<Project>' + $ignorable))
    if (Test-OrdinalEqual $x $y) { Write-Output "ignorable character $([int][char]$ignorable) is ignored"; exit 10 }
}
foreach ($p in @("eng/core-health.lock$([char]0xAD).json", "eng/code-health/base$([char]0x200B)line.json", "tools/repo-checks/x.p$([char]0xAD)y")) {
    if (Test-SynchronizationPath $p) { Write-Output "should refuse [$p]"; exit 11 }
}
$a = Get-GeneratedBlockRest (Block $group)
$b = Get-GeneratedBlockRest ("<Project>`n<ItemGroup />`n$begin`n$group$end`n</Project>`n")
if ($a -ceq $b) { Write-Output 'outside text must differ'; exit 9 }
""")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_bundle_file_drift_fails_even_with_changed_hash(self):
        # A self-consistent PR hash cannot approve altered canonical bytes.
        subprocess.run(['git','init','-q',str(self.root)],check=True)
        shutil.copytree(ROOT/'tools/repo-checks/csharp',self.root/'tools/repo-checks/csharp')
        shutil.copyfile(CHECKER, self.root / 'tools/repo-checks/repo-health.ps1')
        env=os.environ|{'GIT_AUTHOR_NAME':'fixture','GIT_AUTHOR_EMAIL':'fixture@example.test','GIT_COMMITTER_NAME':'fixture','GIT_COMMITTER_EMAIL':'fixture@example.test'}
        subprocess.run(['git','-C',str(self.root),'add','.'],check=True,env=env)
        subprocess.run(['git','-C',str(self.root),'commit','-qm','fixture'],check=True,env=env)
        commit=subprocess.check_output(['git','-C',str(self.root),'rev-parse','HEAD'],env=env,text=True).strip()
        self.lock_write(commit)
        self.write('.editorconfig',(ROOT/'.editorconfig').read_text())
        self.write('eng/core-health/BannedSymbols.Common.txt','P:System.DateTime.Now\n')
        self.lock_write(commit)
        r=self.run_ps("$Mode='Enroll'; Test-BundleDrift")
        self.assertNotEqual(0,r.returncode)
        self.assertIn('differs from the canonical file',r.stderr)

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

    # --- Line endings: identity hashes must not depend on the checkout's newline style ---

    def multiline_source(self, newline):
        text = ('using System.IO;\nclass Fixture {\n void Save() {\n'
                '  File.WriteAllText(@"a\nb", """\nraw\n  text\n""" + $@"x\n{1}");\n }\n}\n')
        (self.root / 'src/Fixture/One.cs').write_bytes(text.replace('\n', newline).encode('utf-8'))

    def hashes(self):
        self.sarif((4,))
        r = self.run_ps("Read-ProjectSarif (Join-Path $Root 'fixture.sarif') 'Fixture' 'src/Fixture/Fixture.csproj'\n"
                        "$m = Measure-Syntax\n"
                        "ConvertTo-Json -Depth 100 -InputObject @{ findings = @($script:ResolvedDiagnostics | ForEach-Object { $_.syntaxHash }); "
                        "entities = @($m.entities | ForEach-Object { $_.contentHash }) }")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        return json.loads(r.stdout)

    def test_hashes_are_identical_for_lf_and_crlf_sources(self):
        self.multiline_source('\n')
        lf = self.hashes()
        self.multiline_source('\r\n')
        crlf = self.hashes()
        self.multiline_source('\r')
        cr = self.hashes()
        self.assertTrue(lf['findings'] and lf['entities'])
        self.assertEqual(lf, crlf)
        self.assertEqual(lf, cr)

    def test_powershell_hash_ignores_line_endings(self):
        r = self.run_ps("(Get-Hash \"a`r`nb`rc`nd\") -ceq (Get-Hash \"a`nb`nc`nd\")")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertEqual('True', r.stdout.strip())

    # --- Descendant EditorConfig keys outside the protected list ---

    def test_descendant_editorconfig_rejects_generated_code_and_analyzer_diagnostic_keys(self):
        cases = (
            ('[*.cs]\ngenerated_code = true\n', 'generated_code'),
            ('[*]\nGENERATED_CODE = false\n', 'generated_code'),
            ('[*.cs]\ndotnet_analyzer_diagnostic.severity = none\n', 'dotnet_analyzer_diagnostic.severity'),
            ('[*.cs]\ndotnet_analyzer_diagnostic.category-Style.severity = error\n', 'dotnet_analyzer_diagnostic.category-Style.severity'),
        )
        for text, key in cases:
            with self.subTest(key=key, text=text):
                r = self.check_editor(text)
                self.assertNotEqual(0, r.returncode)
                self.assertIn('HC_DRIFT src/.editorconfig:2', r.stdout + r.stderr)
                self.assertIn(key.lower(), (r.stdout + r.stderr).lower())

    def test_descendant_editorconfig_rejects_weaker_severity_for_any_rule(self):
        for value in ('none', 'silent', 'suggestion', 'NONE', 'Suggestion'):
            with self.subTest(value=value):
                r = self.check_editor(f'[*.cs]\ndotnet_diagnostic.CA1234.severity = {value}\n')
                self.assertNotEqual(0, r.returncode)
                self.assertIn('HC_DRIFT src/.editorconfig:2', r.stdout + r.stderr)

    def test_descendant_editorconfig_allows_warning_or_error_severity(self):
        for value in ('warning', 'error'):
            with self.subTest(value=value):
                r = self.check_editor(f'[*.cs]\ndotnet_diagnostic.CA1234.severity = {value}\n')
                self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_new_editorconfig_key_rules_also_apply_to_the_root_tail(self):
        for text in ('[*.cs]\ngenerated_code = true\n', '[*.cs]\ndotnet_diagnostic.CA2000.severity = none\n',
                     '[*.cs]\ndotnet_analyzer_diagnostic.severity = none\n'):
            with self.subTest(text=text):
                r = self.check_editor(text, '.editorconfig')
                self.assertNotEqual(0, r.returncode)
                self.assertIn('HC_DRIFT .editorconfig:2', r.stdout + r.stderr)

    def test_descendant_editorconfig_rejects_code_quality_options(self):
        r = self.check_editor('[*.cs]\ndotnet_code_quality.CA2000.excluded_symbol_names = Foo\n')
        self.assertNotEqual(0, r.returncode)
        self.assertIn('HC_DRIFT src/.editorconfig:2', r.stdout + r.stderr)

    def test_editorconfig_bom_does_not_hide_root_true(self):
        self.write('editor.txt', '\ufeffroot = true\n[*.cs]\n')
        r = self.run_ps("Test-EditorOverrides (Get-Content eng/core-health/.editorconfig -Raw) (Read-EditorText (Join-Path $Root 'editor.txt')) 'src/.editorconfig'")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('HC_DRIFT src/.editorconfig:1', r.stdout + r.stderr)

    def test_editorconfig_parsing_matches_roslyn_for_bom_colon_and_inline_comments(self):
        cases = (
            ('[*.cs]\ndotnet_diagnostic.CA2000.severity : none\n', 'src/.editorconfig:2'),
            ('[*.cs]\ndotnet_diagnostic.CA2000.severity = none # reviewed\n', 'src/.editorconfig:2'),
            ('[*.cs]\ndotnet_diagnostic.CA2000.severity = suggestion ; x\n', 'src/.editorconfig:2'),
            ('root : true\n[*.cs]\n', 'src/.editorconfig:1'),
        )
        for text, where in cases:
            with self.subTest(text=text):
                r = self.check_editor(text)
                self.assertNotEqual(0, r.returncode)
                self.assertIn('HC_DRIFT ' + where, r.stdout + r.stderr)
        r = self.check_editor('[*.cs]\ndotnet_diagnostic.CA2000.severity = warning # ok\n')
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    # --- Enroll owner ---

    def test_enroll_owner_follows_repo_and_explicit_parameter(self):
        for setup, expected in (("$Repo = 'core'", 'NVT CORE'), ("$Repo = 'nfc'", 'NFC'), ("$Repo = 'nfh'", 'NFH'),
                                ("$Repo = 'nfc'; $Owner = ' Team X '", 'Team X'), ("$Repo = 'core'; $Owner = 'NFU'", 'NFU')):
            with self.subTest(setup=setup):
                r = self.run_ps(setup + "\nGet-EnrollOwner")
                self.assertEqual(0, r.returncode, r.stdout + r.stderr)
                self.assertEqual(expected, r.stdout.strip())

    def test_enroll_writes_the_owner_to_entities_and_findings(self):
        self.write('eng/code-health/projects.props',
                   '<Project>\n  <!-- BEGIN GENERATED HEALTH WARNING IDS -->\n  <!-- END GENERATED HEALTH WARNING IDS -->\n</Project>\n')
        measurement = ("@{ entities = @(@{ project = 'Fixture'; kind = 'member'; symbol = 'S'; locations = @('One.cs'); contentHash = ('a' * 64); values = @{ methodLines = 999 } }); "
                       "findings = @(@{ rule = 'suppressions'; project = 'Fixture'; path = 'One.cs'; member = 'm'; symbol = 's'; syntaxHash = ('b' * 64); count = 1 }) }")
        r = self.run_ps("$Repo = 'nfc'\n"
                        f"Enroll-Baseline {measurement} (Join-Path $Root 'eng/code-health/baseline.json')")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        baseline = json.loads((self.root / 'eng/code-health/baseline.json').read_text())
        self.assertEqual(['NFC'], [e['owner'] for e in baseline['entities']])
        self.assertEqual(['NFC'], [f['owner'] for f in baseline['findings']])

    # --- Gitlinks and ignored folders ---

    def git(self, *args):
        env = os.environ | {'GIT_AUTHOR_NAME': 'fixture', 'GIT_AUTHOR_EMAIL': 'fixture@example.test',
                            'GIT_COMMITTER_NAME': 'fixture', 'GIT_COMMITTER_EMAIL': 'fixture@example.test'}
        return subprocess.check_output(['git', '-C', str(self.root), *args], env=env, text=True).strip()

    def test_inventory_skips_gitlinks(self):
        self.git('init', '-q')
        self.git('add', 'src')
        self.git('update-index', '--add', '--cacheinfo', '160000,' + 'a' * 40 + ',third-party/sub')
        r = self.run_ps("$Root = [IO.Path]::GetFullPath($Root)\n(Get-TrackedInventory $Root) -join ','")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn('src/Fixture/One.cs', r.stdout)
        self.assertNotIn('third-party', r.stdout)

    def test_health_files_skip_ignored_folders_but_keep_untracked_ones(self):
        self.git('init', '-q')
        self.write('.gitignore', '.packages/\n')
        weaker = '[*.cs]\ndotnet_diagnostic.RS0030.severity = none\n'
        self.write('.packages/pkg/.editorconfig', weaker)
        self.write('untracked/.editorconfig', weaker)
        r = self.run_ps("$Root = [IO.Path]::GetFullPath($Root); $script:GitRoot = $Root\n(Get-HealthFiles | Where-Object { $_.EndsWith('/.editorconfig') } | Sort-Object) -join ','")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn('untracked/.editorconfig', r.stdout)
        self.assertNotIn('.packages', r.stdout)

    # --- Lock-only mode (application repositories without -CoreRoot) ---

    def app_repo(self):
        self.git('init', '-q', '-b', 'main')
        self.write('.editorconfig', (ROOT / '.editorconfig').read_text())
        self.lock_write()
        self.git('add', '.')
        self.git('commit', '-qm', 'base')

    def lock_write(self, core_commit='a' * 40):
        files = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (self.root / 'eng/core-health').iterdir()}
        checker = self.root / 'tools/repo-checks/repo-health.ps1'
        if not checker.exists():
            checker = CHECKER
        files['repo-health.ps1'] = hashlib.sha256(checker.read_bytes()).hexdigest()
        self.write('eng/core-health.lock.json', json.dumps({'schemaVersion': 1, 'coreCommit': core_commit, 'files': files}))

    def copy_checker(self, suffix=b'# fixture change\n'):
        checker = self.root / 'tools/repo-checks/repo-health.ps1'
        checker.parent.mkdir(parents=True, exist_ok=True)
        checker.write_bytes(CHECKER.read_bytes() + suffix)
        shutil.copyfile(self.root / 'eng/core-health/schema.json', checker.with_name('schema.json'))
        return checker

    def remove_checker_hash(self):
        lock = json.loads((self.root / 'eng/core-health.lock.json').read_bytes())
        del lock['files']['repo-health.ps1']
        self.write('eng/core-health.lock.json', json.dumps(lock))

    def test_lock_only_mode_passes_without_a_core_checkout(self):
        self.app_repo()
        self.assertFalse((self.root / 'tools/repo-checks/csharp').exists())
        r = self.run_ps("$Repo = 'nfc'; $Mode = 'Verify'; Test-BundleDrift")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_lock_only_mode_rejects_a_running_script_that_differs_from_the_lock(self):
        self.app_repo()
        checker = self.copy_checker()
        r = self.run_ps("$Repo = 'nfc'; Test-BundleDrift", checker=checker)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('HC_DRIFT eng/core-health.lock.json:1 repo-health.ps1 SHA-256 differs from lock', r.stderr)

    def test_lock_only_mode_rejects_an_edited_script_and_lock_hash_against_base(self):
        self.app_repo()
        self.git('checkout', '-q', '-b', 'pr')
        checker = self.copy_checker()
        self.lock_write()
        self.commit_all('script and lock edited together')
        r = self.run_ps("$Repo = 'nfc'; $BaseRef = 'main'; Test-BundleDrift", checker=checker)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('hash of repo-health.ps1 differs from base', r.stderr)

    def test_lock_only_mode_accepts_a_matching_script_hash_without_base_ref(self):
        self.app_repo()
        checker = self.copy_checker()
        self.lock_write()
        self.commit_all('matching script hash')
        r = self.run_ps("$Repo = 'nfc'; Test-BundleDrift", checker=checker)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_lock_only_mode_skips_the_script_for_an_old_application_lock(self):
        self.app_repo()
        self.remove_checker_hash()
        self.commit_all('old application lock')
        checker = self.copy_checker()
        r = self.run_ps("$Repo = 'nfc'; $BaseRef = 'main'; Test-BundleDrift", checker=checker)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_lock_only_mode_rejects_dropping_the_script_entry_of_the_base_lock(self):
        self.app_repo()
        self.git('checkout', '-q', '-b', 'pr')
        self.remove_checker_hash()
        self.commit_all('script entry dropped from the lock')
        checker = self.copy_checker()
        r = self.run_ps("$Repo = 'nfc'; $BaseRef = 'main'; Test-BundleDrift", checker=checker)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('the repo-health.ps1 entry was removed from the lock', r.stderr)

    def test_lock_only_mode_without_base_ref_rejects_dropping_the_committed_script_entry(self):
        self.app_repo()
        self.remove_checker_hash()
        checker = self.copy_checker()
        r = self.run_ps("$Repo = 'nfc'; Test-BundleDrift", checker=checker)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('the repo-health.ps1 entry was removed from the lock', r.stderr)

    def test_lock_only_mode_rejects_a_script_with_crlf_or_bom(self):
        self.app_repo()
        original = CHECKER.read_bytes()
        for content in (original.replace(b'\n', b'\r\n'), b'\xef\xbb\xbf' + original):
            with self.subTest(prefix=content[:5]):
                checker = self.root / 'tools/repo-checks/repo-health.ps1'
                checker.parent.mkdir(parents=True, exist_ok=True)
                checker.write_bytes(content)
                shutil.copyfile(self.root / 'eng/core-health/schema.json', checker.with_name('schema.json'))
                r = self.run_ps("$Repo = 'nfc'; Test-BundleDrift", checker=checker)
                self.assertNotEqual(0, r.returncode)
                self.assertIn('repo-health.ps1 must use LF line endings and UTF-8 without a BOM', r.stderr)
                self.assertNotIn('HC_DRIFT', r.stderr)

    def test_lock_only_mode_rejects_a_bundle_file_that_differs_from_the_lock(self):
        self.app_repo()
        self.write('eng/core-health/BannedSymbols.Common.txt', 'P:System.DateTime.Now\n')
        r = self.run_ps("$Repo = 'nfc'; $Mode = 'Verify'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('SHA-256 differs from lock', r.stderr)

    def test_lock_only_mode_rejects_a_pull_request_that_edits_a_file_and_its_lock_hash(self):
        self.app_repo()
        self.git('checkout', '-q', '-b', 'pr')
        self.write('eng/core-health/BannedSymbols.Common.txt', 'P:System.DateTime.Now\n')
        self.lock_write()
        self.git('add', '.')
        self.git('commit', '-qm', 'self-consistent edit')
        r = self.run_ps("$Repo = 'nfc'; $Mode = 'Verify'; $BaseRef = 'main'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('hash of BannedSymbols.Common.txt differs from base', r.stderr)

    def test_differently_cased_editorconfig_name_is_checked(self):
        self.app_repo()
        self.write('src/.EditorConfig', '[*.cs]\ndotnet_diagnostic.CA1234.severity = none\n')
        r = self.run_ps("$Repo = 'nfc'; $Mode = 'Verify'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('HC_DRIFT src/.EditorConfig:2', r.stderr)

    def test_csharp_editorconfig_copy_is_checked_outside_core(self):
        self.app_repo()
        self.write('tools/repo-checks/csharp/.editorconfig', '[*.cs]\ndotnet_diagnostic.RS0030.severity = none\n')
        r = self.run_ps("$Repo = 'nfc'; $Mode = 'Verify'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('HC_DRIFT tools/repo-checks/csharp/.editorconfig:2', r.stderr)

    def test_core_root_keeps_the_canonical_comparison_for_applications(self):
        self.app_repo()
        shutil.copytree(ROOT / 'tools/repo-checks/csharp', self.root / 'tools/repo-checks/csharp')
        shutil.copyfile(CHECKER, self.root / 'tools/repo-checks/repo-health.ps1')
        self.git('add', '.')
        self.git('commit', '-qm', 'core copy')
        commit = self.git('rev-parse', 'HEAD')
        self.write('eng/core-health/BannedSymbols.Common.txt', 'P:System.DateTime.Now\n')
        self.lock_write(commit)
        self.git('add', '.')
        self.git('commit', '-qm', 'drift with matching hash')
        r = self.run_ps(f"$Repo = 'nfc'; $Mode = 'Enroll'; $CoreRoot = $Root; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('approved Core revision', r.stderr)

    # --- Core is the source of the bundle: a bundle file changes in one pull request (issue 197) ---

    def core_repo(self):
        """A Core tree whose lock names the commit of its own bundle files. Returns the pin."""
        self.git('init', '-q', '-b', 'main')
        shutil.copytree(ROOT / 'tools/repo-checks/csharp', self.root / 'tools/repo-checks/csharp')
        shutil.copyfile(CHECKER, self.root / 'tools/repo-checks/repo-health.ps1')
        self.write('.editorconfig', (ROOT / '.editorconfig').read_text())
        self.git('add', '.')
        self.git('commit', '-qm', 'bundle files')
        pin = self.git('rev-parse', 'HEAD')
        self.lock_write(pin)
        self.git('add', '.')
        self.git('commit', '-qm', 'lock')
        return pin

    def change_bundle_file(self, pin, text='P:System.DateTime.Now\n'):
        """Change the canonical file and its copy, and the lock hash. The pin stays."""
        for path in ('tools/repo-checks/csharp/BannedSymbols.Common.txt', 'eng/core-health/BannedSymbols.Common.txt'):
            self.write(path, text)
        self.lock_write(pin)

    def commit_all(self, message):
        self.git('add', '.')
        self.git('commit', '-qm', message)
        return self.git('rev-parse', 'HEAD')

    def verify_core(self, base='main'):
        tree_checker = self.root / 'tools/repo-checks/repo-health.ps1'
        return self.run_ps(f"$Mode = 'Verify'; $BaseRef = '{base}'; Test-BundleDrift", checker=tree_checker)

    def test_core_accepts_the_script_when_the_lock_hash_matches(self):
        self.core_repo()
        r = self.verify_core()
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_core_rejects_a_script_that_differs_from_the_lock_hash(self):
        self.core_repo()
        self.copy_checker()
        r = self.verify_core()
        self.assertNotEqual(0, r.returncode)
        self.assertIn('HC_DRIFT eng/core-health.lock.json:1 repo-health.ps1 SHA-256 differs from lock', r.stderr)

    def test_core_accepts_a_script_change_and_hash_update_with_the_same_pin(self):
        pin = self.core_repo()
        self.git('checkout', '-q', '-b', 'pr')
        self.copy_checker()
        self.lock_write(pin)
        self.commit_all('script and hash updated')
        r = self.verify_core()
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_core_rejects_a_moved_pin_whose_script_differs_from_the_tree(self):
        pin = self.core_repo()
        self.copy_checker()
        self.lock_write(pin)
        merged = self.commit_all('script change merged')
        self.git('checkout', '-q', '-b', 'pr')
        self.copy_checker(b'# another fixture change\n')
        self.lock_write(merged)
        self.commit_all('new script with the previous script pin')
        r = self.verify_core()
        self.assertNotEqual(0, r.returncode)
        self.assertIn('the pin moved to a commit whose repo-health.ps1 differs from this tree', r.stderr)

    def test_core_rejects_a_running_script_that_differs_from_the_tree_script(self):
        self.core_repo()
        running = self.root / 'tools/repo-checks/other/repo-health.ps1'
        running.parent.mkdir(parents=True)
        running.write_bytes(CHECKER.read_bytes() + b'# another script\n')
        shutil.copyfile(self.root / 'tools/repo-checks/csharp/schema.json', running.with_name('schema.json'))
        r = self.run_ps("$Mode = 'Verify'; $BaseRef = 'main'; Test-BundleDrift", checker=running)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('the running repo-health.ps1 differs from tools/repo-checks/repo-health.ps1 of this tree', r.stderr)

    def test_core_requires_the_script_hash_in_its_lock(self):
        self.core_repo()
        self.remove_checker_hash()
        r = self.verify_core()
        self.assertNotEqual(0, r.returncode)
        self.assertIn('Core lock must include repo-health.ps1', r.stderr)

    def test_core_reports_a_missing_script_path_as_a_tool_error(self):
        self.core_repo()
        (self.root / 'tools/repo-checks/repo-health.ps1').unlink()
        r = self.run_ps("$Mode = 'Verify'; $BaseRef = 'main'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('Missing repo-health.ps1 script path', r.stderr)
        self.assertNotIn('HC_DRIFT', r.stderr)

    def test_core_reports_script_crlf_and_bom_as_tool_errors(self):
        self.core_repo()
        checker = self.root / 'tools/repo-checks/repo-health.ps1'
        original = checker.read_bytes()
        for content in (original.replace(b'\n', b'\r\n'), b'\xef\xbb\xbf' + original):
            with self.subTest(prefix=content[:5]):
                checker.write_bytes(content)
                r = self.verify_core()
                self.assertNotEqual(0, r.returncode)
                self.assertIn('repo-health.ps1 must use LF line endings and UTF-8 without a BOM', r.stderr)
                self.assertNotIn('HC_DRIFT', r.stderr)

    def test_core_accepts_a_bundle_change_in_one_pull_request(self):
        pin = self.core_repo()
        self.git('checkout', '-q', '-b', 'pr')
        self.change_bundle_file(pin)
        self.commit_all('bundle change')
        r = self.verify_core()
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_core_accepts_the_stale_pin_of_a_merged_bundle_change(self):
        pin = self.core_repo()
        self.change_bundle_file(pin)
        self.commit_all('bundle change, merged')
        r = self.run_ps("$Mode = 'Verify'; Test-BundleDrift")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_core_rejects_a_copy_that_differs_from_the_canonical_file(self):
        pin = self.core_repo()
        self.git('checkout', '-q', '-b', 'pr')
        self.write('eng/core-health/BannedSymbols.Common.txt', 'P:System.DateTime.Now\n')
        self.lock_write(pin)
        self.commit_all('copy only')
        r = self.verify_core()
        self.assertNotEqual(0, r.returncode)
        self.assertIn('differs from the canonical file', r.stderr)

    def test_core_rejects_a_canonical_file_that_differs_from_the_copy(self):
        pin = self.core_repo()
        self.git('checkout', '-q', '-b', 'pr')
        self.write('tools/repo-checks/csharp/BannedSymbols.Common.txt', 'P:System.DateTime.Now\n')
        self.commit_all('canonical only')
        r = self.verify_core()
        self.assertNotEqual(0, r.returncode)
        self.assertIn('differs from the canonical file', r.stderr)

    def test_core_rejects_a_lock_hash_that_differs_from_the_files(self):
        pin = self.core_repo()
        self.git('checkout', '-q', '-b', 'pr')
        for path in ('tools/repo-checks/csharp/BannedSymbols.Common.txt', 'eng/core-health/BannedSymbols.Common.txt'):
            self.write(path, 'P:System.DateTime.Now\n')
        self.commit_all('files without the lock hash')
        r = self.verify_core()
        self.assertNotEqual(0, r.returncode)
        self.assertIn('SHA-256 differs from lock', r.stderr)

    def test_core_accepts_a_pin_only_synchronization(self):
        pin = self.core_repo()
        self.change_bundle_file(pin)
        merged = self.commit_all('bundle change, merged')
        self.git('checkout', '-q', '-b', 'pin')
        self.lock_write(merged)
        self.commit_all('pin only')
        r = self.verify_core()
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_core_rejects_a_pin_that_is_older_than_the_pin_it_replaces(self):
        pin = self.core_repo()
        self.change_bundle_file(pin)
        merged = self.commit_all('bundle change, merged')
        self.lock_write(merged)
        self.commit_all('pin synchronized')
        self.git('checkout', '-q', '-b', 'pin')
        self.lock_write(pin)
        self.commit_all('pin moved back')
        r = self.verify_core()
        self.assertNotEqual(0, r.returncode)
        self.assertIn('older than the pin it replaces', r.stderr)

    def test_core_rejects_a_pin_whose_files_differ_from_the_tree(self):
        pin = self.core_repo()
        self.change_bundle_file(pin)
        merged = self.commit_all('bundle change, merged')
        self.git('checkout', '-q', '-b', 'pin')
        self.change_bundle_file(merged, 'P:System.Environment.TickCount\n')
        self.commit_all('another change and a pin that has the first one')
        r = self.verify_core()
        self.assertNotEqual(0, r.returncode)
        self.assertIn('the pin moved to a commit whose BannedSymbols.Common.txt', r.stderr)

    def test_core_rejects_a_pin_that_is_not_a_commit_of_its_history(self):
        pin = self.core_repo()
        self.git('checkout', '-q', '-b', 'pin')
        self.lock_write('b' * 40)
        self.commit_all('unknown pin')
        r = self.verify_core()
        self.assertNotEqual(0, r.returncode)
        self.assertIn('not a commit of this history', r.stderr)

    def test_core_rejects_a_committed_pin_that_is_not_in_its_history_without_a_base_ref(self):
        self.core_repo()
        self.lock_write('b' * 40)
        self.commit_all('unknown pin, committed')
        r = self.run_ps("$Mode = 'Verify'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('not a commit of this history', r.stderr)

    def test_core_enroll_rejects_a_pin_that_is_not_in_its_history(self):
        self.core_repo()
        self.lock_write('b' * 40)
        self.commit_all('unknown pin, committed')
        r = self.run_ps("$Mode = 'Enroll'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('not a commit of this history', r.stderr)

    def test_lock_schema_accepts_six_and_seven_keys_and_rejects_an_unknown_key(self):
        self.core_repo()
        body = ("$files = @{}; foreach ($f in Get-ChildItem -LiteralPath eng/core-health -File) { $files[$f.Name] = 'a' * 64 }\n"
                "function Probe($extra) { $all = $files.Clone(); foreach ($k in $extra.Keys) { $all[$k] = $extra[$k] }\n"
                "  $text = ConvertTo-Json @{ schemaVersion = 1; coreCommit = 'a' * 40; files = $all } -Depth 5\n"
                "  try { [void](Read-HealthDocument $text 'core-health.lock'); 'accepted' } catch { 'rejected' } }\n"
                "'six=' + (Probe @{}); 'seven=' + (Probe @{ 'repo-health.ps1' = 'b' * 64 }); 'unknown=' + (Probe @{ 'other.ps1' = 'c' * 64 })")
        r = self.run_ps(body)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn('six=accepted', r.stdout)
        self.assertIn('seven=accepted', r.stdout)
        self.assertIn('unknown=rejected', r.stdout)

    def test_core_rejects_a_pin_in_upper_case(self):
        pin = self.core_repo()
        self.lock_write(pin.upper())
        self.commit_all('upper-case pin')
        r = self.run_ps("$Mode = 'Verify'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        # The schema rejects it before the ancestry check runs.
        self.assertIn('Invalid core-health.lock', r.stderr + r.stdout)

    def test_core_ignores_core_root(self):
        self.core_repo()
        r = self.run_ps(f"$Mode = 'Verify'; $CoreRoot = '{self.root.as_posix()}/does-not-exist'; Test-BundleDrift")
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    # --- An application moves its pin in one pull request when -CoreRoot proves it ---

    def core_checkout(self):
        """A separate Core checkout with two commits. Returns (path, first commit, second commit)."""
        temp = tempfile.TemporaryDirectory(dir=Path(__file__).parent, prefix='.h03-core-')
        self.addCleanup(temp.cleanup)
        core = Path(temp.name).resolve()
        env = os.environ | {'GIT_AUTHOR_NAME': 'fixture', 'GIT_AUTHOR_EMAIL': 'fixture@example.test',
                            'GIT_COMMITTER_NAME': 'fixture', 'GIT_COMMITTER_EMAIL': 'fixture@example.test'}

        def run(*args):
            return subprocess.check_output(['git', '-C', str(core), *args], env=env, text=True).strip()

        run('init', '-q', '-b', 'main')
        shutil.copytree(ROOT / 'tools/repo-checks/csharp', core / 'tools/repo-checks/csharp')
        shutil.copyfile(CHECKER, core / 'tools/repo-checks/repo-health.ps1')
        run('add', '.')
        run('commit', '-qm', 'first')
        first = run('rev-parse', 'HEAD')
        (core / 'tools/repo-checks/csharp/BannedSymbols.Common.txt').write_text('P:System.DateTime.Now\n', encoding='utf-8', newline='\n')
        run('add', '.')
        run('commit', '-qm', 'second')
        return core, first, run('rev-parse', 'HEAD')

    def application_pin_move(self, new_text='P:System.DateTime.Now\n', pin=None, old_lock=False):
        """Application at the first commit; a pull request moves the pin to `pin` (default: second commit).

        `old_lock` removes the checker entry from the base lock and from the lock of the pull request."""
        core, first, second = self.core_checkout()
        self.app_repo()
        self.lock_write(first)
        if old_lock:
            self.remove_checker_hash()
        self.git('add', '.')
        self.git('commit', '-qm', 'pin at the first commit')
        self.git('checkout', '-q', '-b', 'pr')
        self.write('eng/core-health/BannedSymbols.Common.txt', new_text)
        self.lock_write(pin or second)
        if old_lock:
            self.remove_checker_hash()
        self.git('add', '.')
        self.git('commit', '-qm', 'pin synchronization')
        return core, first, second

    def verify_application(self, core, mode='Verify', checker=CHECKER):
        return self.run_ps(f"$Repo = 'nfc'; $Mode = '{mode}'; $BaseRef = 'main'; $CoreRoot = '{core.as_posix()}'; Test-BundleDrift", checker=checker)

    def test_application_core_root_rejects_a_running_script_that_differs_from_the_pin(self):
        core, _, second = self.application_pin_move()
        checker = self.copy_checker()
        self.lock_write(second)
        self.commit_all('different checker with matching lock hash')
        r = self.verify_application(core, checker=checker)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('repo-health.ps1 differs from approved Core revision', r.stderr)

    def test_application_core_root_skips_the_script_for_an_old_lock(self):
        core, _, _ = self.application_pin_move(old_lock=True)
        checker = self.copy_checker()
        r = self.verify_application(core, checker=checker)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_application_core_root_rejects_dropping_the_script_entry_of_the_base_lock(self):
        core, _, _ = self.application_pin_move()
        self.remove_checker_hash()
        self.commit_all('script entry dropped from the lock')
        checker = self.copy_checker()
        r = self.verify_application(core, checker=checker)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('the repo-health.ps1 entry was removed from the lock', r.stderr)

    def test_application_may_move_its_pin_forward_when_core_root_proves_it(self):
        core, _, _ = self.application_pin_move()
        r = self.verify_application(core)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_adopt_accepts_the_moved_pin_when_core_root_proves_it(self):
        # Adopt runs the same bundle check through Initialize-Enforcement, so the pin-moving pull request can run it.
        core, _, _ = self.application_pin_move()
        r = self.verify_application(core, 'Adopt')
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)

    def test_application_pin_move_fails_without_core_root(self):
        self.application_pin_move()
        r = self.run_ps("$Repo = 'nfc'; $Mode = 'Verify'; $BaseRef = 'main'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('pin differs from base', r.stderr)

    def test_application_pin_move_fails_when_the_copy_is_not_the_files_of_the_new_pin(self):
        core, _, _ = self.application_pin_move(new_text='P:System.Environment.TickCount\n')
        r = self.verify_application(core)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('approved Core revision', r.stderr)

    def test_application_pin_move_fails_when_the_new_pin_is_not_in_the_core_checkout(self):
        core, _, _ = self.application_pin_move(pin='d' * 40)
        r = self.verify_application(core)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('not a commit of the Core checkout', r.stderr)

    def test_application_pin_move_fails_when_the_new_pin_is_older(self):
        core, first, second = self.core_checkout()
        self.app_repo()
        self.write('eng/core-health/BannedSymbols.Common.txt', 'P:System.DateTime.Now\n')
        self.lock_write(second)
        self.git('add', '.')
        self.git('commit', '-qm', 'pin at the second commit')
        self.git('checkout', '-q', '-b', 'pr')
        shutil.copyfile(ROOT / 'tools/repo-checks/csharp/BannedSymbols.Common.txt', self.root / 'eng/core-health/BannedSymbols.Common.txt')
        self.lock_write(first)
        self.git('add', '.')
        self.git('commit', '-qm', 'pin moved back')
        r = self.verify_application(core)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('does not descend from the pin it replaces', r.stderr)

    def test_application_pin_move_fails_when_the_new_pin_is_on_a_diverged_line(self):
        core, first, second = self.core_checkout()
        identity = ['-c', 'user.name=fixture', '-c', 'user.email=fixture@example.test', '-c', 'commit.gpgsign=false']

        def core_git(*args):
            return subprocess.check_output(['git', '-C', str(core), *args], text=True).strip()

        core_git('checkout', '-q', '-b', 'side', first)
        core_git(*identity, 'commit', '-q', '--allow-empty', '-m', 'side line')
        side = core_git('rev-parse', 'HEAD')
        core_git('checkout', '-q', 'main')
        # The Core checkout must reach both lines from its HEAD, or the first check fails on reachability instead.
        core_git(*identity, 'merge', '--no-ff', '-q', '-m', 'merge side', 'side')
        self.app_repo()
        self.write('eng/core-health/BannedSymbols.Common.txt', 'P:System.DateTime.Now\n')
        self.lock_write(second)
        self.git('add', '.')
        self.git('commit', '-qm', 'pin at the second commit')
        self.git('checkout', '-q', '-b', 'pr')
        self.lock_write(side)
        self.git('add', '.')
        self.git('commit', '-qm', 'pin moved to a diverged line')
        r = self.verify_application(core)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('does not descend from the pin it replaces', r.stderr)

    def test_application_pin_move_reports_a_core_root_that_is_not_a_repository(self):
        _, _, _ = self.application_pin_move()
        # Outside the repository: a directory below it would resolve to the repository of the test itself.
        empty = tempfile.TemporaryDirectory(prefix='h03-notrepo-')
        self.addCleanup(empty.cleanup)
        r = self.verify_application(Path(empty.name).resolve())
        self.assertNotEqual(0, r.returncode)
        self.assertIn('git merge-base exited 128', r.stderr + r.stdout)

    def test_an_application_still_cannot_move_its_pin_in_a_pull_request(self):
        self.app_repo()
        self.git('checkout', '-q', '-b', 'pr')
        self.lock_write('c' * 40)
        self.commit_all('pin moved')
        r = self.run_ps("$Repo = 'nfc'; $Mode = 'Verify'; $BaseRef = 'main'; Test-BundleDrift")
        self.assertNotEqual(0, r.returncode)
        self.assertIn('pin differs from base', r.stderr)


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
