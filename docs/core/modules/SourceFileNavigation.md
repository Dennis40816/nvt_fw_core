[English](SourceFileNavigation.md) | [中文](SourceFileNavigation.zh-TW.md)

# SourceFileNavigation

The BCL-only process-start and default-open mechanisms are available in `Nvt.Core.SourceFileNavigation`, targeting `net10.0`. This extraction follows the owner decision of 2026-10-05 to move reusable modules into Core by 2026-10-18. Tool adoption remains a separate task requiring review and owner approval.

## Frozen source

NFU (`Dennis40816/nvt-event-buffer-replay`), `origin/0.2.0`, commit `915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b`.

- `src/Nvt.Replay.Avalonia/SourceFileNavigator.cs`: `SourceFileOpenResult` (lines 6–10), `TryStart` (lines 134–153), and the default-open branch of `Open` (lines 45–57).
- `tests/Nvt.Replay.Avalonia.Tests/SourceFileNavigatorTests.cs`: inspected for existing coverage; its four extension-routing cases remain in NFU because they test host policy. There were no direct BCL mechanism tests to port.

Core changes visibility and namespace, adds copyright and XML documentation, and replaces the two direct process-start calls with internal overloads accepting a starter for that call. Public methods supply `Process.Start`. There is no shared mutable starter or public testing API.

## Public API

```csharp
public sealed record SourceFileOpenResult(
    bool Opened, bool ExactLine, string Application, string? Error = null);

public static class SourceFileNavigator
{
    public static bool TryStart(
        string executable, IReadOnlyList<string> arguments, out string? error);

    public static SourceFileOpenResult OpenDefault(string path);
}
```

[`TryStart`](../../../src/Nvt.Core/SourceFileNavigation/SourceFileNavigator.cs) sets only `ProcessStartInfo.FileName` and `UseShellExecute = false`, then adds each argument unchanged and in order to `ArgumentList`. Any normal return from `Process.Start`, including `null`, yields `true` and a null error. `InvalidOperationException` and `Win32Exception` yield `false` and the unchanged `exception.Message`.

`OpenDefault` sets only `FileName = path` and `UseShellExecute = true`. A normal return, including `null`, yields `(true, false, "default application", null)`. The same handled exception types yield `(false, false, "default application", exception.Message)`. `ExactLine` is always false. Other exceptions propagate from both methods. Neither method waits for the launched application or verifies that it displayed the file.

NFU retains path validation, normalization and existence checks, Excel COM automation and cleanup, editor discovery, routing order, editor-specific arguments, line clamping, source lookup and UI text. This round adds no Windows project, editor discovery or routing policy to Core.

## Verification and adoption

[`SourceFileNavigatorTests`](../../../tests/Nvt.Core.Tests/SourceFileNavigation/SourceFileNavigatorTests.cs) characterizes the frozen mechanisms with synthetic paths and call-scoped delegates. Tests inspect executable/path values, ordered arguments (including spaces, quotes, empty values and Unicode), empty argument lists, shell flags and untouched process settings. They cover normal returns with both null and unstarted process objects, both handled exception types with exact multiline and empty messages, propagation of other exceptions, all default-open result fields, and nested-call isolation. No test starts a real process.

Run after the existing package restore, without changing the skeleton or lock files:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

For later NFU adoption, compare the frozen BCL branches and Core using the same synthetic paths, arguments and fake starter outcomes. Compare every `ProcessStartInfo` setting, argument order, returned result field and error string; preserve exception propagation and null-return success. Keep NFU's routing tests and inspector/raw-source-link tests in its original suite and run them before and after replacing only the two mechanisms. Real default-application and editor smoke checks require the same OS and application inventory and remain unverified in this extraction. NFU adoption is a separate task that uses the versioned Core package.
