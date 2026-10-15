"""One-way export of the theme batch of Core (theme tokens, control styles, icon name table, font role names).

Reads the files from a fixed Git ref of the Core repository (not from the working tree), applies the
transforms below, writes the result and the file manifest to the output folder. Then it runs leak_check.py.
Nothing is pushed. A push of the result needs a separate approval of the exact tree.

Usage: python export.py --words WORDS_FILE [--core .] [--ref origin/main] [--out artifacts/theme-export]

The words file holds the words that must not appear in the output. It is kept outside this repository.
"""
import argparse
import csv
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PACKAGE = 'Nvt.Core.Theme'
SRC = 'src/Nvt.Core.Avalonia/'

# (Core path relative to the repository root, exported path relative to the staging root, reason)
THEME_AXAML = [
    'ThemeTokens', 'ControlTokens', 'ChoiceTokens', 'DividerTokens', 'FormTokens', 'ListTokens', 'TabTokens',
    'ToggleTokens', 'NfcLegacyFontTokens', 'ShapePill', 'ShapeSquare', 'RestFillNone', 'RestFillSoft',
    'ButtonStyles', 'ChoiceStyles', 'DividerStyles', 'ExpanderStyles', 'FormStyles', 'ListStyles', 'MenuStyles',
    'ProgressStyles', 'ScrollStyles', 'TabStyles', 'TextStyles', 'ToggleStyles',
]
THEME_CS = [
    'FocusRingRadiusConverter', 'MenuCornerRadiusConverter', 'ProgressRadiusConverter',
    'ThemeRestFill', 'ThemeRestFills', 'ThemeShape', 'ThemeShapes',
]
ALLOWLIST = []
for name in THEME_AXAML:
    reason = 'theme token or style (first batch)'
    if name == 'NfcLegacyFontTokens':
        reason = 'font size and family values that ThemeTokens.axaml aliases (first batch)'
    ALLOWLIST.append((f'{SRC}Theme/{name}.axaml', f'src/{PACKAGE}/Theme/{name}.axaml', reason))
for name in THEME_CS:
    ALLOWLIST.append((f'{SRC}Theme/{name}.cs', f'src/{PACKAGE}/Theme/{name}.cs',
                      'converter or shape helper that the styles reference (first batch)'))
ALLOWLIST += [
    (f'{SRC}Icons/IconResources.axaml', f'src/{PACKAGE}/Icons/IconResources.axaml', 'icon name table (first batch)'),
    (f'{SRC}Icons/IconStyles.axaml', f'src/{PACKAGE}/Icons/IconStyles.axaml', 'icon text style (first batch)'),
    (f'{SRC}Icons/NvtIcons.cs', f'src/{PACKAGE}/Icons/NvtIcons.cs', 'icon name table in C# (first batch)'),
    ('src/Nvt.Core.Fonts/FontRoles.axaml', f'src/{PACKAGE}/Fonts/FontRoles.axaml',
     'font role table that the styles and icon style read; font files are not included (first batch)'),
]
STATIC = ['LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md', '.gitattributes', f'src/{PACKAGE}/{PACKAGE}.csproj']

# Comments that name a person or a date are rewritten to a plain fact.
# Each entry is a regular expression and must match at least one file, or the export stops.
TEXT_REPLACEMENTS = [
    (r', so the \w+ \w+ Normal on \d{4}-\d{2}-\d{2}\.', ', so Normal is used.'),
    (r'Preserve the \w+ pressed inset', 'Preserve the pressed inset'),
    (r'Provides single-character glyphs from the bundled Material Symbols Outlined font\.',
     'Provides single-character glyphs for the Material Symbols Outlined font. The font file is not included.'),
    (r"Soft keeps today's rest fills\.", 'Soft keeps the default rest fills.'),
    (r'Colors reuse the shared Core palette\.', 'Colors reuse the shared palette.'),
    (r'This private template part signals', 'This internal template part signals'),
]
USED_REPLACEMENTS = set()

COPYRIGHT_CS = '// Copyright (c) 2026 Dennis Liu. All rights reserved.'
COPYRIGHT_XML = '<!-- // Copyright (c) 2026 Dennis Liu. All rights reserved. -->'
COPYRIGHT_XML2 = '<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->'


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def git(core, *args):
    return subprocess.run(['git', '-C', core, *args], capture_output=True, check=True).stdout


def transform(path, text):
    text = text.replace('\r\n', '\n')
    if path.endswith('.cs'):
        text = text.replace(COPYRIGHT_CS, '// Copyright (c) 2026 Dennis Liu\n// SPDX-License-Identifier: MIT', 1)
    else:
        for old in (COPYRIGHT_XML, COPYRIGHT_XML2):
            text = text.replace(old, '<!-- Copyright (c) 2026 Dennis Liu -->\n<!-- SPDX-License-Identifier: MIT -->', 1)
    for pattern, new in TEXT_REPLACEMENTS:
        text, count = re.subn(pattern, new, text)
        if count:
            USED_REPLACEMENTS.add(pattern)
    text = text.replace('avares://Nvt.Core.Avalonia/', f'avares://{PACKAGE}/')
    text = text.replace('Nvt.Core.Avalonia.Icons', f'{PACKAGE}.Icons')
    text = text.replace('Nvt.Core.Avalonia.Theme', f'{PACKAGE}.Theme')
    if path.endswith('FontRoles.axaml'):
        # The font files are not part of this package. The host registers them. Keep only the family names.
        text = re.sub(r'avares://[^#<]*#', '', text)
    return text


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--core', default=os.path.abspath(os.path.join(HERE, '..', '..')))
    parser.add_argument('--ref', default='origin/main')
    parser.add_argument('--out', default=None)
    parser.add_argument('--words', required=True)
    args = parser.parse_args()
    out = os.path.abspath(args.out or os.path.join(args.core, 'artifacts', 'theme-export'))
    staging = os.path.join(out, 'staging')

    commit = git(args.core, 'rev-parse', args.ref).decode().strip()
    if os.path.isdir(staging):
        shutil.rmtree(staging)
    os.makedirs(staging)

    rows = []
    for source, dest, reason in ALLOWLIST:
        raw = git(args.core, 'show', f'{commit}:{source}')
        blob = git(args.core, 'rev-parse', f'{commit}:{source}').decode().strip()
        text = transform(source, raw.decode('utf-8-sig'))
        data = text.encode('utf-8')
        target = os.path.join(staging, dest)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, 'wb') as handle:
            handle.write(data)
        rows.append({'path': dest, 'source': source, 'source_blob': blob, 'source_sha256': sha256(raw),
                     'sha256': sha256(data), 'bytes': len(data), 'reason': reason})

    for rel in STATIC:
        source = os.path.join(HERE, 'templates', rel.replace('/', os.sep))
        if rel.endswith('.csproj'):
            source = os.path.join(HERE, 'templates', f'{PACKAGE}.csproj')
        data = open(source, 'rb').read().replace(b'\r\n', b'\n')
        target = os.path.join(staging, rel)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, 'wb') as handle:
            handle.write(data)
        rows.append({'path': rel, 'source': 'templates/' + os.path.basename(source), 'source_blob': '',
                     'source_sha256': sha256(data), 'sha256': sha256(data), 'bytes': len(data),
                     'reason': 'repository file written for the export'})

    unused = [old for old, _ in TEXT_REPLACEMENTS if old not in USED_REPLACEMENTS]
    if unused:
        print('replacement did not match any file: ' + repr(unused))
        return 1
    rows.sort(key=lambda r: r['path'])
    with open(os.path.join(out, 'manifest.csv'), 'w', newline='', encoding='utf-8') as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    with open(os.path.join(out, 'manifest.json'), 'w', encoding='utf-8', newline='\n') as handle:
        json.dump({'coreRef': args.ref, 'coreCommit': commit, 'files': rows}, handle, indent=2)
        handle.write('\n')
    print(f'exported {len(rows)} files from {commit}')
    return subprocess.call([sys.executable, os.path.join(HERE, 'leak_check.py'), '--staging', staging,
                            '--manifest', os.path.join(out, 'manifest.json'), '--words', args.words])


if __name__ == '__main__':
    sys.exit(main())
