# Queue behavior notes

This public copy preserves the deployed queue engine with the import changes listed in both READMEs.
Internal deployment records, product plans, compatibility inventories, and historical host test results remain outside this repository.

- The helper rejects missing Risk, empty Accept commands, duplicate fields, and R3 briefs.
- The worker enforces literal Scope paths and host-owned risk floors before it starts work.
- Enforce mode validates exact argv templates before execution. Warn mode retains legacy execution and records template violations.
- Test acceptance requires a successful exit and nonzero passed-test evidence.
- Chain tasks require an accepted predecessor and use its recorded full commit SHA.
- Cleanup saves status, diff, untracked files, and commit evidence. Uncertain ownership or cleanup failures retain the worktree.
- Sibling queues share claim locks, disk reservations, and cleanup-pending reservations.
- Quota checks validate JSON samples. Fresh exhaustion stops new work. Unreadable samples delay claims.
- Host acceptance removes common credential environment variables. Host programs still use the operator's account permissions.
- Refill splits validated briefs from the final report. Report-only output creates no tasks.
- The wrapper uses caller-selected model and effort values. Empty values use Codex defaults.
- Offline suites use synthetic queues, mocked programs, and disposable Git repositories.

Use the README source hashes to compare this copy with the deployed queue tools.
Run both repository suites before deployment. Historical source test results do not validate this copy.
