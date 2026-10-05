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

The source is `1648c42` from NFU (`Dennis40816/nvt-event-buffer-replay`) `0.1.2`. The PR #1 description and the messages for commit `f4ce0d3` and tag `v0.1.0` incorrectly state `ceef4ef`; this paragraph is authoritative.

Compared with the original NFU version, the only difference in `approval_check.py` is the addition of `--policy <path>`. When it is not specified, it still reads `ROOT / ".github/approval-policy.json"`; all other evaluation logic, output messages, and the User-Agent remain unchanged. The action also first verifies that the resolved policy path is inside the base checkout, failing the check otherwise.
