# Shared CI/CD and governance framework

The first step for NVT Core (owner 2026-10-03). Goal: change a governance or CI rule once and have it take effect in all three repositories, without manually changing each one.

- [`inventory.md`](inventory.md): an inventory of the three repositories' current workflows, approval rules, rulesets, governance documents, and validation scripts, marking each item as identical across all three, already diverged, or specific to one repository
- [`approval-carryover.md`](approval-carryover.md): the draft of the first rule to share, 「改動不大時，已有的核准保留」 ("Keep existing approvals for small changes"), including several definitions of 「改動不大」 ("small change"), how to evaluate it, risks, and what needs to change to apply it to each repository
- [`proposal.md`](proposal.md): a comparison of and recommendations for sharing approaches, the adoption sequence, and decisions for the owner
- [`path-guard.md`](path-guard.md): a design draft for a shared path guard, local verification, exceptions, and the NFH-first roll-out

Written by session 「NVT CORE」 (2026-10-03). No repository files or settings were changed during the inventory and proposal stage.

## Current status (2026-10-05)

See [`proposal.md`](proposal.md) for the stage definitions.

| Stage | Status |
|---|---|
| 0 Shared repository and checker v0 | Complete (2026-10-03): `actions/approval-check` released as `v0.1.0`; rulesets, self-test CI, and grouped Dependabot applied to this repository |
| 1 NFH pilot | Not started; starts after NFH's CI fixes and 1.3.2 are complete (owner changed the pilot from NFU to NFH on 2026-10-05) |
| 2 Approval carryover rule | Not started, waiting for stage 1 |
| 3 NFU adoption | Not started |
| 4–5 | Not started |

None of the three tool repositories reference the shared action yet; 「改一次、三邊生效」 ("change once, take effect in all three") has not yet been achieved.
