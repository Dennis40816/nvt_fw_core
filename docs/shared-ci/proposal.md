# Proposal: change once, take effect in all three

Goal (owner 2026-10-03): change a governance or CI rule once and have it take effect in NFC, NFH, and NFU together, without manually changing each one. See [inventory.md](inventory.md) for the inventory and [approval-carryover.md](approval-carryover.md) for the example rule. Progress is in the [single status table](README.md#current-status-2026-10-05).

## Conclusion first

1. Put shared CI and governance in **`nvt_fw_core`, and make it public** (owner's decision at 2026-10-03 08:2x, called 「共用 repo」 ("shared repository") below). Reason: NFC and NFU are public, and GitHub does not allow public repositories to reference actions or reusable workflows from private repositories. Local paths and NFH's internal details were removed before making it public.
2. Package the **approval checker** as a composite action in the shared repository. Each repository references it using a full SHA, and Dependabot automatically opens upgrade PRs in all three repositories.
3. Write **rulesets** as JSON templates, with each repository providing only parameters (branch patterns, required check names). The owner-approved automation uses an admin GitHub App, with its private key stored only in the shared repository's protected environment and owner approval on GitHub required for every application. For the NFH pilot, the owner approved a UI import and deferred that automation at [2026-10-05 20:0x](#owner-decisions-2026-10-05-200x). The weekly read-only drift report remains deferred.
4. **Keep each repository's differences in its own policy file**: which paths are high risk, R3 roles, and the carryover allowlist. The evaluation logic is shared, not the path lists.
5. **Change the pilot to NFH** (owner 2026-10-05 01:2x; originally NFU): NFH's `S15.005c` adopts the shared checker instead of creating a third implementation (the owner has already had NFH pause these two items pending the proposal); start now with NFH policy, caller workflow, and path-guard calibration (owner 2026-10-05 20:0x, Taipei, relayed by commander). New pilot checks run without blocking merges; making them required waits until NFH 1.3.2 is done. First release the small compatibility change needed for NFH's two-tier policy; checker v0 cannot express it unchanged. Implement stage 2 in parallel now, disabled by default in a separate PR, then enable the first new rule, 「改動不大時，已有的核准保留」 ("Keep existing approvals for small changes"). NFU adopts after NFH, and NFC migrates last, during a gap between releases.

## Scope reduction (owner 2026-10-03 10:4x)

The owner asked to avoid overengineering and retain only the minimum needed to achieve 「改一次、三邊生效」 ("change once, take effect in all three"). 「改一次」 ("Change once") relies on the shared action, and 「三邊生效」 ("take effect in all three") relies on Dependabot's upgrade PRs; the following items are deferred, and this section takes precedence when other parts of this document mention them:

| Item | Handling |
|---|---|
| Drift report (weekly comparison of rulesets, referenced SHAs, and shared text sections) | Deferred; reconsider whether it is needed after all three repositories adopt the shared checker |
| Contract tests for the three repositories | Do not create a separate set; when a repository adopts it, add its policy and one or two fixtures to the shared tests in that PR |
| Script to generate CODEOWNERS from policy | Do not implement; continue maintaining CODEOWNERS manually as now |
| Synchronization bot for shared rule text | Do not implement for now; write rule text only in the shared action's README, and when each repository adopts it, manually update `CONTRIBUTING.md` once in the same PR and link to the README |
| Ruleset templates and the admin App's application workflow | Stage 1 needs only NFH's ruleset. The owner approved importing it through the GitHub UI first and deferring the admin App/workflow at 2026-10-05 20:0x. Retain the approved automation design for later. Leave NFC's and NFU's rulesets as they are for now |

## GitHub limitations that affect the design

| Limitation | Source | Impact |
|---|---|---|
| Public repositories can only reference reusable workflows from **public** repositories; private repositories can reference private or public ones | [Reusable workflows reference](https://docs.github.com/en/actions/reference/workflows-and-actions/reusable-workflows) | NFC and NFU are public, so the shared repository must be public |
| A private repository's actions and reusable workflows can only be shared with **other private repositories owned by the same user** (Settings → Actions → General → Access) | [Repository Actions settings](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository) | All three main development repositories, including NFH at `Dennis40816/nvt-freeform-helper`, are now public and need a public shared action |
| `Dennis40816` is a personal account, with no organization | GitHub API `users/Dennis40816` has `type: User` | There are no organization-level rulesets or `required workflows`; rulesets can only be applied to each repository separately, hence the need for templates and scripts |
| Using rulesets in a personal account's private repository requires GitHub Pro | No explicit provision found | Originally affected NFH's rulesets (`S15.005d`). NFH's main development moved to the public `Dennis40816/nvt-freeform-helper` on 2026-10-04, so this row no longer affects NFH |
| A GitHub App needs `workflows` permission to modify `.github/workflows/*`; NFU's rules explicitly prohibit the App from having this permission | NFU `CONTRIBUTING.md`; NFU's `setup-batch` branch was pushed by the owner over SSH because it included `ci.yml` (commander record) | Upgrade PRs that change workflow files cannot be opened by the agent's App. Dependabot can open them |
| When calling a reusable workflow, the caller workflow's `env` is not passed through; `GITHUB_TOKEN` permissions can only be reduced, not increased; up to 10 levels and at most 50 reusable workflows per file | Same as the first row | Shared workflow inputs must be passed explicitly using `with:` |
| When referencing by SHA, commits on a fork can also be retrieved by SHA through the original repository | Generally known GitHub fork network behavior | Pin only SHAs pointed to by protected tags in the shared repository; the drift report must check this |
| dismiss stale only revokes Approve when the diff changes or the merge base introduces new changes | [Available rules for rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets) | See approval-carryover.md |

## Comparison of four approaches

| | A. reusable workflow/composite action | B. Shared scripts as a package or submodule | C. Rulesets as code | D. Bot opens synchronization PRs automatically |
|---|---|---|---|---|
| What it does | Each repository's workflow writes `uses: <shared-repository>/...@<SHA>` | Each repository installs or mounts the shared scripts, with the version recorded in a file | JSON templates plus an application script | After the shared repository releases a version, automatically open PRs in each repository to copy files |
| Visibility | Shared repository must be public | Package or submodule must be public; otherwise CI needs a read token | Unaffected | Unaffected; shared repository can be private |
| How versions are pinned | Full SHA plus `# vX.Y.Z` comment | Lockfile or gitlink SHA | Template version plus per-repository parameter files | The copied files are that version |
| How updates reach all three | **Dependabot** (github-actions ecosystem) automatically opens one PR in each | Requires a custom bot or manual work | Owner runs the script once for each of the three repositories (the same command) | A custom bot opens PRs |
| Required permissions | Changing workflow files requires `workflows` permission, which Dependabot has and the agent's App does not | The App can open PRs if the lockfile is outside the workflows directory | Repository admin permissions, owner only | Copying into the workflows directory also requires `workflows` permission |
| Rollback | Click Revert on that repository's upgrade PR page | Revert the lockfile | Restore from the backup taken before application | Open another PR to reverse the change |
| Drawbacks | Each repository still has its own caller workflow (but it is thin and rarely changes) | Submodules complicate agent sandboxes and local development; custom bots need maintenance | Does not cover file contents | Each repository still has a copy, which can drift through manual edits; drift needs a separate check |
| Suitable for | Checkers, CI steps | Not recommended as the primary approach | Rulesets | Shared rule sections in documents |

### Recommendation: A+C, with manual rule-text updates

| Shared item | Approach | What each repository retains |
|---|---|---|
| Approval checker (review record evaluation, owner approval evaluation, accept carryover, anti-forgery rules) | Composite action in the shared repository, with the code pinned together with the action (under `github.action_path` at runtime). Each repository's `approval.yml` contains only triggers, permissions, and one `uses:` line | `approval-policy.json` (paths, classifications, roles, carryover allowlist), read from the **base branch** |
| CODEOWNERS | Maintain manually under the scope reduction above | The repository's owner-gated path patterns |
| Rulesets | The shared repository's `rulesets/` contains templates and per-repository parameter files. The agent triggers the application workflow using `workflow_dispatch`: it first produces the diff, and only uses the admin App to apply it after the owner approves the environment on GitHub; back up before application and read back afterward (following NFC G0's procedure). The agent cannot access the App's private key | Parameter files: branch patterns, required checks, strict |
| Shared rule text (review record format, carryover rules, pre-merge confirmation steps) | The shared action's README is the source of truth; manually update each repository's `CONTRIBUTING.md` once on adoption and link to it | Repository-specific policy and the link; no synchronization bot |
| Third-party action pinning | Add `dependabot.yml` to each repository (currently only NFC has it) | — |

What the repositories do not share and continue maintaining separately: build, test, shards, golden, C export, per-file line-count thresholds, coverage, performance thresholds, release package contents, firmware parity, and release evidence. These are tied to the products; wait until 「各專案有雛型後」 ("after each project has a prototype"), as the owner said, before reconsidering whether to extract them.

Shared CI rule (owner decision of 2026-10-06 01:1x, Taipei, relayed by commander): 「repo 共同鎖定」 ("Lock packages across repositories.") Every repository enables NuGet package lock files, and CI always restores with `dotnet restore --locked-mode`. Shared package versions align with the set pinned in Core's `Directory.Packages.props`, maintained by NVT CORE.

## Versioning, propagation, and rollback

1. The shared repository creates `vX.Y.Z` tags on `main`, using the same tag ruleset as the three repositories (prohibit updating and deleting `v*`).
2. Dependabot opens an upgrade PR in each repository, containing only the `uses:` SHA and version comment. This PR touches a high-risk path (`.github/**`), so it is reviewed under that repository's current rules and approved by the owner.
3. The upgrade order is fixed: NFH → NFU → NFC. Merge the next repository's PR only after the previous repository has merged and successfully run at least one real PR.
4. Rollback: in the affected repository, go to that upgrade PR's page and click Revert; the owner merges it, returning the SHA to the previous version. This affects only that repository.
5. If the shared repository has a defect, release a fix (`vX.Y.Z+1`) without moving existing tags.
6. Drift report remains deferred until all three adopt the checker. If later approved, compare rulesets and referenced SHAs read-only; NFH's pilot target is already public. No drift workflow or rule-text synchronization is needed for the pilot.

## Who approves governance changes

A shared rule change affects all three repositories, equivalent to the highest risk (NFC's R3 `governance-owner`).

- Shared repository: CODEOWNERS assigns all paths to the owner; rulesets require a PR, 1 approval, code owner review, dismiss stale, last push approval, and the existing checker self-test check covering:
  - Checker unit tests.
  - **Adoption fixtures in the existing checker tests**, not a separate contract suite: retain NFU's cases, add NFH's policy and minimal synthetic cases for stage 1, and add NFC's on migration. 「改一次」 ("Change once") is checked against the policies already supported before release.
- Each repository: the owner approves the upgrade PR again. Two approvals: the shared repository approves the rule itself, and each repository approves when to adopt it.
- Versions that change evaluation results (major version changes) must record the owner's decision in `nvt_fw_core`'s [decisions.md](../decisions.md).
- Agents can write PRs for the shared repository, but every merge requires owner approval; agents have no bypass in the shared repository.

## Adoption order

| Stage | Content | Where changes are made | What the owner needs to do |
|---|---|---|---|
| 0 | Create `nvt_fw_core` on GitHub as public. Checker v0 copies NFU's `approval_check.py` unchanged, packages it as a composite action, and includes NFU's tests and fixtures (PR #1) | Shared repository only | Approve PR; manually configure the shared repository's `main` and `v*` tag rulesets |
| 1 | **NFH pilot starts now**: release the narrow two-tier checker change, then add NFH's caller, policy, CODEOWNERS, Dependabot, and path-guard calibration. Run new pilot checks without blocking; require them only after NFH 1.3.2 is done. Complete the [pilot checklist](#stage-1-nfh-pilot-checklist) | Shared repository, then public NFH (`1.3.x`) | Approve both PRs; import the ruleset through the UI; review the agreed exit sample |
| 2 | **Pilot the first new rule: accept carryover**. Implement it now in parallel in the shared repository, disabled by default, in a separate PR (owner 2026-10-05 20:0x). Later enable it in one repository that has adopted the checker, and configure an allowlist. Run the complete flow once: change once in the shared repository → Dependabot opens a PR → takes effect in that repository. Ask the owner then whether to enable it in NFH or NFU first | Shared repository; that repository's policy and `CONTRIBUTING.md` | Approve two PRs |
| 3 | **NFU adoption, rules unchanged**: change `approval.yml` to `uses: Dennis40816/nvt_fw_core/actions/approval-check@<full SHA>`; keep the commit status name `governance/approval-rule`, so rulesets do not need changes; add `dependabot.yml` | Two NFU files | Approve; workflow files must be pushed by the owner or handled by Dependabot |
| 4 | **NFC migration**: checker v1 supports R0–R3 and roles (based on NFC's `authority_check.py`, with NFU's anti-forgery rules and execution from base added). Schedule it between two releases; revise ADR 0080 at the same time and confirm `release_promotion_policy.py`'s requirements for review evidence | NFC (all R3) | Approve; adjust required checks for trunk and `main` |
| 5 | Reconsider later: package CI steps such as .NET SDK installation, action pinning checks, and line-count checks as shared actions; package the release process as a reusable workflow | As needed | Wait until each project has a prototype |

NFU was originally selected for the pilot: the smallest checker (about 350 lines, standard library only), commit status results, public repository, and lowest product priority. Shared checker v0 is still copied from NFU. On 2026-10-05, the owner changed the pilot to NFH: NFU has the lowest development priority, so waiting for its pilot would take too long; NFH's main development has moved to a public repository and it is undergoing refactoring, while governance items `S15.005c`/`d` were already paused pending the shared proposal.

Why NFC is last: `authority_check.py` exceeds 1,100 lines and covers R3 firmware roles and release evidence; NFC is the highest-priority product, and all governance changes are R3, making it unsuitable as a pilot.

### Stage 1: NFH pilot checklist

Target: public [`Dennis40816/nvt-freeform-helper`](https://github.com/Dennis40816/nvt-freeform-helper), pilot target and default branch `1.3.x`; `main` holds released versions only. NFH's [PR #29](https://github.com/Dennis40816/nvt-freeform-helper/pull/29) is merged, and its updated `AGENTS.md` and `CONTRIBUTING.md`, read from the verified local clone's `origin/1.3.x`, state this branch policy. The owner confirmed it in chat at **2026-10-05 16:2x (Taipei)**. Start the pilot now, with new checks running without blocking merges; making them required waits until NFH 1.3.2 is done (owner 2026-10-05 20:0x, relayed by commander). Record CI-fix and 1.3.2 completion links before enforcement; neither is asserted complete here.

Sources read from the verified public-origin clone: [`CONTRIBUTING.md`](https://github.com/Dennis40816/nvt-freeform-helper/blob/1.3.x/CONTRIBUTING.md), [`AGENTS.md`](https://github.com/Dennis40816/nvt-freeform-helper/blob/1.3.x/AGENTS.md), [`docs/governance/development-workflow.md`](https://github.com/Dennis40816/nvt-freeform-helper/blob/1.3.x/docs/governance/development-workflow.md), [`docs/governance/branch-version-and-release-governance.md`](https://github.com/Dennis40816/nvt-freeform-helper/blob/1.3.x/docs/governance/branch-version-and-release-governance.md), and [`.github/workflows/ci.yml`](https://github.com/Dennis40816/nvt-freeform-helper/blob/1.3.x/.github/workflows/ci.yml). The older [inventory](inventory.md) is background, not evidence of current NFH settings.

#### 1. Resolve the v0 compatibility gap and release it separately

NFH's current documents specify two tiers: changes to `src/**`, `.github/**`, `scripts/**`, build settings, release files, or agent permissions require the owner's GitHub approval for the whole PR; independent review is recommended for that tier. Other documentation/test changes need independent review acceptance, no unresolved P0/P1 findings, and green required checks. Unresolved classification goes to the owner.

The [v0 checker](../../actions/approval-check/approval_check.py) and [NFU policy fixture](../../tests/approval-check/fixtures/nfu/approval/.github/approval-policy.json) cannot express that unchanged: v0 requires an accepted review record even on owner-gated PRs, owner-gates non-added test changes, and requires a single owner-gated base branch. NFH does not document the latter two blanket gates. Setting that branch to `1.3.x` would incorrectly owner-gate every pilot PR.

Make one small shared-checker PR before the NFH adoption PR: allow NFH to leave the existing branch gate and non-added-test gate unset, and require the review record only for its review-gated tier. Preserve the existing defaults and all NFU behavior. Reuse path matching, owner identity, record parsing, base-policy loading, current-head approval, and base/head freshness checks. Do not add R0–R3, roles, a new record format, or carryover. For NFH's owner tier, independent review remains a recommendation rather than an additional automatic requirement.

Add NFH's policy and minimal synthetic cases to the existing shared test suite: ordinary documentation and modified tests need a current-head accept; a mixed/high-risk PR needs current-head owner approval; missing/wrong-head evidence fails; NFU's existing cases remain unchanged. Publish this compatibility change under its own protected tag, **proposed `v0.2.0`**, without moving `v0.1.0`. NFH pins the full commit SHA of that release.

#### 2. Add the NFH caller and repository policy

- Add `.github/workflows/approval.yml` from the [NFU caller example](../../examples/nfu-approval.yml), calling `Dennis40816/nvt_fw_core/actions/approval-check@<full SHA>` with a version comment. Keep the example's PR and review events, per-PR/head concurrency, five-minute timeout, and `contents: read`, `pull-requests: read`, `statuses: write` permissions. Use the [action's](../../actions/approval-check/action.yml) default `.github/approval-policy.json` and `governance/approval-rule` inputs. The action loads policy from the base checkout and runs its own pinned checker; the caller does not execute PR code with the status-write token.
- Add `.github/approval-policy.json` with NFH's two-tier paths, owner, and authorized review-record authors. Include the documented build files (`.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`); enumerate the actual release and agent-permission files before approval. Keep governance documents owner-gated while their classification is unresolved, as decided at 20:0x. Retain NFH's documented pre-edit owner confirmation in chat for `AGENTS.md` and `CONTRIBUTING.md`. Do not copy NFU's branch/test gates into NFH.
- Update NFH's `CONTRIBUTING.md` once to link to the shared action README and explain the retained NFU record syntax, `Review record: <full head SHA> accept|reject`, for its ordinary tier. An accept attests to an independent review with no unresolved P0/P1. The checker validates authorized authors, syntax, verdict, and head; it does not independently prove reviewer independence or inspect findings for severity. Use the evidence mapping approved at 20:0x; enumerate eligible authors in the adoption review.
- Add manually maintained `.github/CODEOWNERS` for the owner-gated paths, including the caller and policy. Compare it with the policy in the adoption review; no generator or new consistency framework. Add weekly, grouped `github-actions` Dependabot updates in `.github/dependabot.yml` for the workflow pins, following this repository's existing configuration.

#### 3. Run the status now; require it after 1.3.2 and apply only NFH's ruleset

Keep NFH's documented required checks **`policy / structure`** and **`dotnet / build-test`**. Run the commit status **`governance/approval-rule`** without blocking merges during the pilot. After NFH 1.3.2 is done, require it with GitHub Actions as its expected source, alongside them; it does not replace either check, and the workflow job name `governance / approval` is not the required status context. Read back the actual default branch, checks, and branch coverage after the CI fix before applying required enforcement. Bootstrap the caller and policy onto `1.3.x` under the current rules with owner review, then verify status publication on a fresh PR and review event. Require the new status only after both this verification and NFH 1.3.2 are complete, so adoption does not wait for a nonexistent check.

Use only NFH's needed ruleset definition, targeting `1.3.x` and any other branches the owner confirms are already protected. Do not impose a blanket owner approval on ordinary-tier PRs: reconcile approval counts and CODEOWNERS requirements with the two-tier policy. Preserve existing protections; record a backup, proposed diff, owner approval, and read-back of branch targets, required checks, and bypass actors. Do not assume local workflow files prove live GitHub settings.

For this pilot, the owner imports the NFH ruleset JSON through GitHub's UI, with the same backup and read-back evidence (owner 2026-10-05 20:0x). Defer the App/application workflow. Later automation retains the approved separate admin App whose private key exists only in a protected environment in the shared repository; the owner approves **every application run** on GitHub.

#### 4. Evidence required to call the pilot done

- CI-fix and 1.3.2 completion links, the compatibility tag/SHA, the NFH adoption PR, and the applied ruleset read-back are recorded.
- **Owner-approved sample: N = 3 real NFH PRs**: an ordinary docs/test PR, a high-risk or mixed PR, and a Dependabot shared-action bump. The evidence shows the ordinary PR failing without a valid current-head accept then passing; the high-risk PR failing without current-head owner approval then passing; and a new push invalidating old evidence. Synthetic tests alone do not close the pilot. Existing required checks must also pass before each merge.
- At least one **Dependabot bump of the shared approval action is merged in NFH**, with the protected release tag and new full SHA verified, owner approval, and a subsequent real PR evaluated successfully at that SHA. Verify that the caller actually publishes `governance/approval-rule` on the Dependabot PR; requesting `statuses: write` alone does not prove that event's token can write. The post-bump PR may be another member of the sample; add one if needed. Dependabot's first setup PR is not a version bump.
- Record observed results for both tiers and the status context/source, plus a rollback result or reviewed procedure to revert the pin. NFU adoption waits for this evidence; NFC remains last. Carryover implementation runs in parallel in stage 2, disabled by default.

### Owner decisions (2026-10-05 20:0x)

The owner chose 「全部照建議」 ("Follow all recommendations"), relayed by commander; time is Taipei time.

- **NFH policy details:** use the existing NFU record syntax for ordinary-tier acceptance, owner approval sufficient for the high-risk tier, no NFU-only branch/test gates, and owner-gated governance documents while their classification is unresolved; enumerate exact release/agent-permission paths and authorized reviewers in the adoption review.
- **Ruleset application:** use an owner UI import for the pilot and defer the admin App workflow; retain the approved App/key/environment/per-run approval design for later automation.
- **Pilot sample:** use N = 3 real PRs with the tier/failure coverage and merged Dependabot bump above, adding a post-bump PR if the three do not include one.

### Source limits and assumptions

No requested source was missing. Live GitHub rulesets, bypass actors, allowed-action settings, prerequisite completion, and Dependabot delivery were not established by these local reads; verify them at the checklist steps above. The updated NFH `AGENTS.md` and `CONTRIBUTING.md` agree on `1.3.x` as default and `main` for released versions only; live branch settings still need read-back. Exact release/agent-permission path lists and authorized reviewers need enumeration in the adoption review; the pilot evidence mapping is now an owner decision.

## Owner decisions

At 2026-10-03 08:2x, the owner answered questions in the NVT CORE interface (question 6 was answered at 07:2x in the commander interface and relayed by commander). The original options and comparisons remain in this document's history.

| # | Question | Owner's choice |
|---|---|---|
| 1 | Which approval to retain for a small change | Retain only the independent review's accept; the owner's GitHub Approve continues to expire when the diff changes, with no ruleset changes |
| 2 | Definition of 「改動不大」 ("small change") | Paths plus content: allowlisted `.md`/`.txt`, at most 5 files and 40 lines, with no changes to code fences, URLs, HTML, or invisible characters |
| 3 | Where to put shared CI | Make `nvt_fw_core` public and put it there together |
| 4 | Sharing approach | A mix: composite action plus Dependabot for the checker, templates for rulesets, synchronization PRs plus drift checks for shared rule text |
| 5 | Pilot | Originally NFU. **Changed to NFH at 2026-10-05 01:2x (Taipei)**; timing superseded at **20:0x**: start now, new checks do not block until NFH 1.3.2 is done. NFU follows and NFC is last (relayed by commander). The exit sample is approved at 20:0x; the compatibility release version remains proposed |
| 6 | NFH's `S15.005c`/`S15.005d` | Pause for now pending the proposal; adopt directly once finalized (now the NFH pilot in stage 1) |
| 7 | Approval for governance changes | Two approvals: the owner approves every PR in the shared repository, then approves each repository's upgrade PR |
| 8 | Who applies rulesets | Create a separate admin App; store its private key only in the shared repository's protected environment, with the owner approving each application on GitHub. The owner approved a UI import for the NFH pilot at 2026-10-05 20:0x, deferring App/workflow creation while retaining this design for later automation |
| 9 | review record and check result formats | Standardize on one structured record when NFC migrates, with results as commit statuses |
| 10 | Trunk naming | Keep existing branches; standardize later (the standard form is undecided; NFC and NFH use `X.Y.x`) |

The [inventory](inventory.md#facts-for-the-owner-to-confirm-on-github) also identifies settings to read back: rulesets, bypass lists, the agent App's `workflows` permission, and allowed actions. Its NFH private-repository links are historical; use the public NFH repository's [rules](https://github.com/Dennis40816/nvt-freeform-helper/settings/rules) and [Actions settings](https://github.com/Dennis40816/nvt-freeform-helper/settings/actions). The private-repository account-plan question no longer gates this pilot.

## Out of scope for this planning update

- No implementation, workflow, policy, CODEOWNERS, ruleset, or other-repository changes in this documentation PR.
- No new risk/role framework, carryover implementation, CODEOWNERS generator, synchronization bot, drift report, product tests, or private data. The [path guard](path-guard.md) is a separate design; its calibration starts now alongside the approval pilot.
- NFU adoption, NFC migration, and release automation remain later work. End-to-end Dependabot propagation is an NFH pilot exit check, not an assumed stage-0 result.
