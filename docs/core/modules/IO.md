[English](IO.md) | [中文](IO.zh-TW.md)

# IO: AtomicOutput

[`AtomicOutput.WriteAsync`](../../../src/Nvt.Core/IO/AtomicOutput.cs) is NFU's ordinary stream-output helper, exposed in `Nvt.Core.IO` on `net10.0` with BCL dependencies only. It writes a sibling temporary file, flushes it, then moves it over the destination. **It must not replace NFC's hardened firmware output writer.** Tool adoption is a separate task.

Frozen parent baseline: NFU (`nvt-event-buffer-replay`), `origin/0.2.0`, commit `915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b`. Extracted from:

- `src/Nvt.Replay.Core/AtomicOutput.cs`
- `tests/Nvt.Replay.Tests/ReplaySidecarTests.cs`: `Interrupted_atomic_write_preserves_prior_output_and_removes_temporary_file`

The executable source is unchanged apart from the namespace; Core adds the copyright header and API documentation. The existing direct helper test is ported to [`AtomicOutputTests`](../../../tests/Nvt.Core.Tests/IO/AtomicOutputTests.cs), with synthetic characterization for exact binary and UTF-8 bytes, replacement timing, cancellation, writer/flush/publication failures, cleanup, path normalization and argument validation. Replay schemas, report writers and their integration tests remain in NFU.

Current behavior is preserved: an already-cancelled token still reaches the delegate before the helper checks cancellation; successful writes overwrite the destination. Parent directories created before a failure remain. Cleanup attempts to delete the temporary file; a deletion error can supersede the original error. This helper makes no additional crash-recovery or firmware-output guarantees.

Core verification after the existing package restore:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build
```

For NFU's later switch to Core, run the following suites before and after the change, building each version first with `--no-restore` and telemetry opt-out:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Replay.Tests/Nvt.Replay.Tests.csproj --no-restore
dotnet test tests/Nvt.Replay.Tests/Nvt.Replay.Tests.csproj --no-build --filter "FullyQualifiedName~ReplaySidecarTests|FullyQualifiedName~CaptureAnalysisTests|FullyQualifiedName~ReadableCommunicationLogWriterTests|FullyQualifiedName~ReplayExportTests"
```

Use the same frozen synthetic documents, reports and render inputs, including fixed serialized metadata. Preserve the baseline outputs, regenerate at the same destinations with Core, and compare final file sets and every JSON/CSV/JSONL/PNG byte or SHA-256. Retain NFU's deterministic heatmap golden hash, cancellation assertions, prior-output preservation and temporary-file cleanup checks. Do not refresh a baseline to accept differences. NFU adoption and its packaging acceptance remain pending.

## WriteBytesAsync

```csharp
public static Task WriteBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
```

[`AtomicOutput.WriteBytesAsync`](../../../src/Nvt.Core/IO/AtomicOutput.cs) publishes bytes for NFC launcher state files.
NFC validates its state path before the call.
NFC encodes its bytes before the call.
Tool adoption is a separate task.
This method must not replace NFC's firmware output writer.

The method uses the same path validation and normalization as `WriteAsync`.
It creates missing parent directories.
It writes a sibling temporary file with the same name pattern and stream arguments as `WriteAsync`.
It writes the bytes without a cancellation check before `FlushAsync(token)`.
It then calls `Flush(flushToDisk: true)`.
It closes the stream before checking cancellation and moving the temporary file over the destination.
The write and `FlushAsync` awaits use `ConfigureAwait(false)`, as in the NFC loop.
Temporary-file cleanup suppresses `IOException` and `UnauthorizedAccessException`.
Other cleanup exceptions propagate.
`WriteAsync` retains its cancellation check between the writer and `FlushAsync`.
`WriteAsync` retains its captured-context awaits.
`WriteAsync` retains cleanup errors that can replace the original error.
Both methods share one private publication engine.

Frozen NFC source: `Dennis40816/nvt_fw_combiner`, `origin/1.2.x`, commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`.
The private `WriteAtomicallyAsync(string destination, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)` has identical copies in:

- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/JsonVersionManagerStateStore.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/JsonLauncherBootstrapStateStore.cs`

[`AtomicOutputWriteBytesTests`](../../../tests/Nvt.Core.Tests/IO/AtomicOutputWriteBytesTests.cs) contains a private copy of that frozen loop.
Each comparable scenario runs the loop and Core in separate fresh folders.
The tests compare exception types and messages, destination bytes and temporary-file counts.
Message comparison normalizes the fresh folder and generated temporary-file identifier.
The tests use synthetic names and bytes.
They cover payload sizes, replacement, missing folders, cancellation, flush failures, move failure and cleanup failure.
An abandoned temporary file remains untouched by the next successful write.
A characterization test preserves `WriteAsync`'s cleanup-error behavior.
NFC's launcher tasks L04 and L11 test a real process kill with the shared test probe.

Verification uses the restored packages:

```powershell
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```
