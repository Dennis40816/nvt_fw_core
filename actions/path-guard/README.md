# Shared path guard

This composite action and its local check detect nonportable literal paths in configured Git-tracked text.
They use Git, PowerShell and standard-library Python.
Diagnostics contain only the repository-relative file, physical source line and rule.

## Scope

The guard targets accidental literal paths.
It decodes doubled backslashes, escaped whitespace and JSON strings.
Deliberate encodings, such as variable-width hex or Unicode escapes of letters, are outside the supported scope.

## Checks and policy

The scanner checks configured test, project/configuration and text-fixture paths for these rules:

- `windows-drive`: Windows drive-rooted paths with either separator.
- `unc`: UNC paths.
- `msys-drive`: MSYS drive mounts.
- `wsl-drive`: WSL drive mounts.
- `home-directory`: macOS/Linux home-directory paths.

The scanner recognizes escaped literals and decodes JSON strings.
It decodes escaped whitespace (`\n`, `\r`, `\t`) before boundary checks in every scanned file type.
It keeps physical source line numbers.
Relative paths and URLs are allowed.
The scanner ignores documentation, binary files, generated/build output, dependencies and untracked files.
It supports UTF-8 and BOM-marked UTF-16 text.

The caller supplies a tracked `.github/path-guard.json`.
Its only fields are `paths` and optional `exceptions`.
The `paths` field contains a nonempty list of exact repository-relative files or directories, using `/`.

```json
{
  "paths": ["tests", "src/Example/Example.csproj", "test-data"],
  "exceptions": []
}
```

Each exception has exactly `file`, `sha256` and a nonempty `reason`.
The hash is SHA-256 of the exact source line encoded as UTF-8, excluding its CRLF/LF ending.
The scanner does not trim whitespace before hashing.
An exception permits that line in that file only.
Changing the line invalidates the exception.
The policy supports no directory/rule suppressions or custom detection rules.
Review policy changes through the caller's existing governance and CODEOWNERS.

The scanner reads current tracked working-tree text so local edits are checked before push.
Public submodules inside configured scope must already be initialized at the index's recorded gitlink commits.
The scanner reads their text from those commits, including nested gitlinks.
Missing configured scope, tracked scan files or required submodules fails the check.
The guard performs no fetch, build or product test run.

## Action use

After release, pin the action to the full SHA of a protected version tag in the caller's existing verification job.
Include the version comment.

```yaml
- name: Check paths
  uses: Dennis40816/nvt_fw_core/actions/path-guard@<full 40-character SHA> # vX.Y.Z
```

The caller checks out its repository and required public submodules first.
During Freeform Helper (NFH) calibration, use the caller step's existing `continue-on-error: true`.
The scanner has no report-only mode.
Enforcement follows calibration and NFH 1.3.2.
Existing `github-actions` Dependabot updates the pin.
Rollback reverts that update.
NFH adopts first, then NVT FW UTIL (NFU), then NVT FW Combiner (NFC) between releases.
This implementation adds no workflow or product-repository changes.

## Local use

For local verification before push, use a Core checkout at the same commit as the action pin.
From that checkout, run:

```powershell
pwsh -NoProfile -File ./actions/path-guard/check.ps1 -RepositoryRoot $callerCheckout
```

`$callerCheckout` identifies the caller's checkout.
With no argument, the script finds the repository root from the current working directory.
Both entry points return 0 for a clean scan and 1 for findings or unavailable/malformed scan inputs.
The Python entry point is also available:

```powershell
python -B actions/path-guard/path_guard.py --repository $callerCheckout
```

## Frozen parent baselines

Both baselines come from public `Dennis40816/nvt_fw_core`.
The accepted design required a new scanner, so no existing scanner tests were available to port.

| Purpose | Ref | Full commit SHA | Source paths |
| --- | --- | --- | --- |
| Accepted design | `docs/nfh-pilot-plan` | `57d324c3526b5859b1181fea76c7e6060d3f26a8` | `docs/shared-ci/path-guard.md` |
| Existing composite-action and unittest distribution pattern | `feature/core/path-guard` (starting skeleton) | `68aa7b77611b3162ff153af1a918c1d1a043e47b` | `actions/approval-check/action.yml`, `tests/approval-check/test_approval_check.py` |

## Verification and adoption

Run the synthetic characterization suite and the unchanged approval suite from the Core checkout:

```powershell
python -B -m unittest discover -s tests/path-guard -v
python -B -m unittest discover -s tests/approval-check -v
```

The path-guard suite pins these behaviors:

- Supported path forms, relative paths and URLs.
- Escaped literals, decoded JSON strings and physical line numbers.
- Exact exceptions across CRLF/LF and line movement.
- Exclusions and missing scope/submodules.
- Gitlink content and local PowerShell/Python parity from changed working directories.

For zero-difference adoption, run this suite at the pinned revision.
Run the same `check.ps1` locally and through the existing CI job against the same tracked checkout, policy and gitlink SHAs.
Compare exit codes and the complete sorted `file:line:rule` diagnostics, including a changed local working directory.
Record NFH scan time, a failing case and its correction.
Keep existing product/privacy tests.

These adoption gates remain:

- A real NFH calibration PR.
- Enforcement after NFH 1.3.2.
- A merged Dependabot pin update.

Synthetic tests do not provide that evidence.
