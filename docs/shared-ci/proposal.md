# Proposal: change once, take effect in all three

Goal (owner 2026-10-03): change a governance or CI rule once and have it take effect in NFC, NFH, and NFU together, without manually changing each one. See [inventory.md](inventory.md) for the current state; see [approval-carryover.md](approval-carryover.md) for the example rule.

## Conclusion first

1. Put shared CI and governance in **`nvt_fw_core`, and make it public** (owner's decision at 2026-10-03 08:2x, called 「共用 repo」 ("shared repository") below). Reason: NFC and NFU are public, and GitHub does not allow public repositories to reference actions or reusable workflows from private repositories. Local paths and NFH's internal details were removed before making it public.
2. Package the **approval checker** as a composite action in the shared repository. Each repository references it using a full SHA, and Dependabot automatically opens upgrade PRs in all three repositories.
3. Write **rulesets** as JSON templates, with each repository providing only parameters (branch patterns, required check names). An admin GitHub App applies them, with its private key stored only in the shared repository's protected environment and owner approval on GitHub required for every application; the shared repository produces a weekly read-only drift report.
4. **Keep each repository's differences in its own policy file**: which paths are high risk, R3 roles, and the carryover allowlist. The evaluation logic is shared, not the path lists.
5. **Change the pilot to NFH** (owner 2026-10-05 01:2x; originally NFU): NFH's `S15.005c` adopts the shared checker directly instead of creating a third implementation (the owner has already had NFH pause these two items pending the proposal); start after NFH's CI fixes and 1.3.2 are complete. Then introduce the first new rule, 「改動不大時，已有的核准保留」 ("Keep existing approvals for small changes"). NFU adopts it after NFH, and NFC migrates last, during a gap between releases.

## Scope reduction (owner 2026-10-03 10:4x)

The owner asked to avoid overengineering and retain only the minimum needed to achieve 「改一次、三邊生效」 ("change once, take effect in all three"). 「改一次」 ("Change once") relies on the shared action, and 「三邊生效」 ("take effect in all three") relies on Dependabot's upgrade PRs; the following items are deferred, and this section takes precedence when other parts of this document mention them:

| Item | Handling |
|---|---|
| Drift report (weekly comparison of rulesets, referenced SHAs, and shared text sections) | Deferred; reconsider whether it is needed after all three repositories adopt the shared checker |
| Contract tests for the three repositories | Do not create a separate set; when a repository adopts it, add its policy and one or two fixtures to the shared tests in that PR |
| Script to generate CODEOWNERS from policy | Do not implement; continue maintaining CODEOWNERS manually as now |
| Synchronization bot for shared rule text | Do not implement for now; write rule text only in the shared action's README, and when each repository adopts it, manually update `CONTRIBUTING.md` once in the same PR and link to the README |
| Ruleset templates and the admin App's application workflow | Defer until the NFH pilot (stage 1), creating only the one NFH needs; create the admin App then. Leave NFC's and NFU's rulesets as they are for now |

## GitHub limitations that affect the design

| Limitation | Source | Impact |
|---|---|---|
| Public repositories can only reference reusable workflows from **public** repositories; private repositories can reference private or public ones | [Reusable workflows reference](https://docs.github.com/en/actions/reference/workflows-and-actions/reusable-workflows) | NFC and NFU are public, so the shared repository must be public |
| A private repository's actions and reusable workflows can only be shared with **other private repositories owned by the same user** (Settings → Actions → General → Access) | [Repository Actions settings](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository) | If `nvt_fw_core` stays private, only NFH (private) can use it; FreeformHelper plans to launch a public version, which would also be unable to use it |
| `Dennis40816` is a personal account, with no organization | GitHub API `users/Dennis40816` has `type: User` | There are no organization-level rulesets or 「required workflows」; rulesets can only be applied to each repository separately, hence the need for templates and scripts |
| Using rulesets in a personal account's private repository requires GitHub Pro | No explicit provision found | Originally affected NFH's rulesets (`S15.005d`). NFH's main development moved to the public `Dennis40816/nvt-freeform-helper` on 2026-10-04, so this row no longer affects NFH |
| A GitHub App needs `workflows` permission to modify `.github/workflows/*`; NFU's rules explicitly prohibit the App from having this permission | NFU `CONTRIBUTING.md`; NFU's `setup-batch` branch was pushed by the owner over SSH because it included `ci.yml` (commander record) | Upgrade PRs that change workflow files cannot be opened by the agent's App. Dependabot can open them |
| When calling a reusable workflow, the caller workflow's `env` is not passed through; `GITHUB_TOKEN` permissions can only be reduced, not increased; up to 10 levels and at most 50 reusable workflows per file | Same as the first row | Shared workflow inputs must be passed explicitly using `with:` |
| When referencing by SHA, commits on a fork can also be retrieved by SHA through the original repository | Generally known GitHub fork network behavior | Pin only SHAs pointed to by protected tags in the shared repository; the drift report must check this |
| dismiss stale only revokes Approve when the diff changes or the merge base introduces new changes | [Available rules for rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets) | See approval-carryover.md |

## Comparison of four approaches

| | A. reusable workflow/composite action | B. Shared scripts as a package or submodule | C. Rulesets as code | D. Bot opens synchronization PRs automatically |
|---|---|---|---|---|
| What it does | Each repository's workflow writes `uses: <共用 repo>/...@<SHA>` | Each repository installs or mounts the shared scripts, with the version recorded in a file | JSON templates plus an application script | After the shared repository releases a version, automatically open PRs in each repository to copy files |
| Visibility | Shared repository must be public | Package or submodule must be public; otherwise CI needs a read token | Unaffected | Unaffected; shared repository can be private |
| How versions are pinned | Full SHA plus `# vX.Y.Z` comment | Lockfile or gitlink SHA | Template version plus per-repository parameter files | The copied files are that version |
| How updates reach all three | **Dependabot** (github-actions ecosystem) automatically opens one PR in each | Requires a custom bot or manual work | Owner runs the script once for each of the three repositories (the same command) | A custom bot opens PRs |
| Required permissions | Changing workflow files requires `workflows` permission, which Dependabot has and the agent's App does not | The App can open PRs if the lockfile is outside the workflows directory | Repository admin permissions, owner only | Copying into the workflows directory also requires `workflows` permission |
| Rollback | Click Revert on that repository's upgrade PR page | Revert the lockfile | Restore from the backup taken before application | Open another PR to reverse the change |
| Drawbacks | Each repository still has its own caller workflow (but it is thin and rarely changes) | Submodules complicate agent sandboxes and local development; custom bots need maintenance | Does not cover file contents | Each repository still has a copy, which can drift through manual edits; drift needs a separate check |
| Suitable for | Checkers, CI steps | Not recommended as the primary approach | Rulesets | Shared rule sections in documents |

### Recommendation: a mix of A+C+D

| Shared item | Approach | What each repository retains |
|---|---|---|
| Approval checker (review record evaluation, owner approval evaluation, accept carryover, anti-forgery rules) | Composite action in the shared repository, with the code pinned together with the action (under `github.action_path` at runtime). Each repository's `approval.yml` contains only triggers, permissions, and one `uses:` line | `approval-policy.json` (paths, classifications, roles, carryover allowlist), read from the **base branch** |
| CODEOWNERS | A shared script generates it from policy, and each repository's CI checks consistency (NFC already has this kind of test) | The generated file |
| Rulesets | The shared repository's `rulesets/` contains templates and per-repository parameter files. The agent triggers the application workflow using `workflow_dispatch`: it first produces the diff, and only uses the admin App to apply it after the owner approves the environment on GitHub; back up before application and read back afterward (following NFC G0's procedure). The agent cannot access the App's private key | Parameter files: branch patterns, required checks, strict |
| Shared rule text (review record format, carryover rules, pre-merge confirmation steps) | The shared repository's documents are the source of truth; a synchronization script uses the App to open PRs in each repository, copying it into marked sections of `CONTRIBUTING.md`; the drift report checks it (NFC's `sync_derived.py` pattern) | Content outside the marked sections |
| Third-party action pinning | Add `dependabot.yml` to each repository (currently only NFC has it) | — |

What the repositories do not share and continue maintaining separately: build, test, shards, golden, C export, per-file line-count thresholds, coverage, performance thresholds, release package contents, firmware parity, and release evidence. These are tied to the products; wait until 「各專案有雛型後」 ("after each project has a prototype"), as the owner said, before reconsidering whether to extract them.

## Versioning, propagation, and rollback

1. The shared repository creates `vX.Y.Z` tags on `main`, using the same tag ruleset as the three repositories (prohibit updating and deleting `v*`).
2. Dependabot opens an upgrade PR in each repository, containing only the `uses:` SHA and version comment. This PR touches a high-risk path (`.github/**`), so it is reviewed under that repository's current rules and approved by the owner.
3. The upgrade order is fixed: NFH → NFU → NFC. Merge the next repository's PR only after the previous repository has merged and successfully run at least one real PR.
4. Rollback: in the affected repository, go to that upgrade PR's page and click Revert; the owner merges it, returning the SHA to the previous version. This affects only that repository.
5. If the shared repository has a defect, release a fix (`vX.Y.Z+1`) without moving existing tags.
6. Drift report (scheduled workflow in the shared repository, weekly, read-only): compare the three repositories' rulesets with the templates, check whether the referenced SHAs correspond to protected tags, and check whether the shared sections of `CONTRIBUTING.md` match the source of truth. If differences are found, open an issue in the shared repository without changing any repositories. NFH is private, so reading its rulesets requires a read-only token; it can be left out until FreeformHelper becomes public.

## Who approves governance changes

A shared rule change affects all three repositories, equivalent to the highest risk (NFC's R3 `governance-owner`).

- Shared repository: CODEOWNERS assigns all paths to the owner; rulesets require a PR, 1 approval, code owner review, dismiss stale, last push approval, and two sets of required checks:
  - Checker unit tests.
  - **Contract tests for the three repositories**: use each repository's policy and real-PR fixtures to confirm that the new version's evaluations match expectations for all three. 「改一次」 ("Change once") is validated against all three before release.
- Each repository: the owner approves the upgrade PR again. Two approvals: the shared repository approves the rule itself, and each repository approves when to adopt it.
- Versions that change evaluation results (major version changes) must record the owner's decision in `nvt_fw_core`'s [decisions.md](../decisions.md).
- Agents can write PRs for the shared repository, but every merge requires owner approval; agents have no bypass in the shared repository.

## Adoption order

| Stage | Content | Where changes are made | What the owner needs to do |
|---|---|---|---|
| 0 | Create `nvt_fw_core` on GitHub as public. Checker v0 copies NFU's `approval_check.py` unchanged, packages it as a composite action, and includes NFU's tests and fixtures (PR #1) | Shared repository only | Approve PR; manually configure the shared repository's `main` and `v*` tag rulesets |
| 1 | **NFH pilot** (starts after NFH's CI fixes and 1.3.2 are complete): `S15.005c` uses the shared action plus NFH's own policy (initially two levels, consistent with current documents), with CODEOWNERS maintained manually; `S15.005d` rulesets use templates, applied by the admin App (templates and the application workflow are only created at this stage) | NFH (public) | Approve; create admin App; approve application on GitHub |
| 2 | **Pilot the first new rule: accept carryover**. Implement it in the shared repository (disabled by default), enable it in one repository that has adopted the checker, and configure an allowlist. Run the complete flow once: change once in the shared repository → Dependabot opens a PR → takes effect in that repository. Ask the owner then whether to enable it in NFH or NFU first | Shared repository; that repository's policy and `CONTRIBUTING.md` | Approve two PRs |
| 3 | **NFU adoption, rules unchanged**: change `approval.yml` to `uses: <共用 repo>/approval-check@<SHA>`; keep the commit status name `governance/approval-rule`, so rulesets do not need changes; add `dependabot.yml` | Two NFU files | Approve; workflow files must be pushed by the owner or handled by Dependabot |
| 4 | **NFC migration**: checker v1 supports R0–R3 and roles (based on NFC's `authority_check.py`, with NFU's anti-forgery rules and execution from base added). Schedule it between two releases; revise ADR 0080 at the same time and confirm `release_promotion_policy.py`'s requirements for review evidence | NFC (all R3) | Approve; adjust required checks for trunk and `main` |
| 5 | Reconsider later: package CI steps such as .NET SDK installation, action pinning checks, and line-count checks as shared actions; package the release process as a reusable workflow | As needed | Wait until each project has a prototype |

NFU was originally selected for the pilot: the smallest checker (about 350 lines, standard library only), commit status results, public repository, and lowest product priority. Shared checker v0 is still copied from NFU. On 2026-10-05, the owner changed the pilot to NFH: NFU has the lowest development priority, so waiting for its pilot would take too long; NFH's main development has moved to a public repository and it is undergoing refactoring, while governance items `S15.005c`/`d` were already paused pending the shared proposal.

Why NFC is last: `authority_check.py` exceeds 1,100 lines and covers R3 firmware roles and release evidence; NFC is the highest-priority product, and all governance changes are R3, making it unsuitable as a pilot.

## Owner decisions

At 2026-10-03 08:2x, the owner answered questions in the NVT CORE interface (question 6 was answered at 07:2x in the commander interface and relayed by commander). The original options and comparisons remain in this document's history.

| # | Question | Owner's choice |
|---|---|---|
| 1 | Which approval to retain for a small change | Retain only the independent review's accept; the owner's GitHub Approve continues to expire when the diff changes, with no ruleset changes |
| 2 | Definition of 「改動不大」 ("small change") | Paths plus content: allowlisted `.md`/`.txt`, at most 5 files and 40 lines, with no changes to code fences, URLs, HTML, or invisible characters |
| 3 | Where to put shared CI | Make `nvt_fw_core` public and put it there together |
| 4 | Sharing approach | A mix: composite action plus Dependabot for the checker, templates for rulesets, synchronization PRs plus drift checks for shared rule text |
| 5 | Pilot | NFU: first move the checker without changing behavior, then introduce the approval carryover rule. **Changed to NFH at 2026-10-05 01:2x**, starting after NFH's CI fixes and 1.3.2 are complete (answered in the commander interface and relayed by commander) |
| 6 | NFH's `S15.005c`/`S15.005d` | Pause for now pending the proposal; adopt directly once finalized (now the NFH pilot in stage 1) |
| 7 | Approval for governance changes | Two approvals: the owner approves every PR in the shared repository, then approves each repository's upgrade PR |
| 8 | Who applies rulesets | Create a separate admin App; store its private key only in the shared repository's protected environment, with the owner approving each application on GitHub |
| 9 | review record and check result formats | Standardize on one structured record when NFC migrates, with results as commit statuses |
| 10 | Trunk naming | Keep existing branches; standardize later (the standard form is undecided; NFC and NFH use `X.Y.x`) |

There are also 4 facts for the owner to confirm on GitHub (NFH's rulesets and account plan, bypass lists, the agent App's `workflows` permission, and each repository's allowed actions), with links at the end of [inventory.md](inventory.md).

## What this step did not do

- Did not create a shared repository, change any files, rulesets, or workflows in the three repositories, open PRs, or push.
- Whether Dependabot updates SHAs for composite actions and reusable workflows, and the plan requirements for using rulesets in a GitHub personal account's private repository, have not yet been tested or confirmed by explicit provisions; validate them in stage 0.
