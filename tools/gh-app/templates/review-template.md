# Independent first review (read only)

Repository worktree: `{WORKTREE}`. Review the change `{BASE}..{HEAD}` (`git diff {BASE} {HEAD}`). The implementer's report is `{REPORT}`; host build and test output is `{TESTLOG}`. Do not modify anything. You did not write this change.

Check, citing `file:line`:
1. **Zero-difference evidence:** the frozen parent baseline (source repository, ref, full SHA, paths) is recorded, and the ported and characterization tests really pin the source behavior. Compare with the source using `git show <sha>:<path>` in the source repository.
2. **Minimal API:** nothing beyond what the task needs; no speculative abstractions, options or extension points.
3. **No product logic leaked** into Core (firmware, Event Buffer, Freeform business rules; NFC's output-byte code).
4. **Public repository hygiene:** no local absolute paths, user or machine names, secrets, private repository details.
5. **License:** every new `.cs` file starts with `// Copyright (c) 2026 Dennis Liu. All rights reserved.`
6. **Tests:** they pass in `{TESTLOG}`, cover the behavior, and are deterministic.
7. **Conventions:** code in `src/<Library>/<Module>/`, namespace `<Library>.<Module>`, tests mirrored; shared files (`Directory.Packages.props`, `Directory.Build.props`, `global.json`, `Nvt.Core.sln`, lock files) unchanged unless the task says so.

Output findings, most important first, with a minimal fix for each, or "no findings". End with exactly one line: `Review record: {HEAD} accept` if there are no findings in 1, 3, 4, 5 or 6, otherwise `Review record: {HEAD} reject`.
