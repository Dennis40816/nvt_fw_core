# Repository checker engines

These two Python engines preserve the source checker behavior and accept repository-owned policy values.
They use only the Python standard library.
Python 3.10 or later supports their type syntax.

The caller imports the files from `tools/repo-checks`.
The engines return measurements and diagnostics.
The caller selects files, prints results, and sets the process exit code.

## Skill metadata

`skill_metadata_validation.py` parses the existing closed YAML schema for skill metadata.
It does not parse general YAML.

Call `parse_skill_metadata(metadata_path, repository_root, errors)` first.
It returns metadata or appends structural errors and returns `None`.
Call `validate_skill_metadata_fields(metadata, metadata_path, repository_root, skill_name, errors)` only after parsing succeeds.

The schema accepts these fields:

- `interface.display_name`: a JSON-quoted string.
- `interface.short_description`: a JSON-quoted string.
- `interface.default_prompt`: a JSON-quoted string.
- `policy.allow_implicit_invocation`: the literal `true` or `false`.

The validator requires three nonempty interface strings.
The short description requires at least 25 characters after trimming and at most 64 characters before trimming.
The default prompt must reference `$<skill_name>` with the source's name-boundary rule.
The parser rejects comments, unknown fields, duplicate entries, and tabs in nonblank lines.
Blank lines remain valid.
File and decoding errors propagate to the caller.

Repository inventory, frontmatter, routing, and invocation-policy decisions remain with the caller.

## Code size

`code_size_policy.py` measures physical source files and exact hotspot baselines.
A hotspot is a type that requires enrollment under the caller's size policy.

`measure_code_size(root, ...)` returns a frozen `CodeSizeSnapshot`.
It measures C# and Avalonia markup under `src`.
It counts nonblank lines from whole files, including comments and literals.
It counts each declaring file once for each qualified C# type.
It preserves the source's declaration recognizer and generated-file exclusions.

The caller can supply these existing measurement boundaries:

| Argument | Meaning when supplied | Default |
| --- | --- | --- |
| `runtime_excluded_project` | Exclude one immediate `src` project from runtime counts. | Include all C# projects. |
| `python_runtime_directory` | Include owned Python files below this repository-relative directory in runtime counts. | Include no Python files. |
| `json_directories` | Measure byte-identical JSON copies in these repository-relative directories. | Measure no JSON files. |

`validate_code_size_policy(root, hotspots, *, entry_lines, exit_lines)` returns ordered errors.
The caller supplies the enrollment map and both thresholds.
No product enrollment map or thresholds live in Core.

- Unenrolled types at the entry threshold require enrollment.
- Enrolled types below the exit threshold require removal.
- Growth requires a higher measured baseline and owner approval.
- Reduction requires a lower measured baseline.
- Exact baselines pass, including the retention band between both thresholds.

`review_code_size_policy(root, *, policy_reference, ...)` returns one advisory string.
It accepts the same measurement arguments as `measure_code_size`.
The caller supplies the policy reference printed in that string.
Diagnostics preserve the source wording when the caller supplies its original values.

## Frozen source

The extraction uses NVT FW Combiner, identified here as NFC.
The source repository is `nvt_fw_combiner`.
The frozen ref is `origin/1.2.x`.
The full commit is `aedd200fae106a36c1f248aa9978cdcb12f3044f`.
Read each source file with `git show <full-commit>:<path>`.

| Source path | Extraction |
| --- | --- |
| `scripts/skill_metadata_validation.py` | Parser and semantic validator, unchanged except for the required license header. |
| `scripts/code_size_policy.py` | Measurements and hotspot checks, with product values supplied by the caller. |
| `tests/scripts/test_code_size_policy.py` | All synthetic regressions, converted from pytest to unittest. |
| `tests/scripts/test_skill_inventory_validation.py` | Metadata regressions that exercise the extracted parser and validator. |

The source's real enrollment assertion uses a synthetic enrollment map in Core.
The source's inventory and routing tests stay with NFC.
Core adds characterization tests for exact outputs and caller-owned policy values.

## Tests and zero-difference verification

Run this command from the Core repository root:

```text
python -B -m unittest discover -s tests/repo-checks -v
```

For later NFC adoption, keep its parent commit and policy values frozen.
Run its checker regressions before and after replacing the duplicate imports:

```text
python -B -m pytest tests/scripts/test_code_size_policy.py
python -B -m unittest discover -s tests/scripts -p test_skill_inventory_validation.py -v
```

The pytest command belongs to NFC's existing test environment.
Core requires no pytest dependency.

Compare both implementations on identical synthetic input trees before adoption:

1. Load the source modules with `git show` at the frozen full commit.
2. Supply the source's enrollment map, thresholds, runtime boundaries, JSON directories, and advisory policy reference to Core.
3. Compare every snapshot field, ordered error list, metadata result, and advisory string.
4. Compare the adopting caller's printed output and exit code before and after changing its imports.

Extraction verification passed 43 unittest methods on Python 3.13.5.
A read-only comparison passed 65 exact result comparisons against the frozen source.
All 18 metadata test methods also passed against the unmodified frozen parser.
The parser source matches byte-for-byte after removing the required license header.

Do not update baselines to accept differences.
NFC adoption and workflow changes remain separate tasks.

The host commit should record the repository, ref, full commit, and four source paths listed above.
