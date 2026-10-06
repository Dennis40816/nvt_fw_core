# Shared approval checker v0

This composite action runs the approval evaluation from NFU's merged version on a pull request and writes the result as a status on the head commit. The checker verifies that base and head have not moved, that head contains the latest base, and that the review record is valid; it determines whether owner approval is also required based on the base branch and changed paths. The check results are also appended to the job summary.

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
