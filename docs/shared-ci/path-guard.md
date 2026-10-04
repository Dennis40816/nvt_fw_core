# Shared CI path guard: design draft

Status: design only, 2026-10-05. No scanner, action, workflow, or policy is implemented by this document. Roll-out follows the [NFH pilot](proposal.md#adoption-order).

## Problem

A test can pass locally because its fixture path points into a developer's home directory, then fail in CI where that directory does not exist. For example, a test should locate `tests/fixtures/sample.json` from the checkout instead of embedding the developer's machine-specific location.

The guard catches literal paths before tests run. It cannot prove that dynamically constructed paths or runtime file access are portable.

## Checks and exclusions

The proposed first version scans Git-tracked text in the repository's test, project/configuration, and text-fixture paths. A small `.github/path-guard.json` lists those paths and exact exceptions; it does not define custom detection rules. Include public example/test-data submodules needed by those tests, at their recorded gitlink commits. A missing configured scan path or required submodule fails the check instead of silently reducing coverage.

Detect Windows drive-rooted paths with either separator, UNC paths, MSYS and WSL drive mounts, and macOS/Linux home-directory paths. Recognize escaped literals and decode JSON string values before checking them. Match path structure, not developer names or the current runner's home directory. Report only the repository-relative file, line, and rule; do not print the matched value.

Ignore documentation examples, binary files, generated/build output, dependencies, and untracked files. Relative paths and URLs are not violations. Existing repository privacy checks stay in place; excluding documents from this new portability check does not exempt them from those checks. Do not fetch private data for the shared guard.

## Exceptions

A deliberate invalid-path test or retained text fixture may need an exception. Propose one entry per repository-relative file and SHA-256 fingerprint of the exact offending source line, excluding its line ending, with a reason. This avoids copying the path into the policy, survives CRLF/LF changes, and stops matching when the line changes. Do not allow directory-wide or rule-wide suppressions.

Review exception changes through the repository's existing governance process and CODEOWNERS. An exception permits that source line only; it does not establish that a required runtime fixture was loaded. Prefer fixing an accidental dependency on a local path.

## Sharing and local use

Use the existing composite-action distribution model: a future `actions/path-guard/action.yml` wraps one `check.ps1` in this public repository. Add synthetic positive and negative fixtures here; do not copy product data into shared tests. Release with a protected version tag and pin callers as `Dennis40816/nvt_fw_core/actions/path-guard@<full SHA>`, with a version comment. The caller's existing `github-actions` Dependabot configuration proposes updates; adoption and rollback follow [the shared versioning rules](proposal.md#versioning-propagation-and-rollback).

NFH's existing local `scripts/verify.ps1 -StructureOnly` should invoke the same shared script before push, alongside its existing privacy checks. Developers use a checkout of the same shared commit as the workflow pin; no second implementation, package, or hook manager is needed. Local and CI runs scan the same tracked scope with the same policy and produce the same result. Updating the pin includes checking local verification against that revision.

Keep the guard to one lightweight scan using Git and PowerShell already available to verification. It needs no build, product test run, service, secret, network lookup during scanning, or operating-system matrix. Measure scan time on NFH during calibration. Add a step to the existing `policy / structure` job, rather than requiring another job. The owner's rule remains: private repositories run few CI jobs; tests belong in public repositories.

## Roll-out and acceptance

1. After NFH's CI fixes and 1.3.2 are complete, confirm its public scan scope and exceptions, implement the shared action, and connect NFH's local verification and existing CI verification job to the same revision. This is separate from completing the approval-checker pilot.
2. Use one real NFH PR for report-only calibration, using the caller step's existing `continue-on-error` mechanism, with no new scanner mode. Synthetic fixtures must catch each supported path form, allow relative paths/URLs, verify JSON escaping and exact exceptions, and detect a match in a configured submodule. Compare local and CI results, including a changed working directory; record scan time and resolve false positives.
3. Subject to the owner's enforcement decision, make the step fail its existing CI job for unexcepted findings. Confirm a failing case and its correction on a real NFH PR, and merge one Dependabot update of the path-guard pin before NFU adopts it.
4. NFU adopts next, then NFC between releases. Keep each repository's existing product tests and required checks. Roll back by reverting that repository's pin update; any temporary removal of enforcement needs owner approval.

## Questions for the owner

- **Initial scope:** include tracked tests, project/configuration files, text fixtures, and required public submodules? **Recommend yes**, with explicit repository-relative paths in the policy and documentation excluded from this new check.
- **Exception format:** use file plus exact source-line fingerprint and reason, reviewed under existing governance? **Recommend yes**; omit broad exclusions and additional exception metadata for the first version.
- **Enforcement and cost:** calibrate on one NFH PR, then fail an existing verification job and run the same check locally before push? **Recommend yes**; keep private-repository CI small and keep shared tests public.

## Out of scope

Code or workflow changes in this planning update; approval-policy changes; product-specific loading assertions or missing-file behavior; automatic path rewriting or fallback lookup; access to private fixtures; binary-content scanning; runtime I/O auditing or sandboxing; a full environment matrix; configurable rule engines, synchronization bots, and new hook/package infrastructure.
