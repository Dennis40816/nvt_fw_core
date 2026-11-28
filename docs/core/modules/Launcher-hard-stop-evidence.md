<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

# Launcher recovery hard-stop verification

The pre-delete follow-up adds an internal nullable execution hook and threads it through
the existing Files deletion primitive. PublicAPI files, project references, health ledgers,
CHANGELOG and ROADMAP are unchanged.

## Implementation

Both modes belong to LinkedProbe because they run Core code. TestProbe remains BCL-only.
LinkedProbe discovers ordinary attributed `RunAsync(ProbeContext)` methods.
They load two fixed test-assembly entry points, reusing the existing setup/recovery fixtures
and synthetic codecs without making the probe depend on the test project.

The setup mode blocks at eight existing `ManagedSetupBoundary` callbacks.
For `BeforeFirstDurableApplicationStateWrite` and `AfterFirstDurableApplicationStateWrite`,
it completes materialization and records Bootstrap launch, then runs the real
`ManagedVersionSeedBootstrapper`. Its destination store blocks immediately before,
or immediately after awaiting, the real `VersionManagerStateFile.WriteAsync`.
No cancellation substitutes for process termination. Package bytes are saved once,
so reconstructing the package fixture cannot change admission identity through ZIP timestamps.

The executor mode reloads serialized synthetic transaction/state bytes, creates fresh
evidence and writer custody, and blocks in the selected `BeforeDelete(n)` or
`AfterDelete(n)` with the operation still active. The existing `boundary` input carries
`before:n` or `after:n`; setup boundary names and the assembly-loading path are unchanged.
The parent independently lists all 25/26 deletion paths, including files, directories,
state(s), staging container and final marker. It compares the complete surviving
names/types and file hashes, then creates fresh stores and an executor over disk.
Before rows expect the first n-1 deletions removed, after rows the first n.
Restart must delete exactly the remaining count, or diagnose `NoRecoveryNeeded`
after the final marker deletion. Fixture constructors used to reopen state create no workspace
and neither recreate missing installation content nor own the parent's cleanup.

Each gate atomically publishes its exact boundary marker, then flushes
`READY:<boundary>` through standard output. The parent uses bounded output waits,
checks the marker and that the child is alive, kills the tree and awaits exit.
`ChildProcessFixture` cleans up on every failure path. The child waits through
`SignalWait`/`TaskBlock` with a 30-second watchdog. There are no synchronization sleeps.

Setup restart asserts marker phase, promotion, application-state presence, staged versus
final payload location, every payload file hash, exact directory inventory, and the recovery result.
`BeforeStagingMarkerWrite` leaves an empty marker and requires manual intervention.
`AfterPromotion` and `BeforeRootPromotedMarkerWrite` leave a staging marker beside an
already promoted root; recovery must require manual intervention without mutation.
The other seven rows must offer rollback and complete physical removal under a new writer lease.

## Exact pre-delete boundary

`RecoveryExecutionTestHooks.BeforeDelete` defaults to null, like `AfterDelete`.
Each of the three executor deletion sites passes the callback and `DeleteCount + 1`
directly into `TryDeleteHeldEntry`: state/marker files, rollback tree entries, and
empty staging. No closure, delegate wrapper or other allocation is added when unset.
A successful deletion still increments the count once and raises the existing after hook.

The hook runs inside `MarkDeleteOnClose`, immediately before
`SetFileInformationByHandle(FileDispositionInfo)`. `TryDeleteHeldEntry` has already
acquired the exclusive path-chain handles, checked the final target identity and,
where required, verified directory emptiness. All executor evidence, hashes, authority
and staging checks have also completed. No validation or custody acquisition remains
between the hook and the native disposition call.

Deletion is not one call: disposition marks the file/directory for deletion, then
`ForgetHeldEntry` closes its held handle and the `finally` closes exclusive handles.
For state/marker files the borrowed read stream is also disposed as its scope exits,
before `AfterDelete`. Stopping after disposition would already leave a pending delete
that closing handles (including process termination) can finish. The gate before
disposition is the last boundary at which this deletion has neither removed the name
nor marked it for deletion.

`DeleteHooksBracketEachPhysicalDeletionInOrder` adds three shape rows. Each callback
checks the independently listed n-th target still exists before deletion and is absent
afterward; the final assertion requires exactly `before(1), after(1), ...` through the
last marker. The 154 hard-stop rows have no implementation skip:

| Shape | Deletions on each side | Before rows | After rows |
| --- | --- | ---: | ---: |
| `root` | 1 through 25 inclusive | 25 | 25 |
| `root-with-launcher-state` | 1 through 26 inclusive | 26 | 26 |
| `staging` | 1 through 26 inclusive | 26 | 26 |

## Results

Windows 11 host with native custody access, 2026-11-29, Release build. The filter selects the
154 executor hard-stop rows and the 3 hook-order rows.

| Run | Before rows (77) | After rows (77) | Hook-order rows (3) |
| --- | --- | --- | --- |
| Clean | 77 passed | 77 passed | 3 passed |
| Before rows expect n instead of n-1 removed | 77 failed | 77 passed | 3 passed |
| Product hook moved after the delete disposition | 77 failed | 77 passed | 3 passed |
| Sources restored | 77 passed | 77 passed | 3 passed |

The hook-order rows do not notice a hook that runs after the disposition: the name still exists
while the delete is pending. The hard-stop before rows do notice it, because stopping the process
closes the handles and completes the delete.

The restored run of these 157 rows took 77 s of wall time, and the clean run 113 s.
No LinkedProbe process was left running.

The full `Nvt.Core.Tests` run passed 5,606 cases, skipped 16 and failed none. Whitespace format and
repo health `Verify` passed against the unchanged debt ledger.

Not covered: other file systems than the host volume, and power loss. A hard stop kills the
process; it does not prove what survives a machine crash.
