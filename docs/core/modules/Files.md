[Traditional Chinese](Files.zh-TW.md)

# Files

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Infrastructure/Files/FileSystemPathGuard.cs` (complete file; renamed `RootedPathGuard`)
- `src/NvtFwCombiner.Infrastructure/Files/RegularFileGuard.cs` (complete file)
- `src/NvtFwCombiner.Infrastructure/Files/FileContentSnapshotInspector.cs` (lines 37-80: path opening; lines 86-181: existing stream read)
- `src/NvtFwCombiner.Application/Ports/ISelectedFileContentInspector.cs` (lines 26-106: existing result, mode, change-kind, and exception contracts)
- `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileSystemPathGuardTests.cs` (all four methods and eight relative-path cases)
- `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileContentSnapshotInspectorTests.cs` (the five file-inspection tests mapped below)
- `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileContentSnapshotInspectorTests.BoundedIdentity.cs` (stream tests plus complete-file hashes and path validation/cancellation slices)
- `tests/NvtFwCombiner.TestSupport/TempWorkspace.cs` (unique workspace, byte writing, and bounded Windows disposal slices)
- `tests/NvtFwCombiner.TestSupport/RepositoryPaths.cs` (`NormalizeRelativePath` only)

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Files` resolves rooted paths, rejects non-regular files, opens files under an explicit byte ceiling, and reads complete content through its existing bounded SHA-256 stream reader. The stream read loop, trailing-byte probe, and final length/position checks are unchanged. Product hints and models remain in NFC.

## Public API

```csharp
public enum FileCaptureMode { IdentityOnly, CaptureBytes }
public enum FileChangeKind { Unspecified, ShortRead, Growth, Shrinkage, PositionChanged }
public sealed class FileSizeLimitExceededException : Exception
public sealed class FileChangedDuringReadException : IOException
public readonly record struct BoundedReadResult(long Length, byte[] Sha256, byte[]? Bytes);
public static ValueTask<BoundedReadResult> BoundedFileReader.ReadAndHashAsync(
    Stream stream, long observedLength, FileCaptureMode mode, CancellationToken cancellationToken);
public static ValueTask<BoundedReadResult> BoundedFileReader.ReadFileAsync(
    string path, IReadOnlyList<string>? allowedRoots, long maximumBytes,
    FileCaptureMode mode, CancellationToken cancellationToken);
public static class RootedPathGuard
{
    public static string ResolveRoot(string rootDirectory);
    public static string ResolveExistingRoot(string rootDirectory);
    public static string ResolveExistingFileUnderRoots(string path, IReadOnlyList<string> allowedRoots);
    public static string ResolveFileUnderRoots(string path, IReadOnlyList<string> allowedRoots, bool mustExist);
    public static string ResolveExistingRelativeFileUnderRoot(string relativePath, string rootDirectory);
    public static string ResolveFileNameUnderRoot(string fileName, string rootDirectory);
    public static bool IsUnderRoot(string fullPath, string root);
}
public static partial class RegularFileGuard
{
    public static void RequirePath(string path);
    public static void RequireOpenHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, string displayPath);
    public static (long Device, long Inode)? ReadUnixIdentity(string path);
}
```

`FileSizeLimitExceededException` has constructors `(long observedBytes, long maximumBytes)` and `(long observedBytes, long maximumBytes, bool isCaptureStorageLimit)`. Its read-only properties are `ObservedBytes`, `MaximumBytes`, and `IsCaptureStorageLimit`. The two-argument constructor sets the flag to `false`. Both constructors require a nonnegative observed length and a positive maximum, checking the observed length first.

`FileChangedDuringReadException` has constructor `(FileChangeKind changeKind = FileChangeKind.Unspecified)` and read-only property `ChangeKind`.

The existing Core read exceptions use generic file wording. NFC's adoption adapter retains its original selected-file exception types, messages, and mode mapping.

## Usage and ownership

The host application supplies a readable stream at the beginning of its content and the complete length measured before calling the reader. The host application owns and disposes the stream; the reader does not rewind or close it. The stream entry point uses this caller-owned stream; the path entry point below opens and disposes its own stream and checks the caller's explicit size ceiling.

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

## Rooted and regular-file contract

`ResolveRoot` creates a missing directory. `ResolveExistingRoot` requires an existing directory. Both reject reparse points in the existing ancestry and return a trailing directory separator. Paths compare with `OrdinalIgnoreCase` on Windows and `Ordinal` elsewhere. Containment includes a trailing separator, so a sibling sharing the root name prefix is outside; the root directory itself is not a file under that root.

`ResolveFileUnderRoots` validates the path before the roots list, normalizes the path, and checks containment before existence. An existing file is checked for reparse points and regular-file status. For a missing target, `mustExist` produces `FileNotFoundException` before directory-target or parent checks. A new target must have an existing parent and no linked ancestry. Empty roots produce `InvalidOperationException`.

`ResolveExistingRelativeFileUnderRoot` validates the relative path and root arguments first. It rejects fully qualified paths, backslashes, colons, NUL, and empty, current, or parent segments before resolving the existing root. It then checks containment, existence, reparse points, and regular-file status in that order. `ResolveFileNameUnderRoot` accepts only plain filenames; it does not create or require the root or target. These frozen generic guards have no 512-character path ceiling. Stricter product/package path admission stays in NFC.

`RegularFileGuard.RequirePath` preserves the Windows device, directory, and reparse-point attribute mask and Unix `lstat` regular-file mask. `RequireOpenHandle` validates the handle, display path, and invalid/closed state in that order, then uses Windows `GetFileType` or Unix `fstat`. `ReadUnixIdentity` is for non-Windows hosts: it follows `stat`, returns device/inode on success, returns null for native errors 2 (`ENOENT`) and 20 (`ENOTDIR`), and preserves other native errors. The complete `UnixFileStatus` layout is unchanged.

Only these guard messages were renamed; all other messages, exception types, predicates, and ordering remain frozen:

- `File paths must be relative and use forward slashes.`
- `File paths cannot contain empty, current, or parent segments.`
- `Relative file was not found.`
- `File '{displayPath}' has no valid open handle.`
- `File '{path}' must be a regular filesystem file.`
- `Could not inspect file '{path}' (native error {n}).`

`Could not inspect local file '{path}' (native error {n}).` remains unchanged. The relative-path exception argument is `relativePath`.

## Path-based read

The caller supplies an explicit positive, inclusive `maximumBytes`. NFC's frozen fixed-workflow hard ceiling is **100,000,000 bytes**, as characterized by its sparse 100,000,001-byte rejection test; Core does not choose or widen product ceilings. The fixed mechanism bounds remain a 64 KiB buffer, one trailing-byte probe, and `Array.MaxLength` for captured storage. `IdentityOnly` has no array-storage ceiling.

`ReadFileAsync` checks positive maximum, defined mode, cancellation, `Path.GetFullPath(path)`, and rooted file resolution in that order. The caller resolves a non-null roots list once with `ResolveExistingRoot`; the reader uses that list as supplied without revalidating every root's existence. The frozen containment helper still normalizes root strings with `Path.GetFullPath`. With null roots, the file's existing parent directory is resolved as its only root.

The reader opens with `Open`, `Read`, `FileShare.Read`, `Asynchronous | SequentialScan`, and a 64 KiB buffer. It owns and disposes the stream through `await using`, measures length, rejects length above the caller ceiling with `FileSizeLimitExceededException`, then delegates to the unchanged `ReadAndHashAsync` with `ConfigureAwait(false)`.

These are rooted checks before opening, not held filesystem custody. They do not close path-replacement races or detect every same-length rewrite during one read. `ReadFileAsyncDetectsSameSizeMutation` characterizes two completed reads separated by a rewrite. The later Files Windows custody slice supplies the stronger guarantees.

## Path test mapping and boundaries

The following map retains all four frozen path methods and eight theory cases. `Tests.cs` abbreviates `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileContentSnapshotInspectorTests.cs`; `BoundedIdentity.cs` abbreviates its `.BoundedIdentity.cs` peer. File tests are in `BoundedFileReaderPathTests`. Only product hint assertions were dropped; both modes and all original assertions and thresholds were retained.

| Frozen source test | Core test |
| --- | --- |
| `FileSystemPathGuardTests.ResolveExistingManifestFileUnderRootReturnsConfinedFile` | `RootedPathGuardTests.ResolveExistingRelativeFileUnderRootReturnsConfinedFile` |
| `FileSystemPathGuardTests.ResolveExistingManifestFileUnderRootRejectsPathSyntax` (all eight cases) | `RootedPathGuardTests.ResolveExistingRelativeFileUnderRootRejectsPathSyntax` |
| `FileSystemPathGuardTests.ResolveExistingManifestFileUnderRootRequiresFile` | `RootedPathGuardTests.ResolveExistingRelativeFileUnderRootRequiresFile` |
| `FileSystemPathGuardTests.ResolveExistingManifestFileUnderRootRequiresDirectoryRoot` | `RootedPathGuardTests.ResolveExistingRelativeFileUnderRootRequiresDirectoryRoot` |
| `Tests.cs:13 InspectAsyncReturnsContentAuthoritativeStampAndNonAuthoritativeHints` | `ReadFileAsyncReturnsLengthHashAndBytes` |
| `Tests.cs:34 InspectAsyncRejectsFileAboveResolvedMaximum` | `ReadFileAsyncRejectsFileAboveResolvedMaximum` |
| `Tests.cs:55 InspectAsyncRejectsHundredMegabyteOverflowBeforeMaterialization` | `ReadFileAsyncRejectsHundredMegabyteOverflowBeforeMaterialization` |
| `Tests.cs:123 InspectAsyncDetectsSameSizeMutation` | `ReadFileAsyncDetectsSameSizeMutation` |
| `Tests.cs:163 InspectAsyncRejectsPathOutsideAllowedRoot` | `ReadFileAsyncRejectsPathOutsideAllowedRoot` |
| `BoundedIdentity.cs:15 InspectAsyncHashesCompleteFileWithExplicitPayloadMode` | `ReadFileAsyncHashesCompleteFileWithExplicitPayloadMode` |
| `BoundedIdentity.cs:247 InspectionRejectsInvalidPayloadModeBeforeReading` (path half) | `ReadFileAsyncRejectsInvalidModeBeforeAccessingPath` |
| `BoundedIdentity.cs:271 InspectionPropagatesCancellationBeforeOpeningOrReading` (path half) | `ReadFileAsyncPropagatesCancellationBeforeAccessingPath` |

Added suites contain 54 public test methods and 110 cases: `RootedPathGuardTests` has 20 methods/42 cases, `RegularFileGuardTests` has 15/20, `BoundedFileReaderPathTests` has 17/42, and `BoundedFileReaderStorageBoundaryTests` has 2/6. The existing 24-method/47-case stream suite is unchanged. All fixtures are synthetic, using a Files-owned `TestWorkspace` under the system temp folder with the original bounded Windows deletion retry (450 ms total, sleeps at most 50 ms).

Coverage includes root creation and missing roots; outside and shared-prefix sibling paths; the second root; new files and missing parents; existing-directory targets; missing required files; empty/null roots and blank arguments; plain filenames and all original relative syntax; file links, linked parents, linked roots; Windows case folding and non-Windows case sensitivity; regular, directory, and missing paths; valid, null, invalid, and closed handles; real Windows and Unix pipe handles; and Unix identity equality and absent-path null results. Ordering checks use deterministic invalid arguments, pre-cancelled tokens, and sequentially completed mutation writes.

| Boundary | Added coverage |
| --- | --- |
| Positive caller maximum | 0, -1, and `long.MinValue` rejected before mode/cancellation/path; smallest positive maximum 1 admits lengths 0 and 1, rejects 2 |
| NFC's explicit 100,000,000-byte ceiling | 99,999,999; 100,000,000; 100,000,001 in both modes, plus the original sparse capture rejection |
| Fixed 64 KiB buffer | Complete files at 65,535; 65,536; 65,537 bytes in both modes |
| Fixed `Array.MaxLength` capture ceiling | One below, exact, and one above; one above rejects before reading, plus cancellation-before-allocation at all three lengths |
| Original complete-file hash cases | Lengths 0 and 131,089; original `long.MaxValue` ceiling |

Near-`Array.MaxLength` successful capture tests allocate about 2 GiB each and explicitly skip by default. A dedicated 64-bit host must set `NVT_CORE_TEST_LARGE_CAPTURE=1` with at least about 6 GiB available to the runtime to execute the below/exact cases. Their results must be reported separately; skipped allocation evidence is not a pass. Symbolic links explicitly skip when the OS refuses creation; platform-specific cases explicitly skip on the other platform. No unavailable case returns early as a pass.

## Ownership and adoption

Files owns this rooted mechanism, the regular-file guard, and the single complete-content stream hashing loop. No duplicate read loop or native custody owner was introduced. NFC retains `ProtectedPathGuard`, `LocalFileIdentity`, hardened output writers, product ceilings and package-path policy, stamps, selected-file models, display hints, schemas, trust, and release authority.

NFC adopts this module in its own pull request. That pull request downloads verified, versioned nupkg files at build time through `core-packages.json` and uses exact `[x]` versions, locked dependencies and source mapping. The manifest records each package's Release tag and SHA-256. Shared references and locks stay with their owner in NFC. NFC deletes only the relocated NFC generic mechanisms after callers use Core, NFC mode/exception mapping is preserved, all 40 path and 12 file cases pass, and required product output and pixel evidence passes. Core tests alone do not prove NFC product or pixel parity.

A later Files extension adds held Windows read custody. This version does not provide it.

## Offline verification

Run from the solution root with `AVALONIA_TELEMETRY_OPTOUT=1`:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Files"
dotnet test Nvt.Core.sln --no-build
```
