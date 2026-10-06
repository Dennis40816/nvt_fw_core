# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Synthetic portability fixtures in disposable Git repositories; no network."""

from __future__ import annotations

from contextlib import redirect_stderr, redirect_stdout
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'actions/path-guard/path_guard.py'
WRAPPER = ROOT / 'actions/path-guard/check.ps1'
spec = importlib.util.spec_from_file_location('path_guard', SCRIPT)
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class LiteralTests(unittest.TestCase):
    def test_supported_forms(self):
        cases = (
            (r'Q:\synthetic\fixture.json', 'windows-drive'),
            ('Q:/synthetic/fixture.json', 'windows-drive'),
            (r'"Q:\\synthetic\\fixture.json"', 'windows-drive'),
            (r'@"Q:\synthetic\fixture.json"', 'windows-drive'),
            (r'\\sample-server\sample-share\fixture.json', 'unc'),
            (r'"\\\\sample-server\\sample-share\\fixture.json"', 'unc'),
            ('//sample-server/sample-share/fixture.json', 'unc'),
            ('/q/synthetic/fixture.json', 'msys-drive'),
            ('"/q"', 'msys-drive'),
            ('/mnt/q/synthetic/fixture.json', 'wsl-drive'),
            ('"/mnt/q"', 'wsl-drive'),
            ('/home/sample/fixture.json', 'home-directory'),
            ('/Users/sample/fixture.json', 'home-directory'),
            ('/home/sample', 'home-directory'),
            ('/root/fixture.json', 'home-directory'),
            ('~/fixture.json', 'home-directory'),
            (r'"\u0051\u003a\u005csynthetic\u005cfixture.json"', 'windows-drive'),
            (r'"\x51\x3a\x5csynthetic\x5cfixture.json"', 'windows-drive'),
            (r'"\/mnt\/q\/synthetic\/fixture.json"', 'wsl-drive'),
        )
        for value, expected in cases:
            with self.subTest(value=value):
                self.assertEqual(guard.line_rules(value, False), {expected})

    def test_relative_paths_and_urls(self):
        cases = (
            'tests/fixtures/sample.json', './tests/sample.json',
            '../fixtures/sample.json', r'tests\fixtures\sample.json',
            'Q:relative.json', '/var/tmp/sample.json', '/homework/sample.json',
            'https://example.invalid/Q:/synthetic/fixture.json',
            'file:///Q:/synthetic/fixture.json',
            'https://example.invalid/home/sample/fixture.json',
            'ssh://sample-server/sample-share/fixture.json',
            r'"https:\/\/example.invalid\/mnt\/q\/fixture.json"',
        )
        for value in cases:
            with self.subTest(value=value):
                self.assertEqual(guard.line_rules(value, True), set())

    def test_url_does_not_hide_a_second_literal(self):
        value = 'https://example.invalid/fixture.json "Q:/synthetic/fixture.json"'
        self.assertEqual(guard.line_rules(value, False), {'windows-drive'})

    def test_relative_paths_do_not_match_absolute_rules_inside_tokens(self):
        cases = (
            './a/fixture.json', '../home/sample/fixture.json', './c/x',
            '../Users/sample/x', 'a/home/sample/x', '../mnt/q/fixture.json',
            './root/fixture.json', './~/fixture.json', './Q:/fixture.json',
            r'..\Q:\fixture.json', './sample//sample-server/sample-share/x',
            r'.\\sample-server\sample-share\x', 'sample-/home/sample/x',
        )
        for value in cases:
            for line, json_file in (
                    (value, False),
                    (f'var input = "{value}";', False),
                    (f'$input = "{value}"', False),
                    (json.dumps({'file': value}), True)):
                with self.subTest(line=line, json_file=json_file):
                    self.assertEqual(guard.line_rules(line, json_file), set())

    def test_absolute_paths_match_at_token_boundaries(self):
        cases = (
            ('Q:/synthetic/fixture.json', 'windows-drive'),
            (r'Q:\synthetic\fixture.json', 'windows-drive'),
            (r'\\sample-server\sample-share\fixture.json', 'unc'),
            ('//sample-server/sample-share/fixture.json', 'unc'),
            ('/a/fixture.json', 'msys-drive'),
            ('/c/x', 'msys-drive'),
            ('/mnt/q/fixture.json', 'wsl-drive'),
            ('/home/sample/fixture.json', 'home-directory'),
            ('/Users/sample/x', 'home-directory'),
            ('/root/fixture.json', 'home-directory'),
            ('~/fixture.json', 'home-directory'),
        )
        for value, expected in cases:
            for line in (f'"{value}"', f"'{value}'", f'file={value}',
                         f'file {value}', f'Open({value})'):
                with self.subTest(line=line):
                    self.assertEqual(guard.line_rules(line, False), {expected})

    def test_json_surrogate_pair_is_decoded(self):
        value = r'"\u0051\u003a\u002f\ud83d\ude80\u002ffixture.json"'
        self.assertEqual(guard.line_rules(value, True), {'windows-drive'})


class RepositoryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='path-guard-test-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.environment = dict(os.environ, GIT_CONFIG_GLOBAL=os.devnull,
                                GIT_CONFIG_NOSYSTEM='1',
                                GIT_AUTHOR_NAME='Path Guard Tests',
                                GIT_AUTHOR_EMAIL='tests@example.invalid',
                                GIT_COMMITTER_NAME='Path Guard Tests',
                                GIT_COMMITTER_EMAIL='tests@example.invalid',
                                GIT_AUTHOR_DATE='2026-01-01T00:00:00Z',
                                GIT_COMMITTER_DATE='2026-01-01T00:00:00Z')
        self.environment_patch = mock.patch.dict(os.environ, self.environment)
        self.environment_patch.start()
        self.addCleanup(self.environment_patch.stop)
        self.git('init', '-q')
        self.write('tests/relative.txt', 'tests/fixtures/sample.json\n')
        self.policy()

    def git(self, *arguments, root=None, data=None):
        result = subprocess.run(
            ['git', '-C', str(root or self.root), *arguments], input=data,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=self.environment)
        self.assertEqual(result.returncode, 0, result.stderr.decode('utf-8', 'replace'))
        return result.stdout

    def write(self, file, content, tracked=True):
        target = self.root / file
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content.encode('utf-8') if isinstance(content, str) else content)
        if tracked:
            self.git('add', '-f', '--', file)
        return target

    def policy(self, paths=None, exceptions=None):
        self.write(guard.POLICY, json.dumps({
            'paths': ['tests'] if paths is None else paths,
            'exceptions': [] if exceptions is None else exceptions,
        }))

    def exception(self, file, line):
        return {'file': file, 'sha256': hashlib.sha256(line.encode('utf-8')).hexdigest(),
                'reason': 'Synthetic invalid-path characterization fixture.'}

    def result(self):
        output, errors = io.StringIO(), io.StringIO()
        with redirect_stdout(output), redirect_stderr(errors):
            code = guard.main(['--repository', str(self.root)])
        return code, output.getvalue(), errors.getvalue()

    def run_script(self, command, cwd):
        return subprocess.run(command, cwd=cwd, env=self.environment,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    def snapshot(self, root):
        tree = self.git('write-tree', root=root).strip().decode('ascii')
        commit = self.git('commit-tree', tree, root=root,
                          data=b'Synthetic fixture baseline\n').strip().decode('ascii')
        self.git('update-ref', 'refs/heads/fixture', commit, root=root)
        self.git('symbolic-ref', 'HEAD', 'refs/heads/fixture', root=root)
        return commit

    def submodule(self, file='tests/public-data', content='Q:/synthetic/fixture.json\n'):
        module = self.root / file
        module.mkdir(parents=True)
        self.git('init', '-q', root=module)
        (module / 'sample.txt').write_text(content, encoding='utf-8')
        self.git('add', 'sample.txt', root=module)
        commit = self.snapshot(module)
        self.git('update-index', '--add', '--cacheinfo', f'160000,{commit},{file}')
        return module, commit

    def test_clean_scope_passes(self):
        self.assertEqual(self.result(), (0, 'path-guard: passed\n', ''))

    def test_all_rules_have_stable_relative_diagnostics(self):
        self.write('tests/sample.txt',
                   'relative.json\nQ:/synthetic/fixture.json\n'
                   '\\\\sample-server\\sample-share\\fixture.json\n'
                   '/q/synthetic/fixture.json\n/mnt/q/synthetic/fixture.json\n'
                   '/home/sample/fixture.json\n')
        self.assertEqual(self.result(), (1,
            'tests/sample.txt:2: windows-drive\n'
            'tests/sample.txt:3: unc\n'
            'tests/sample.txt:4: msys-drive\n'
            'tests/sample.txt:5: wsl-drive\n'
            'tests/sample.txt:6: home-directory\n', ''))

    def test_json_escaping_keeps_physical_line_number(self):
        self.write('tests/sample.json',
                   '{\n  "file": "\\u0051\\u003a\\u005csynthetic\\u005cfixture.json",\n'
                   '  "relative": "tests\\/sample.json",\n'
                   '  "url": "https:\\/\\/example.invalid\\/mnt\\/q\\/fixture.json"\n}\n')
        self.assertEqual(self.result(), (1, 'tests/sample.json:2: windows-drive\n', ''))

    def test_utf8_bom_does_not_hide_a_first_line_path(self):
        self.write('tests/sample.txt', b'\xef\xbb\xbfQ:/synthetic/fixture.json\n')
        self.write('tests/sample.cs', b'\xef\xbb\xbf/home/sample/fixture.json\n')
        self.write('tests/sample.json', b'\xef\xbb\xbf{"file": "Q:/synthetic/fixture.json"}\n')
        self.assertEqual(self.result(), (1,
            'tests/sample.cs:1: home-directory\n'
            'tests/sample.json:1: windows-drive\n'
            'tests/sample.txt:1: windows-drive\n', ''))

    def test_json_escaped_newline_is_not_a_source_line_break(self):
        self.write('tests/sample.json', json.dumps({'file': 'relative\nQ:/synthetic/fixture.json'}))
        self.assertEqual(self.result(), (1, 'tests/sample.json:1: windows-drive\n', ''))

    def test_csharp_escaped_whitespace_keeps_physical_line_numbers(self):
        self.write('tests/sample.cs',
                   '// Relative fixtures\n'
                   r'var input = "relative\nQ:\\synthetic\\fixture.json";' '\n'
                   r'var input = "relative\rQ:\\synthetic\\fixture.json";' '\n'
                   r'var input = "relative\tQ:\\synthetic\\fixture.json";' '\n'
                   r'var input = "Q:\\synthetic\\fixture.json";' '\n')
        self.assertEqual(self.result(), (1,
            'tests/sample.cs:2: windows-drive\n'
            'tests/sample.cs:3: windows-drive\n'
            'tests/sample.cs:4: windows-drive\n'
            'tests/sample.cs:5: windows-drive\n', ''))

    def test_powershell_escaped_whitespace_keeps_physical_line_numbers(self):
        self.write('tests/sample.ps1',
                   '# Relative fixtures\n'
                   r'$input = "relative\nQ:\synthetic\fixture.json"' '\n'
                   r'$input = "relative\rQ:\synthetic\fixture.json"' '\n'
                   r'$input = "relative\tQ:\synthetic\fixture.json"' '\n'
                   r'$input = "Q:\synthetic\fixture.json"' '\n')
        self.assertEqual(self.result(), (1,
            'tests/sample.ps1:2: windows-drive\n'
            'tests/sample.ps1:3: windows-drive\n'
            'tests/sample.ps1:4: windows-drive\n'
            'tests/sample.ps1:5: windows-drive\n', ''))

    def test_markdown_escaped_whitespace_keeps_physical_line_numbers(self):
        # Store Markdown input as a text fixture; documentation files stay excluded.
        self.write('tests/markdown-fixture.txt',
                   'Relative fixtures\n'
                   r'`relative\nQ:\synthetic\fixture.json`' '\n'
                   r'`relative\rQ:\synthetic\fixture.json`' '\n'
                   r'`relative\tQ:\synthetic\fixture.json`' '\n'
                   r'`Q:\synthetic\fixture.json`' '\n')
        self.assertEqual(self.result(), (1,
            'tests/markdown-fixture.txt:2: windows-drive\n'
            'tests/markdown-fixture.txt:3: windows-drive\n'
            'tests/markdown-fixture.txt:4: windows-drive\n'
            'tests/markdown-fixture.txt:5: windows-drive\n', ''))

    def test_malformed_json_fixture_still_scans(self):
        self.write('tests/invalid.json', '{ "file": "Q:/synthetic/fixture.json", }\n')
        self.assertEqual(self.result(), (1, 'tests/invalid.json:1: windows-drive\n', ''))

    def test_tracked_unstaged_changes_are_scanned(self):
        target = self.write('tests/sample.txt', 'tests/relative.json\n')
        target.write_text('Q:/synthetic/fixture.json\n', encoding='utf-8')
        self.assertEqual(self.result()[0], 1)

    def test_untracked_and_out_of_scope_files_are_ignored(self):
        self.write('tests/untracked.txt', 'Q:/synthetic/fixture.json', tracked=False)
        self.write('src/not-configured.cs', 'Q:/synthetic/fixture.json')
        self.assertEqual(self.result()[0], 0)

    def test_project_configuration_and_fixture_scope(self):
        self.write('project.xml', '<Fixture>Q:/synthetic/fixture.json</Fixture>')
        self.write('fixtures/sample.txt', '/Users/sample/fixture.json')
        self.policy(['project.xml', 'fixtures'])
        self.assertEqual(self.result(), (1,
            'fixtures/sample.txt:1: home-directory\n'
            'project.xml:1: windows-drive\n', ''))

    def test_excluded_documents_generated_output_and_dependencies(self):
        for file in ('tests/docs/sample.txt', 'tests/README.txt', 'tests/example.md',
                     'tests/example.rst', 'tests/example.adoc', 'tests/bin/sample.txt',
                     'tests/obj/sample.txt', 'tests/build/sample.txt',
                     'tests/dist/sample.txt', 'tests/artifacts/sample.txt',
                     'tests/node_modules/sample.txt', 'tests/vendor/sample.txt',
                     'tests/packages/sample.txt', 'tests/View.g.cs',
                     'tests/View.generated.cs', 'tests/View.Designer.cs'):
            self.write(file, 'Q:/synthetic/fixture.json')
        self.assertEqual(self.result()[0], 0)

    def test_document_named_source_is_still_scanned(self):
        self.write('tests/ReadmeValidationTests.cs', 'Q:/synthetic/fixture.json')
        self.assertEqual(self.result()[0], 1)

    def test_binary_files_are_ignored(self):
        for file, content in (
                ('tests/sample.bin', b'Q:/synthetic/fixture.json'),
                ('tests/sample.dat', b'\x00Q:/synthetic/fixture.json'),
                ('tests/non-text.dat', b'\xffQ:/synthetic/fixture.json')):
            self.write(file, content)
        self.assertEqual(self.result()[0], 0)

    def test_utf16_text_is_scanned(self):
        self.write('tests/sample.txt', 'Q:/synthetic/fixture.json\r\n'.encode('utf-16'))
        self.assertEqual(self.result(), (1, 'tests/sample.txt:1: windows-drive\n', ''))

    def test_exact_exception_survives_line_endings_and_line_movement(self):
        line = '  Q:/synthetic/fixture.json  '
        self.policy(exceptions=[self.exception('tests/sample.txt', line)])
        for text in (line + '\n', line + '\r\n', 'relative.json\n' + line + '\n'):
            with self.subTest(text=text):
                self.write('tests/sample.txt', text)
                self.assertEqual(self.result()[0], 0)

    def test_changed_line_invalidates_exception(self):
        line = 'Q:/synthetic/fixture.json'
        self.policy(exceptions=[self.exception('tests/sample.txt', line)])
        for changed in (line + ' ', line.replace('fixture', 'changed')):
            with self.subTest(changed=changed):
                self.write('tests/sample.txt', changed)
                self.assertEqual(self.result()[0], 1)

    def test_exception_is_scoped_to_exact_file(self):
        line = 'Q:/synthetic/fixture.json'
        self.policy(exceptions=[self.exception('tests/first.txt', line)])
        self.write('tests/first.txt', line)
        self.write('tests/second.txt', line)
        self.assertEqual(self.result(), (1, 'tests/second.txt:1: windows-drive\n', ''))

    def test_exception_permits_all_rules_on_that_source_line_only(self):
        line = 'Q:/synthetic/fixture.json /home/sample/fixture.json'
        self.policy(exceptions=[self.exception('tests/sample.txt', line)])
        self.write('tests/sample.txt', line + '\n/mnt/q/synthetic/fixture.json\n')
        self.assertEqual(self.result(), (1, 'tests/sample.txt:2: wsl-drive\n', ''))

    def test_missing_configured_scope_fails(self):
        self.policy(['tests', 'fixtures/missing'])
        self.assertEqual(self.result(), (1, '', 'path-guard: missing-scan-path\n'))

    def test_untracked_scope_fails(self):
        self.write('fixtures/untracked.txt', 'relative.json', tracked=False)
        self.policy(['fixtures'])
        self.assertEqual(self.result()[2], 'path-guard: missing-scan-path\n')

    def test_missing_configured_directory_fails_even_with_excluded_index_entries(self):
        self.write('fixtures/sample.bin', b'synthetic binary')
        (self.root / 'fixtures/sample.bin').unlink()
        (self.root / 'fixtures').rmdir()
        self.policy(['fixtures'])
        self.assertEqual(self.result()[2], 'path-guard: missing-scan-path\n')

    def test_deleted_tracked_scan_file_fails(self):
        (self.root / 'tests/relative.txt').unlink()
        self.assertEqual(self.result()[2], 'path-guard: missing-scan-file\n')

    def test_missing_or_untracked_policy_fails(self):
        self.git('rm', '--cached', guard.POLICY)
        self.assertEqual(self.result()[2], 'path-guard: untracked-policy\n')
        (self.root / guard.POLICY).unlink()
        self.assertEqual(self.result()[2], 'path-guard: invalid-policy\n')

    def test_policy_rejects_broad_or_custom_configuration(self):
        valid = {'paths': ['tests'], 'exceptions': []}
        cases = (
            {}, {'paths': []}, {'paths': 'tests'}, {'paths': ['/tests']},
            {'paths': ['../tests']}, {'paths': ['tests/*']}, {'paths': [r'tests\fixtures']},
            {'paths': ['tests//fixtures']}, {'paths': ['Q:/synthetic']},
            {**valid, 'rules': []},
            {**valid, 'exceptions': [{'file': 'tests/*', 'sha256': 'a' * 64, 'reason': 'test'}]},
            {**valid, 'exceptions': [{'file': 'tests/sample.txt', 'reason': 'test'}]},
            {**valid, 'exceptions': [{'file': 'tests/sample.txt', 'sha256': 'a' * 64, 'reason': ''}]},
            {**valid, 'exceptions': [{'rule': 'windows-drive', 'reason': 'test'}]},
            {**valid, 'exceptions': [
                {**self.exception('tests/sample.txt', 'sample'), 'expires': 'later'}]},
        )
        for policy in cases:
            with self.subTest(policy=policy):
                self.write(guard.POLICY, json.dumps(policy))
                self.assertEqual(self.result(), (1, '', 'path-guard: invalid-policy\n'))

    def test_diagnostics_never_print_values_or_local_root(self):
        value = 'Q:/synthetic/sensitive-fixture.json'
        self.write('tests/sample.txt', value)
        code, output, errors = self.result()
        self.assertEqual(code, 1)
        self.assertNotIn(value, output + errors)
        self.assertNotIn(str(self.root), output + errors)
        self.write(guard.POLICY, '{ invalid ' + value)
        self.assertEqual(self.result(), (1, '', 'path-guard: invalid-policy\n'))

    def test_python_cli_is_independent_of_working_directory(self):
        self.write('tests/sample.txt', 'Q:/synthetic/fixture.json\n')
        command = [os.sys.executable, '-B', str(SCRIPT), '--repository', str(self.root)]
        first = self.run_script(command, ROOT)
        second = self.run_script(command, self.root / 'tests')
        self.assertEqual((first.returncode, first.stdout, first.stderr),
                         (second.returncode, second.stdout, second.stderr))
        self.assertEqual(first.stdout.decode('utf-8').splitlines(),
                         ['tests/sample.txt:1: windows-drive'])

    @unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is needed for wrapper parity')
    def test_local_wrapper_matches_cli_from_changed_working_directory(self):
        for content, expected in (('tests/fixture.json\n', 0),
                                  ('Q:/synthetic/fixture.json\n', 1)):
            with self.subTest(content=content):
                self.write('tests/sample.txt', content)
                python = self.run_script(
                    [os.sys.executable, '-B', str(SCRIPT), '--repository', str(self.root)], ROOT)
                wrapper = self.run_script(
                    ['pwsh', '-NoProfile', '-File', str(WRAPPER), '-RepositoryRoot', str(self.root)],
                    self.root / 'tests')
                default = self.run_script(
                    ['pwsh', '-NoProfile', '-File', str(WRAPPER)], self.root / 'tests')
                self.assertEqual((wrapper.returncode, wrapper.stdout, wrapper.stderr),
                                 (python.returncode, python.stdout, python.stderr))
                self.assertEqual((default.returncode, default.stdout, default.stderr),
                                 (python.returncode, python.stdout, python.stderr))
                self.assertEqual(wrapper.returncode, expected)

    def test_configured_submodule_is_scanned_at_gitlink_commit(self):
        module, _ = self.submodule()
        (module / 'sample.txt').write_text('tests/relative.json\n', encoding='utf-8')
        self.assertEqual(self.result(), (1,
            'tests/public-data/sample.txt:1: windows-drive\n', ''))

    def test_submodule_untracked_files_are_ignored(self):
        module, _ = self.submodule(content='tests/relative.json\n')
        (module / 'untracked.txt').write_text('Q:/synthetic/fixture.json', encoding='utf-8')
        self.assertEqual(self.result()[0], 0)

    def test_nested_submodule_uses_parent_recorded_gitlink(self):
        module, _ = self.submodule(content='tests/relative.json\n')
        child = module / 'nested-data'
        child.mkdir()
        self.git('init', '-q', root=child)
        (child / 'sample.txt').write_text('/mnt/q/synthetic/fixture.json\n', encoding='utf-8')
        self.git('add', 'sample.txt', root=child)
        commit = self.snapshot(child)
        self.git('update-index', '--add', '--cacheinfo', f'160000,{commit},nested-data', root=module)
        parent = self.snapshot(module)
        self.git('update-index', '--cacheinfo', f'160000,{parent},tests/public-data')
        self.assertEqual(self.result(), (1,
            'tests/public-data/nested-data/sample.txt:1: wsl-drive\n', ''))

    def test_deep_submodule_scope_and_exact_exception(self):
        self.submodule()
        self.policy(['tests/public-data/sample.txt'],
                    [self.exception('tests/public-data/sample.txt', 'Q:/synthetic/fixture.json')])
        self.assertEqual(self.result()[0], 0)

    def test_missing_submodule_fails_without_fetching(self):
        module, _ = self.submodule()
        module.rename(self.root / 'detached-data')
        self.assertEqual(self.result()[2], 'path-guard: missing-submodule\n')
        module.mkdir()
        self.assertEqual(self.result()[2], 'path-guard: missing-submodule\n')

    def test_submodule_revision_mismatch_fails(self):
        module, _ = self.submodule()
        (module / 'sample.txt').write_text('tests/relative.json\n', encoding='utf-8')
        self.git('add', 'sample.txt', root=module)
        self.snapshot(module)
        self.assertEqual(self.result()[2], 'path-guard: submodule-revision-mismatch\n')

    def test_unconfigured_submodule_is_not_required(self):
        module, _ = self.submodule(file='examples/public-data')
        module.rename(self.root / 'detached-data')
        self.assertEqual(self.result()[0], 0)

    def test_missing_dependent_submodule_is_excluded(self):
        module, _ = self.submodule(file='tests/node_modules')
        module.rename(self.root / 'detached-data')
        self.assertEqual(self.result()[0], 0)


if __name__ == '__main__':
    unittest.main()
