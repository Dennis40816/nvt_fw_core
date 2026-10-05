[English](Files.md) | [中文](Files.zh-TW.md)

# Files

`Nvt.Core.Files` reads an exact, already-measured stream length, computes SHA-256, and optionally returns the content bytes. The reader performs at most one trailing-byte read after the measured content, so stream growth cannot extend the work without a bound.

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Infrastructure/Files/FileContentSnapshotInspector.cs` (lines 86-181)
- `src/NvtFwCombiner.Application/Ports/ISelectedFileContentInspector.cs` (lines 26-106)

The read loop, the trailing-byte probe and the final length and position checks follow those lines. The exception texts say "File" instead of "Selected file". NFC keeps its own exception types and messages when it adopts this module. Tests were ported from `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileContentSnapshotInspectorTests.BoundedIdentity.cs` with synthetic streams.

## Public API

```csharp
public enum FileCaptureMode { IdentityOnly, CaptureBytes }
public enum FileChangeKind { Unspecified, ShortRead, Growth, Shrinkage, PositionChanged }
public sealed class FileSizeLimitExceededException : Exception
public sealed class FileChangedDuringReadException : IOException
public readonly record struct BoundedReadResult(long Length, byte[] Sha256, byte[]? Bytes);
public static ValueTask<BoundedReadResult> BoundedFileReader.ReadAndHashAsync(
    Stream stream, long observedLength, FileCaptureMode mode, CancellationToken cancellationToken);
```

`FileSizeLimitExceededException` has constructors `(long observedBytes, long maximumBytes)` and `(long observedBytes, long maximumBytes, bool isCaptureStorageLimit)`. Its read-only properties are `ObservedBytes`, `MaximumBytes`, and `IsCaptureStorageLimit`. The two-argument constructor sets the flag to `false`. Both constructors require a nonnegative observed length and a positive maximum, checking the observed length first.

`FileChangedDuringReadException` has constructor `(FileChangeKind changeKind = FileChangeKind.Unspecified)` and read-only property `ChangeKind`.

## Usage and ownership

The host application supplies a readable stream at the beginning of its content and the complete length measured before calling the reader. The host application owns and disposes the stream; the reader does not rewind or close it. This module does not open paths or resolve a caller's size limit.

```csharp
BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
        stream, observedLength, FileCaptureMode.IdentityOnly, cancellationToken)
    .ConfigureAwait(false);
```

`Length` equals the supplied measured length. `Sha256` holds the 32 raw hash bytes. `IdentityOnly` returns `Bytes == null`; `CaptureBytes` returns an array containing exactly the bytes that were hashed, including an empty array for empty content. Returned arrays are mutable and belong to the caller. The hash describes the bytes actually read; the length and position checks cannot detect every same-length content change.

## Read rules

1. Validate the stream, nonnegative length, and defined mode in that order. Check cancellation next, then the capture storage limit. An undefined mode produces `ArgumentOutOfRangeException` with parameter name `mode`.
2. Reject `CaptureBytes` when the measured length exceeds `Array.MaxLength`, before reading. Identity reads use long counters and do not have this capture storage limit.
3. Create an incremental SHA-256 hash and a 64 KiB buffer. Capture allocates `new byte[checked((int)observedLength)]` and reads directly into that array. Identity reads reuse the buffer.
4. Until the measured length has been read, request at most 64 KiB and no more than the remaining length. Check cancellation before and after each read. Continue on partial positive reads; a zero read produces `ShortRead`.
5. Check cancellation, read at most one trailing byte, then check cancellation again. Any trailing content produces `Growth`.
6. For seekable streams, compare the final length first: smaller produces `Shrinkage`, larger produces `Growth`. Then require the final position to equal the measured length, otherwise produce `PositionChanged`. Non-seekable streams never have their `Length` or `Position` accessed.
7. Return a result only after all checks pass. All reader awaits use `ConfigureAwait(false)`. Cancellation remains an `OperationCanceledException`; failures do not return partial hashes or captured bytes.

## Exception messages

- Caller limit: `File length {observedBytes} exceeds the resolved maximum {maximumBytes} bytes.`
- Capture storage limit: `File length {observedBytes} exceeds the capture storage limit {maximumBytes} bytes.`
- Stream change: `File length changed during complete-content read.`

## Tests

`BoundedFileReaderStreamTests` contains 24 public test methods and 47 cases. All data is synthetic. The internal `GeneratedReadStream` helper generates `0xA5` bytes on demand, records read requests, supports partial reads, and can simulate length, position, and cancellation changes without storing a large payload.

The long-counter theory reads 2,147,483,665 and 4,294,967,313 generated bytes against fixed SHA-256 values. These cases can take several seconds. Additional coverage compares `SHA256.HashData` with both modes for 0 and 131,089 bytes generated as `(byte)(index % 251)`.

### Test mapping

| Source stream test | Core stream test |
| --- | --- |
| `HashExactLengthAsyncRejectsGrowthWithOneByteProbe` | `ReadAndHashAsyncRejectsGrowthWithOneByteProbe` |
| `HashExactLengthAsyncRejectsShortReadAsContentChange` | `ReadAndHashAsyncRejectsShortReadAsContentChange` |
| `HashExactLengthAsyncPropagatesCancellation` | `ReadAndHashAsyncPropagatesCancellationForCancelledToken` |
| `HashExactLengthAsyncAccumulatesPartialReadsAcrossBufferBoundary` | `ReadAndHashAsyncAccumulatesPartialReadsAcrossBufferBoundary` |
| `ReadAndHashExactLengthAsyncUsesLongCountersWithoutPayload` | `ReadAndHashAsyncUsesLongCountersWithoutPayload` |
| `ReadAndHashExactLengthAsyncRejectsUnrepresentableCaptureBeforeReading` | `ReadAndHashAsyncRejectsUnrepresentableCaptureBeforeReading` |
| `ReadAndHashExactLengthAsyncCapturesPartialReads` | `ReadAndHashAsyncCapturesPartialReads` |
| `ReadAndHashExactLengthAsyncRejectsFinalPositionChange` | `ReadAndHashAsyncRejectsFinalPositionChange` |
| `ReadAndHashExactLengthAsyncSupportsNonSeekableStream` | `ReadAndHashAsyncSupportsNonSeekableStream` |
| `ReadAndHashExactLengthAsyncRejectsGrowthDuringRead` | `ReadAndHashAsyncRejectsGrowthDuringRead` |
| `ReadAndHashExactLengthAsyncRejectsShortReadDuringRead` | `ReadAndHashAsyncRejectsShortReadDuringRead` |
| `ReadAndHashExactLengthAsyncRejectsFinalLengthChange` | `ReadAndHashAsyncRejectsFinalLengthChange` |
| `InspectionRejectsInvalidPayloadModeBeforeReading` (stream only) | `ReadAndHashAsyncRejectsInvalidModeBeforeReading` |
| `InspectionPropagatesCancellationBeforeOpeningOrReading` (stream only) | `ReadAndHashAsyncPropagatesCancellationBeforeReading` |
| `ReadAndHashExactLengthAsyncPropagatesCancellationMidRead` | `ReadAndHashAsyncPropagatesCancellationMidRead` |

Additional test methods:

- `ReadAndHashAsyncHashesCompleteStreamWithExplicitPayloadMode`
- `ReadAndHashAsyncRejectsNullStream`
- `ReadAndHashAsyncRejectsNegativeObservedLength`
- `ReadAndHashAsyncValidatesArgumentsBeforeCancellation`
- `FileSizeLimitExceededExceptionRejectsInvalidLengths`
- `FileSizeLimitExceededExceptionReportsCallerLimit`
- `FileSizeLimitExceededExceptionReportsExplicitLimit`
- `FileChangedDuringReadExceptionDefaultsToUnspecified`
- `FileChangedDuringReadExceptionExposesChangeKind`

## Offline verification

Run from the solution root with `AVALONIA_TELEMETRY_OPTOUT=1`:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
dotnet test Nvt.Core.sln --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Files"
```
