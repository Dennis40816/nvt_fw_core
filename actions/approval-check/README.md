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

The source is `1648c42` from NFU (`Dennis40816/nvt-event-buffer-replay`) `0.1.2`. The PR #1 description and the messages for commit `f4ce0d3` and tag `v0.1.0` incorrectly state `ceef4ef`; this paragraph is authoritative.

Compared with the original NFU version, the only difference in `approval_check.py` is the addition of `--policy <path>`. When it is not specified, it still reads `ROOT / ".github/approval-policy.json"`; all other evaluation logic, output messages, and the User-Agent remain unchanged. The action also first verifies that the resolved policy path is inside the base checkout, failing the check otherwise.
