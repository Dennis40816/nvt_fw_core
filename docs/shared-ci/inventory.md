# CI/CD and governance inventory for the three repositories

Inventory time: around 2026-10-03 07:00 (Taipei). Only Git refs and the public GitHub API were read; no repositories or settings were changed.

Since 2026-10-04, NFH's main development has moved to the public `Dennis40816/nvt-freeform-helper`, and the original private `FreeformHelper` will be archived later. The NFH portion of this document is the inventory from before the move.

## Read scope

| Abbreviation | Repo | GitHub visibility | Ref read | Notes |
|---|---|---|---|---|
| NFC | `Dennis40816/nvt_fw_combiner` | public | `origin/1.2.x` `8479dee8`; compared with `origin/main` `0cb57b90` | The only difference in governance files between the two is the addition of 5 CLI files to the R3 paths |
| NFH | `Dennis40816/FreeformHelper` | private (the API returns 404 when not logged in) | `origin/1.3.x` `99ad6c5e` | The governance documentation branch `s15005b-governance-docs` was merged into 1.3.x during the inventory |
| NFU | `Dennis40816/nvt-event-buffer-replay` | public | `feature/0.1.2/setup-batch` `b40be44` (local batch branch, not yet merged); rulesets read from the current GitHub settings | Approval checks are based on this branch, which includes the hardening after 0.1.2 |

- The GitHub account `Dennis40816` is a personal account (API `type: User`), not an organization.
- NFC's and NFU's rulesets are the actual settings read through the unauthenticated GitHub API. Viewing the bypass list requires admin permissions; it is marked 「未確認」 ("unconfirmed"). NFH is private, so its rulesets could not be read.
- NFC's and NFH's details were compiled by codex by reading Git refs in a read-only sandbox; NFU was read directly by NVT CORE. NFH is a private repository, so this document only provides summaries (what exists and what is missing), without details of private data or product rules. No builds, tests, or workflows were run, so this document cannot describe any checks as 「目前是綠燈」 ("currently green").

Classification labels:

- **Identical**: the same approach across all three, ready to serve as a shared rule.
- **Diverged**: the same name or purpose, but the content already differs.
- **Specific**: present in only one repository.
- **Missing**: not yet present in that repository, but its documents say it is planned.

## 1. Branches, merges, and GitHub identities

| Item | NFC | NFH | NFU | Classification |
|---|---|---|---|---|
| Role of `main` | Released versions only | Same as left | Same as left | Identical |
| Version trunk naming | `X.Y.x` (`1.2.x`) | `X.Y.x` (`1.3.x`) | Three-part numeric `X.Y.Z` (`0.1.2`, `0.2.0`) | Diverged |
| release branch | `X.Y.Z` frozen from trunk, deleted after merging into `main` | Not yet implemented (`S15.005e`) | None; trunk itself uses three-part numeric versions | Specific (NFC) |
| Working branches | `feature/<version>/<topic>` | Same as left | Same as left | Identical |
| Merge method | Only merge commits allowed (ruleset) | Documents require `gh pr merge --merge --match-head-commit` | Same as NFH; rulesets also only allow merge | Identical (NFH's GitHub settings unconfirmed) |
| Agent identity for writing to GitHub | GitHub App `nfc-agent-dennis40816[bot]` (id 334370883) | Documents say to use the existing App | The same App | Identical |
| App must not have `workflows` permission | `.github/AGENTS.md`: workflow changes require human review | Not stated explicitly | `CONTRIBUTING.md` explicitly states: the App must not have `workflows` or commit status write permissions | Diverged (actual App permissions unconfirmed) |

## 2. Workflows

| Item | NFC | NFH | NFU | Classification |
|---|---|---|---|---|
| Main CI file | `ci.yml`: `policy / polytail`, `python-worker / verify`, `python / repository policy (<shard>)`, `dotnet / build`, `dotnet / test (<shard>)`, aggregate `dotnet / build-test`, all on `windows-latest` | `ci.yml`: `policy / structure`, `dotnet / build`, four `dotnet / test (<shard>)`, aggregate `dotnet / build-test` | `ci.yml`: single job `build-and-test` (restore, build, test, 800-line check and its self-test, performance smoke) | Diverged (NFC and NFH both have a `dotnet / build-test` aggregate job; NFU does not) |
| CI push branches | `[main, 1.2.x]`, version hardcoded | `[main, '[0-9]+.[0-9]+.x']` | `[main, '[0-9]+.[0-9]+.[0-9]+']` | Diverged |
| Approval check workflow | `authority.yml` → check run `governance / authority` | None (`S15.005c`) | `approval.yml` → commit status `governance/approval-rule`; `approval-self-test.yml` runs checker unit tests | Diverged; missing in NFH |
| Release | `release.yml`: triggered after `ci` completes on `main`; candidate, eligibility, promote (`release` environment), published-smoke, plus three v0.9.16 parity jobs | None (`S15.005e`) | `release.yml`: manually triggered, `PREPARE_ONLY`/`PUBLISH`; candidate, promote (`release` environment), published-smoke | Diverged (the same pattern, with NFU a simplified version); missing in NFH |
| Rehearsal/preview package | `release-rehearsal.yml` | None | `preview-package.yml` (retained for 3 days) | Diverged |
| Third-party action pinning | All pinned to full SHAs | All pinned to full SHAs, checked by `verify.ps1 -StructureOnly` | All pinned to full SHAs, no automated check | Identical (automated checks diverged) |
| Pinned versions | `checkout@9c091bb…` (v7.0.0), `upload-artifact@043fb46…` (v7.0.1), `setup-python@ece7cb0…` (v6.3.0) | First two are the same | All three are the same, plus `setup-dotnet@a98b568…` (v6.0.0) | Identical |
| Default permissions | `contents: read`, elevated only for jobs that need it | Same as left | Same as left | Identical |
| .NET SDK installation | `scripts/install-dotnet.ps1`, installer pinned to a commit; wrapped in local composite action `setup-toolchain` | `scripts/ci/install-dotnet.ps1`, installer pinned to commit `cbd31355` | `actions/setup-dotnet` reads `global.json` | Diverged |
| Dependabot | NuGet, pip, GitHub Actions, weekly | None | None | Specific (NFC) |
| Private test data | Private Golden runner (details not read) | Has private test data, fetched in CI using a dedicated key (private repository, details omitted) | Private data stored in ignored `captures/` and `golden/` | Specific (each) |

## 3. Approval checks

| Item | NFC | NFH | NFU | Classification |
|---|---|---|---|---|
| Risk classification | R0–R3 plus roles (`firmware-owner`, `release-owner`, `governance-owner`, all currently Dennis40816) | Documents only have two levels: high risk requires owner approval on GitHub; everything else requires an independent review accept; R0–R3 is listed in `S15.005c` | Two levels: owner-gated (the entire PR requires owner approval) and review-gated | Diverged |
| Classification configuration file | `docs/governance/authority-policy.json` and schema, 18 entries, unclassified paths default to R3 | None | `.github/approval-policy.json`: list of path patterns, `tests/**` with only added files is not owner-gated, merging into `main` is always owner-gated | Diverged; missing in NFH |
| High-risk examples | Firmware bytes, Golden, write range, profiles, CRC worker, release, signing, the approval mechanism itself (R3) | `src/**`, `.github/**`, `scripts/**` that determine gates, build settings, release, agent permissions | `src/**`, build settings, changes to existing tests, `.github/**`, `scripts/**`, `eng/**`, ADRs, product specifications, agent settings, `CONTRIBUTING.md` | Diverged |
| CODEOWNERS | Projects only R3 patterns, all assigned to `@Dennis40816`; tests ensure consistent projection | None | Projects all owner-gated patterns, all assigned to `@Dennis40816`; header explains which rules CODEOWNERS cannot express and are handled by the script | Diverged; missing in NFH |
| Checker | `scripts/authority_check.py`: runs from **PR head**; applies both base and head policies, taking the stricter one; also checks submodules | None | `scripts/approval_check.py`: runs from the **base branch**, so the PR cannot change the checker or policy; fails if the PR changes during evaluation | Diverged |
| Check result format | check run (ruleset requires `governance / authority`) | — | commit status (ruleset requires `governance/approval-rule`). Rationale in `CONTRIBUTING.md`: check runs with the same name accumulate, and old failures do not disappear | Diverged |
| review record format | `nfc-review-record` JSON block in a `COMMENTED` review: `head`, `state: complete`, `openP0P1: 0`, `verdict: accept` or `accept-with-changes`, runtime relationship | Documents only require 「獨立審查 accept、無 P0／P1」 ("independent review accept, no P0/P1"), with no format | First review line `Review record: <完整 head SHA> <accept\|reject>`; anything resembling 「review record」 is treated as a record and blocked if malformed (to prevent hiding records using HTML or invisible characters) | Diverged |
| Who can post a review record | `nfc-agent-dennis40816[bot]`, `Dennis40816` | — | Same as NFC | Identical |
| Severity | No P0/P1 allowed | No P0/P1 allowed | P0–P3 definitions; accept requires 0 P0 and P1 findings | Identical |
| After a new commit | Any new SHA requires a new review record, even if the tree is identical; GitHub dismisses stale reviews | Documents require checking head before merging; no automated check | Any new push requires another review and a reposted record; the owner's approve must also match the current head | Identical (all bind to exact head; NFH is not yet automated) |
| Exception for 「部分變更不讓核准失效」 ("some changes do not invalidate approval") | None. R0 is 「普通的非規範文字」 ("ordinary non-normative prose") (`docs/handoff/**/*.md`); only local validation can take a shorter path, and review must still be repeated | None | None | Identical (none have it) |
| Reconfirmation by the merging agent before merging | Compare approval snapshot, live record, base authority, and check run | Check head and approval | Rerun the checker locally from a base checkout; must exit 0 | Diverged |

## 4. Rulesets (current GitHub settings)

NFC and NFU both use GitHub Actions (`integration_id` 15368) as the source of required checks. Their rulesets share the same structure, but the details have already diverged:

| Rule | NFC `main` | NFC trunk `*.*.x` | NFC release `*.*.*` (excluding `*.*.x`) | NFU `main` | NFU trunk `*.*.*` |
|---|---|---|---|---|---|
| Required approvals | 1 | 0 | 1 | 1 | 0 |
| dismiss stale reviews | Yes | Yes | Yes | Yes | Yes |
| code owner review | Yes | Yes | Yes | Yes | Yes |
| last push approval | Yes | No | Yes | Yes | No |
| Conversations must be resolved | Yes | Yes | Yes | Yes | Yes |
| `require_extra_approval_for_unattributed_changes` | Yes | Yes | Yes | Yes | Yes |
| Merge method | merge | merge | merge | merge | merge |
| Prohibit force pushes | Yes | Yes | Yes | Yes | Yes |
| Prohibit deletion | Yes | Yes | No (can be deleted after tagging) | Yes | Yes |
| required checks | `policy / polytail`, `python-worker / verify`, `dotnet / build-test` | Same as left plus `governance / authority` | Same as `main` | `build-and-test`, `governance/approval-rule` | Same as `main` |
| strict (must be up to date with base) | Yes | No | No | Yes | Yes |

- Both also have a tag ruleset 「Protect stable v* tags」: prohibits updating and deleting `refs/tags/v*`.
- Ruleset names: NFC's `main` and tag names are exactly the same as NFU's, while the trunk names differ (NFC `trunk`, `release branches`; NFU `Protect version trunks`).
- NFC's `main` does not yet require `governance / authority`; ADR 0080 says to add it when renaming the check in G2.
- **NFH: unconfirmed.** `S15.005d` lists rulesets as a to-do; the required checks specified in the documents are `policy / structure` and `dotnet / build-test`. NFH is a private repository, and using rulesets in a personal account's private repository requires GitHub Pro; the owner needs to confirm the plan.
- **Bypass lists: unconfirmed.** NFC's ADR 0080 says only the owner's Repository admin can bypass, not the App.
- **Rulesets as code: only NFC has this.** `docs/handoff/1.1.13/g0-scripts/rulesets/RS-*.json` plus an owner checklist (backup, apply, read back, restore). These JSON files do not fully match the current settings (for example, the owner has changed trunk's last push to false), so they cannot be treated as the current settings.

## 5. Governance documents

| Document | NFC | NFH | NFU | Classification |
|---|---|---|---|---|
| `AGENTS.md` | Root plus 10 or more subdirectory AGENTS files; `validate_repository.py` limits the root file to 16 KiB | One at the root | One at the root | Diverged |
| `CONTRIBUTING.md` | Yes | Yes (just added in 1.3.x) | Yes, with the most complete approval rules | Diverged |
| Branch and version governance | `docs/governance/branch-version-and-release-governance.md` | Same filename, different content | In `CONTRIBUTING.md` and `docs/release.md` | Diverged (same filename in NFC and NFH) |
| PR template | Required `nfc-authority` JSON block, Golden impact, validation | None | owner-gated/review-gated checkboxes, validation, review record format reminder | Diverged; missing in NFH |
| Issue templates | engineering-change, firmware-change | None (uses GitHub Issues plus 5 lifecycle labels) | None | Specific (NFC) |
| ADRs | 82 in `docs/adr/`; `0000-template.md`; `README.md` defines statuses | No ADR directory | 4 short ADRs, no template | Diverged |
| ADRs related to governance or CI | 0021 (code size, superseded by 0080), 0033 (protected CI release), 0060 (release package size limit), 0068 (derived file synchronization), 0079 (test architecture), 0080 (current R0–R3 governance) | — | 0004 (MVP and 800 lines per file) | Specific |
| `SECURITY.md` | Yes (title still says Draft) | None | None | Specific (NFC) |
| Handoff documents | `docs/handoff/` | `docs/handoff/` (version coordination board, workflow handoff, bug ledger) | Single file `docs/nvt-fw-util-claude-handoff.md` | Diverged |

## 6. Validation scripts

| Item | NFC | NFH | NFU | Classification |
|---|---|---|---|---|
| Validation entry point | `scripts/verify.py` (`verify.ps1` and `verify.sh` are wrappers), multiple lanes and CI shards | `scripts/verify.ps1`: `-StructureOnly`, `-CiLane build/test`, `-All` | `scripts/verify.ps1`: release identity, restore, build, test, line-count check, performance smoke | Diverged (same name) |
| Per-file size | 2,000 or more nonblank lines require registration and must not exceed the registered value; remove from the register when below 1,500; tests also block 2,500 or more lines | Measurement only (`measure-code-size.ps1`), no blocking | 800 lines per handwritten `.cs`/`.axaml` file, baseline limits for 6 legacy files, which must be lowered as the files shrink | Diverged |
| Coverage | `coverage_policy.py`: overall coverage must not fall below baseline; changed Domain/Application modules require line 85%, branch 80% | None | None | Specific (NFC) |
| Performance thresholds | Only measures startup time, no threshold | Has startup and regression performance budgets, outside CI | `performance-gate.ps1 -Mode Smoke` in CI | Diverged |
| Golden | R3; run `--release-golden` before release | Has golden and byte-by-byte output comparisons (private repository, details omitted) | Private golden data stays out of the repository | Specific (each) |
| Release package checks | closed allowlist, SHA256SUMS, SBOM; ZIP limit 128 MiB | None | closed allowlist, SHA256, `smoke-release.ps1` | Diverged |
| Derived file synchronization | `sync_derived.py`: only checks drift by default, `--write` requires specifying a provider; CI templates are byte-for-byte mirrors | None | None | Specific (NFC) |
| Formatting and line endings | Not read | CRLF normalization, `dotnet format --verify-no-changes`, XAML action role checks | Not read | Specific (NFH) |

## 7. Conclusions: the actual current state of the shared baseline

1. **Only the structure is shared.** What all three share are concepts: the `main`, trunk, and feature branch model; merge commits only; actions pinned to full SHAs; read-only default permissions; the same GitHub App; review records bound to exact head; accept only with zero P0/P1; ruleset dismiss stale and code owner review.
2. **Approval checks have already split into two implementations.** NFC (R0–R3, check runs, execution from PR head, JSON records) and NFU (two levels, commit statuses, execution from base, single-line records) differ in format, execution source, and result format. Changing the same rule requires writing code, adding tests, and updating documents separately in both.
3. **NFH is about to create a third copy.** Neither `S15.005c` (R0–R3 policy, checker script, CODEOWNERS, PR template, review record) nor `S15.005d` (rulesets) has been done yet. This is the point at which adopting the shared framework costs the least.
4. **Rulesets have already started to drift.** Trunk naming, strict, required check names, and whether the `governance` check applies to `main` all differ between NFC and NFU. Currently only NFC has ruleset JSON and an application procedure, and the JSON already lags behind the current settings.
5. **Repository-specific parts that should not be shared.** NFC's firmware R3 paths, parity, and release evidence; NFH's golden and output comparisons and private test data; NFU's 800-line rule and performance smoke. These remain in each repository, with the shared framework reading their respective settings.

## Facts for the owner to confirm on GitHub

These could not be read during the inventory; the owner is asked to confirm or read them back:

1. Whether NFH already has rulesets or branch protection; whether the `Dennis40816` account's plan supports rulesets in private repositories (requires GitHub Pro). Settings page: <https://github.com/Dennis40816/FreeformHelper/settings/rules>
2. The bypass lists for the three repositories' rulesets. NFC: <https://github.com/Dennis40816/nvt_fw_combiner/settings/rules>; NFU: <https://github.com/Dennis40816/nvt-event-buffer-replay/settings/rules>
3. Whether GitHub App `nfc-agent-dennis40816` actually has `workflows` permission. App settings page: <https://github.com/settings/apps>
4. Which actions and reusable workflows the three repositories' 「Actions permissions」 allow (affecting whether they can reference the shared repository). NFC: <https://github.com/Dennis40816/nvt_fw_combiner/settings/actions>; NFH: <https://github.com/Dennis40816/FreeformHelper/settings/actions>; NFU: <https://github.com/Dennis40816/nvt-event-buffer-replay/settings/actions>
