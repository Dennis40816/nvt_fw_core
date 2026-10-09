[English](Startup.md) | [中文](Startup.zh-TW.md)

# Startup

`Nvt.Core.Startup.StartupTrace` records startup milestones, elapsed time, and
allocation totals. The host application chooses the output environment variable,
schema name, milestone names, and any extra JSON sections. There are no package
dependencies beyond the .NET 10 base libraries.

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/StartupTraceSession.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/StartupTraceFileSink.cs`

The recorder, the provider call order and the JSON property order follow those files. The host now supplies the environment variable, the schema name and any extra sections. Product markers and preload stages stay in NFC. Four tests were ported from `tests/NvtFwCombiner.UiSmoke.Tests/StartupTraceSessionTests.cs` with synthetic data.

## Public API

```csharp
public sealed class StartupTrace
{
    public static StartupTrace Disabled { get; }
    public bool IsEnabled { get; }
    public TimeSpan? ElapsedSinceManagedEntry { get; }
    public static StartupTrace StartFromEnvironment(string outputPathEnvironmentVariable);
    public static StartupTrace Create(
        string? outputPath,
        TimeProvider? timeProvider = null,
        Func<long>? allocatedBytesProvider = null,
        bool measureWithoutOutput = false);
    public void Mark(string stage);
    public bool Complete(
        string finalStage,
        string schemaVersion,
        Action<System.Text.Json.Utf8JsonWriter>? writeHostSections = null);
}
```

## Host usage

```csharp
using Nvt.Core.Startup;

StartupTrace trace = StartupTrace.StartFromEnvironment("APP_STARTUP_TRACE_PATH");
trace.Mark("options.ready");
bool written = trace.Complete("window.opened", "app-startup-trace-v1", writer =>
{
    writer.WriteString("hostStatus", "ready");
});
TimeSpan? elapsed = trace.ElapsedSinceManagedEntry;
```

`Create` trims the path and treats a blank path as absent. With no path and
`measureWithoutOutput: false`, it returns `Disabled` without reading providers.
With `measureWithoutOutput: true`, timing remains active but `IsEnabled` is false,
marks are ignored, and completion returns false. `StartFromEnvironment` always
selects this timing behavior. It is the only API that reads an environment variable.

The default providers are `TimeProvider.System` and
`GC.GetTotalAllocatedBytes(precise: false)`. Creation reads the timestamp,
allocated bytes, then UTC time. Each recorded mark reads the timestamp before
allocated bytes. Completion marks its final stage, becomes final, then reads UTC
time before writing. `ElapsedSinceManagedEntry` reads a fresh timestamp whenever
timing is active, including after completion; it is not a frozen final duration.

Recording begins with `managed-entry` and zero measurements. Stage names must be
nonblank, even for a disabled or completed recorder. Elapsed milliseconds use the
provider's timestamp frequency. Allocation totals are relative to the creation
value and clamped to zero; allocation deltas are relative to the previous recorded
total and also clamped to zero. Use the instance serially; it does not synchronize
concurrent calls.

## File contract

Completion uses `FileMode.CreateNew`, `FileAccess.Write`, and `FileShare.Read`.
It never overwrites a file or creates a parent directory. The host must provision
the destination directory and choose an unused file name. Completion is attempted
once; later marks are ignored and later completion calls return false, including
after a failed write.

The JSON is indented. Root properties appear in this order:

1. `schemaVersion`: the host argument.
2. `processId`: the current process identifier.
3. `runtime`: the framework description.
4. `osArchitecture`: the operating system architecture.
5. `processArchitecture`: the process architecture.
6. `startedUtc`: UTC time read at creation.
7. `completedUtc`: UTC time read after the final mark.
8. `stages`: ordered milestone objects.

Each milestone writes `name`, `elapsedMilliseconds`, `deltaMilliseconds`,
`allocatedBytesSinceManagedEntry`, and `allocationDeltaBytes`, in that order.
`deltaMilliseconds` is elapsed time minus the previous point's elapsed time,
starting from zero.

After `stages` closes, the optional callback writes additional properties into
the still-open root object. The host must emit valid properties and leave the
root object open. File writing catches `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, and `ArgumentException`,
logs `Startup trace was not written: {0}` with the exception message, and returns
false. Other callback exception types propagate. A failed write or callback may
leave a partial file; completion remains final.

## Verification

`StartupTraceTests` uses queued timestamps and UTC times, synthetic allocations,
unique environment variables, and disposable temporary workspaces. It covers
ordered JSON, host sections, provider order, timing without output, allocation
clamping, stage validation, completion finality, callback exceptions, existing
files, and missing directories.

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
dotnet test Nvt.Core.sln --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Startup"
```
