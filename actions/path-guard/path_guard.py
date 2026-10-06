# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Scan the caller's tracked scope without printing literal path values."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys


POLICY = '.github/path-guard.json'
EXCLUDED_DIRECTORIES = frozenset({
    '.git', '.vs', '.idea', '.dotnet', '.packages', '__pycache__',
    'docs', 'doc', 'bin', 'obj', 'artifacts', 'build', 'dist', 'release',
    'node_modules', 'packages', 'vendor', 'testresults',
})
BINARY_SUFFIXES = frozenset({
    '.bin', '.exe', '.dll', '.pdb', '.so', '.a', '.lib', '.png', '.jpg',
    '.jpeg', '.gif', '.ico', '.pdf', '.zip', '.7z', '.gz', '.tar', '.woff',
    '.woff2', '.ttf', '.otf',
})
URL = re.compile(r'''\b[A-Za-z][A-Za-z0-9+.-]+://[^\s"'<>`]+''')
# Absolute paths start a token after whitespace or a source delimiter.
RULES = (
    ('windows-drive', re.compile(r'''(?<![^\s"'<>`=(){}\[\],;])[A-Za-z]:[\\/]''')),
    ('unc', re.compile(r'''(?<![^\s"'<>`=(){}\[\],;])(?:\\{2,}|/{2,})[^\\/\s"'<>`]+[\\/][^\\/\s"'<>`]+''')),
    ('msys-drive', re.compile(r'''(?<![^\s"'<>`=(){}\[\],;])/[A-Za-z](?=/|$|[\s"'<>`])''')),
    ('wsl-drive', re.compile(r'''(?<![^\s"'<>`=(){}\[\],;])/mnt/[A-Za-z](?=/|$|[\s"'<>`])''')),
    ('home-directory', re.compile(r'''(?<![^\s"'<>`=(){}\[\],;])(?:/(?:home|Users)/[^/\\\s"'<>`]+|/root(?=/|$|[\s"'<>`])|~[\\/])''')),
)
ESCAPE = re.compile(r'\\(\\|/|[nrt]|u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|x[0-9a-fA-F]{2})')
JSON_STRING = re.compile(r'"(?:[^"\\]|\\.)*"')


class GuardError(Exception):
    """A fixed diagnostic, never including source content or local paths."""


def require(condition: bool, rule: str) -> None:
    if not condition:
        raise GuardError(rule)


def git(root: Path, *arguments: str, data: bytes | None = None) -> bytes:
    result = subprocess.run(
        ['git', '-C', str(root), *arguments], input=data,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    require(result.returncode == 0, 'git-input-unavailable')
    return result.stdout


def relative_path(value: object) -> str:
    require(isinstance(value, str) and bool(value), 'invalid-policy')
    require(not any(ord(char) < 32 for char in value), 'invalid-policy')
    require(not any(char in value for char in '\\:*?[]'), 'invalid-policy')
    require(all(part not in ('', '.', '..') for part in value.split('/')),
            'invalid-policy')
    return value


def load_policy(root: Path) -> tuple[list[str], set[tuple[str, str]]]:
    file = root / POLICY
    require(file.resolve().is_relative_to(root), 'policy-outside-repository')
    try:
        policy = json.loads(file.read_bytes())
    except (OSError, UnicodeError, ValueError) as error:
        raise GuardError('invalid-policy') from error
    require(isinstance(policy, dict) and 'paths' in policy
            and policy.keys() <= {'paths', 'exceptions'}, 'invalid-policy')
    require(isinstance(policy['paths'], list) and bool(policy['paths']),
            'invalid-policy')
    paths = [relative_path(value) for value in policy['paths']]
    entries = policy.get('exceptions', [])
    require(isinstance(entries, list), 'invalid-policy')
    exceptions = set()
    for entry in entries:
        require(isinstance(entry, dict)
                and entry.keys() == {'file', 'sha256', 'reason'}, 'invalid-policy')
        file = relative_path(entry['file'])
        require(isinstance(entry['sha256'], str)
                and re.fullmatch(r'[0-9a-f]{64}', entry['sha256']) is not None,
                'invalid-policy')
        require(isinstance(entry['reason'], str) and bool(entry['reason'].strip()),
                'invalid-policy')
        exceptions.add((file, entry['sha256']))
    return paths, exceptions


def excluded(file: str) -> bool:
    path = PurePosixPath(file)
    name = path.name.lower()
    return (any(part.lower() in EXCLUDED_DIRECTORIES for part in path.parts[:-1])
            or path.suffix.lower() in BINARY_SUFFIXES | {'.md', '.rst', '.adoc'}
            or (path.suffix.lower() in ('', '.txt')
                and path.stem.lower() in {'readme', 'changelog', 'license', 'contributing'})
            or name.endswith(('.g.cs', '.generated.cs', '.designer.cs')))


def selected(file: str, paths: list[str], submodule: bool = False) -> bool:
    return any(file == path or file.startswith(path + '/')
               or (submodule and path.startswith(file + '/')) for path in paths)


def committed_blobs(root: Path, objects: list[str]) -> list[bytes]:
    if not objects:
        return []
    output = git(root, 'cat-file', '--batch',
                 data=('\n'.join(objects) + '\n').encode('ascii'))
    contents = []
    offset = 0
    for object_id in objects:
        end = output.index(b'\n', offset)
        sha, kind, size = output[offset:end].split()
        require(sha.decode('ascii') == object_id and kind == b'blob',
                'invalid-git-object')
        offset = end + 1
        length = int(size)
        contents.append(output[offset:offset + length])
        offset += length + 1
    return contents


def collect_files(root: Path, paths: list[str], tracked: set[str],
                  prefix: str = '', revision: str | None = None) -> dict[str, bytes]:
    if revision is None:
        records = git(root, 'ls-files', '--stage', '-z')
    else:
        records = git(root, 'ls-tree', '-r', '-z', revision)
    files = {}
    blobs = []
    for record in records.split(b'\0'):
        if not record:
            continue
        metadata, raw_name = record.split(b'\t', 1)
        name = raw_name.decode('utf-8')
        file = prefix + name
        tracked.add(file)
        fields = metadata.decode('ascii').split()
        mode = fields[0]
        object_id = fields[1] if revision is None else fields[2]
        if (not selected(file, paths, mode == '160000') or excluded(file)
                or (mode == '160000'
                    and PurePosixPath(file).name.lower() in EXCLUDED_DIRECTORIES)):
            continue
        require(revision is not None or fields[2] == '0', 'unmerged-scan-path')
        target = root / name
        require(target.resolve().is_relative_to(root), 'scan-path-outside-repository')
        if mode == '160000':
            require(target.is_dir(), 'missing-submodule')
            checkout = Path(git(target, 'rev-parse', '--show-toplevel')
                            .decode('utf-8').strip()).resolve()
            require(checkout == target.resolve(), 'missing-submodule')
            head = git(target, 'rev-parse', 'HEAD').decode('ascii').strip()
            require(head == object_id, 'submodule-revision-mismatch')
            files.update(collect_files(target, paths, tracked, file + '/', object_id))
        else:
            require(mode in ('100644', '100755'), 'unsupported-scan-file')
            if revision is None:
                require(target.is_file(), 'missing-scan-file')
                files[file] = target.read_bytes()
            else:
                blobs.append((file, object_id))
    for (file, _), content in zip(
            blobs, committed_blobs(root, [object_id for _, object_id in blobs])):
        files[file] = content
    return files


def decode_text(content: bytes) -> str | None:
    if content.startswith((b'\xff\xfe', b'\xfe\xff')):
        encoding = 'utf-16'
    elif b'\0' in content:
        return None
    else:
        # utf-8-sig drops a leading BOM, so a path on line 1 still starts a token.
        encoding = 'utf-8-sig'
    try:
        return content.decode(encoding)
    except UnicodeError:
        return None


def unescape(match: re.Match) -> str:
    value = match.group(1)
    if value in ('\\', '/'):
        return value
    if value in ('n', 'r', 't'):
        return {'n': '\n', 'r': '\r', 't': '\t'}[value]
    codepoint = int(value[1:], 16)
    return chr(codepoint) if codepoint <= 0x10ffff else match.group(0)


def line_rules(line: str, json_file: bool) -> set[str]:
    values = [line, ESCAPE.sub(unescape, line)]
    if json_file:
        for token in JSON_STRING.finditer(line):
            try:
                values.append(json.loads(token.group(0)))
            except ValueError:
                # Malformed JSON fixtures still receive the literal scan.
                pass
    rules = set()
    for value in values:
        value = URL.sub('', value)
        rules.update(rule for rule, pattern in RULES if pattern.search(value))
    return rules


def scan(repository: Path) -> list[tuple[str, int, str]]:
    root = Path(git(repository, 'rev-parse', '--show-toplevel')
                .decode('utf-8').strip()).resolve()
    paths, exceptions = load_policy(root)
    tracked = set()
    files = collect_files(root, paths, tracked)
    require(POLICY in tracked, 'untracked-policy')
    for path in paths:
        require(any(file == path or file.startswith(path + '/') for file in tracked),
                'missing-scan-path')
        require((root / path).exists(), 'missing-scan-path')
    findings = []
    for file, content in sorted(files.items()):
        text = decode_text(content)
        if text is None:
            continue
        for number, line in enumerate(text.split('\n'), 1):
            line = line.removesuffix('\r')
            fingerprint = hashlib.sha256(line.encode('utf-8')).hexdigest()
            if (file, fingerprint) in exceptions:
                continue
            for rule in sorted(line_rules(line, file.lower().endswith('.json'))):
                findings.append((file, number, rule))
    return findings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, default=Path('.'))
    args = parser.parse_args(argv)
    try:
        findings = scan(args.repository)
    except GuardError as error:
        print(f'path-guard: {error}', file=sys.stderr)
        return 1
    except (OSError, UnicodeError, ValueError):
        # Never expose OS/Git diagnostics, which may contain private paths.
        print('path-guard: scan-input-unavailable', file=sys.stderr)
        return 1
    for file, number, rule in findings:
        safe_file = json.dumps(file, ensure_ascii=True)[1:-1]
        print(f'{safe_file}:{number}: {rule}')
    if not findings:
        print('path-guard: passed')
    return int(bool(findings))


if __name__ == '__main__':
    sys.exit(main())
