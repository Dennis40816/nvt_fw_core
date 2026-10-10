# Nvt.Core.TestSupport

English | [繁體中文](https://github.com/Dennis40816/nvt_fw_core/blob/main/tests/Nvt.Core.TestSupport/README.zh-TW.md)

A packable net10.0 library for deterministic tests. It inherits the Core version
from Directory.Build.props and has no dependency on Nvt.Core, xUnit or Avalonia.
Reference this project or package **only from test projects**. Do not create new
copies of clocks or workspace helpers; extend the shared implementation and its
tests when another consumer needs behavior.

## ManualTimeProvider

Construct ManualTimeProvider with an explicit DateTimeOffset start. Time is
normalized to UTC; LocalTimeZone is UTC and the initial timestamp is zero.
TimestampFrequency is fixed at TimeSpan.TicksPerSecond, so inherited
GetElapsedTime reports the same elapsed duration as GetUtcNow.

Advance rejects negative deltas and UTC overflow. Timers fire synchronously on
the advancing thread at their due timestamps. One advance fires every elapsed
period, in due order; equal timestamps follow scheduling order. Infinite due time
disables a timer. Zero or infinite period makes it one-shot. Other negative timer
durations are rejected. Change is relative to current manual time and returns
false after disposal. Pending disposal cancels future callbacks.

Callbacks execute outside the clock lock and can read time or create, change
and dispose timers. Recursive and concurrent Advance calls throw
InvalidOperationException. Callback exceptions propagate and stop the clock at
that callback's due time; a later Advance can resume. DisposeAsync on a timer
waits for an already claimed callback to finish. A callback must not synchronously
wait for its own asynchronous disposal.

Task.Delay(delay, provider, token) and CancellationTokenSource(delay, provider)
use these timers. Outcomes require no real-time sleeps. Use bounded signal waits
with TimeProvider.System only for watchdogs, never to decide the tested outcome.

## TestWorkspace

Create returns an owned, unique OS-temp directory with a short nvt- name.
RootPath is absolute. GetPath resolves a fixture path and creates no files.
Both slash forms are separators. It rejects empty paths, rooted/drive/UNC forms,
colons (including Windows alternate data streams), every parent segment, and
ambiguous segments made only of dots/spaces. A single dot names the root;
ordinary names such as ..fixture are allowed. Path validation is lexical:
fixtures must not create links or junctions to data outside the root.

TestWorkspace implements IDisposable and IAsyncDisposable: use using / await using.
Dispose and DisposeAsync remove synthetic fixture data and are idempotent,
including when the directory was already removed or cleanup failed. IOException and
UnauthorizedAccessException receive at most ten deletion attempts within a
500 ms monotonic retry budget, with waits of at most 50 ms between attempts.
An OS filesystem call already in progress cannot be interrupted by this budget.
Failure throws IOException naming the retained path, preserving the underlying
exception. Later disposals report the same failure without another retry budget;
the retained path is available for manual cleanup after resolving the cause.
DisposeAsync performs the same synchronous bounded cleanup. Internal deletion,
wait and elapsed-budget hooks let tests exercise retries without sleeping.
Production cleanup itself waits in real time, at most 50 ms between attempts.

The helpers initialize no UI and contain no product data. The representative
Core Processes tests consume this project. Existing Files, Startup and Progress
helpers, including the private StartupTraceTests and ThrottledProgressTests
clocks, remain for migration after release. The private LinkedProbeWorkspace in
the Launcher transport tests is another workspace copy and also remains.

## SignalWait

SignalWait is a one-shot signal for a test to set and wait for. It replaces
Task.Delay and Thread.Sleep as a way to wait for a result. Set returns true for
the first call and false after that; waiters continue on another thread, never
inside Set. WaitAsync waits for the signal, the token, or the watchdog.

The watchdog only prevents a hang. It is finite and positive (default 30 s,
DefaultWatchdog) and never decides a result. When it expires, WaitAsync throws
TimeoutException and the message names the signal. A token cancellation stays an
OperationCanceledException. The watchdog clock is TimeProvider.System unless the
test passes another one, for example a ManualTimeProvider, so a test can prove
the expiry without waiting. The static WaitAsync(Task, name, ...) puts the same
watchdog around any task and passes the task's own failure through unchanged.

## ChildProcessFixture

ChildProcessFixture.Start runs an executable without a shell. It captures
standard output and standard error together, in arrival order, up to a
character limit (default 64 KiB, OutputTruncated tells that output was cut).
Standard input is closed at once. A watchdog (default 60 s) ends the process
tree if the child does not exit; WaitForExitAsync then throws TimeoutException
and WatchdogExpired is true. WaitForOutputAsync waits until the output contains
a text, and fails when the output ends without it. KillTree ends the tree on
request. Dispose and DisposeAsync end the whole tree, wait a bounded time for
the root to exit, and release the handle; they are idempotent. A child that
stays alive holds files open and makes the next test fail at random, so start
every child through the fixture. The tree kill reaches descendants while their
parent is alive; a descendant that outlives its parent is not reachable.

## Baseline and deliberate differences

The helpers replace `tests/Nvt.Core.Tests/Processes/ManualTimeProvider.cs` and
`tests/Nvt.Core.Tests/Processes/TestWorkspace.cs` of repository
Dennis40816/nvt_fw_core (branch `main`, the PR #147 clock). The two files are
identical at commit d3f0a1ddb467b0abb1cb832b81b0bf6da69ef559 and at the parent
of this change, f90900bbb04f84e590aa77dc47b6e04b7a77d9c6. Compare with
`git show <commit>:<path>`. These behaviors differ on purpose, and the migrated
Core tests do not depend on the old ones:

Clock:

- LocalTimeZone is UTC. The old clock inherited the machine zone.
- The constructor needs an explicit start. The old clock used a fixed origin.
- Change after disposal returns false. The old clock re-armed the timer.
- `Change(Infinite, period)` replaces the period. The old clock kept the old one.
- A timer that is disposed after the clock selected it still runs its claimed callback. The old clock skipped one-shot timers only. Periodic timers still ran. No test pins this one: it needs a race between selection and invoke.
- Timer DisposeAsync waits for a callback that is already running. The old one returned at once.
- Recursive or concurrent Advance throws InvalidOperationException. The old clock allowed it.
- Invalid timer durations and an advance beyond the UTC range throw. A period that would overflow the timestamp range fires once, then stops. The old clock wrapped and fired again and again.
- Several waiters can be pending together (internal WhenPendingAsync). The old clock kept only the last one, and the first never completed.
- A periodic re-arm signals a waiter. A canceled waiter is removed.

Same as the old clock: a callback exception leaves time at that callback's due
time, and a timer disposed by an earlier callback does not fire.

Workspace:

- Cleanup runs on every OS, with at most ten attempts in 500 ms, and throws IOException naming the path.
- A root outside the OS temp directory, or the temp directory itself, fails at construction. The prefix is nvt-.
- GetPath normalizes with Path.GetFullPath (a/./b becomes a/b, a single dot is the root). It rejects empty and whitespace paths, colons, parent segments and segments of only dots or spaces. A backslash is a separator on every OS.

The package is built with the Core projects but is not packed by
`scripts/pack.ps1` (it packs `Nvt.Core` and `Nvt.Core.Avalonia` by name), so the
`core-v*` release does not publish it. Publishing this package is a release
follow-up.
