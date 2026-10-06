# Shared approval checker

This composite action runs the approval evaluation from NFU's merged version on a pull request and writes the result as a status on the head commit. The checker verifies that base and head have not moved, that head contains the latest base, and that the review record is valid; it determines whether owner approval is also required based on the base branch and changed paths. The check results are also appended to the job summary.

## NFH pilot compatibility

Freeform Helper (NFH) can use its two-tier policy without changing NVT FW UTIL (NFU) evaluation results.
Ordinary documentation and test changes require an authorized review record that accepts the current head.
Owner-gated changes require the owner's GitHub approval for the current head.
Independent review remains recommended for NFH's owner tier.

The base policy controls these differences:

| Policy field | Behavior |
| --- | --- |
| `owner_gated_base_branch` | A configured branch requires owner approval. Omit this field or use `null` to disable the branch gate. |
| `tests_non_added` | A configured pattern requires owner approval for non-added tests. Omit this field or use `null` to disable that gate. |
| `owner_requires_review_record` | Defaults to `true`. Only Boolean `false` removes the review record requirement from owner-gated PRs. |

NFU keeps its existing branch gate, test gate, and review record requirement for both tiers.
NFH omits the first two fields and sets `owner_requires_review_record` to `false`.
The [synthetic NFH policy](../../tests/approval-check/fixtures/nfh/approval/.github/approval-policy.json) demonstrates this mapping with sample identities.
The NFH adoption PR must enumerate its actual release paths, agent-permission paths, and authorized record authors.
Keep governance documents owner-gated until the owner resolves their classification.

A mixed PR uses the owner tier if any changed path matches `owner_gated_patterns`.
Renames check both the previous path and the new path.
Modified tests remain in NFH's ordinary tier unless another path rule requires owner approval.

The review record syntax remains:

```text
Review record: <full head SHA> accept|reject
```

Put the record on the review body's first non-empty line.
An ordinary-tier accept attests to independent review with no unresolved P0 or P1 findings.
The checker verifies the author's login and numeric ID, syntax, verdict, and head.
It does not prove reviewer independence or inspect finding severity.
Base freshness, head freshness, record parsing, and owner identity checks retain their existing behavior.
This change adds no carryover rule.

Release the compatibility change separately before NFH adoption.
The pilot proposal suggests `v0.2.0`; the release owner must confirm the version and protected tag.
Keep `v0.1.0` unchanged and pin the new full commit SHA in NFH.
Run the pilot status without blocking merges until NFH 1.3.2 and the pilot's enforcement prerequisites are complete.

### Frozen baselines

| Repository | Ref | Full commit SHA | Source paths |
| --- | --- | --- | --- |
| NFU (`nvt-event-buffer-replay`) | `origin/0.2.0` | `915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b` | `scripts/approval_check.py`, `tests/scripts/test_approval_check.py`, `tests/scripts/fixtures/approval/`, `.github/approval-policy.json`, `.github/CODEOWNERS` |
| NFH (`nvt-freeform-helper`) | `origin/1.3.x` | `922ba49c6801a8348958655b5473bd8b9b8bdc87` | `CONTRIBUTING.md`, `AGENTS.md` |

NFU's listed files match the original checker baseline at `origin/0.1.2`, commit `1648c42a9bf922642f5f304c521bfcb17cd8e58e`.
NFH supplies policy evidence only; this change extracts no NFH runtime code.
Read source files with `git show` from these frozen commits.

### Verification and zero difference

Run the shared suite from the Core checkout:

```text
python -B -m unittest discover -s tests/approval-check -v
```

The existing NFU tests and fixtures stay unchanged.
The NFH tests cover ordinary changes, mixed changes, owner approval, missing evidence, stale evidence, identity checks, and freshness checks.
Synthetic cases verify checker compatibility; real pilot evidence remains an adoption requirement.

Before NFU switches to Core, verify zero difference with its frozen checker:

1. Read the listed NFU files with `git show` into a temporary test layout.
2. Also read `.github/workflows/approval.yml` and `CONTRIBUTING.md` from the frozen NFU commit; its tests inspect these supporting files.
3. Run `python -B -m unittest discover -s tests/scripts -p test_approval_check.py -v` there with the source checker.
4. Replace only the temporary checker with Core's checker and run the same source tests again.
5. Compare all four result tuples, output lines, exit codes, and API request labels for identical NFU payloads.
6. Run the shared suite above with the unchanged NFU policy and fixtures.

Both checker runs must pass the same source tests and produce identical NFU results.
The original v0 provenance below describes the checker before this NFH compatibility change.

## Inputs

| Input | Default | Description |
| --- | --- | --- |
| `policy-path` | `.github/approval-policy.json` | Path to the policy JSON relative to the base branch checkout. |
| `status-context` | `governance/approval-rule` | Status context written to the head commit. |
| `token` | `${{ github.token }}` | Token for reading the PR and writing commit status. |

The caller workflow should use NFU's existing `pull_request` events (`opened`, `synchronize`, `reopened`, `ready_for_review`, `edited`, `converted_to_draft`) and `pull_request_review` events (`submitted`, `edited`, `dismissed`). Permissions must include `contents: read`, `pull-requests: read`, and `statuses: write`. See [NFU caller](../../examples/nfu-approval.yml) for a complete example.

## Trust model

The checker comes from the shared action pinned by the caller to a full SHA; the policy comes from the PR's base branch. GitHub reads the workflow definition from the PR's merge ref, so someone who can modify the workflow may change how it is called; the agent's GitHub App must not have `workflows` permission. The caller must pin the SHA referenced by a protected tag in the shared repository.

The ruleset should require commit status `governance/approval-rule`, with **GitHub Actions** selected as the source; do not require a check run with the same name. Each time, this action first sends pending, then sends a success or failure commit status at the end.

The frozen source is NFU (`nvt-event-buffer-replay`), ref `origin/0.1.2`, commit `1648c42a9bf922642f5f304c521bfcb17cd8e58e`.
The source paths are `scripts/approval_check.py` and `tests/scripts/test_approval_check.py`.

The checker adds `--policy <path>` to the original NFU version. When it is not specified, it still reads `ROOT / ".github/approval-policy.json"`. The action first verifies that the resolved policy path is inside the base checkout, failing the check otherwise. Without the optional setting below, evaluation and output remain unchanged.

## Optional accept carryover

Carryover is disabled by default. To enable it, add this object to the **base branch's policy**:

```json
"review_carryover": {
  "enabled": true,
  "allowlist": ["docs/notes/*.md", "docs/notes/*.txt"]
}
```

Only the latest independent review record's `accept` can carry over; owner GitHub approval still requires the current head. Both heads' net changes are compared against the current base by status, blob SHA, and previous filename. An identical net change, including a clean base merge, carries over. File modes are checked separately.

Changed files must match the allowlist, avoid owner-gated paths, and use modified or added status. The checker uses its existing path-pattern matching.
Files must be regular, non-executable `.md` or `.txt` files with unchanged mode.
The fixed limits are **5 files and 40 added plus deleted lines**. A replaced line counts as two.

A changed file cannot carry over if its base, accepted, or current blob contains three consecutive backticks or tildes anywhere.
Reject changed lines if they or the nearest earlier non-empty line contain `[`, `]`, `<`, or `(`, or that earlier line ends with `:`.

Changed lines containing URLs, HTML, or Unicode `Cf` characters, including HTML entities, require another review.
The checker examines complete UTF-8 blobs on both sides. Unchanged URLs, HTML, and Unicode `Cf` characters can remain.
A changed line containing a URL or `<`/`>` requires another review even when that token is unchanged.
Open HTML elements, including non-void self-closing tags, protect subsequent lines.
Unavailable or truncated evidence, including compare lists of 300 or more files, cannot carry over.

Every run compares with the SHA actually named by the latest review record, so carryovers do not accumulate. The step summary reports that SHA, the changed paths, and the line count. An absent setting or any value other than Boolean `true` for `enabled` keeps exact-head behavior.

## Disabled behavior comparison

Run these PowerShell commands from the Core checkout. The commands read the frozen source into a temporary directory and leave the source repository unchanged.
Replace `../nvt-event-buffer-replay` with the source checkout location when necessary.

```powershell
$baseline = Join-Path ([IO.Path]::GetTempPath()) ("approval-baseline-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $baseline | Out-Null
$sourcePaths = @(
    "scripts/approval_check.py", "tests/scripts/test_approval_check.py",
    "tests/scripts/fixtures/approval", ".github/approval-policy.json",
    ".github/CODEOWNERS", ".github/workflows/approval.yml", "CONTRIBUTING.md"
)
git -C ../nvt-event-buffer-replay archive --format=tar --output="$baseline/source.tar" `
    1648c42a9bf922642f5f304c521bfcb17cd8e58e -- $sourcePaths
tar -xf "$baseline/source.tar" -C $baseline
python -B -m unittest discover -s "$baseline/tests/scripts" -p test_approval_check.py -v
if ($LASTEXITCODE -ne 0) { throw "Frozen source tests failed" }
$checkArgs = @(
    "--repository", "example/repository", "--pull-request", "1",
    "--fixture", "$baseline/tests/scripts/fixtures/approval"
)
$originalOutput = python -B "$baseline/scripts/approval_check.py" @checkArgs
$originalExitCode = $LASTEXITCODE
Copy-Item actions/approval-check/approval_check.py "$baseline/scripts/approval_check.py"
python -B -m unittest discover -s "$baseline/tests/scripts" -p test_approval_check.py -v
if ($LASTEXITCODE -ne 0) { throw "Core checker failed the source tests" }
$coreOutput = python -B "$baseline/scripts/approval_check.py" @checkArgs
if ($LASTEXITCODE -ne $originalExitCode -or (Compare-Object $originalOutput $coreOutput)) {
    throw "Disabled behavior differs from the frozen source"
}
python -B -m unittest discover -s tests/approval-check -v
if ($LASTEXITCODE -ne 0) { throw "Core approval tests failed" }
python -B -m unittest discover -s tests/approval-check -k test_off_keeps_existing_results_and_requests -v
if ($LASTEXITCODE -ne 0) { throw "Disabled result or request comparison failed" }
```

Both runs must pass the same 56 unchanged source tests. The fixture comparison must produce identical output and exit codes.
The final test compares result tuples and API request labels with carryover absent or disabled, for current and older review records.
