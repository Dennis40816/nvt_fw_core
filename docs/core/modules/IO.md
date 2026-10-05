[English](IO.md) | [中文](IO.zh-TW.md)

# IO: AtomicOutput

[`AtomicOutput.WriteAsync`](../../../src/Nvt.Core/IO/AtomicOutput.cs) is NFU's ordinary stream-output helper, exposed in `Nvt.Core.IO` on `net8.0` with BCL dependencies only. It writes a sibling temporary file, flushes it, then moves it over the destination. **It must not replace NFC's hardened firmware output writer.** Tool adoption is a separate task.

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
