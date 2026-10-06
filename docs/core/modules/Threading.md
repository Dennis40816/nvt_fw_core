[English](Threading.md) | [中文](Threading.zh-TW.md)

# Threading

`UiThread` in `src/Nvt.Core.Avalonia/Threading/` uses namespace `Nvt.Core.Avalonia.Threading`. It keeps a process-wide registry of the running UI dispatcher independently of Avalonia's global dispatcher slot. Registration and lookup retain the source's `Volatile.Write` and `Volatile.Read`. The registry has no unregister operation; application startup owns registration.

## Public API

The static class and its four methods are public. Names and signatures are retained:

```csharp
public static void RegisterRunningDispatcher(Dispatcher dispatcher);
public static bool TryGetRunningDispatcher(out Dispatcher? dispatcher);
public static bool IsCurrent(out Dispatcher? dispatcher, out Avalonia.Application? application);
public static bool IsUiThreadThatRunsALoop(bool hasThreadAccess, bool dispatcherRunsLoops);
```

- `RegisterRunningDispatcher` throws `ArgumentNullException` with parameter name `dispatcher` for null. It stores only dispatchers with `SupportsRunLoops`; other registrations leave the existing value unchanged.
- `TryGetRunningDispatcher` returns false and a null out value when there is no current application, no registration, or the registered dispatcher does not support run loops. Otherwise it returns the registered dispatcher, including from a background thread.
- `IsCurrent` returns true only when the registered dispatcher supports run loops, grants thread access, and a current application exists. When thread access fails, the dispatcher out value keeps the registered dispatcher and the application out value is null. When no current application exists, both out values are null.
- `IsUiThreadThatRunsALoop` returns `hasThreadAccess && dispatcherRunsLoops`.

## Provenance

Frozen parent baseline: NFH, repository `Dennis40816/nvt-freeform-helper`, ref `1.3.x`, full commit `e01e07a361b8dc264a06b3741f40274feeeace2d`, Avalonia 11.3.12 and xUnit 2. Extracted paths:

- `src/FreeformHelper.UI/Services/UiThread.cs`
- `tests/FreeformHelper.Tests/UI/TestHost/UiThreadTests.cs`

The helper and its existing fallback test were ported to Core. Changes consist of the namespace, public visibility, copyright/XML documentation, and test naming and initialization.

## Verification

Core uses Avalonia 12.0.5 and xUnit v3. Tests in `tests/Nvt.Core.Avalonia.Tests/Threading/` reuse the existing headless `ThemeTestApplication`; no additional application attribute or host is introduced. Each test leaves the current UI dispatcher registered. The fallback test also restores Avalonia's global slot in `finally`.

The ported test clears `Dispatcher.s_uiThread` temporarily and checks that the registered dispatcher is returned without repopulating that slot. Characterization covers the four-case Boolean truth table, null registration and its parameter name, dispatcher identity on the headless UI thread, successful UI-thread access, and background-thread failure that keeps the dispatcher out value and returns no application.

With packages already restored:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Threading"
```

For NFH adoption, first freeze results at the parent SHA above. Before and after switching to Core, build NFH with `--no-restore` and run `dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.UiThreadTests"`, then the full `FreeformHelper.Tests` project to cover consumers. Preserve the existing fallback assertions and compare the returned dispatcher by reference, the unchanged empty global slot during fallback, the null-registration exception/parameter, the Boolean truth table, and UI/background return values and application identity. Run the same characterization cases against the parent helper and Core. Do not refresh expected evidence to hide differences. NFH adoption and its before/after verification remain out of scope.

## Known differences

- The helper is public in Core; NFH's class and members were internal.
- Avalonia 12.0.5 still has `Dispatcher.s_uiThread`, so the original reflection-based fallback scenario is retained. No other private dispatcher members are inspected, and no runtime Avalonia API adaptation is needed.
- Tests use xUnit v3 and Core's existing headless application instead of xUnit 2, NFH's bootstrap, and its `HeadlessUiSerial` collection. Test method names omit underscores to satisfy Core analyzers.

## What stays in NFH

`tests/FreeformHelper.Tests/UI/TestHost/HeadlessDispatcherSetup.cs` remains in NFH, including its platform-setup validation and registration. NFH's startup registration, bootstrap, logging, resource resolution, runtime query, view/view-model call sites, and their product behavior remain unchanged. This extraction does not adopt Core in NFH or move any other helper.
