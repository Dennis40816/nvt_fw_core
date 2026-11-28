<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

# Launcher recovery hard-stop verification

Only tests and documentation change. Product source, public product APIs,
project references, health ledgers, CHANGELOG and ROADMAP are unchanged.

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
evidence and writer custody, and blocks in `AfterDelete(n)` with the operation still active.
The parent independently lists all 25/26 deletion paths, including files, directories,
state(s), staging container and final marker. It compares the complete surviving
names/types and file hashes, then creates fresh stores and an executor over disk.
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

## Deliberately skipped rows

All 154 executor rows remain enumerated. Exactly these 77 rows retain an implementation skip:

| Shape | Side | Deletions | Rows |
| --- | --- | --- | ---: |
| `root` | `before` | 1 through 25 inclusive | 25 |
| `root-with-launcher-state` | `before` | 1 through 26 inclusive | 26 |
| `staging` | `before` | 1 through 26 inclusive | 26 |

There is no exact pre-delete callback. `AfterDelete(n-1)` runs before additional
revalidation/custody work and cannot prove the requested `BeforeDelete(n)` boundary.
The first deletion similarly follows preparation and validation inside `ExecuteAsync`.
Adding a product seam is outside this change. These rows must not be counted as evidence.

## Results

Windows 11 host with native custody access, 2026-11-29, Release build.

| Run | Setup rows (10) | Executor rows (154) |
| --- | --- | --- |
| Hard-stop rows, clean | 10 passed | 77 passed, 77 skipped (before-delete) |
| One assertion inverted per group | 10 failed | 77 failed, 77 skipped |
| Sources restored | 10 passed | 77 passed, 77 skipped |

The inverted assertions were the application-state presence check of the setup rows and the
expected surviving prefix of the executor rows (`deletion - 1`). Both run after the child is stopped.
No LinkedProbe process was left running.

Cost: the 87 child rows took about 48 s of wall time in the clean run and 74 s in the restored run.
The slowest single row took 2.7 s.

The full `Nvt.Core.Tests` run passed 5,525 cases, skipped 93 and failed none. Before this change
the same suite skipped 180; the difference is these 87 rows. Whitespace format and repo health
`Verify` passed.

Not covered: the 77 before-delete rows, other file systems than the host volume, and power loss.
A hard stop kills the process; it does not prove what survives a machine crash.
