# Draft rule: keep existing approvals for small changes

## Source

Owner's original words (2026-10-03 06:4x):

> 例如 NFU 提出改動非重要的文字說明 accept 保持有效看起來不錯 但如果這種修改每次要透過人工平展效率低 ("For example, NFU proposed keeping accept valid when nonessential explanatory text changes, which looks good, but manually propagating this kind of change every time is inefficient.")

Owner's clarification (2026-10-03 07:2x, in the commander interface, relayed by commander):

> 應該說他之前有題更改不大 approve 會保留? ("More precisely, did they previously mention that approve would be retained for a small change?")

So this rule comes from a concept the owner remembered: **keep existing approvals for small changes**. Its scope is not limited to explanatory text, and which session proposed it is also uncertain. **The owner decides the definition of 「改動不大」 ("small change")**; the draft below is only a starting point proposed by NVT CORE.

## Two types of approval

A PR has two types of 「核准」 ("approval"), and whether to retain them can be decided separately:

| | What it is | When it currently becomes invalid |
|---|---|---|
| review record's accept | A record posted by an independent review session (`Review record: <SHA> accept`, `verdict: accept`) | The checker requires the record to point to the current head, so any new push invalidates it |
| Owner's GitHub Approve | The Approve the owner clicks on GitHub, required only for high-risk PRs | The ruleset's dismiss stale: becomes invalid when the PR's diff changes |

The draft recommends **retaining only accept**, without changing rulesets. See "Why not retain the owner's Approve" for the rationale.

## How to define 「改動不大」 ("small change")

| Definition | Benefit | Risk |
|---|---|---|
| A. Paths only: differences are limited to allowlisted documents | Simple rule, easy to evaluate | Substantive changes can still be hidden in a document |
| B. Paths plus content (used by this draft): allowlisted `.md`/`.txt`, with a line-count limit and no changes to code fences, URLs, HTML, or invisible characters | Blocks known ways of hiding changes | Legitimate large documentation changes still require another review |
| C. Size only: any path, with differences no greater than N lines | Also covers small code fixes | One line of code can change firmware bytes or approval rules; NFC's R3 and NFU's owner-gated paths cannot use this definition |

## Current state: any new head requires another review

| | NFC | NFU | NFH |
|---|---|---|---|
| review record binding | Both `commit_id` and `head` in the block must equal the current head (`authority_check.py`) | The SHA in the first line must equal the current head (`record_result` in `approval_check.py`) | No checker yet |
| Owner's Approve | Ruleset dismiss stale: becomes invalid when the diff changes | Same as left; the checker additionally requires `commit_id` to equal the current head | Unconfirmed |
| Document provisions | ADR 0080: any new SHA requires a new review record, even if the tree is identical | `CONTRIBUTING.md`: After any later push, review again and post a new record | `CONTRIBUTING.md`: check the current head before merging |

GitHub's dismiss stale revokes Approve only when 「PR 的 diff 改變」 ("the PR's diff changes") or 「merge base 帶進新變更」 ("the merge base introduces new changes"), not on every push.

## Draft rule (definition B)

### Terms

- **A**: the head pointed to by the latest accept record (the reviewed head).
- **H**: the PR's current head.
- **Net changes**: which files the PR changed relative to the current base, and what each file's content was changed to.

### Evaluation steps

1. Find the latest review record (using the same method as now; malformed records, reject, and dismissed are still blocked). Enter carryover evaluation only if it is accept, points to A, and A ≠ H.
2. Use the GitHub compare API to fetch two sets of net changes: `compare/<目前 base>...A` and `compare/<目前 base>...H`. Compare status, blob SHA, and old filename for each file; files with all three unchanged are treated as unchanged. The remaining files form the **difference set D**.
   - This comparison covers both 「在 A 後面追加 commit」 ("appending commits after A") and 「rebase 或併入新的 base」 ("rebasing or merging in a new base").
   - Files changed in both base and the PR appear in D and are evaluated in the next step; if they fail evaluation, another review is required (conservative).
   - Never carry over if the compare result is truncated (more than 300 files) or A is no longer available.
3. Accept carries over to H only if **every** file in D meets the following conditions:
   1. The path matches that repository's configured carryover allowlist and does not match any high-risk pattern (NFU's owner-gated, NFC's R1 and above, NFH's high-risk scope). Both the allowlist and the high-risk patterns are read from the **base copy** of the policy, so the PR cannot relax them itself.
   2. Status is modified or added; no carryover for deleted, renamed, or copied files.
   3. File mode is unchanged, and the file is not a symlink, submodule, or executable.
   4. The extension is `.md` or `.txt`.
   5. Added or deleted lines are outside code fences, do not add or modify URLs, HTML tags, or HTML comments, and contain no invisible formatting characters (Unicode category `Cf`, such as zero-width or bidirectional control characters).
   6. D's total size does not exceed the limit: by default, 5 files and 40 lines (additions plus deletions).
4. When carryover applies, the check result states 「accept 沿用自 A」 ("accept carried over from A") and lists D's files and line count so the owner and merging agent can see them.
5. Always compare H with **the last A that was actually reviewed**, without accumulating one carryover after another.

### Per-repository allowlists (defaults, to be confirmed by each repository)

| Repo | Paths eligible for carryover | Must exclude |
|---|---|---|
| NFC | Paths with floor R0 in the policy (currently only `docs/handoff/**/*.md`) | Everything else; the R1 `docs/handoff/1.1.12.md` is read by tests and is ineligible for carryover |
| NFU | `README.md`, `docs/**/*.md` | `docs/adr/**`, `docs/product-spec.md`, `docs/release.md`, `docs/nvt-fw-util-claude-handoff.md` (required reading for agents), `TODO*.md` (work lists). Note: NFU's `AGENTS.md` specifies that other documents under `docs/` are also product contracts, so the allowlist may need further narrowing |
| NFH | Handoff and review-record Markdown documents (exact paths to be confirmed by NFH) | Governance documents, roadmap and golden rules, performance benchmarks, work lists |

## Risks and mitigations

| Risk | Example | Mitigation |
|---|---|---|
| Hiding substantive changes in text | Changing an installation command or download URL in README | Condition 3.5: no changes to code fences or URLs |
| Hidden instructions for agents | Adding an HTML comment in Markdown, invisible on screen but readable by agents | Condition 3.5: no changes to HTML tags or comments, and no invisible characters |
| Changing rules or contracts | Changing ADRs, product specifications, governance documents, or required reading for agents | Exclude from allowlist; high-risk patterns take precedence over the allowlist |
| Documents are actually program input | NFC has tests that read `docs/handoff/1.1.12.md` | Allowlist only documents that programs do not read; NFC directly uses the R0 definition |
| PR relaxes its own allowlist | PR changes both policy and documents | Read base policy; changing policy itself touches a high-risk path |
| Major rewrite | Rewriting an entire document | Limit of 5 files and 40 lines |
| Semantic conflict with new base | Another PR changed the same document after a rebase | The file enters D; if it is not allowlisted, review again; CI reruns on H as usual |
| Checker itself is changed | PR changes `approval_check.py` | Already a high-risk path; NFU runs the checker from base; after sharing, a pinned version from the shared repository runs |

## Why not retain the owner's Approve

To retain the owner's Approve after text changes, `dismiss_stale_reviews_on_push` must be disabled in the ruleset and the checker must make the decision itself. The costs:

- GitHub's native protection would be disabled for **all** changes, not just text. Everything would then depend on the checker.
- The check workflow's definition is read from the PR's merge ref (NFU's `CONTRIBUTING.md` already states this limitation). GitHub's native dismiss stale currently provides a second line of defense; disabling it leaves only one.
- Re-approving an owner-gated PR after text changes takes the owner only one click on GitHub; the main saving is the review session's time, which carrying over accept already saves.

Recommend carrying over only accept in the first stage, without changing rulesets.

## Changes needed to apply this to each repository

| Repo | Files to change | Notes |
|---|---|---|
| NFU | `scripts/approval_check.py` (add carryover evaluation to `record_result`, requiring access to the compare API), `tests/scripts/test_approval_check.py` and fixtures, `.github/approval-policy.json` (add `review_carryover` setting), `CONTRIBUTING.md` step 5 of 「Change sequence」 and 「Review record」, `.github/pull_request_template.md` | All are owner-gated. Rulesets do not need changes |
| NFC | `scripts/authority_check.py` (review record head comparison), `tests/scripts/test_authority_check.py`, `docs/governance/authority-policy.schema.json` (if adding settings), `docs/adr/0080-governance-reset.md` (revise the 「tree 相同也要新紀錄」 ("a new record is required even if the tree is identical") rule), `AGENTS.md`'s Risk-adaptive gates, `.github/pull_request_template.md` | All are R3 `governance-owner`. Also confirm whether `scripts/release_promotion_policy.py` requires an exact-head review record when collecting release evidence; if so, a carried-over record would cause release eligibility evaluation to fail |
| NFH | No checker yet. If `S15.005c` adopts the shared checker, build this rule in directly; only the merge-boundaries section of `CONTRIBUTING.md` needs to state it explicitly | See the Adoption order section in [proposal.md](proposal.md) |

If each of the three repositories implements this separately, the table above means three copies of code, tests, and documents, exactly what the owner called 「人工平展效率低」 ("manual propagation is inefficient"). After sharing, the evaluation logic is written once in the shared checker, and each repository only changes its policy allowlist and documents.

## Decisions for the owner

See questions 1 and 2 in the "Owner decisions" section of [proposal.md](proposal.md).
