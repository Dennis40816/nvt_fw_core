[English](Threading.md) | [中文](Threading.zh-TW.md)

# Threading

## UiEventRunner

[`UiEventRunner`](../../../src/Nvt.Core/Threading/UiEventRunner.cs) is a sealed, UI-independent helper in `Nvt.Core.Threading`. It targets `net10.0` and uses only BCL dependencies. Hosts supply primary and emergency reporters; the runner has no Avalonia dependency or global reporting state.

### Public API and usage

~~~csharp
public UiEventRunner(Action<string, Exception> report, Action<string, Exception> fallbackReport);
public void Run(string operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
public Task RunAsync(string operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
~~~

A synchronous event handler can call `uiEvents.Run("CopyReport", token => CopyReportAsync(token), lifetimeToken)`. `Run` delegates to the fully observing `RunAsync` and returns when the delegate yields pending work. Both begin on the calling thread and synchronization context; they do not queue the action to a worker thread. The await retains that context for failure reporting.

- Synchronous delegate throws and faulted tasks reach the primary reporter once with the unchanged name and original exception.
- Cancellation is silent only if the supplied token is cancelled and the exception carries that token. A different token is an error even if both tokens are cancelled.
- An operation that links the supplied token with another source (for example to add a timeout) throws a cancellation that carries the linked token. The runner reports it. Catch it in the operation and rethrow with `cancellationToken.ThrowIfCancellationRequested()` when the supplied token is the one that was cancelled.
- If the primary reporter throws, the fallback receives the same operation name and original operation exception once. If the fallback also throws, its failure is swallowed.
- Null reporters, operation names, and actions throw `ArgumentNullException` synchronously at the public boundary. Empty operation names are allowed and passed unchanged.
- The operation owns its busy flag, cancellation source, cleanup `finally`, and success status. Observation-task completion does not make a failed save or export successful.

### Source consumption and provenance

This helper is new work from the owner-approved C# health plan (2026-10-09, H04, section 7); it is not an extraction from an app baseline. NFH and NFU consume the Core package. NFC copies the canonical file and checks its SHA-256 without taking a Core runtime dependency.

Define `NVT_CORE_SOURCE_CONSUMPTION` to compile the same type as `internal` in namespace `Nvt.Core.Threading`. Consumer CI must reject compiling the copy while also referencing the package, including transitive references. Remove the copy and symbol when adopting the package. The [source-consumption guide and manifest](../../../tools/source-consumption/README.md) describe copying, LF preservation, verification, and upgrades.

### Runner verification

[`UiEventRunnerTests`](../../../tests/Nvt.Core.Tests/Threading/UiEventRunnerTests.cs) cover success, both failure forms, token identity and cancellation state, reporter failure containment, immediate invocation on the calling context, pending `Run` completion, and null validation. A queued synchronization context records posted exceptions; signal waits are bounded and use no fixed sleeps. The [standalone source-consumption project](../../../tests/Nvt.Core.SourceConsumption.Tests/) compiles the linked file without a Core reference and verifies internal visibility, SHA-256, and byte length.

~~~powershell
./tools/source-consumption/New-SourceConsumptionManifest.ps1 -Check
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Threading.UiEventRunnerTests"
dotnet test tests/Nvt.Core.SourceConsumption.Tests/Nvt.Core.SourceConsumption.Tests.csproj --no-build
~~~

App handler migrations and consumer CI integration remain in their own repositories.

## Breaking changes before 0.9.0

`UiThread.IsUiThreadThatRunsALoop` has been removed.
Use `TryGetRunningDispatcher` to obtain the registered dispatcher.
Use `IsCurrent` to check access to the running UI thread and current application.
A local Boolean-only test can evaluate `hasThreadAccess && dispatcherRunsLoops` itself.

NFH must update `CadLoadOverlayFrameYieldPolicyTests` before upgrading its Core package.
Replace its helper assertion with a local predicate or real dispatcher evidence.
Rebuild existing dispatcher consumers against the accepted package.
Registration, fallback lookup, and dispatcher access behavior remain unchanged.
Core tests now dispatch background work through the real headless dispatcher.

`UiThread` in `src/Nvt.Core.Avalonia/Threading/` uses namespace `Nvt.Core.Avalonia.Threading`. It keeps a process-wide registry of the running UI dispatcher independently of Avalonia's global dispatcher slot. Registration and lookup retain the source's `Volatile.Write` and `Volatile.Read`. The registry has no unregister operation; application startup owns registration.

## Public API

The static class exposes three dispatcher methods:

```csharp
public static void RegisterRunningDispatcher(Dispatcher dispatcher);
public static bool TryGetRunningDispatcher(out Dispatcher? dispatcher);
public static bool IsCurrent(out Dispatcher? dispatcher, out Avalonia.Application? application);
```

- `RegisterRunningDispatcher` throws `ArgumentNullException` with parameter name `dispatcher` for null. It stores only dispatchers with `SupportsRunLoops`; other registrations leave the existing value unchanged.
- `TryGetRunningDispatcher` returns false and a null out value when there is no current application, no registration, or the registered dispatcher does not support run loops. Otherwise it returns the registered dispatcher, including from a background thread.
- `IsCurrent` returns true only when the registered dispatcher supports run loops, grants thread access, and a current application exists. When thread access fails, the dispatcher out value keeps the registered dispatcher and the application out value is null. When no current application exists, both out values are null.

## Provenance

Frozen parent baseline: NFH, repository `Dennis40816/nvt-freeform-helper`, ref `1.3.x`, full commit `e01e07a361b8dc264a06b3741f40274feeeace2d`, Avalonia 11.3.12 and xUnit 2. Extracted paths:

- `src/FreeformHelper.UI/Services/UiThread.cs`
- `tests/FreeformHelper.Tests/UI/TestHost/UiThreadTests.cs`

The helper and its existing fallback test were ported to Core. Changes consist of the namespace, public visibility, copyright/XML documentation, and test naming and initialization. The public member `IsUiThreadThatRunsALoop` was removed (see Known differences).

## Verification

Core uses Avalonia 12.1.1 and xUnit v3. Tests in `tests/Nvt.Core.Avalonia.Tests/Threading/` reuse the existing headless `ThemeTestApplication`; no additional application attribute or host is introduced. Each test leaves the current UI dispatcher registered. The fallback test also restores Avalonia's global slot in `finally`.

The ported test clears `Dispatcher.s_uiThread` temporarily and checks that the registered dispatcher is returned without repopulating that slot. Characterization covers background work scheduled through the running dispatcher, null registration and its parameter name, dispatcher identity on the headless UI thread, successful UI-thread access, and background-thread failure that keeps the dispatcher out value and returns no application.

With packages already restored:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Threading"
```

For NFH adoption, first freeze results at the parent SHA above. Before and after switching to Core, build NFH with `--no-restore` and run `dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.UiThreadTests"`, then the full `FreeformHelper.Tests` project to cover consumers. Preserve the existing fallback assertions and compare the returned dispatcher by reference, the unchanged empty global slot during fallback, the null-registration exception/parameter, the Boolean results of the remaining predicates, and UI/background return values and application identity. Run the same characterization cases against the parent helper and Core. Do not refresh expected evidence to hide differences. NFH adoption and its before/after verification remain out of scope.

## Known differences

- The helper is public in Core; NFH's class and members were internal.
- `IsUiThreadThatRunsALoop` is not in Core. It only served one test, and the supported predicates are `TryGetRunningDispatcher` and `IsCurrent`. NFH keeps its local copy until it adopts Core, and its Boolean truth table test stays in NFH as a local predicate test.
- Avalonia 12.1.1 still has `Dispatcher.s_uiThread`, so the original reflection-based fallback scenario is retained. No other private dispatcher members are inspected, and no runtime Avalonia API adaptation is needed.
- Tests use xUnit v3 and Core's existing headless application instead of xUnit 2, NFH's bootstrap, and its `HeadlessUiSerial` collection. Test method names omit underscores to satisfy Core analyzers.

## What stays in NFH

`tests/FreeformHelper.Tests/UI/TestHost/HeadlessDispatcherSetup.cs` remains in NFH, including its platform-setup validation and registration. NFH's startup registration, bootstrap, logging, resource resolution, runtime query, view/view-model call sites, and their product behavior remain unchanged. This extraction does not adopt Core in NFH or move any other helper.
