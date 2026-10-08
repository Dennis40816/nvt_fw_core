[繁體中文](Processes.zh-TW.md)

# Processes

## Breaking changes before 0.9.0

`ProcessInheritedHandle.EnvironmentVariable` is now `string?`.
The uninitialized default contains a null environment name and a zero handle.
`ProcessLaunchGate.StartContained` rejects that default before callbacks or process creation.
Validated constructor and `Parse` results retain their exact names and handle values.

Create bindings through the constructor or `Parse` before contained launch.
Guard the environment name when inspecting a potentially uninitialized binding.
Keep the parent-owned original handle alive through launch.
Tests cover default rejection before native work and unchanged valid binding behavior.

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Infrastructure/ExternalTools/BoundedProcessOutputReader.cs` (complete reader and output record).
- `src/NvtFwCombiner.Infrastructure/ExternalTools/ExternalProcessResult.cs` (lines 1–67: result, cleanup enum, and exceptions; product cleanup text in lines 69–127 stays in NFC).
- `src/NvtFwCombiner.Infrastructure/ExternalTools/ExternalProcessStartInfo.cs` (launch request, excluding `ToExecutedCommand()` in lines 36–40).
- `src/NvtFwCombiner.Infrastructure/ExternalTools/IExternalProcessRunner.cs` (complete interface).
- `src/NvtFwCombiner.Platform/Processes/WindowsSynchronousReadCancellation.cs` (complete cancellation mechanism).
- `tests/NvtFwCombiner.Infrastructure.Tests/ExternalTools/BoundedProcessOutputReaderTests.cs` (all 11 scenarios and complete helper support).

- `src/NvtFwCombiner.Platform/Processes/ProcessLaunchGate.cs` (complete gate and handle record, split into separate Core files).
- `src/NvtFwCombiner.Platform/Processes/WindowsContainedProcessStarter.cs` (complete native contained starter).
- `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/ProcessLaunchGateTests.cs` (all eight scenarios and helper behavior).
- `tests/NvtFwCombiner.TestSupport/TempWorkspace.cs` (temporary path, byte-writing, and bounded cleanup support; product prefix and repository path adapter stay in NFC).

- `src/NvtFwCombiner.Infrastructure/ExternalTools/SystemExternalProcessRunner.cs` (complete runner, cleanup timing, schedule, capacity, phases, and seams; helper types split into separate Core files).
- `src/NvtFwCombiner.Infrastructure/ExternalTools/SystemExternalProcessRunner.Invocation.cs` (complete invocation custody and terminal cleanup).
- `tests/NvtFwCombiner.Infrastructure.Tests/ExternalTools/SystemExternalProcessRunnerTests.cs` (all six scenarios and process identity/exit assertions; child fixtures use the shared test probe).

- `tests/NvtFwCombiner.Infrastructure.Tests/ExternalTools/SystemExternalProcessRunnerLifetimeTests.cs` (25 runner, scheduling, capacity, and cancellation methods with their nested helpers; the cleanup-text method remains in NFC).

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

The BCL-only, net8.0 `Nvt.Core.Processes` module owns external-process contracts and command execution, bounded UTF-16 diagnostics, Windows synchronous-read cancellation, and the single process-local launch gate. Launcher consumes contained creation and owns its readiness protocols and long-lived Jobs. Launcher.Transport retains its separate strict UTF-8 line reader.

## API

```csharp
public readonly record struct ProcessInheritedHandle
{
    public ProcessInheritedHandle(string environmentVariable, IntPtr handle);
    public string? EnvironmentVariable { get; }
    public IntPtr Handle { get; }
    public static ProcessInheritedHandle Parse(string environmentVariable, string handle);
}

public static class ProcessLaunchGate
{
    public static Process? Start(ProcessStartInfo startInfo);
    public static Process? StartContained(
        ProcessStartInfo startInfo, IReadOnlyList<ProcessInheritedHandle> inheritedHandles);
    public static Process? StartContained(
        ProcessStartInfo startInfo, IReadOnlyList<ProcessInheritedHandle> inheritedHandles,
        Func<bool> validateImmediatelyBeforeStart);
    public static bool TryClearInheritance(IntPtr handle);
}

public interface IExternalProcessRunner
{
    ValueTask<ExternalProcessResult> RunAsync(
        ExternalProcessStartInfo startInfo, CancellationToken cancellationToken);
}

public sealed partial class SystemExternalProcessRunner : IExternalProcessRunner
{
    public SystemExternalProcessRunner();
    public ValueTask<ExternalProcessResult> RunAsync(
        ExternalProcessStartInfo startInfo, CancellationToken cancellationToken);
}

public sealed class ExternalProcessStartInfo
{
    public ExternalProcessStartInfo(
        string executablePath, string workingDirectory,
        IEnumerable<string> arguments, TimeSpan timeout);
    public string ExecutablePath { get; }
    public string WorkingDirectory { get; }
    public IReadOnlyList<string> Arguments { get; }
    public TimeSpan Timeout { get; }
}

public sealed record ExternalProcessResult(
    int ExitCode, bool TimedOut, string StandardOutput, string StandardError)
{
    public ExternalProcessCleanup Cleanup { get; init; } = ExternalProcessCleanup.Complete;
}

public enum ExternalProcessCleanup
{
    Complete, TerminationUnconfirmed, OutputStreamHeldOpen, OutputReadFailed
}

public sealed class ExternalProcessCleanupCapacityException : Exception
{
    public ExternalProcessCleanupCapacityException(int inUseInvocations, int limit);
    public int InUseInvocations { get; }
    public int Limit { get; }
}

public sealed class ExternalProcessStartFailedException : Exception
{
    public ExternalProcessStartFailedException(Exception startException);
}
```

The internal API for contained-launch and external-runner consumers is `BoundedProcessOutputReader.ReadAsync(TextReader) : Task<string>`, `DrainProcessStreamAsync(TextReader, CancellationToken) : Task<BoundedProcessOutput>`, and `DrainAsync(TextReader, CancellationToken) : Task<BoundedProcessOutput>`. `BoundedProcessOutput` is an internal readonly record struct with `string Text` and `bool ReachedEndOfStream`. `WindowsSynchronousReadCancellation` remains internal, sealed, partial, disposable, and annotated `[SupportedOSPlatform("windows")]`.

The frozen assigned source defines no product-specific path or argument ceiling. Production invocation capacity is a fixed mechanism bound of eight, shared by all default runner instances. The reader bounds, terminal timing, and five-second suspended-child termination confirmation bound remain fixed. The launch request's only positive numeric parameter is `timeout`; the capacity exception reports its supplied integers without validating them. Product admission policy remains with the host application.

## Preserved behavior

| Mechanism | Frozen contract |
| --- | --- |
| Capture capacity | At most 65,536 UTF-16 characters. Output at the capacity is complete; capacity plus one is truncated. |
| Prefix | 32,768 characters before surrogate adjustment. |
| Tail | A 32,768-character pooled ring. The retained tail uses the remaining capture budget after prefix and marker. |
| Read buffer | Exactly 4,096 characters requested per read. |
| Marker | `"\n...[process output truncated]...\n"` exactly. |
| Scalar boundaries | Drop a prefix-ending high surrogate, skip a tail-starting low surrogate, and drop a tail-ending high surrogate only when producing truncated output. Complete output is returned character-for-character. |
| Counter and storage | Saturate the total-character counter at `long.MaxValue`; return both pooled buffers with `clearArray: true`. |
| Process drainage | `LongRunning | DenyChildAttach` on `TaskScheduler.Default`, with startup faults returned as faulted tasks. |
| Windows reads | The dedicated owning thread opens its own thread handle with `THREAD_TERMINATE` access (`0x0001`), performs synchronous reads, and registers stop cancellation. |
| Cancellation race | Check stop before and after registration; inline cancellation does not cancel unrelated I/O. Retry `CancelSynchronousIo` every 1 ms until the read completes, then join the callback before another read or handle close. |
| Stopped drainage | Return captured text and `ReachedEndOfStream == false`. Suppress read cancellation only if the stop token fired. Treat Windows low-word error 995 (`ERROR_OPERATION_ABORTED`) the same way only when stopped. |
| Other platforms | Keep `reader.ReadAsync`. A reader that ignores stop is not made interruptible by this mechanism. |
| Analyzer and platform rules | Preserve the source's platform guards, platform attribute, and justified CA1031/CA1849 suppressions. |

Launch validation remains executable path, working directory, argument sequence, then timeout. Paths use `ArgumentException.ThrowIfNullOrWhiteSpace`; arguments use `ArgumentNullException.ThrowIfNull`. Nonpositive timeouts throw `ArgumentOutOfRangeException` with `ParamName == "timeout"`, actual value, and `Timeout must be positive.`. Validation completes before enumerating arguments into a new array. Paths and argument elements are not normalized or newly validated.

Cleanup defaults to `Complete` and participates in result equality. Successful or protocol use of captured text requires complete cleanup; incomplete cleanup can still accompany error diagnostics. Observing the direct child's exit and both stream ends does not prove that every descendant stopped: a descendant holding neither stream is unobservable.

The capacity-refusal message is preserved verbatim:

```text
The external process runner is at its limit of {limit} invocations that are running or still cleaning up ({inUseInvocations} in use when the reservation was refused); a new run is refused. Restart the application.
```

The start-failure message remains `The external process could not be started ({startException.GetType().Name}).`; the original exception instance remains the inner exception. Passing a null start exception retains the source's dereference failure.

## Tests and source mapping

Reader and external-process contract coverage has 40 documented public test methods and 95 statically enumerated cases: 11 ported scenarios, 15 reader-boundary methods (49 cases), and 14 contract methods (35 cases). All inputs are synthetic. Tests pass `TestContext.Current.CancellationToken` directly or through a linked stop source where a token is accepted.

All source names, assertions, thresholds, `BeforeKernelReadReader`, `HeldOpenReader`, `CreatePattern`, and `AssertWellFormedUtf16` are retained. The three source `Production.Drain` calls now use `DrainProcessStreamAsync`. Test-support changes add the Core namespace, explicit xUnit import, XML documentation on overrides, and test-token wiring.

| Frozen source scenario | Core scenario in `BoundedProcessOutputReaderTests` |
| --- | --- |
| `ProductionDrainStartupFailureReturnsFaultedTask` | `ProductionDrainStartupFailureReturnsFaultedTask` |
| `ProductionDrainAlreadyStoppedReturnsWithoutReading` | `ProductionDrainAlreadyStoppedReturnsWithoutReading` |
| `ProductionDrainStopsWhenCancellationPrecedesKernelRead` | `ProductionDrainStopsWhenCancellationPrecedesKernelRead` |
| `SmallOutputRemainsExact` | `SmallOutputRemainsExact` |
| `StoppedDrainKeepsCapturedTextWithoutEndOfStream` | `StoppedDrainKeepsCapturedTextWithoutEndOfStream` |
| `CompletedDrainReportsEndOfStream` | `CompletedDrainReportsEndOfStream` |
| `ExactCaptureLimitRemainsExact` | `ExactCaptureLimitRemainsExact` |
| `FirstTruncatedCharacterRetainsExactPrefixAndTail` | `FirstTruncatedCharacterRetainsExactPrefixAndTail` |
| `MultipleRingWrapsRetainExactOrderedTail` | `MultipleRingWrapsRetainExactOrderedTail` |
| `TruncationDoesNotRetainUnpairedHighSurrogateBeforeMarker` | `TruncationDoesNotRetainUnpairedHighSurrogateBeforeMarker` |
| `TruncationDoesNotRetainUnpairedLowSurrogateAtTailStart` | `TruncationDoesNotRetainUnpairedLowSurrogateAtTailStart` |

Additional reader coverage:

- `CaptureConstantsRetainFrozenValues` pins the fixed capacity and marker.
- `CaptureBoundariesRetainExactExpectedText` covers 0, 1, 32,767/32,768/32,769 and 65,535/65,536/65,537 characters.
- `ReadBufferBoundariesUseFixedReadRequests` covers 4,095/4,096/4,097 characters and verifies all requests are 4,096.
- `SingleAndChunkedSendsRetainIdenticalOutput` compares complete and truncated delivery, 1/17/4,095/4,096/4,097-character chunks, and multiple ring wraps.
- `CompleteCapturePreservesSurrogatePairsAcrossBoundaries`, `TruncationPreservesPairsAroundReadAndPrefixBoundaries`, `TruncationPreservesPairsAroundTailStart`, and `TruncationHandlesSurrogatesAtTailEnd` cover scalar boundaries, including pairs across the tail ring wrap.
- `CompleteOutputPreservesSourceUtf16WithoutDecoding` characterizes unpaired source characters in complete output.
- `StoppedDrainRetainsTruncatedOutputWithoutEndOfStream` uses a deterministic all-text-served gate.
- `DrainAsyncRejectsNullReaderWhenAwaited` and `ReadAsyncRejectsNullReaderWhenAwaited` pin asynchronous null failures.
- `UnrequestedReadCancellationPropagates`, `OperationAbortedRequiresWindowsAndStoppedToken`, and `ProcessDrainUsesPlatformReadPath` pin failure filters and read-path selection.

Additional contract coverage:

- `StartInfoRejectsNullPaths`, `StartInfoRejectsBlankPaths`, `StartInfoRejectsNullArguments`, and `StartInfoPreservesValidationOrder` cover constructor failures and enumeration order.
- `StartInfoRejectsNonpositiveTimeout` and `StartInfoAcceptsPositiveTimeout` cover negative one tick, zero, positive one tick, and both representable extremes.
- `StartInfoCopiesArguments` covers list and array ownership; `StartInfoPreservesUnboundedNonblankValues` characterizes 511/512/513-character values and unchanged empty/null argument elements.
- `ResultDefaultsToCompleteAndUsesValueEquality` and `ResultEqualityIncludesAllObservedValues` cover all equality components.
- `CapacityExceptionPreservesPropertiesAndExactMessage` covers counts below/at/above a supplied capacity of eight and unvalidated zero/negative/extreme constructor values.
- `StartFailurePreservesTypeTextAndInnerException`, `StartFailureWithNullExceptionPreservesSourceFailure`, and `CleanupEnumPreservesExactNamesAndOrder` pin the remaining contracts.

The before-kernel-read test uses a real Windows anonymous pipe and deterministic gates. It calls `Assert.Skip` on non-Windows systems. Such a skip supplies no native evidence. The contained-launch tests below add actual child and inherited-handle evidence. Job membership is a separate lifetime concern.

## Contained creation behavior

`Start` and both `StartContained` overloads serialize through the same private static `object` lock. Only starts routed through this API participate in that gate. Ordinary start checks `startInfo` before locking. Contained start checks `startInfo`, `inheritedHandles`, and `validateImmediatelyBeforeStart` for null, in that order, then rejects any uninitialized inherited handle with `ArgumentException` (`ParamName == "inheritedHandles"`). All of this happens before locking. The two-argument overload supplies a callback returning true.

`ProcessInheritedHandle` rejects blank names, then names containing `=`, then handles whose signed `ToInt64()` value is nonpositive. Names and values are otherwise unchanged. `Parse` uses `NumberStyles.None` and invariant culture; a parse failure uses `ParamName == "handle"` and `Inherited handle must be a positive decimal value.`. Parsed zero reaches the constructor's nonpositive predicate. Record equality includes the exact name and handle. The default record retains null/zero fields.

On Windows, one internal `WindowsContainedProcessStarter` performs these steps:

1. Reject shell execution, any redirected standard stream, or a non-fully-qualified executable; then alternate username/password credentials; then mixed `Arguments` and `ArgumentList`; then duplicate binding names under ordinal case-insensitive comparison. These checks precede duplication and final validation.
2. Duplicate each original handle in declared order with the same access and inheritance enabled. Copy the environment with ordinal case-insensitive keys and bind each declared name to its duplicate's invariant decimal value. The caller's environment remains unchanged. Originals remain owned by the caller; callers clear their inheritance through `TryClearInheritance` when needed.
3. With a nonempty allowlist, initialize exactly one `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` containing exactly those duplicate handles and enable extended startup information. With no handles, disable inheritance and use ordinary startup information. The mechanism adds no handle-count ceiling.
4. Sort environment entries ordinally ignoring case, omit null values, preserve empty values, and append the frozen NUL terminators. Quote the executable and each `ArgumentList` element with the frozen space/tab/quote and backslash rules; append a raw `Arguments` string unchanged when there is no argument list.
5. Prepare native buffers, invoke final custody validation while the gate is held, and return null without creating a child when it refuses. Validation exceptions propagate after cleanup. Call `CreateProcessW` with suspended and Unicode-environment flags, plus no-window when requested. Look up the managed process and acquire its handle before resuming the native thread.
6. On a failure after creation and before resume, dispose any managed process, terminate the suspended child with exit code 1, and wait at most 5,000 ms for confirmation. The original failure propagates on confirmed termination; termination or confirmation failure wraps it with the native error or bounded timeout.
7. In `finally`, close native thread/process handles, delete the attribute list, free handle-list/environment/command-line buffers, and dispose all duplicates in frozen order. The frozen routine does not free the attribute-list allocation itself; that allocation behavior is preserved.

The native rejection messages remain exactly:

- `Contained process starts require an absolute executable, shell disabled, and no redirected streams.`
- `Contained process starts do not support alternate credentials.`
- `Contained process starts cannot mix Arguments and ArgumentList.`
- `Inherited handle environment names must be unique.` (`ParamName == "inheritedHandles"`).

Post-create failures retain `Contained process creation failed and the suspended child could not be terminated.`, `Contained process creation failed and child termination could not be confirmed.`, and `The suspended child did not terminate within the bounded confirmation deadline.`. Other native failures remain `Win32Exception` instances using the last native error.

Outside Windows, contained start runs final validation under the same gate and then calls `Process.Start`, or returns null. Windows-specific restrictions and inheritance allowlists do not apply there. `TryClearInheritance` returns true on other platforms; on Windows it rejects 0 and -1 before `SetHandleInformation`. Native members retain their Windows platform attributes and mutable-layout warning suppression. Core replaces the net9 lock with `object` and stores the same quoting characters in a static readonly array for analyzer compatibility.

### Intentional differences from the frozen source

Core differs from the frozen NFC contained start in one place. It rejects input that the source accepted or failed late.

- An uninitialized inherited handle (null name or zero handle) now throws `ArgumentException` on every platform. The check runs before the callback and before any native work.
- On Windows the source tried to duplicate the zero handle and failed with `Win32Exception`. Outside Windows the source ignored the handle and started the process. Both cases now throw.
- The check also runs before the native configuration checks, so `UseShellExecute = true` with a default handle now throws `ArgumentException` instead of `InvalidOperationException`.
- Valid bindings, callback order and every other rejection keep the frozen behavior.

## Contained launch test mapping

Contained launch coverage adds 47 documented public test methods and 107 statically enumerated cases: eight ported scenario methods and the input, gate, and preparation boundary methods below.

Every frozen launch-gate scenario retains its name in `ProcessLaunchGateTests`. Tests use the shared BCL-only probe; no product probe project is copied.

| Frozen and Core scenario | Shared probe mode |
| --- | --- |
| `ContainedChildExcludesUnstatedAmbientInheritableHandle` | `ambient-pipe` |
| `InvalidAllowlistHandleStartsNoChildAndDoesNotPoisonNextLaunch` | `ambient-pipe` |
| `InheritedHandleRejectsEveryNonPositiveValue` | No child |
| `ParallelContainedStartsDoNotCrossInheritHandles` | `contained-isolation` |
| `ContainedStartPreservesUnicodeArgumentsAndEnvironment` | `arguments-environment` |
| `FinalValidationRejectsChangeAfterNativePreparation` | `ambient-pipe` |
| `ValidationWaitsForGateAndRejectsChangedStateBeforeStart` | `arguments-environment` |
| `PostCreateFailureTerminatesSuspendedChildAndReleasesPhysicalPipe` | `contained-isolation` |

`ProcessSerialCollection` serializes gate tests sharing process state. Original physical-pipe assertions, `first`/`second` payloads, Unicode values, and two-second/200-ms thresholds remain. Otherwise unbounded fixture waits use a 30-second bound. Windows-only cases report an xUnit skip elsewhere.

`ProcessInheritedHandleTests` adds constructor check-order, blank/equals names, exact unnormalized names, zero/negative/positive-one handles, signed pointer extremes, invariant decimal syntax, parser values immediately below/at/above `long.MaxValue`, and record equality. There is no adjustable positive launch-gate limit parameter.

`ProcessLaunchGateBoundaryTests` adds public/internal null order, ordinary `exit` starts, final refusal without a marker, callback failure and gate reuse, ordinary start serialization, native validation order, default-record rejection before the callback runs, exact zero/one/two allowlists, duplicate-original bindings, unchanged parent environment, native-create cleanup, and invalid/real-pipe/non-Windows inheritance clearing. A complete probe-folder copy renames only the apphost and exercises raw arguments.

`WindowsContainedProcessStarterContractTests` pins empty and nonempty command lines, raw arguments, zero/one/two backslashes, space/tab/quote/newline edges, Unicode, ordinal case-insensitive environment sorting, null omission, empty values, zero/one/two environment entries, and exact terminators. It pins the fixed 5,000-ms confirmation constant; that deadline has no caller-supplied below/above input. The native post-create scenario proves a suspended child cannot run and its physical pipe closes within the original two-second threshold.

`OptionalPowerShellQuotedArguments` separately exercises `two words`, `quote"inside`, and `trail\` through PowerShell. An unavailable executable or different PowerShell argument interpretation reports a skip and does not alter the contained starter.

## External command execution

`SystemExternalProcessRunner` is the sole owner of one external command invocation. Creation uses only `ProcessLaunchGate.Start(CreateProcessStartInfo(startInfo))`; output uses the existing `BoundedProcessOutputReader.DrainProcessStreamAsync`. Launcher owns READY, ADMITTED, protocol Jobs, and admission deadlines separately.

`RunAsync` checks null input, caller cancellation, then atomically reserves capacity before creating a process. Capacity refusal starts no process and reports the count observed during reservation. All start failures release the slot. `Win32Exception` becomes `ExternalProcessStartFailedException`; other failures propagate unchanged. A null process retains `External process did not start.`. Start information preserves the executable, working directory, and every ordered `ArgumentList` entry, with shell execution disabled, no console window, and both output streams redirected.

| Mechanism | Frozen runner contract |
| --- | --- |
| Production capacity | Eight invocations across all default instances, including detached cleanup; no public capacity setting. |
| Total terminal deadline | Five seconds from the terminal signal, shared by every terminal wait. |
| Held-output grace | Two seconds after natural exit. |
| Reader-stop reserve | Final one second inside the total deadline. |
| Cancellation callback | Signals only; it performs no OS termination or resource disposal. |
| Termination | One background work item per invocation using `Kill(entireProcessTree: true)`. |
| Exit and output | Observe `WaitForExitAsync` and drain both streams concurrently, retaining bounded partial output. |
| Production observer | Null; ordering and fault seams are internal. |

Both streams and exit observation start before cancellation registration and `Started`. Caller cancellation observed at terminal selection wins over a simultaneous exit or timeout. Natural exit waits for streams or cancellation until the grace point; a still-open stream starts termination, and `OutputHeldAfterExit` records grace expiration without cancellation. Other terminal signals start termination immediately. Termination, exit, and stream settlement share the reader-stop point. Unsettled readers receive `CancelAsync`, and all terminal work plus cancellation dispatch share the final deadline.

Cleanup priority remains `TerminationUnconfirmed`, `OutputStreamHeldOpen`, `OutputReadFailed`, then `Complete`. Aggregate, Win32, invalid-operation, and unsupported termination failures use the existing external cleanup outcome. Timeout returns exit code -1 and `TimedOut == true`; natural exit retains the direct child's exit code. Cancellation throws with the caller's token and exact text `The external process run was canceled; observed cleanup: {cleanup}.`.

`Release` retains the original capacity reservation while any tracked work remains. After settlement, every late fault is observed, stdout, stderr, process, reader-stop source, and exit-observation source are each disposed independently, then capacity returns. Successful disposal publishes `ResourcesReleased`; any disposal failure publishes `ResourcesReleaseFailed`. The net8.0 port uses a private `object` for the invocation gate and retains both lock statements. No launcher Job or READY state enters this runner. A descendant holding neither redirected stream remains outside its observation contract.

The internal contract consists of `ExternalProcessCleanupTiming`, `CleanupSchedule`, `ExternalProcessCapacity`, `ExternalProcessRunnerPhase`, and `ExternalProcessRunnerSeams`. Timing validation retains this order: positive deadline, positive grace, positive reserve, grace plus reserve no greater than deadline, then deadline no greater than `int.MaxValue` milliseconds. A sum violation has `ParamName == "HeldOutputGrace"`. Scheduling retains `(long)(span.TotalSeconds * Stopwatch.Frequency)` and absolute timestamps from the terminal signal. Internal test capacity must be positive; production remains eight.

## Runner tests and source mapping

All six frozen scenarios retain their names, assertions, and execution/exit observation thresholds in `SystemExternalProcessRunnerTests`. The frozen class has no collection attribute; that choice is retained. The shared probe contract is [the probe README](../../../tests/Nvt.Core.TestProbe/README.md). No private child harness or probe change is needed.

| Frozen scenario and Core method | Shared probe and retained evidence |
| --- | --- |
| `CreateProcessStartInfoIsHeadlessAndShellFree` | No child; flags, executable, working directory, and argument order. |
| `RunAsyncTranslatesOperatingSystemStartFailureToTypedExceptionAndReleasesCapacity` | Missing executable, then `exit`; typed Win32 failure, zero capacity, successful reuse. |
| `RunAsyncCancellationKillsChildProcessBeforeThrowing` | `tree-root-wait`; capture root and child PID/start-time identity before cancellation, 30-second execution timeout, three-second exit observation for each process. |
| `RunAsyncTimeoutKillsChildProcessBeforeReturning` | `tree-root-wait`; identities before a ten-second timeout, timeout flag, exit -1, and three-second exit observation. |
| `RunAsyncBoundsAndDrainsBothOutputStreams` | `dual-output-exit`; 131,072 characters on each stream plus `OUT-END`/`ERR-END`, ten-second timeout, exact capture length, prefix, truncation marker, and suffix assertions. |
| `RunAsyncTimeoutRetainsBoundedPartialOutputAfterKill` | `dual-output-wait`; 131,072 characters per stream plus `OUT-PARTIAL-END`/`ERR-PARTIAL-END`, ten-second timeout (NFC used two seconds; a loaded host can need more than one second to start the probe, and the probe still waits 30 seconds), exit -1, timeout flag, exact bounded lengths and suffixes. |

`SystemExternalProcessRunnerBoundaryTests` adds 23 methods and 56 statically enumerated cases:

- Default timing, eight-slot production capacity, null observer, and every phase name/value.
- Exact `Schedule(1000)` timestamps and fractional tick conversion, including the maximum compatible deadline.
- Zero and negative values for each positive timing field and private capacity parameter; minimum positive timing; validation order and source overflow behavior.
- One tick below/at/above grace-plus-reserve and maximum deadline boundaries; constructor timing validation.
- Private capacities one, seven, eight, nine, and `int.MaxValue`, including exact admission, refusal, observed count, release, and reuse.
- Null seams, null-input precedence, already-canceled input with empty/full capacity, and a private one-slot refusal before attempting a missing executable.
- Exact launch arguments/working directory; native `exit --exit-code 7`, phase ordering, and capacity release observed within five seconds.
- Separate real stdout/stderr routing and capture lengths 65,535/65,536/65,537.
- Shared OS termination failures classified as `TerminationUnconfirmed`, one termination work item, and private capacity release.

Tests use synthetic data, unique temporary folders for tree markers, and test-context cancellation tokens. Native cases execute Windows processes and report an xUnit skip on unsupported systems. Marker polling retries only transient sharing violations inside its fixture bound; PID plus start time prevents emergency cleanup from killing a reused PID. These tests establish external-command behavior; long-lived Job evidence belongs to Launcher.

## Runner lifetime test mapping

Core contains 25 of the 26 frozen lifetime scenarios. These runner, scheduling, capacity, and cancellation methods retain their names and source order in `SystemExternalProcessRunnerLifetimeTests`. NFC keeps `CleanupDiagnosticsDescribeOnlyTheObservation` and its three theory cases because they test NFC's product formatter, `ExternalProcessCleanupText.Describe`. Core has no cleanup-text API and does not copy that formatter; the exact wording assertions remain in NFC.

| Frozen source method | Core method or retained owner | Shared probe and frozen evidence |
| --- | --- | --- |
| `CancelReturnsAtOnceAndRunEndsWithinDeadlineWhileTerminationBlocks` | Same name | `silent-wait`; blocked termination, cancellation returns before one second, bounded run, late release. |
| `TimeoutWithBlockedTerminationReturnsUnconfirmedWithinDeadline` | Same name | `silent-wait`; 300-ms timeout, blocked termination, unconfirmed cleanup. |
| `RefusedTerminationIsClassifiedAsUnconfirmed` | Same name | `silent-wait`; aggregate, Win32, invalid-operation, and unsupported refusal data. |
| `RefusedTerminationOnCancellationEndsCanceled` | Same name | `silent-wait`; refused termination yields caller cancellation. |
| `TerminationWithoutObservedExitIsBoundedAndUnconfirmed` | Same name | `silent-wait`; ignored termination, one bounded cleanup deadline. |
| `CancellationRightAfterTimeoutSignalEndsCanceled` | Same name | `silent-wait`; cancellation injected at `TimeoutSignaled`. |
| `CancellationRightAfterExitSignalEndsCanceled` | Same name | `exit`; cancellation injected at `ExitSignaled`. |
| `CancellationAfterTerminalDecisionKeepsResult` | Same name | `exit`; cancellation injected at `Returning` preserves the result. |
| `UncooperativeReaderIsDetachedAtDeadlineAndObservedLater` | Same name | `exit`; a held reader detaches, then faults and releases custody. |
| `ReaderFaultWithoutCancellationIsOutputReadFailed` | Same name | `exit`; reader fault is a cleanup classification. |
| `ReaderStartupFaultIsOutputReadFailed` | Same name | `exit`; production drain startup fault and single-slot release. |
| `ReaderFaultWithCancellationEndsCanceled` | Same name | `silent-wait`; cancellation takes precedence over reader failure. |
| `ExitObservationFaultIsUnconfirmedOrCanceled` | Same name | `silent-wait`; both original cancellation theory values and exit-observer failure. |
| `HeldOutputAfterNaturalExitIsBoundedAndReported` | Same name | `tree-root-exit`; production timing and output-held phase. |
| `CancellationDuringHeldDrainEndsCanceledWithinDeadline` | Same name | `tree-root-exit`; cancellation at direct-root exit under production timing. |
| `OrphanHoldingOutputAfterTimeoutIsBoundedAndReported` | Same name | `orphan-chain-root`; leaf PID, `.middle`, and `.ready`; parent-exited handshake before the three-second timeout. |
| `OrphanHoldingOutputAfterExitStopsWithoutDetachingAndKeepsText` | Same name | `orphan-chain-exit --stdout-text before-reader-stop`; the middle exits before the root exits naturally with code 0, while the surviving leaf holds both streams. |
| `DescendantWithoutRedirectedStreamIsNotObserved` | Same name | `detached-descendant-root`; complete cleanup can coexist with an invisible live descendant. |
| `CleanupScheduleKeepsReaderStopWithinTheSingleDeadline` | Same name | No child; exact production schedule and reserve inside the deadline. |
| `CleanupDiagnosticsDescribeOnlyTheObservation` | NFC product formatter | Three theory cases test `ExternalProcessCleanupText.Describe`; NFC retains the exact sentence fragments and both assertions against attributing an unobserved cause. |
| `CancelWithinGuardFailsPromptlyOnBlockingCancel` | Same name | No child; one-second blocking-cancel guard and ten-second reporting ceiling. |
| `DetachedCleanupIsBoundedAndRefusesNewRunsAtTheLimit` | Same name | `silent-wait`, then `exit`; single-slot refusal and reuse after late termination. |
| `CapacityIsAHardCapUnderConcurrentStarts` | Same name | `exit`; barrier races eight dedicated threads against three slots for ten iterations. |
| `TryReserveIsAtomicUnderContention` | Same name | No child; 32 dedicated contenders, eight slots, 200 rounds. |
| `DisposalFailureStillReturnsSlotAndSignalsFailure` | Same name | `exit`; all five disposals, failure publication, and slot reuse. |
| `DetachedDisposalFailureStillReturnsSlotAndSignalsFailure` | Same name | `silent-wait`; detached termination followed by five disposals and failure publication. |

The class retains its absence of a collection attribute. All native cases require actual Windows processes and use xUnit skip on other operating systems. The schedule, atomic reservation, and blocking-cancel guard cases remain portable. `TestToken` is `TestContext.Current.CancellationToken`; phase hooks, blocked termination, held readers, and dedicated-thread barriers drive ordering. Every original run is awaited under the 40-second watchdog, and clocks start before triggering calls, including `Cancel()`.

| Frozen test limit | Value and meaning |
| --- | --- |
| Helper lifetime | 30 seconds; shared `silent-wait` and descendant modes outlive every terminal bound. |
| Watchdog | 40 seconds for run and phase waits. |
| Scheduling margin | Four seconds beyond the selected cleanup deadline. |
| Cancellation return | Strictly less than one second, measured before `Cancel()`. |
| Fast cleanup | Deadline 1,500 ms; held-output grace 300 ms; reader-stop reserve 500 ms. |
| Production cleanup | Deadline five seconds; grace two seconds; reserve one second. |
| Timeout fixtures | 300 ms for slow-runner cases; three seconds for the orphan-chain parent-exited handshake; all original 30-second and 60-second launch timeouts remain unchanged. |
| Guard fixture | One-second blocking-cancel bound; report strictly before ten seconds. |
| Capacity bursts | Three slots/eight starts/ten iterations; eight slots/32 contenders/200 rounds. |
| Marker and contention polling | Original 100-ms readiness poll and 20-ms predicate poll. The probe separately uses its documented five-second handshake and 10-ms poll. |

The 300-ms slow-runner timeout bounds the run rather than proving that probe startup has finished. A loaded Windows host can take more than one second to start the probe. The three-second orphan-chain threshold also requires the leaf marker and actual middle exit before timeout; that threshold is preserved. Neither wait is shortened or widened.

The source helpers `PhaseRecorder`, `TerminationSeam`, `ThrowingDisposal`, `FirstReader`, `CancelWithinAsync`, `SpinUntilAsync`, `HasExited`, `ReadPidAsync`, `WaitForFileAsync`, and `KillById` retain their mechanisms. Only launch setup uses the shared probe instead of shell scripts. Probe inputs are arguments, and markers live in the Processes `TestWorkspace` under the system temporary folder. The orphan-chain marker contains the leaf PID; `.middle` contains the middle PID, and `.ready` appears after that process exits. The fixtures wait for `.ready` before reading the PIDs, then observe the middle's exit before awaiting the run.

`orphan-chain-exit` writes and flushes `before-reader-stop` before starting the middle process. The middle starts a leaf that inherits stdout and stderr, then exits; the root exits naturally with code 0 after the ready marker. On Windows, the retained line is exactly `before-reader-stop\r\n`. The leaf remains alive when the run returns with `OutputStreamHeldOpen`; reader stop preserves the line, avoids detachment, and releases resources and capacity. The fixture terminates the leaf in `finally`.

`Complete` still observes only direct-child exit and both stream ends. A detached descendant holding neither redirected stream is invisible; complete cleanup is not proof that the process tree is empty. Cleanup uncertainty precedence, cancellation precedence, late fault observation, all five disposal attempts, failure publication, and release only after settlement remain unchanged.

`SystemExternalProcessRunnerLifetimeBoundaryTests.DetachedReadersRetainExactCapacityUntilLateSettlement` adds 12 native cases: capacities one, two, three, and eight, each with late reader completion, fault, or cancellation. Deterministic gates retain the real exited process's custody. The cases admit at one below the capacity, fill the exact capacity, refuse the next request without consuming another slot, release exactly one settled slot, reuse it, and finally return to zero. Existing `SystemExternalProcessRunnerBoundaryTests` supplies zero/negative positive-parameter cases, one-tick minima, grace-plus-reserve below/at/above the deadline, and deadline below/at/above `int.MaxValue` milliseconds. Fixed fixture waits are scheduling bounds, not caller-adjustable positive-limit parameters.

## NFC ownership and adoption

NFC keeps `ExternalProcessCleanupText`, its issue codes and product wording, the `ToExecutedCommand` audit adapter, trust, manifest validation, staging, routing, firmware behavior, schemas, golden evidence, and release authority. Mechanism comments no longer carry private record identifiers. Executable predicates, limits, and messages are unchanged.

NFC adopts this module in its own pull request, separate from this extraction. All contained launcher callers use `StartContained(startInfo, inheritedHandles, validateImmediatelyBeforeStart)`; ordinary external-tool starts use `Start(startInfo)`. Raw process creation stays in Processes rather than Launcher. Lifetime evidence must pass before the adoption closes.

NFC deletes its generic external runner, invocation helpers, reader, synchronous cancellation, launch gate, inherited-handle record, and contained-starter copies only after every caller uses the verified Processes package and the adoption proves zero difference: frozen behavior, native lifetime, and the required UI evidence. NFC removes its Platform copy only after its remaining Platform callers move and the host structure trial passes. NFC keeps its historical executor evidence.

The UI comparisons use the shared environment manifest and require zero changed decoded pixels. Where they apply, the comparisons also cover the complete output bytes and the event traces and record each artifact's SHA-256. The eight legacy font values stay unchanged.

NFC downloads verified, versioned nupkg files at build time through its `core-packages.json` and uses exact `[x]` versions, source mapping, lock files, and locked restore. The manifest records each package's Release tag and SHA-256. NFC owns its package references, version pins, source mapping, and lock files. NFC never adds a ProjectReference to a Core checkout.

Core and NFC keep independent versioned releases.
