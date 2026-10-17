"""Leak check for an export staging tree. Exit 0 when clean, 1 when a rule fails, 2 when it cannot run.

Usage: python leak_check.py --staging DIR --manifest manifest.json --words WORDS_FILE

Rules:
1. The files in staging are exactly the files of the manifest. Nothing else.
2. No local path, e-mail address or date in any text file.
3. Every exported source file carries the MIT SPDX header and no "All rights reserved".
4. Text files use LF and have no byte order mark.
5. No binary file (fonts, images, archives).
6. No word from the words file. The words file is not part of this repository.
   The check does not run without it, so a clean result always includes the word rule.
"""
import argparse
import json
import os
import re
import sys

# Generic rules only. Names, internal words and file markers live in the words file.
STRUCTURAL = [
    (r'(?<![A-Za-z])[A-Za-z]:[\\/]|/Users/|/home/|\\\\', 'local path'),
    (r'[\w.+-]+@[\w-]+\.[\w.]+', 'e-mail address'),
    (r'\b20\d\d-\d\d-\d\d\b', 'date'),
]
TEXT_EXT = {'.cs', '.axaml', '.csproj', '.md', '.gitattributes', ''}


def load_words(path):
    """One rule per line: a regular expression, a tab, a label. Blank lines and lines starting with # are skipped."""
    rules = []
    with open(path, encoding='utf-8') as handle:
        for number, line in enumerate(handle, 1):
            line = line.rstrip('\r\n')
            if not line.strip() or line.startswith('#'):
                continue
            pattern, _, label = line.partition('\t')
            if not label:
                raise ValueError(f'{path}:{number}: expected "regex<TAB>label"')
            rules.append((pattern, label))
    if not rules:
        raise ValueError(f'{path}: no rules')
    return rules


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--staging', required=True)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--words', required=True)
    args = parser.parse_args()
    try:
        words = load_words(args.words)
    except (OSError, ValueError) as error:
        print(f'cannot run: {error}')
        return 2

    manifest = json.load(open(args.manifest, encoding='utf-8'))
    expected = {f['path'] for f in manifest['files']}
    actual = set()
    for base, _, names in os.walk(args.staging):
        for name in names:
            actual.add(os.path.relpath(os.path.join(base, name), args.staging).replace('\\', '/'))
    problems = []
    for extra in sorted(actual - expected):
        problems.append(f'not in the manifest: {extra}')
    for missing in sorted(expected - actual):
        problems.append(f'missing from staging: {missing}')

    rules = STRUCTURAL + words
    for rel in sorted(actual & expected):
        ext = os.path.splitext(rel)[1]
        data = open(os.path.join(args.staging, rel), 'rb').read()
        if ext not in TEXT_EXT and os.path.basename(rel) not in ('LICENSE', '.gitattributes'):
            problems.append(f'binary or unknown type: {rel}')
            continue
        if data.startswith(b'\xef\xbb\xbf'):
            problems.append(f'byte order mark: {rel}')
        if b'\r' in data:
            problems.append(f'CR in text file: {rel}')
        text = data.decode('utf-8')
        for pattern, label in rules:
            for match in re.finditer(pattern, text):
                line = text.count('\n', 0, match.start()) + 1
                problems.append(f'{label} in {rel}:{line}')
        if rel.endswith(('.cs', '.axaml')):
            if 'SPDX-License-Identifier: MIT' not in str(text.split('\n', 3)[0:3]):
                problems.append(f'no SPDX header in the first lines: {rel}')
            if 'All rights reserved' in text:
                problems.append(f'"All rights reserved" in {rel}')

    if problems:
        print('LEAK CHECK FAILED')
        for item in problems:
            print('  ' + item)
        return 1
    print(f'leak check passed: {len(actual)} files, {len(rules)} rules')
    return 0


if __name__ == '__main__':
    sys.exit(main())
