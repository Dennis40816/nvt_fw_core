# Review and approval

Each pull request receives an independent Codex first review and a Claude second review. Commander then requests owner approval.

## Prepare the review evidence

The project session provides the following:

- The full pull request URL and full head commit SHA.
- The task brief, scope, and changed files.
- The source baseline and comparison evidence when the task extracts code.
- Exact verification commands, results, and unrun checks.
- Known risks and remaining owner decisions.
- Public hygiene and licensing evidence.

Use public or sanitized evidence in published records. Keep private provenance and fixtures outside the public repository.

## Codex first review

A Codex reviewer other than the implementing worker performs a read-only review of the full diff and supporting evidence.

The reviewer checks the following:

- Scope and acceptance evidence.
- Minimal public APIs and the need for each abstraction.
- Separation of generic code from product logic and data.
- Public hygiene in files and submitted artifacts.
- Required license notices and publication authority.
- Test results and coverage of changed behavior.
- Zero-difference evidence when the task extracts code.
- New or changed state against the 11 rules in [State management](../core/conventions.md#state-management).

The review records the head SHA, verdict, findings, file references, and required corrections.

The project session addresses findings and supplies updated evidence before the review proceeds.

## Claude second review

Claude checks the Codex review against the pull request and its evidence. Claude examines high-risk code directly.

High-risk areas include the following:

- Package verification and activation.
- Recovery and rollback.
- Process execution and containment.
- File input and output.

Claude also checks new or changed state against [State management](../core/conventions.md#state-management).

Claude records the head SHA, verdict, remaining risks, and required corrections.

Refresh affected reviews when content changes. Keep unresolved findings visible in the review record.

## Owner approval and merge

1. The project session sends commander a one-line summary with the full pull request URL.
2. Commander collects a batch with head SHAs, both reviews, verification evidence, risks, and direct approval links.
3. Owner gives an explicit approval for the listed pull requests and their scope.
4. The integrating project session merges in dependency order after required checks and approvals pass.
5. The project session updates the single status table with the decision and merge evidence.

Record the owner's approval statement and decision time. Commander cannot approve on owner's behalf.

An approved pull request can retain approval after a new commit that only merges `main` cleanly.

Required checks must pass on the new head. Any manually resolved conflict requires owner approval again.

Other changes follow the repository's approval policy. A clean merge does not authorize additional edits.

## Remote branch deletion

For categories without prior authorization, send owner the branch names and full head SHAs before deletion.

Owner has authorized deletion of remote branches whose content is already in `main`. Verify that condition before deleting a branch.

## Module import acceptance

A module counts as imported into Core only when all five conditions hold:

1. Core contains the code through a pull request that owner approved.
2. Local Core builds and tests pass before remote verification.
3. At least one adopting tool uses Core and deletes its duplicate. Its existing tests and UI or output comparisons show zero behavior difference.
4. Each adopting tool still builds and releases independently. It ships Core with its own package.
5. English and Traditional Chinese README documentation is available. [The component record](../components.md) lists the source, version, and adopting tools.

For extracted code, freeze the parent baseline before changes. Record the approved source repository reference, ref, full commit SHA, and extracted file paths.

Run behavior and output comparisons against that baseline. For UI comparisons, keep the operating system, fonts, display scale, and theme identical.

Keep the baseline unchanged when a comparison fails. Resolve the difference or request a separate owner decision.

For document tasks, verify required content, links, language, and public hygiene. Code extraction and zero-difference checks do not apply.
