# Repository checker engines

These two Python engines preserve the source checker behavior and accept repository-owned policy values.
They use only the Python standard library.
Python 3.10 or later supports their type syntax.
[Shared C# health policy bundle](csharp/README.md) provides the canonical props, EditorConfig, banned symbols, schema, and manifest.

See [C# syntax health ratchet](repo-health.md) for the SDK Roslyn checker and baseline modes.

The caller imports the files from `tools/repo-checks`.
The engines return measurements and diagnostics.
The caller selects files, prints results, and sets the process exit code.

## Test duration report

`duration_report.py` reads TRX files and does three things:

- It lists the 20 slowest tests and writes the list to the GitHub job summary.
- It converts each duration to reference seconds with the calibration unit that `CalibrationTests` prints.
- It checks the slow-test ratchet in `tests/slow-tests-baseline.json` ([testing rules 15 to 20](../../docs/core/testing.md#test-duration)).

```powershell
python tools/repo-checks/duration_report.py --trx artifacts/test-results --baseline tests/slow-tests-baseline.json --github-summary --check
```

- `--seed-baseline FILE` writes a first baseline from the TRX files. Every slow test gets the reason `unclassified`, which is a note and not a failure.
- `--require-trx` exits with 2 when no TRX file is found.
- The script reads no traits. The baseline file is the list of slow tests.
- Tests run in parallel, so a duration includes the wait for a CPU. Compare runs of the same setup only.

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


## Documentation sync

`doc_sync.py` checks a selected Git diff against a repository-owned JSON policy.
It is also an importable module.
It uses Git and committed file contents.
It never calls a model or the network.
It never changes a file.

Run this command from the repository root:

```text
python tools/repo-checks/doc_sync.py --repo . --config tools/repo-checks/doc-sync.core.json --base <ref> [--head <ref>] [--pr-body-file <path>] [--mode warn|enforce] [--all-links]
```

`--head` defaults to `HEAD`.
The diff uses `<base>...<head>` with rename detection.
Both paths of a rename count as changed.
Uncommitted changes do not enter the checks.

`load_config(path)` validates the version-one JSON schema.
It accepts UTF-8 with or without a BOM.
It rejects unknown keys and wrong types.
`check_repository(repo, config, base, ...)` returns findings and an accepted exemption reason.

The config has `version`, `mappings`, `bilingual`, and `moduleLists` fields.
Each mapping has a name, source path patterns, and required document templates.
`*` matches one path segment.
`**` matches any number of path segments.
`{name}` captures a whole path segment.
Document templates substitute those captures.
Required documents must appear in the same changed set.

A PR body line `Docs: none — <reason>` exempts only the mapping check.
The separator can also be an en dash or a hyphen.
`Docs: none` ignores case.
The reason must contain a non-space character.
An empty reason produces a finding and leaves mapping active.
The report prints the accepted reason.

The bilingual check pairs `X.md` with `X.zh-TW.md`.
A companion at either selected commit must also change.
`bilingual.exclude` uses the same path patterns.

Module lists run on every invocation.
Modules are immediate source-root folders with a tracked file at the head.
List patterns substitute `{lib}` and `{module}`.
A missing list document produces a finding.
`reverse` defaults to false.
A reverse list also reports references to missing module folders.
The Core config checks both README lists and `docs/components.md`.
The translated README may link either module document.
It maps source changes to both module documents.
The pattern `src/{lib}/{module}/*/**` needs a file inside a module folder.
Files directly under a library folder, such as lock files, map to nothing.
Core conventions do not require a mapping for test-only changes.
The components list permits planned modules.

The links check reads relative Markdown links, images, and reference definitions.
It skips fenced code and inline code.
It skips HTTP, HTTPS, mail, and anchor-only targets.
It removes fragments and queries before decoding percent escapes.
A leading `/` starts at the repository root.
Targets must exist in the head tree.
Changed Markdown files receive the normal link check.
`--all-links` extends that check to all tracked Markdown files.
Every tracked Markdown file is checked for links to deleted or renamed paths.

Each finding is one GitHub Actions annotation line, for example `::warning file=README.md,line=3::doc-sync links: ...`.
The line stays readable outside CI.
Every finding names a file and a line; a file-wide finding uses line 1.
A missing exemption reason has no location, because the PR body is not a repository file.
An accepted exemption prints a `::notice::` line.
Warn mode prints `::warning` findings and exits 0.
Enforce mode prints `::error` findings and exits 1 when findings exist.
Both modes exit 2 for usage, config, file, or Git errors.
The final summary counts mapping, bilingual, module-list, and links findings.
The owner chose one week of warnings before enforcement.
Core integration owns rollout timing and CI wiring.
This tool is new code with no frozen source baseline.

## Handoff report

`handoff_check.py` reports stale or incomplete WIP summaries.
It is report-only.
It is not wired into CI.
It never changes a file.
It never calls `gh` or the network.

```text
python tools/repo-checks/handoff_check.py --wip <file> --repo . [--branch <ref>] [--open-prs <file>] [--utc-offset +HH:MM]
```

`--branch` defaults to `HEAD`.
The current section starts at the first heading containing `以本節為準`.
It ends at the next heading of the same or a higher level.
Child headings remain in the section.
Headings inside fenced code do not count.
The summary reports the number of marked headings.
A missing marked heading produces a finding.

The section time is its latest valid timestamp.
The accepted forms are `YYYY-MM-DD HH:MM` and `MM-DD HH:MM`.
A minute written as `Mx`, such as `11:2x`, is not a time and is ignored.
The short form uses the head commit's year.
WIP times are clock times, so they use `--utc-offset`, which defaults to this computer's zone.
The head commit's zone is not used, because a GitHub merge commit records UTC.
Other forms do not supply a section time.
A missing timestamp produces a finding.
A newer head commit produces a finding.
A section time more than 10 minutes after the current time produces a finding.
A head SHA mention accepts any 7 to 40 character hex prefix.
The prefix ignores case.
A missing head SHA produces a finding.

`--open-prs` reads the JSON array produced by `gh pr list --json number,url`.
The caller supplies that file.
Each entry needs an integer number and a nonempty URL.
Each missing full URL produces a finding.
A URL followed by a letter, digit, `/`, `_` or `-` does not count, so `pull/3` never matches `pull/30`.
Only the current section supplies evidence.

The command prints one line per finding and a final summary.
A completed report exits 0 even when findings exist.
Usage, file, and Git errors exit 2.
`check_handoff(wip, repo, ...)` returns findings and the marked-heading count.
This tool is new code with no frozen source baseline.
