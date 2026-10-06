[English](Launcher.md) | [中文](Launcher.zh-TW.md)

# Launcher contracts

`Nvt.Core.Launcher.Contracts` contains the BCL-only `net8.0` version, content identity, package-policy port, executable-lease port and normalized result values shared by package verification and managed installation. NFC must retain an adapter for its strict schemas, exact product/runtime/registry values, protocol names, payload allowlist, source policy and retention policy. These Core values do not define a new serialized NFC schema.

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedAppVersion.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedVersionRepository.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementPolicy.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateCatalogModels.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherBootstrapContracts.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedLauncherEntry.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedInstallationLayout.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedSetupTransactionDocuments.cs`

NFC Contracts and `docs/contracts/**` remain R3 authority, read only for behavior checks and never imported. The matching catalog, release-manifest and launcher-bootstrap contracts remain unchanged. Activation phases, state machines, root resolution, transaction documents, process code, native custody, ZIP plans and repository inventory/delete ports belong to their respective owners.

[`ProductDescriptor`](../../../src/Nvt.Core/Launcher/Contracts/ProductDescriptor.cs) requires explicit names and callbacks. Executable paths must be safe slash-separated relative paths; Bootstrap and each evaluated archive root must be safe single names. Blank, rooted, traversal, alternate-stream, backslash, C0 control-character, invalid punctuation and Windows device-name inputs fail. Paths have at most 512 characters, as in NFC. Callback results are checked for every requested version. The descriptor supplies runtime configuration, not content trust.

[`ManagedAppVersion`](../../../src/Nvt.Core/Launcher/Contracts/ManagedAppVersion.cs) preserves the canonical stable three-component parser, numeric comparison and invariant text. [`UpdateCatalogVersionSnapshot.Create`](../../../src/Nvt.Core/Launcher/Contracts/UpdateCatalogVersionSnapshot.cs) makes previously internal construction available after product admission. It requires product-supplied positive ceilings for package bytes and UTF-8 release-note bytes, UTC metadata, a structurally safe relative path of at most 512 characters, a positive length within the package ceiling, lowercase SHA-256 hashes, present notes within the note ceiling and a defined notification policy. NFC supplies its frozen ceilings (134,217,728 package bytes and 65,536 note bytes) and still enforces its ZIP grammar and strict timestamp wire grammar. The immutable properties and identity composition remain:

```text
version|relative-package-path|invariant-package-size|package-sha256|release-manifest-sha256
```

Configured source roots, publication time, notes and notification policy remain outside this identity. `VerifiedUpdateCandidate` preserves version, admission identity and release notes; `ManagedVersionAdmission` also preserves the exact manifest digest. Paths cannot infer admission.

[`IProductPackagePolicy`](../../../src/Nvt.Core/Launcher/Contracts/PackageContracts.cs) consumes exact manifest bytes. `archivePaths: null` retains installed-verification mode. `PackageManifest` carries generic product/runtime/version values and complete normalized files. Its optional `PackageLauncher` is an unowned parsed declaration. The retained NFC adapter must validate the strict manifest and closed payload before returning it; Core's verifier then binds the actual owner admission and exact manifest hash.

[`ManagedLauncherIdentity.Create`](../../../src/Nvt.Core/Launcher/Contracts/ManagedLauncherIdentity.cs) requires the descriptor, an explicit positive executable bound at most 200,000,000 bytes, and all existing owner/version/hash/protocol/path/size fields. Protocol remains exactly `1`, paths match the descriptor ordinally, owner admission remains bounded to 2,048 characters, and both digests remain lowercase SHA-256. `MatchesOwner` compares application version, admission identity and release-manifest hash ordinally. `ManagedImmutableBootstrapIdentity.Create` binds the descriptor's exact root filename and retains the 200,000,000-byte ceiling. There are no product-specific identity defaults.

[`ManagedPackageResults`](../../../src/Nvt.Core/Launcher/Contracts/ManagedPackageResults.cs) preserves installation, verification, executable-lease and installed-launcher issue names, numeric values and success predicates. `HasSupportedManagedLauncher` is retained. `IManagedExecutableLaunchLease` remains disposable custody with exact executable/working-directory values and final synchronous validation; implementations and process behavior remain with their owners. NFC's wire DTOs and exact `manual-only`/`notify` mappings remain in its adapter.

The whole three frozen source test files and their local helpers were read before porting. Generic tests preserve `NonCanonicalOrNonStableVersionFailsClosed`, `PackageIdentityDoesNotIncludeConfiguredSourcePath`, `LauncherIdentityMatchesOnlyItsExactOwnerAdmission` and `ImmutableBootstrapIdentityAdmitsCanonicalMaximum`, plus generic round-trip, ordering and launcher validation scenarios. Product schema, real-package and activation-state scenarios stay in NFC. New synthetic tests cover descriptor fault paths, explicit ceilings, Bootstrap name binding, identity bytes across source moves and cultures, metadata exclusions, factory faults, nullable policy mode, all result predicates and the bounded public API. No internal test access or shared project changes are required.

Core exception messages name the failing parameter and can differ from NFC's text. NFC's adapter keeps its own messages where its callers depend on them.

Verification: the host restored in locked mode, built with 0 warnings and ran the full solution on this branch, based on Core `main` `d47dfe3`. DEL and C1 characters stay accepted, as in NFC. The pull request records the exact results and both reviews.

## Bounded archive reads

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/BoundedArchiveReader.cs` — complete actual expanded-byte accounting, hashing, bounded copying and file-read wrapper.

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Launcher.Verification.BoundedArchiveReader` and `ExpandedByteBudget` are internal. The reader hashes actual expanded bytes, optionally copies accepted blocks, and shares one explicit positive byte budget across entries. `ReadAndHashAsync` and `CopyAndHashAsync` require exact declared lengths; `ReadAtMostAndHashAsync` permits shorter content. `ReadFileAndHashAsync` opens and disposes its own read-only file stream. The stream-based methods borrow their input and destination and leave both open. They do not require seek or length metadata. Compressed package hashing belongs to `Nvt.Core.Files.BoundedFileReader.ReadAndHashAsync`.

The fixed buffer remains 65,536 bytes. Each read requests at most one byte beyond the lesser of the remaining entry limit and aggregate budget. Actual bytes, including that overflow sentinel, enter the aggregate counter before the entry-length check. Aggregate overflow takes precedence over entry overflow. An overflowing block reaches neither the destination nor the progress callback. A short exact read returns an entry-length mismatch after preserving the accepted prefix. Failed bounded results carry no hash; successful results use canonical 64-character lowercase SHA-256. Cancellation, read faults, write faults and callback exceptions propagate. Destination writes precede progress notification. NFC supplies its frozen expanded ceiling of 536,870,912 bytes (512 MiB).

The source-to-Core reader test map is:

| Frozen source case | Core case |
| --- | --- |
| `FileSystemManagedVersionRepositoryTests.Security.cs`: `ActualEntryBytesCannotExceedDeclaredLength` | `BoundedArchiveReaderTests.ActualEntryBytesCannotExceedDeclaredLength` preserves declared length 8, budget 2,048, and the 9-byte sentinel. |
| `FileSystemManagedVersionRepositoryTests.Security.cs`: `ActualExpandedBytesShareOneAggregateBudget` | `BoundedArchiveReaderTests.ActualExpandedBytesShareOneAggregateBudget` preserves the 6-byte first entry, 10-byte shared budget, 11-byte consumed count and 5-byte second read. |

Additional synthetic tests exercise 512 MiB minus one, exactly 512 MiB and one byte over; neighboring exact and at-most entry lengths; zero-length entries; 65,536-byte buffer neighbors with partial reads; zero and negative budget arguments; long counters; argument and exception order; overflow materialization; deterministic cancellation; bounded faults; borrowed stream lifetime; and owned-file disposal. Runtime file fixtures use unique temporary directories.

NFC retains strict manifest and admission schemas, product payload allowlists, release trust, firmware metadata and installation policy. NFC adopts relocated mechanisms in a separate pull request. It downloads versioned nupkg files from Core's Release at build time, checks their SHA-256 against its committed manifest, and uses exact `[x]` package versions, locked restore and source mapping. NFC removes its generic reader only after callers consume Core and its original package, installation and product compatibility assertions pass with unchanged values and output bytes. Core reader tests do not establish NFC product or pixel parity.

## Bounded package verification

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedPackageVerifier.cs` — closed archive inventory, declared and actual expansion checks, checksum parsing, content verification and internal extraction; strict JSON parsing and product metadata remain in NFC.
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/FileSystemManagedVersionRepository.cs` — compressed package length/hash admission and public verification issue mapping; filesystem resolution, native custody, promotion and inventory remain separate consumers.
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/BoundedArchiveReader.cs` — actual expanded-byte accounting, as described above.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateCatalogModels.cs` — exact candidate identity and release-note propagation through the existing normalized contracts.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedVersionRepository.cs` — existing verification result, issue values and launcher-presence flag.

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

[`ManagedPackageVerifier`](../../../src/Nvt.Core/Launcher/Verification/ManagedPackageVerifier.cs) is the only public type in `Nvt.Core.Launcher.Verification`. Its constructor requires `ProductDescriptor`, `IProductPackagePolicy` and `PackageVerificationLimits`. `VerifyAsync(Stream package, UpdateCatalogVersionSnapshot candidate, CancellationToken cancellationToken)` returns the existing `ManagedPackageVerificationResult`. A successful result preserves the exact candidate version, admission identity and release notes. Its launcher flag requires a descriptor-bound launcher identity whose owner version, admission identity and manifest hash all come from that candidate.

The caller lends a readable, seekable package stream and retains stable read custody throughout verification and any internal plan use. The verifier leaves the package open. Unsupported capability shapes fail without buffering. Complete compressed content is hashed only through `Files.BoundedFileReader.ReadAndHashAsync`, in identity-only mode, including its exact-length EOF probe and final length/position checks. Length failures precede hash mismatch; hash mismatch precedes opening the ZIP. The frozen public mapping returns `PackageUnavailable` for I/O, access and malformed-ZIP failures, `PackageMismatch` for a matching-length package with another digest, `UnsafeArchive` for unsafe archive shape or aggregate expansion, and `InvalidPayload` for invalid manifest, checksum or member content. Cancellation propagates; adapter programming exceptions propagate, while adapter I/O failures follow the same frozen I/O mapping.

Archive admission rejects empty archives, excessive member counts, wrong ordinal root prefixes, backslashes, links and reparse attributes. Structural relative paths retain the 512-character bound, Windows device-name exclusions and unsafe-segment checks before mandatory product path policy. File paths are unique ignoring case. Explicit directory records must contain no bytes and have safe paths; they consume archive member slots. As in the source, only unique implicit parents of file members count toward installed directories, and repeated empty directory records are allowed. Declared lengths use subtraction before addition. One shared `ExpandedByteBudget` then counts actual manifest, checksum and payload bytes; aggregate overflow takes precedence over entry overflow, including the one-byte sentinel.

The exact manifest hash must equal the catalog pin before the mandatory adapter sees the exact bytes and complete file inventory. NFC validates strict JSON and its schema before returning normalized facts. Core independently rechecks ordinal product/runtime equality, candidate version equality, positive bounded file sizes, canonical lowercase 64-character SHA-256, reserved-member exclusions, duplicate paths and closed inventory. Application files use the expanded-byte limit; the executable ceiling applies only to declared launchers. It snapshots the normalized file list before asynchronous content verification. Launcher declarations must match the exact descriptor path, supported protocol and declared member length/hash, then pass `ManagedLauncherIdentity.Create` with the exact candidate owner admission and manifest digest. A successful adapter cannot bypass these checks.

Checksums use strict UTF-8 and exact lowercase 64-character hashes, two ASCII spaces and safe relative paths. Paths are compared ordinally, and the checksum inventory contains exactly every declared payload plus the exact manifest digest. Invalid UTF-8, BOMs, duplicates, absent entries, extra entries, changed hashes and invalid delimiters fail. The source accepts LF or CRLF, blank lines, reordered entries and an EOF without a final newline. These accepted byte shapes remain unchanged.

All product limits are mandatory positive parameters in the existing order; `MaximumInstalledDirectories` follows `MaximumExecutableBytes`. The verifier also rechecks copied records. NFC supplies these frozen values; Core supplies no product defaults:

| Bound | NFC value | Core receipt |
| --- | ---: | --- |
| Archive members | 4,096 | `MaximumArchiveEntries` |
| Complete compressed bytes | 134,217,728 | `MaximumPackageBytes` |
| Actual expanded bytes | 536,870,912 | `MaximumExpandedBytes` |
| Manifest and checksum bytes, each | 1,048,576 | `MaximumManifestBytes`, shared document ceiling |
| Admission document bytes | 4,096 | `MaximumAdmissionBytes`, positive reservation for the extraction consumer |
| Declared launcher bytes | 200,000,000 | `MaximumExecutableBytes`, also bounded by the existing identity contract |
| Installed files | 4,097 | Archive ceiling plus one admission reservation, using a long counter |
| Installed directories | 4,096 | `MaximumInstalledDirectories` |
| Relative path characters | 512 | Existing contract validation |
| UTF-8 release-note bytes | 65,536 | Existing catalog snapshot factory; bytes, not characters |

The source verifier's 200-character single asset-name predicate applies to SBOM/provenance manifest fields. Those fields are excluded from `PackageManifest`, so NFC's strict adapter retains that predicate with its schema. Synthetic adapter tests characterize 199, 200 and 201 characters; Core does not acquire release metadata authority.

`ManagedPackagePlan` and `ManagedPackagePlanResult` are internal. Successful plans retain a live ZIP reader, a closed read-only file inventory, exact document bytes, file/directory counts, expansion facts and the owner-bound launcher. They own the ZIP reader and leave caller package custody open on disposal. Internal extraction shares one fresh actual-byte budget, rechecks every exact length/hash, owns and disposes destination streams, and rejects changes with the source message `Archive content changed after admission.` Disposed plans cannot extract. The admission and installed-file reservations are available to the internal installation consumer. The verifier creates no installation root, staging tree, admission JSON, activation transaction or promotion.

The source-to-Core package test map uses `ManagedPackageVerifierTests`:

| Frozen source case | Core evidence |
| --- | --- |
| `Security.cs: ChangedPackageNeverReachesZipAdmission` | Same name; preserves length/digest issue order and proves zero policy/destination admission. |
| `Security.cs: DuplicateAndLinkArchiveMembersNeverVerify` | Same name; preserves case-insensitive duplicates and UNIX links, adds Windows reparse attributes. |
| `ExpandedBytes.cs: UnderreportedZipEntryFailsVerifyAndInstallWithoutMaterialization` | Same name; preserves a one-byte README declaration and rejects before internal extraction can create destinations. |
| `ExpandedBytes.cs: Zip64DeclaredSizeOverflowFailsVerifyAndInstallWithoutResidue` | Same name; preserves a positive first entry and second entry of `long.MaxValue`, with no plan or destination residue. |
| `Security.cs: NonCanonicalChecksumDocumentNeverVerifies` | Same name; preserves changed-hash and extra-line mutations. |
| `Security.cs: InvalidManifestOrClosedPayloadNeverVerifies` | Same name; preserves generic product/version/hash/size/missing/extra assertions; synthetic role and unknown-field cases reject in strict policy before projection. |
| `ExpandedBytes.cs: ExactExpandedByteBudgetInstallsWhileOneByteLessFailsBeforeMaterialization` | Same name; preserves the exact summed entry budget and one-byte-smaller rejection, and compares internal extraction output bytes. |

The source test filenames above are `FileSystemManagedVersionRepositoryTests.Security.cs` and `FileSystemManagedVersionRepositoryTests.ExpandedBytes.cs` under `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/`. Core cases whose source names mention installation exercise the closed extraction plan; repository staging, atomic promotion and installed inventory assertions remain NFC adoption evidence.

`PackageCeilingTests` covers frozen member, installed-file, directory, document, compressed, declared-launcher and relative-path boundaries, their neighboring values, application acceptance above the executable ceiling, actual expansion independently of metadata, document positivity and underreported documents. `PackageVerificationLimitsTests` covers zero/negative arguments, copied limits, unchanged argument order, explicit NFC values and the existing executable identity ceiling. `PackageIdentityAndChecksumTests` covers forged normalized identities, launcher owner binding, strict-policy rejection and checksum byte grammar. `PackageStreamAndPlanTests` covers public surface, borrowed custody, deterministic cancellation, bounded compressed faults, Files probe ordering, immutable plan facts, disposal, post-admission tamper and destination write failures. The reader cases above cover the frozen 512 MiB actual budget and overflow sentinel directly.

NFC retains strict schemas, product payload roles and allowlists, wire grammar, release metadata, firmware data, trust and release authority. Adoption downloads independently versioned nupkg files from Core's Release at build time, checks their SHA-256 against the tool's committed manifest, and uses exact `[x]` versions, locked restore and source mapping. Original NFC schema, package, installation, values, traces and output-byte assertions must pass before deleting its relocated generic verifier and reader. UI-affecting adoption also preserves decoded pixels in the same recorded environment. Core synthetic tests do not establish NFC product or pixel parity, and this API provides no Bootstrap package wiring authority.
