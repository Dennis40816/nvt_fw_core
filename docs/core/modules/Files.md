[Traditional Chinese](Files.zh-TW.md)

# Files

## Breaking changes before 0.9.0

`RegularFileGuard.ReadUnixIdentity` is internal.
Use the public `RequirePath` and `RequireOpenHandle` guards for file admission.
Core retains its Unix identity tests through internal test visibility.

`BoundedReadResult.Sha256` is now `byte[]?`.
Its positional constructor hash parameter and `Deconstruct` hash output carry the same annotation.
The uninitialized default contains zero length, a null hash, and null content.
Successful reads still supply the complete SHA-256 hash in both capture modes.
Check the hash before converting or consuming a stored result:

```csharp
if (result.Sha256 is not { } hash)
{
    throw new InvalidOperationException("The read result is uninitialized.");
}
string digest = Convert.ToHexString(hash).ToLowerInvariant();
```

An absent hash exists only in the uninitialized default result. A successful read never returns it. Launcher treats an absent hash as a mismatch: the package verifier returns `PackageMismatch`, and executable measurement and the installed launcher check return `Tampered`.
Its validated content identities retain the same bytes.

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

- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/WindowsStablePathCustody.cs` (held identity, closed-tree capture, cloning, explicit limits and native issue mapping; product defaults replaced by required parameters)
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/WindowsStablePathCustody.Native.cs` (complete no-follow relative opens, identity, duplication, deletion and native rename primitives)
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/WindowsStablePathCustody.Promotion.cs` (complete held capture, promotion transition, exact snapshots, deletion and missing-child observations)
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/WindowsStableRelativeWriteTree.cs` (complete relative write, reservation, native promotion and exact cleanup mechanism; directory names and strict path admission supplied by the caller)
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedPathSafety.cs` (`PathComparer` only; product policy and repository helpers remain in NFC)
- `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/WindowsStablePathCustodyTests.cs` (generic custody assertions)
- `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/FileSystemManagedVersionRepositoryTests.WriteCustody.cs` (relative-write, promotion and cleanup assertions; repository and product assertions remain in NFC)

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Files` resolves rooted paths, rejects non-regular files, opens files under an explicit byte ceiling, and reads complete content through its existing bounded SHA-256 stream reader. The stream read loop, trailing-byte probe, and final length/position checks are unchanged. Product hints and models remain in NFC.

## Public API

```csharp
public enum FileCaptureMode { IdentityOnly, CaptureBytes }
public enum FileChangeKind { Unspecified, ShortRead, Growth, Shrinkage, PositionChanged }
public sealed class FileSizeLimitExceededException : Exception
public sealed class FileChangedDuringReadException : IOException
public readonly record struct BoundedReadResult(long Length, byte[]? Sha256, byte[]? Bytes);
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

`BoundedFileReaderStreamTests` contains 26 public test methods and 50 cases. All data is synthetic. The internal `GeneratedReadStream` helper generates `0xA5` bytes on demand, records read requests, supports partial reads, and can simulate length, position, and cancellation changes without storing a large payload.

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

`RegularFileGuard.RequirePath` preserves the Windows device, directory, and reparse-point attribute mask and Unix `lstat` regular-file mask. `RequireOpenHandle` validates the handle, display path, and invalid/closed state in that order, then uses Windows `GetFileType` or Unix `fstat`. `ReadUnixIdentity` is an internal non-Windows helper: it follows `stat`, returns device/inode on success, returns null for native errors 2 (`ENOENT`) and 20 (`ENOTDIR`), and preserves other native errors. The complete `UnixFileStatus` layout is unchanged.

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

These are rooted checks before opening, not held filesystem custody. They do not close path-replacement races or detect every same-length rewrite during one read. `ReadFileAsyncDetectsSameSizeMutation` characterizes two completed reads separated by a rewrite. Files Windows custody supplies the stronger guarantees described below.

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

Added suites contain 55 public test methods and 111 cases: `RootedPathGuardTests` has 20 methods/42 cases, `RegularFileGuardTests` has 16/21, `BoundedFileReaderPathTests` has 17/42, and `BoundedFileReaderStorageBoundaryTests` has 2/6. The existing stream suite gains the default-result and empty-read identity tests and now has 26 methods and 50 cases. All fixtures are synthetic, using a Files-owned `TestWorkspace` under the system temp folder with the original bounded Windows deletion retry (450 ms total, sleeps at most 50 ms).

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

Files owns rooted resolution, regular-file guards, the single complete-content hashing loop, and held Windows custody. NFC retains `ProtectedPathGuard`, `LocalFileIdentity`, hardened output writers, product ceilings and package-path policy, stamps, selected-file models, display hints, schemas, trust, and release authority.

NFC adopts this module in its own pull request. That pull request downloads verified, versioned nupkg files at build time through `core-packages.json` and uses exact `[x]` versions, locked dependencies and source mapping. The manifest records each package's Release tag and SHA-256. Shared references and locks stay with their owner in NFC. NFC deletes only the relocated NFC generic mechanisms after callers use Core, NFC mode/exception mapping is preserved, all 40 path and 12 file cases pass, and required product output and pixel evidence passes. Core tests alone do not prove NFC product or pixel parity.



## Offline verification

Run from the solution root with `AVALONIA_TELEMETRY_OPTOUT=1`:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Files"
dotnet test Nvt.Core.sln --no-build
```
## Windows held custody

`Nvt.Core.Files.Windows` owns the sole internal Windows native path, promotion and relative write-tree implementation. These types are internal collaborators for Core consumers; they add no public Files API. Native structures, constructors, snapshots and deterministic test hooks stay internal.

The required immutable-tree seam is:

```csharp
internal static WindowsStableCustodyResult TryAcquireImmutableTree(
    string absoluteRoot, WindowsStableTreeLimits limits,
    CancellationToken cancellationToken,
    Action<WindowsStableCustodyStage>? testHook = null);
```

`WindowsStableTreeLimits(maximumFiles, maximumDirectories, maximumBytes)` requires positive ceilings in that order. Acquisition also rejects a default limits struct. Counts within a reservation may be zero, but never negative. `ForInstalledVersion(maximumFiles, maximumDirectories, maximumExpandedBytes, maximumAdmissionBytes)` validates every positive input in that order and adds expansion and admission with checked arithmetic. No package-specific default is supplied.

| Reservation | Frozen NFC value | Supplied contract |
| --- | ---: | --- |
| Files | 4,097 | `MaximumFiles` |
| Child directories, excluding the held root | 4,096 | `MaximumDirectories` |
| Expanded payload bytes | 536,870,912 | `maximumExpandedBytes` |
| Admission allowance bytes | 4,096 | `maximumAdmissionBytes` |
| Total installed bytes | 536,875,008 | Checked expansion plus admission; `MaximumBytes` |
| Relative path characters | 512 | `maximumRelativePathCharacters` and mandatory strict NFC admission |

Core does not silently enlarge these ceilings. The admission allowance reserves aggregate tree space; admission-document parsing and its individual read ceiling belong to the installation consumer. Remaining byte budgets use subtraction before addition. The child enumeration allowance retains checked arithmetic and the one-child overflow sentinel.

Acquisition normalizes local fixed-volume absolute paths and rejects relative, UNC, device, extended and alternate-stream authority. It opens the volume and every ancestor without following reparse points, then opens children relative to retained parents. Ancestors deny deletion; immutable tree files and directories deny writes and deletion. Every opened entry is checked for its expected type and reparse attributes. Complete closed topology is captured twice with ordinal sorted names, and final validation checks every held volume/file identity and each directory's exact child inventory. Added children invalidate proof even while original content remains locked. Paths and held identities are both required; normalization alone grants no custody.

`TryAcquireFile`, `TryAcquireWritableParent`, `TryAcquirePromotableTree`, and the delete-capable acquisition methods retain their separate sharing contracts. Missing-child evidence requires two observations below the same held parent and does not collapse inaccessible ancestors or a creation race into absence. `TryClone` duplicates existing handles rather than reacquiring by path, preserves sharing, and independently owns every duplicate. Failed acquisition, cancellation, interrupted cloning and hook faults release partial ownership. Borrowed held roots remain caller-owned.

`OpenReadOnlyFile` transfers a duplicate into a caller-owned stream. Its fixed stream buffer is 4,096 bytes. Complete reads use `BoundedFileReader.ReadAndHashAsync` with its existing 65,536-byte buffer, trailing-byte probe and final length/position checks; custody adds no private hash loop.

### Promotion and relative write contract

`WindowsStableRelativeWriteRoot.TryAcquire(root, out writeRoot)` holds a writable root and its ancestors. `FromHeldDirectory(root, heldRoot)` borrows the supplied directory identity by duplicating its handle. Neither method invents an installation root.

```csharp
internal WindowsStableCustodyIssue TryCreateVersionTree(
    string versionName, string stagingDirectoryName, string versionsDirectoryName,
    WindowsStableTreeReservation reservation, int maximumRelativePathCharacters,
    Func<string, bool> isSafeRelativePayloadPath,
    Action<string>? afterDirectoryCreated,
    out WindowsStableRelativeWriteTree? tree);
```

The caller supplies both directory names, an exact file/directory/byte reservation, a positive path ceiling and a mandatory strict product path callback. Plain directory-name resolution uses `RootedPathGuard`; stronger write custody continues through held native parents. NFC keeps its protected `ManagedRelativePathRules` implementation and supplies that stricter callback. There is no accepting default or fallback.

`CreateFile` creates each directory and leaf relative to retained no-follow parents, uses create-new semantics, and retains ownership of the original handles. A nested junction race fails without redirecting writes outside the tree. `PrepareForPromotion` checks exact file and directory counts, subtraction-based actual bytes, and exact topology twice before recording the owned identity snapshot and releasing descendant handles. `Promote` performs native same-volume relative rename under the held destination parent with replacement disabled. It never substitutes an ordinary path-based move.

`CapturePromotedImmutableTree` captures the same held root and compares every snapshot identity. `TryTransitionPromotedTreeToImmutableCustody` retains a read bridge while changing delete-capable promotion custody into immutable custody over the same identity. Rename structures retain 32-bit and 64-bit layouts, with filename offsets 12 and 20 respectively. `Cleanup` and `RollbackPromotionAndCleanup` remove only owned identities; added foreign entries or substituted descendants survive and produce `Changed`. A reused staging name is never treated as the promoted tree. Cleanup is idempotent and does not let cancellation discard exact rollback responsibility.

The write tree stores one closed lifecycle phase: `Writing`, `Prepared`, `Promoted`, `CleanedUp` or `Disposed`. The owned snapshot belongs to `Prepared` and `Promoted`; the cached cleanup result belongs to `CleanedUp`. Successful preparation changes `Writing` to `Prepared`, and successful promotion changes `Prepared` to `Promoted`. Failed preparation or promotion retains the current phase. Cleanup (including rollback) changes any owning phase to `CleanedUp`; repeated cleanup returns its cached result. Disposal cleans up a `Writing` or `Prepared` tree before entering `Disposed`, releases a `Promoted` tree without deleting it, and releases the parents of a `CleanedUp` tree. Repeated disposal is harmless. The valid-sequence native call order, reservation checks, result values and existing IO error messages remain unchanged.

| Member | Allowed phases |
| --- | --- |
| `StagingPath` | Any phase except `Disposed` |
| `CreateFile`, `PrepareForPromotion` | `Writing` |
| `Promote` | `Prepared` |
| `CapturePromotedImmutableTree` | `Promoted` |
| `Cleanup`, `RollbackPromotionAndCleanup` | `Writing`, `Prepared`, `Promoted`, `CleanedUp` |
| `Dispose` | All phases; idempotent in `Disposed` |

Every operation checks its phase before accessing handles or invoking callbacks. A wrong live phase throws `InvalidOperationException` naming the current phase and attempted operation. After disposal, every member except `Dispose` throws `ObjectDisposedException`, including `StagingPath` and both cleanup methods.

### Custody test mapping

Source filenames below are under `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/`; Core Files tests are under `tests/Nvt.Core.Tests/Files/Windows/`.

| Frozen source case | Core case |
| --- | --- |
| `WindowsStablePathCustodyTests:102 ImmutableTreeLocksContentAndDetectsAddedChildUntilDisposed` | Same method in `WindowsStablePathCustodyTests` |
| `WindowsStablePathCustodyTests:136 FileCustodyCannotMixAnAncestorReplacementWithTheOriginalLeaf` | Same method in `WindowsStablePathCustodyTests` |
| `WindowsStablePathCustodyTests:274 ImmutableTreeRejectsReparseChild` | Same method in `WindowsStablePathCustodyTests` |
| `WindowsStablePathCustodyTests:325 ImmutableTreeEnforcesPackageEntryBoundAndReleasesPartialCustody` | Same method in `WindowsStablePathCustodyTests`, preserving 4,098 physical entries |
| Other generic cases in `WindowsStablePathCustodyTests` | Same method names, including exact frozen file/byte ceilings, independent directory limits, cancellation, missing-child races and long descendant identity |
| `WriteCustody.cs: PreparedTreePromotesWithHeldParentAndDeletesExactFinalTree` | Same method in `WindowsStableRelativeWriteTreeTests` |
| `WriteCustody.cs: PromotedTreeCancellationCleansHeldRootAndPreservesForeignStagingReplacement` | Same method in `WindowsStableRelativeWriteTreeTests` |
| `WriteCustody.cs: PreparedTreeRejectsAndPreservesSubstitutedDescendant` | Same method in `WindowsStableRelativeWriteTreeTests` |
| `WriteCustody.cs: InstallRejectsNestedJunctionRaceWithoutOutsideWriteOrPromotion` | `WindowsStableRelativeWriteTreeEdgesTests.NestedJunctionRaceNeverWritesOutsideAndPreservesForeignResidue` |
| `WriteCustody.cs: InstallBlocksVerifiedFileRewriteAndRejectsLateChildBeforePromotion`, `InstallReportsCleanupIncompleteWhenForeignChildPreventsExactCleanup` | `HeldRewriteIsDeniedAndLateChildPreventsPromotionAndExactCleanup` retains generic physical assertions |
| `WriteCustody.cs: InstallPromotionNeverReplacesLateDestination` | `WindowsStableRelativeWriteTreeEdgesTests.PromotionNeverReplacesLateDestination` |

`WindowsStableTreeLimitsTests` covers all nonpositive ceilings, default structs, exact NFC reservations and neighboring values, expansion/admission allowance inputs and checked addition overflow. `WindowsStablePathCustodyEdgesTests` uses actual Windows handles at frozen file, directory and sparse installed-byte boundaries, each below/exact/above; it also covers linked ancestors, native open failures, stage faults, interrupted duplicates and sharing after clone disposal. `WindowsStablePromotionAcquisitionTests` covers failed capture in every acquisition mode, borrowed ownership, missing-child observation faults, checked enumeration overflow cleanup in every mode, null-root exception order, and the checked 32,767-character native UTF-16 name bound at 32,766/32,767/32,768. Write-tree edge tests cover 511/512/513 path characters, mandatory policy, nonpositive path limits, independent file/directory reservations and exact actual byte reservations.

Fixtures contain synthetic bytes in unique temporary directories. Native cases explicitly skip on unsupported operating systems, and link cases explicitly skip when link creation privilege is unavailable. Repository installation results, product schemas and release authority remain NFC evidence. NFC removes its relocated native owner only after Core consumers hold the same identities through adoption, original product assertions retain their values and outputs, and independently reviewed versioned package adoption satisfies its product and UI compatibility rules. No Bootstrap package wiring follows from this extraction.
