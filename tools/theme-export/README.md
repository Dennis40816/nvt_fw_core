# Theme export

Conclusion: this tool copies the theme part of Core into a separate folder, in a form that another project can take over. It never pushes anything.

## What it exports

- Theme tokens and control styles (`Nvt.Core.Avalonia/Theme`).
- The icon name table (`Nvt.Core.Avalonia/Icons`).
- The font role names (`Nvt.Core.Fonts/FontRoles.axaml`). No font file is exported.

The list of files is in `ALLOWLIST` in `export.py`. A file that is not in the list is never copied.

## Run

```bash
python tools/theme-export/export.py --words <path to the words file> --ref origin/main
```

- `--words` is required. The words file is not part of this repository. Ask the maintainer for the path.
- `--ref` is a Git ref. The export reads the files from that ref, not from the working tree.
- `--out` is the output folder. The default is `artifacts/theme-export`, which Git ignores.

The output folder holds `staging/` (the files), `manifest.json` and `manifest.csv` (path, source blob, SHA-256, size, reason).

## Checks

`leak_check.py` runs at the end of every export and fails the export on:

- a file that is not in the manifest, or a missing file
- a local path, an e-mail address or a date
- a word from the words file
- a missing MIT header, a byte order mark, a CR character or a binary file

It does not run without a words file. The words file format is one rule per line: a regular expression, a tab and a label.

## After the export

A public push of the result needs a separate sign-off on the exact tree. Compare the SHA-256 values in `manifest.json` with the tree that was signed off.
