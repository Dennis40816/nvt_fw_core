[繁體中文](Processes.zh-TW.md)

# Processes

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

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

The BCL-only, net8.0 `Nvt.Core.Processes` module owns external-process contracts, bounded UTF-16 diagnostics, Windows synchronous-read cancellation, and the single process-local launch gate. Launcher consumes contained creation and owns its readiness protocols and long-lived Jobs. Launcher.Transport retains its separate strict UTF-8 line reader.

## API

```csharp
public readonly record struct ProcessInheritedHandle
{
    public ProcessInheritedHandle(string environmentVariable, IntPtr handle);
    public string EnvironmentVariable { get; }
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

The frozen assigned source defines no product-specific path, argument, or invocation-admission ceiling. It defines the fixed reader bounds below and the five-second suspended-child termination confirmation bound. The launch request's only positive numeric parameter is `timeout`; the capacity exception reports its supplied integers without validating them. Product admission policy remains with the host application and the external runner contract.

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

`Start` and both `StartContained` overloads serialize through the same private static `object` lock. Only starts routed through this API participate in that gate. Ordinary start checks `startInfo` before locking. Contained start checks `startInfo`, `inheritedHandles`, and `validateImmediatelyBeforeStart`, in that order, before locking. The two-argument overload supplies a callback returning true.

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

`ProcessLaunchGateBoundaryTests` adds public/internal null order, ordinary `exit` starts, final refusal without a marker, callback failure and gate reuse, ordinary start serialization, native validation order, default-record duplication failure, exact zero/one/two allowlists, duplicate-original bindings, unchanged parent environment, native-create cleanup, and invalid/real-pipe/non-Windows inheritance clearing. A complete probe-folder copy renames only the apphost and exercises raw arguments.

`WindowsContainedProcessStarterContractTests` pins empty and nonempty command lines, raw arguments, zero/one/two backslashes, space/tab/quote/newline edges, Unicode, ordinal case-insensitive environment sorting, null omission, empty values, zero/one/two environment entries, and exact terminators. It pins the fixed 5,000-ms confirmation constant; that deadline has no caller-supplied below/above input. The native post-create scenario proves a suspended child cannot run and its physical pipe closes within the original two-second threshold.

`OptionalPowerShellQuotedArguments` separately exercises `two words`, `quote"inside`, and `trail\` through PowerShell, with its ten-minute checkpoint. An unavailable executable or different PowerShell argument interpretation reports a skip and does not alter the contained starter.

## NFC ownership and adoption

NFC keeps `ExternalProcessCleanupText`, its issue codes and product wording, the `ToExecutedCommand` audit adapter, trust, manifest validation, staging, routing, firmware behavior, schemas, golden evidence, and release authority. Mechanism comments no longer carry private record identifiers. Executable predicates, limits, and messages are unchanged.

NFC adopts this module in its own pull request, separate from this extraction. All contained launcher callers use `StartContained(startInfo, inheritedHandles, validateImmediatelyBeforeStart)`; ordinary external-tool starts use `Start(startInfo)`. Raw process creation stays in Processes rather than Launcher. Lifetime evidence must pass before the adoption closes.

NFC deletes its generic reader, synchronous cancellation, launch gate, inherited-handle record, and contained-starter copies only after every caller uses the verified Processes package and the adoption proves zero difference: frozen behavior, native lifetime, and the required UI evidence. NFC removes its Platform copy only after its remaining Platform callers move and the host structure trial passes. NFC keeps its historical executor evidence.

The UI comparisons use the shared environment manifest and require zero changed decoded pixels. Where they apply, the comparisons also cover the complete output bytes and the event traces and record each artifact's SHA-256. The eight legacy font values stay unchanged.

NFC downloads verified, versioned nupkg files at build time through its `core-packages.json` and uses exact `[x]` versions, source mapping, lock files, and locked restore. The manifest records each package's Release tag and SHA-256. NFC owns its package references, version pins, source mapping, and lock files. NFC never adds a ProjectReference to a Core checkout.

Core and NFC keep independent versioned releases. Both independent review stages cover the exact heads of the extraction pull request and the adoption pull request.
