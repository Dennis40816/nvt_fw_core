# nvt-sched

English | [繁體中文](README.zh-TW.md)

A local Windows Task Scheduler wrapper for the allowlisted shadow tick. Requires
PowerShell 7 in `Program Files\PowerShell\7` and Windows `ScheduledTasks`.
The owner runs management commands in a normal, non-elevated window. No passwords,
network requests, policy bypasses or process termination are used.

## Path parameters

| Interface | Purpose |
| --- | --- |
| `-CommanderDir` / `COMMANDER_DIR` | Reviewed local folder containing `tick-shadow.ps1`. The argument overrides the environment. There is no local default. |
| Runner `-TaskPath` | `\NVT\` for readable task names; `\` for legacy names. The generated action selects the correct path. |
| `allowlist.psd1` | Keeps the fixed script filename, task description, and success codes. The CLI resolves the filename under the commander folder. |

Installation embeds `-CommanderDir` in the runner arguments. Use the same folder for later commands.
A legacy root task keeps the source action, which has no `-CommanderDir`. Its runner reads `COMMANDER_DIR` and exits with code 2 if it is unset.
The host executable and local record folders come from Windows system folders.

## Headless scheduled action

Scheduled actions use `conhost.exe --headless` to run PowerShell without a terminal window.
Windows Terminal can open a window even when PowerShell receives `-WindowStyle Hidden`.
The headless console avoids that window, so the action omits `-WindowStyle`.

`--headless` is an undocumented conhost option that requires Windows 10 version 1809 or later.
Microsoft documents that minimum for the underlying [pseudoconsole API](https://learn.microsoft.com/en-us/windows/console/createpseudoconsole).
On older systems, the task may fail to start or open a console window.
Check the version and build in Settings > System > About before installation.
Windows 10 version 1809 uses build 17763.
After an owner-authorized run, check `list` for `Result` and `ResultSource`, and confirm that no console window appeared.

nvt-sched reads the runner state for the headless `\NVT\commander-tick` action.
conhost discards the child exit code and normally leaves Scheduler's `LastTaskResult` at 0.
The [upstream report](https://github.com/microsoft/terminal/issues/17178) describes this behavior.

- Resolve `conhost.exe` from `[Environment]::SystemDirectory` and validate it with `Assert-NvtPath`.
- Resolve `pwsh.exe` from `Program Files\PowerShell\7`, never PATH or a caller-supplied executable.
- Validate the fixed runner and keep the working directory equal to the allowlisted tick directory.

The action arguments have this form:

```text
--headless "<pwsh path>" -NoLogo -NoProfile -NonInteractive -File "<runner>" -Id commander-tick -TaskPath \NVT\ -CommanderDir "<commander folder>"
```

The action uses no password, encoded command, or execution policy switch.
`list` and `status` report the previous `pwsh.exe -WindowStyle Hidden` action as `DefinitionOutdated`.
`run` and `remove` reject that outdated definition with code 4.
`install` and `add` replace it only when every other field matches the requested allowlisted definition.
Updating requires the runner lock, confirmed worker exit, and a ready task.
The tool verifies the exported headless definition before cleaning up any legacy task.
Other definition changes and disabled tasks remain refused.

## Re-register in one command

Set `COMMANDER_DIR` to the reviewed commander folder, then run this command from the repository root:

```powershell
pwsh -NoProfile -File tools/nvt-sched/nvt-sched.ps1 install -Id commander-tick -EveryMinutes 20
```

Expect `\NVT\commander-tick verified; legacy removed; history retained.` if a legacy
task existed; otherwise the message confirms the same settings and retained history.
Task Scheduler shows folder `\NVT\`, name `commander-tick`, author `commander`,
and a Chinese/English description of purpose, interval, scripts and maintenance.
The description template lives in the required `Description` field in `allowlist.psd1`;
without it, Task Scheduler cannot explain the job's purpose, cadence or maintainer.
Changing the interval updates its text.
The principal remains the current user's SID with InteractiveToken/LeastPrivilege.
`install` and the compatible `add` command have the same behavior.
They update the exact outdated action described above.
They refuse other definition changes and disabled tasks.

Installation registers the new task, exports it and validates execution settings,
SID, author and description. Only then does it verify, disable and remove the old
root task `\NVT-S-<SID>-commander-tick`. Registration/verification failure leaves
the legacy task untouched and returns a nonzero error. The new task can remain if
verification fails; inspect it before retrying. Legacy cleanup uses the same runner
lock and PID/start-time checks as removal. A busy/unknown worker leaves the legacy
task disabled and reports an error; wait for completion, inspect `list`, then retry
the same installation command. Both task definitions use the same retained local
state/history and runner lock, so their workers cannot overlap. A refused overlap
may appear as a failed attempt. The runner distinguishes legacy and new task paths,
so the legacy task can still execute if registration fails.

## Inspect, preview and remove

From the repository root:

```powershell
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 install -Id commander-tick -EveryMinutes 20 -DryRun
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 list
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 status -Id commander-tick
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 list -Json
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 run -Id commander-tick
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 remove -Id commander-tick
```

`list`/`status` are identical read-only queries. They show `\NVT\` tasks and current
SID legacy tasks at root. Root leftovers have `Legacy: True`, are `Unmanaged`, and
are not run/removed by normal commands. `Installed`, `DefinitionMismatch`,
`NotInstalled` and `Unmanaged` retain their previous meanings.
`DefinitionOutdated` identifies the previous action. JSON is always an array.
Added `TaskPath` prevents ambiguity between folders; `Legacy` identifies
remaining old names. Existing completion fields and the newest five history
entries remain, with unknown/unreadable evidence shown as null and never repaired.
Query errors return nonzero with sanitized stderr, without partial JSON or writes.
`run` only submits a request; use `list` for completion. `remove` targets `\NVT\`,
disables first and only unregisters after confirmed worker exit; it never kills a
process or deletes records. Reused PIDs are compared by process start time.

`Result` shows the effective result, and `ResultSource` identifies its evidence:

- `RunnerState`: Use a completed state's integer `ExitCode` when `UpdatedAtUtc` is fresh enough for Scheduler's `LastRunTime`.
- `Scheduler`: Use `LastTaskResult` for running code 267009, never-run code 267011, legacy tasks, and actions without conhost's headless option.
- `Unknown`: The headless task lacks readable, completed state or valid timestamps, or its state predates the latest run beyond the tolerance.

The freshness check converts Scheduler's local time to UTC and allows state timestamps up to five seconds before `LastRunTime`.
This tolerance covers timestamp precision and small clock differences.
An earlier timestamp returns `Unknown`, even when retained state records success.
`LastTaskResult`, `ExitCode`, and existing completion fields remain unchanged for compatibility.
`status` and both JSON and text output include the same effective fields.

`EveryMinutes` (1–44640, default 20), `DataDir` and `DryRun` apply only to
`install`/`add`. `DataDir` must equal the allowlisted tick's directory. `Json` is
supported by `list`/`status`. Unknown commands, parameters and IDs are rejected.
DryRun prints TaskPath, TaskName and XML without Scheduler or state IO.

## Weekly task audit

```powershell
pwsh -NoProfile -File .\tools\nvt-sched\nvt-sched.ps1 audit
```

`audit` reads Scheduler metadata and headless runner state, excluding `\Microsoft\` and its children.
It writes `%LOCALAPPDATA%\NVT\sched\audit\audit-<yyyyMMdd>.md` and `latest.json`, then prints the report path.
The Traditional Chinese mobile report starts with a conclusion and groups attention items, ours, and vendors.
It includes path, name, state, last run, effective result, result source, next run, author, and description.

- Failed: Flag nonzero runner results and `Unknown` as failures. Exempt Scheduler codes 0, 267009, and 267011.
- Never run: Use Scheduler code 267011 or an absent or initial `LastRunTime`.
- Disabled: Flag disabled tasks.
- Missing author: Flag tasks without an author.
- Ours: Include `\NVT\` and its children.

The first snapshot establishes a baseline.
Later reports compare full path+name for additions/deletions. Microsoft-like vendor
folders such as `\MicrosoftVendor\` are still included. Suggestions only cover
ready vendor tasks that never ran and lack an author, with a reason and a reminder
to confirm their purpose. Legacy tasks are excluded from suggestions. No task is
automatically disabled. Result 10 is flagged by the requested nonzero rule, but
our tick's report explains that 10 is a successful change notification.

The existing tick invokes the audit on its first run after Monday 08:30 local
time, at most once per ISO week; later weekdays catch up after sleep/logout.
No additional task is installed. With the default 20-minute interval it may run
around 08:40 rather than exactly 08:30. It logs `weekly task audit ready: <path>`
and returns 10. A manual audit in the same week is reused. A snapshot acknowledgement
is written only after logging, so failed logging retries the notification without
regenerating the audit. Unreadable prior audit evidence fails rather than silently
resetting the baseline. A same-day manual audit replaces that day's Markdown.

## Retained files and safeguards

The data root remains `%LOCALAPPDATA%\NVT\sched`. Existing state, error, runner lock
and bounded history files remain unchanged; completed attempts retain the newest
50 lines, and queries show five. Raw tick output and exception contents are not
stored. Tick exit 0/10 means success. `latest.json` contains only `At` (needed to
identify the ISO week) and `Tasks` (the required metadata needed for comparisons).
Each task now includes `Result` and `ResultSource` alongside its unchanged `LastTaskResult`.
It also supplies the exclusive audit lock. The dated Markdown is the owner-readable
weekly list. `snapshot.json` adds `weekly_task_audit_week` to acknowledge successful
logging; without it a crash after report creation could lose the notification or
cause repeated notifications. These are the only new persisted audit fields/files.

Local physical paths are checked for traversal, UNC/device paths, alternate streams
and reparse points. XML comparison normalizes known Scheduler omissions and SID
identities, but still refuses changed actions, permissions and execution settings.
The old sanitized XML fixture remains a regression case for exported defaults.
The task uses indefinite repetition plus current-user logon, StartWhenAvailable,
IgnoreNew, no wake, battery operation and a five-minute limit. Sleep, logout and
shutdown delay it; it does not wake a Claude session. Owner acceptance is still
needed for GPO, folder creation permissions, reboot/sleep/logon and timeout behavior.

Tool exit codes remain: 0 success, 2 invalid input/allowlist, 3 unsafe path,
4 definition mismatch, 5 unknown/disabled state, 6 live worker, 7 busy lock,
8 missing task for run/remove, 9 local API/file failure. Audit failures do not write
job error records. A crash while writing JSON can leave unreadable evidence.

## Offline verification and rollback

```powershell
pwsh -NoProfile -File .\tools\nvt-sched\test-nvt-sched.ps1
pwsh -NoProfile -File .\tools\nvt-sched\test-nvt-sched.ps1 -ShowExamples
```

Tests replace all Scheduler APIs and use synthetic state, tasks and isolated tick
inputs/outputs. They never query/register/change real Windows tasks or access the
network. No live registration is part of implementation delivery.

Before owner installation, export the old task in Task Scheduler if rollback may
be needed. If legacy cleanup failed, the owner can disable the new task and re-enable
the inspected old one in Task Scheduler. If the old task was removed, remove the
new task with the current tool, then import the saved old XML (or restore the old
tool version and run its original add command). Keep script locations and the same
SID; local state/history stay intact. Verify there is only one enabled task before
resuming. Live installation and notification continuity require owner acceptance.

The imported suite uses a synthetic tick caller for subprocess audit and notification checks.
The deployed external tick is outside this import. Its integration with the scheduler still requires host verification.
The suite retains migration, task metadata, weekly audit, audit reuse, and notification retry checks.

## Source snapshot

Source: the commander's scheduler tools. Snapshot time: `2026-10-05T18:24:14+00:00` (UTC).

The source has no Git baseline. These per-file SHA-256 values replace a source ref and commit SHA.
File names are relative to the source tool folder. Copy status compares the complete file bytes.

| Imported file | Source SHA-256 | Repository copy |
| --- | --- | --- |
| `allowlist.psd1` | `b16ad24e85480061cb519e981de4f51a7ceabfe603208efdcafefe32d725f082` | Changed |
| `nvt-sched-runner.ps1` | `2611e1f754e1dc86ca5a9bb29dd64de77c4f66ce482b79f0d504bd655e1a27da` | Changed |
| `nvt-sched.ps1` | `04ae2328ab928dba32f3b7b49b9e3ab41ce3ca71ea07b8a07b6b765c50e6484e` | Changed |
| `README.md` | `f24729513ea87e7dd5b0473048310f544c1e230d3b72ac716b6581bf0e1ed89d` | Changed |
| `README.zh-TW.md` | `250c07c425e8b87652ebaafe310bebb32d1b8c20cd2f7923f4c902d32880e305` | Changed |
| `registered-task.fixture.xml` | `8f2a57eb7021745c9d45c772157c682c3cdad49fcd69d8bd7337626888732921` | Changed |
| `test-nvt-sched.ps1` | `efec2708c404e4aac60b851995184dcad25c68762e27b95762bcdd26376f0d18` | Changed |

Every imported script adds the owner-required copyright notice. Text changes use UTF-8 without a BOM and LF line endings.

- `allowlist.psd1`: Adds a copyright header. Keeps the fixed tick filename and resolves its directory through the existing parameter.
- `nvt-sched-runner.ps1`: Adds a copyright header. Requires CommanderDir through the argument or environment and forwards it to the CLI.
- `nvt-sched.ps1`: Adds a copyright header. Retains CommanderDir parameterization, required-folder checks and Windows argument escaping. Adds the headless action and exact outdated-definition migration.
- `README.md`: Uses repository-relative commands and path parameters. Removes local deployment details. Adds source hashes and the external tick verification limit.
- `README.zh-TW.md`: Uses repository-relative commands and path parameters. Removes local deployment details. Adds source hashes and the external tick verification limit.
- `registered-task.fixture.xml`: Uses the headless action with account, console, host, runner, and directory placeholders. Keeps synthetic SID and exported omissions.
- `test-nvt-sched.ps1`: Adds a copyright header. Retains parameter tests and derives local path negatives from fixtures. Uses account placeholders and a synthetic external tick caller.

To verify zero difference, compare source hashes first. Review each listed transformation, then run every imported suite from the repository root.
Queue fixtures verify parser, gate, evidence and cleanup behavior. Scheduler mocks verify task definitions, migration, history, and audits.
Use reviewed path arguments for host comparisons. Keep private queue state, external tick evidence and live acceptance outside this repository.
