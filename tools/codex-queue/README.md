# codex-queue

English | [繁體中文](README.zh-TW.md)

Git Bash dispatch queue tools for Windows, copied from the deployed implementation. Requires Python 3.10+, Git for Windows (including `cygpath`), PowerShell 7, and the Codex CLI for actual runs. No model or reasoning effort is selected unless a brief or caller explicitly supplies it.

Keep runtime queues outside this checkout. A queue contains `queue.env`, optional `build-notes.md`, `accept-templates.txt` and `risk-floor.txt`, plus `proposed/`, `ready/`, `running/`, `done/`, `failed/` and `work/`. The host trusts `queue.env` as Bash code; review it before use. A `stop` file stops new claims. Sibling queues share the claim lock and disk reservation.

From the repository root in Git Bash:

```bash
bash tools/codex-queue/qworker.sh worker '<QUEUE>'
bash tools/codex-queue/qrefill.sh '<QUEUE>'
bash tools/codex-queue/qfocus.sh '<QUEUE>'
```

Replace all angle-bracket placeholders with reviewed paths before running. Use Git Bash paths in `queue.env`. Absolute queue arguments are recommended; a bare/relative queue argument resolves beside the tool directory (the original sibling-queue convention). `qrefill.sh` and `qfocus.sh` fetch and detach the dedicated `TRUNK_WT`; its role is read-only for Codex. They read `refill-prompt.md` and `refill-focus-prompt.md` respectively and split the result into proposals. Review proposals before moving them to `ready/`.

## Parameters

| Interface | Parameters |
| --- | --- |
| `qworker.sh` | Required positional worker name and queue folder. |
| `qrefill.sh`, `qfocus.sh` | Required positional queue folder; optional environment `REF` overrides `BASE` for the refill checkout. |
| `restart-pool.sh` | Required positional queue folder. Existing one-off behavior: set stop, wait for four stopped log entries (up to forty 60-second checks), start workers `w5`–`w10`, release `work/requeue/*.md` to ready. Review this operation before use. |
| `run-codex-ws.ps1` | Required `-PromptFile`, `-Out`, `-Log`, `-Dir`; optional `-Model`, `-Effort` (empty: Codex defaults), `-Sandbox` (default `read-only`), `-AddDirs` (semicolon-separated directories, default empty). PowerShell common parameters apply. |
| `tests/test-p0.py` | Helper path and disposable fixture root; provided by `test-p0.sh`. Product audit modes are removed. |
| `tests/test-wrapper.ps1` | `-Wrapper` path and disposable `-Fixture` root; provided by `test-p0.sh`. |

`queue.env` settings:

| Setting | Required / default / purpose |
| --- | --- |
| `REPO` | Required by worker; repository path. |
| `WT` | Required by worker; existing parent for task worktrees and disk checks. |
| `BASE` | Required by worker/refill/focus; Git base ref. |
| `TRUNK_WT` | Required by refill/focus; dedicated worktree path. |
| `QUOTA_CHECK` | Required by worker unless `ALLOW_CREDITS=1`; trusted command producing one JSON line with `status` (`OK` or `EXHAUSTED`), numeric `used_percent` and `sample_age_min`. No local fallback. |
| `ALLOW_CREDITS` | Default `0`; `1` explicitly bypasses the quota gate. |
| `SETUP` | Optional trusted host Bash command; default empty. |
| `MIN_FREE_GB` | Default `215`; minimum free-space floor in GiB. |
| `TASK_RESERVE_GB` | Default `20`; positive integer GiB estimate per running, cleanup-pending and new task. |
| `ACCEPT_POLICY` | Default `warn`; `enforce` requires exact trusted Accept templates. Evidence and Scope/risk checks remain mandatory in either mode. |
| `PROJECT_PREFIX` | Default `Project`; name prefix of the .NET projects that `Prebuild:` builds, for example `NvtFwCombiner` for `src/NvtFwCombiner.Desktop/NvtFwCombiner.Desktop.csproj`. Must be a plain project name (letters, digits, `_`, `.`). |
| `EXTRA_ADD_DIRS` | Legacy setting; worker ignores shared directories and grants only the task's temporary directory. |

The worker sets `QUEUE_BUILD_NOTES` to `<QUEUE>/build-notes.md`; direct `q.py prompt` callers may set that environment variable to an optional notes file. The wrapper sets no-bytecode/no-pytest-cache and existing telemetry/language environment settings; they are not caller configuration.

Minimal local `queue.env` (replace placeholders; do not commit the filled file):

```bash
REPO='<REPO>'
WT='<WT>'
BASE=origin/main
TRUNK_WT='<TRUNK_WT>'
QUOTA_CHECK="pwsh -NoProfile -File '<QUOTA_SCRIPT>'"
```

`example-queue/` contains four generic starter files. Copy them to a runtime queue outside this checkout.
Replace every placeholder before use. Review the quota command, acceptance templates, and risk rules for that queue.
The commented templates grant no extra commands. A comments-only risk file uses common protections instead of all missing-file legacy floors.
Product plans, repository names, work items, and private dependencies belong in each runtime queue.
Both `--compat` and `--compat-all` are removed because their audits name specific product queues.

## Python helper and briefs

All `q.py` positional parameters (run from the target repository where Git checks are needed):

```text
get <brief> <Key>
accept <brief>
check <brief>
run-accept <brief> <log-prefix> [base-sha]
run-prebuild <brief> <log-prefix>
quota                              JSON sample on stdin; exit 3 if exhausted
check-diff <brief> <base-sha>
unique <queue> <name> [source]      caller holds the claim lock
chain-base <brief> <queue> <base>
required-free <queue> <minimum> <estimate>
prompt <brief> <worktree> <branch> <base>
split <refill-out.md> <proposed-dir>
```

Brief fields remain `Title`, `Risk`, `Base`, `Wait`, `Prebuild`, `Model`, `Effort`, `Why`, `Goal`, `Scope`, `Accept`, `Not in scope`. Risk is R0–R2; R3 never runs. `Scope` lists literal repository-relative paths, one per line. Accept commands start with `$ ` and are checked against the existing defaults or queue templates. Optional model/effort overrides require a task-specific reason; omit them for Codex defaults. See the `q.py` module description and `CHANGES.md` for the unchanged parser, quota, chain, evidence, cleanup and risk-rule contracts.

The fixed `Prebuild` expansion uses anonymous project names:

- `Desktop` builds `src/Project.Desktop/Project.Desktop.csproj`.
- Other names build `tests/Project.<Name>.Tests/Project.<Name>.Tests.csproj`.
- Leave `Prebuild` empty when the target repository uses other names. Supply reviewed build commands in `Accept` instead.

## Offline tests

From the repository root, using Git Bash:

```bash
bash tools/codex-queue/tests/test-gate.sh
bash tools/codex-queue/tests/test-p0.sh
```

`test-p0.sh` reuses the gate fixture helpers and runs its integration cases, Python unit tests and the PowerShell wrapper test. The suites mock Codex, quota, SDK programs and fetch; Git worktrees, commits, evidence and cleanup use disposable synthetic fixtures. Tests do not touch live queues or invoke Codex. Actual quota/Codex runs and pool restart require the operator's own runtime configuration. Source backups and runtime state are excluded from this directory.

## Source snapshot

Source: the deployed queue tools. Snapshot time: `2026-10-05T18:24:14+00:00` (UTC).

The source has no Git baseline. These per-file SHA-256 values replace a source ref and commit SHA.
File names are relative to the source tool folder. Copy status compares the complete file bytes.

| Imported file | Source SHA-256 | Repository copy |
| --- | --- | --- |
| `CHANGES.md` | `6323b355d8f536f4804f1d16637101e69e7ede4dc631da83250e39c198a17757` | Changed |
| `q.py` | `23c4f52386f0b14a13c290b0b4b4c5ed061922c0ab78755e3be4ca8f3a6413d9` | Changed |
| `qfocus.sh` | `f91508c5f9c8e05360699756475c60991dafa42663ae0b7cf4c99db85eb31645` | Changed |
| `qrefill.sh` | `6df8a5db311df5eb48d81d5f7fc46d0fe012176a4e91dfe6ed360f1dc9b30f14` | Changed |
| `qworker.sh` | `d139eb0b4edf121447e33c2e8ca9c30393389031f15fbf32d1820cb7de1a2048` | Changed |
| `restart-pool.sh` | `d920d5de66f828c96170bcc1493a2c03bf0adb6639bfeab731522e0eccb3a7ea` | Changed |
| `run-codex-ws.ps1` | `98f6abfb89925aad799684072289358a13bf365db81fc3fe56eea2b88fd7819e` | Changed |
| `tests/test-gate.sh` | `e79f381c8d79237a8e8d14d4bc7e2041f1776b5998af7b7ee64ea4ff5bf0e281` | Changed |
| `tests/test-p0.py` | `aab7cb0ad4933e77a175cef13850d58c8379df9866c9f3375375738f6cce177d` | Changed |
| `tests/test-p0.sh` | `ed6ffa395f74d16a8ce632421dc2f246da7fd25a9e8edcb59a8a4d0b835b117c` | Changed |
| `tests/test-wrapper.ps1` | `4edc03d2110f1796476a6621d32bd86ddd87fd3bbb13f825c08f0b2d727803c3` | Changed |

Every imported script adds the owner-required copyright notice. Text changes use UTF-8 without a BOM and LF line endings.

- `CHANGES.md`: Replaces internal deployment records and product work items with generic behavior notes.
- `q.py`: Adds a copyright header. Replaces the private Prebuild project prefix with the anonymous Project prefix.
- `qfocus.sh`: Adds a copyright header and required TRUNK_WT/BASE checks. No deployed logic refresh.
- `qrefill.sh`: Adds a copyright header and required TRUNK_WT/BASE checks. Removes the product queue example. No deployed logic refresh.
- `qworker.sh`: Adds a copyright header. Requires REPO, WT, BASE and explicit quota configuration. Removes the local quota default and path comment.
- `restart-pool.sh`: Adds a copyright header. Takes the queue as an argument. Uses generic pool instructions instead of deployment history.
- `run-codex-ws.ps1`: Adds a copyright header only.
- `tests/test-gate.sh`: Adds a copyright header. Uses a synthetic Git identity and explicit mock quota command. Tests missing quota configuration.
- `tests/test-p0.py`: Adds a copyright header. Removes both product audit modes. Uses synthetic queue rules, example files, anonymous projects and derived absolute-path negatives.
- `tests/test-p0.sh`: Adds a copyright header. Uses synthetic queue rules and fixture-derived shared paths.
- `tests/test-wrapper.ps1`: Adds a copyright header only.

The two READMEs and four example files are repository-authored. Per-tool configurations, compatibility reports, backups, and runtime state are excluded.

To verify zero difference, compare source hashes first. Review each listed transformation, then run every imported suite from the repository root.
Queue fixtures verify parser, gate, evidence and cleanup behavior. Scheduler mocks verify task definitions, migration, history, and audits.
Use reviewed path arguments for host comparisons. Keep private queue state, external tick evidence and live acceptance outside this repository.
