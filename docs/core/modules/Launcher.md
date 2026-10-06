[繁體中文](Launcher.zh-TW.md)

# Launcher

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedAppVersion.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedVersionRepository.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementPolicy.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateCatalogModels.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherBootstrapContracts.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedLauncherEntry.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionActivationPolicy.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherMutationFence.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagerStateStore.cs` — read/save results and ports; writer result, exact live custody contract and state-store port.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateSourceRegistry.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedInstallationLayout.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedSetupTransactionDocuments.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/FileSystemVersionManagerWriteLease.cs` — exclusive writer acquisition, lock identity and live custody.
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedPathSafety.cs` — `ReadBoundedFileAsync` path admission, stream opening and length checks; complete-content reading delegates to Files.
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/JsonVersionManagerStateStore.cs` — explicit raw path, bounded byte read/write and writer delegation; product defaults and strict codecs remain in NFC.
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/JsonLauncherBootstrapStateStore.cs` — injective path derivation, bounded raw byte access and typed write failures; suffix configuration and strict codecs remain in NFC.

- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/StableManagedExecutableLaunchLease.cs` (complete lease adapter, PE checks, held measurement and copying; complete-content hashing delegates to Files)
- `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/FileSystemManagedVersionRepositoryTests.cs` (`AcquiredApplicationLeaseDeniesExecutableSwapUntilReleased` generic custody assertions only)
- `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/FileSystemInstalledLauncherRepositoryTests.cs` (generic held lease, ancestor replacement, content and late-child assertions; product schema and repository policy remain in NFC)
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedActivationCoordinator.cs` — application process/READY ports, results and supervision; stable desktop handoff declarations stay with the process adapter.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherBootstrapCoordinator.cs` — complete launcher supervisor.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherBootstrapCoordinator.ActiveAttemptRecovery.cs` — complete active-attempt recovery.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedProcessLifetimeContracts.cs` — ManagedProcessLifetimeKind only.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedVersionSeedBootstrapper.cs` — canonical seed policy and bootstrapper.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedApplicationStartupCoordinator.cs` — READY dispatch; desktop snapshots are projected through the initialization port.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementInitialization.cs` — read-only and writer-qualified initialization dispatch.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.cs` — initialization and guarded delete methods and operation outcomes; source/check/session/UI composition stays in NFC.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.Install.cs` — prepared install transaction; catalog selection is a mandatory caller port.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.Activation.cs` — activation preparation/cancellation and retention acknowledgement.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.Recovery.cs` — prepared mutation convergence and commit helpers; retention advice is a mandatory caller port.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.State.cs` — durable root/load/recovery and inventory projection; source/session snapshots stay in NFC.

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Launcher.Contracts` and `Nvt.Core.Launcher.Activation` provide BCL-only `net8.0` values and ports for managed application and launcher activation. `Nvt.Core.Launcher.Persistence` supplies bounded raw state access and the exact application-state writer. Verification and the internal Windows lease adapter are described below. Pure path normalization uses the current platform's path rules. `Nvt.Core.Launcher.Coordination` supervises READY through injected process ports; process creation and native custody use their respective mechanism owners.

The extracted slices are version and content identities, descriptor and package-policy declarations, normalized package results, immutable application and launcher state, transition helpers, durable snapshot comparison, generic inventory, delete-owner protection, repository/state ports, raw state access and writer custody. `UpdateSourceRegistry.cs` contributes only `VersionSourceRegistryState`. `VersionManagementPolicy.cs` contributes inventory and generic delete decisions; its retention threshold, automatic deletion policy and discovery notifications remain in NFC. `LauncherMutationFence.cs` contributes protection values, its port and the writer-scoped experience guards; the strict JSON projection adapter remains in NFC. Native custody structures belong to Files. Strict wire DTOs/codecs stay with their product owner; execution tokens, ZIP plans and test hooks remain internal.

## Contracts

[ProductDescriptor](../../../src/Nvt.Core/Launcher/Contracts/ProductDescriptor.cs) requires explicit product, runtime, registry, executable and protocol names, the Bootstrap filename and an archive-root callback. Executable paths are safe slash-separated relative paths; Bootstrap and evaluated archive roots are safe single names. Blank, rooted, traversal, alternate-stream, backslash, C0 control, invalid punctuation and Windows device-name inputs fail. Paths have at most 512 characters. DEL and C1 characters retain their frozen acceptance. Every archive-root callback result is checked. Configuration supplies no content trust.

[ManagedAppVersion](../../../src/Nvt.Core/Launcher/Contracts/ManagedAppVersion.cs) retains the canonical stable three-component parser, numeric ordering and invariant formatting. [UpdateCatalogVersionSnapshot.Create](../../../src/Nvt.Core/Launcher/Contracts/UpdateCatalogVersionSnapshot.cs) requires explicit positive package and UTF-8 release-note ceilings, UTC metadata, a structurally safe relative package path of at most 512 characters, positive package length within the supplied ceiling, lowercase SHA-256 digests, present notes within their ceiling and a defined notification policy. NFC's frozen ceilings are 134,217,728 package bytes and 65,536 release-note bytes. NFC also validates its strict ZIP and timestamp wire grammar.

Catalog admission identity remains `version|relative-package-path|invariant-package-size|package-sha256|release-manifest-sha256`. Configured roots, publication time, notes and notification policy remain outside that identity. `VerifiedUpdateCandidate` preserves version, admission identity and notes; `ManagedVersionAdmission` additionally preserves the exact manifest digest. Paths never infer admission.

[IProductPackagePolicy](../../../src/Nvt.Core/Launcher/Contracts/PackageContracts.cs) receives exact manifest bytes and returns product-admitted normalized facts. `archivePaths: null` retains installed-verification mode. Its mandatory NFC adapter validates strict schema, exact product/runtime values and closed payload before returning a `PackageManifest`. `PackageLauncher` is an unowned parsed declaration. No default-accept policy or fallback adapter is supplied.

[ManagedLauncherIdentity.Create](../../../src/Nvt.Core/Launcher/Contracts/ManagedLauncherIdentity.cs) requires the descriptor, an explicit positive executable ceiling no greater than 200,000,000 bytes, and every exact owner/version/hash/protocol/path/size field. NFC supplies 200,000,000 bytes. Protocol remains exactly `1`; the executable path matches the descriptor ordinally; the nonblank owner admission is at most 2,048 characters; both digests are lowercase SHA-256. `MatchesOwner` compares application version, admission string and manifest digest ordinally. `ManagedImmutableBootstrapIdentity.Create` retains its exact descriptor-bound root filename and 200,000,000-byte ceiling.

[ManagedPackageResults](../../../src/Nvt.Core/Launcher/Contracts/ManagedPackageResults.cs) preserves install, verification, executable-lease and installed-launcher issue values and success predicates, including `HasSupportedManagedLauncher`. `IManagedExecutableLaunchLease` supplies disposable custody, exact executable/working-directory values and final synchronous validation. The port declares no process starter; its internal Windows adapter delegates custody to Files.

## Activation and inventory API

[VersionManagerState.Create](../../../src/Nvt.Core/Launcher/Activation/VersionActivationPolicy.cs) validates unique admissions, nonblank admission strings and exact lowercase manifest digests before checking all referenced versions. It rejects simultaneous activation and filesystem journals, undefined transaction/phase values, unmatched delete admissions and installs of already admitted versions. An ordinary active-launch journal must identify the committed active version and exact prior active/fallback versions. Other application phases retain their frozen prior-version membership predicate. Application admission strings retain their source predicate without borrowing the launcher owner's string ceiling.

`VersionSourceRegistryState` preserves accepted revision, exact digest and manual pin. Revision zero requires an absent digest and a manual pin; positive revisions require a lowercase SHA-256 digest. Negative revisions fail, and there is no upper revision ceiling. Registry-bound sources must be fully qualified and already normalized; an unbound seed state may acquire its root binding once. Root checks use the platform path comparer. Durable snapshot comparison includes every persisted application field, including registry revision, digest and pin; stored root/source strings are compared ordinally in tokens.

[VersionActivationPolicy](../../../src/Nvt.Core/Launcher/Activation/VersionActivationPolicy.cs) exposes `BeginActivation`, `RecordCandidateLaunch`, `CancelRequestedActivation`, `CommitReady`, `FailActivation`, `RecordRollbackLaunch`, `CommitRollback`, `RecordActiveLaunch` and `ClearActiveLaunch`. Phase meanings remain `Requested = 0`, `CandidateLaunchRecorded = 1`, `RollbackLaunchRecorded = 2` and `ActiveLaunchRecorded = 3`. Candidate ready commits only its recorded candidate launch. Cancellation closes only an unlaunched request. Failure restores the recorded admitted LKG when distinct from the failed candidate, otherwise the prior active version. Rollback launch selection is recorded once; repeated selection yields no second target. These journals do not guarantee exactly-once process creation after a crash. Explicit selection of a healthy admitted older version remains legal; NFC's newer-only automatic notification policy is separate.

[LauncherBootstrapState.Create](../../../src/Nvt.Core/Launcher/Activation/LauncherBootstrapState.cs) normalizes the root first, then rejects split active/fallback presence, a journal with different exact prior identities, or an ordinary active guard for another launcher. Its candidate, rollback, ready and failure transitions retain their source predicates and messages. Launcher rollback recording requires a recorded candidate launch. Every launcher durable field participates in snapshot comparison. Internal transition/observer helpers supply no IO authority.

[ManagedVersionInventory.Create](../../../src/Nvt.Core/Launcher/Activation/VersionManagementPolicy.cs) copies input rows, rejects duplicate versions, permits at most one active row and validates admission/integrity facts in frozen order. Rows sort newest first. Healthy/damaged counts include only admitted rows; the remaining rows count as unadmitted. `Find` resolves an exact version.

`VersionManagementPolicy.DecideDelete` and [LauncherMutationProtection](../../../src/Nvt.Core/Launcher/Activation/LauncherMutationFence.cs) preserve journal fencing before exact launcher-owner protection, absent/uncommitted inventory handling and active-version protection. Owner equality includes version, admission identity and manifest digest. Fallback-only authority supplies rollback-loss information. This decision grants no deletion consent. NFC retains confirmation, retention thresholds and notifications.

[IManagedVersionRepository](../../../src/Nvt.Core/Launcher/Activation/ManagedVersionRepository.cs) preserves `AcquireApplicationLaunchLeaseAsync`, `VerifyPackageAsync`, `InstallAsync`, `InventoryAsync` and `DeleteAsync` argument order and typed results. Its default lease method checks cancellation and returns unavailable custody. Whole-inventory unavailability contains no partial inventory. [IInstalledLauncherRepository](../../../src/Nvt.Core/Launcher/Activation/IInstalledLauncherRepository.cs) preserves owner-bound verification and lease acquisition. [IVersionManagerStateReader](../../../src/Nvt.Core/Launcher/Activation/VersionManagerStateReader.cs) loads a validated snapshot without inventing identities. `ILauncherBootstrapStateStore` exposes load and typed save under the caller's application-state lease, with no second writer lease. Transitions are independent of `IO.AtomicOutput` and `Lifecycle.UndoService`.

## Frozen test mapping

All test paths below are under `tests/NvtFwCombiner.Application.Tests/VersionManagement/` at the frozen parent. Core equivalents are under `tests/Nvt.Core.Tests/Launcher/Activation/`.

| Frozen source case | Core case |
| --- | --- |
| `VersionActivationPolicyTests:14 ReadyCommitsPendingCandidate` | `VersionActivationPolicyTests.ReadyCommitsPendingCandidate` |
| `VersionActivationPolicyTests:34 FailureRestoresPriorVersionAndCannotOscillate` | `VersionActivationPolicyTests.FailureRestoresPriorVersionAndCannotOscillate` |
| `VersionActivationPolicyTests:72 RequestedActivationCannotCommitReady` | `VersionActivationPolicyTests.RequestedActivationCannotCommitReady` |
| `VersionActivationPolicyTests:85 ConcurrentDurableTransactionsAreRejected` | `VersionActivationPolicyTests.ConcurrentDurableTransactionsAreRejected` |
| `VersionActivationPolicyTests:107 DeleteMutationAdmissionMustMatchCommittedAdmission` | `VersionActivationPolicyTests.DeleteMutationAdmissionMustMatchCommittedAdmission` |
| `VersionActivationPolicyTests:129 RegistryAuthorityDriftInvalidatesDurableSnapshotToken` | `VersionActivationPolicyTests.RegistryAuthorityDriftInvalidatesDurableSnapshotToken` |
| `LauncherBootstrapContractTests:125 LauncherStateRejectsSplitActivePairAndMismatchedPendingSnapshot` | `LauncherBootstrapContractTests.LauncherStateRejectsSplitActivePairAndMismatchedPendingSnapshot` |
| `LauncherBootstrapContractTests` identity, phase and active-guard cases | Same named generic cases in `LauncherBootstrapContractTests` |
| `VersionManagementPolicyTests.DeleteDecisionProtectsActiveAndWarnsForLastKnownGood` | `InventoryAndPortTests.DeleteDecisionProtectsActiveAndWarnsForLastKnownGood` |

`ActivationTransitionTests` covers the complete application/launcher phase matrices, exact messages, wrong identities, repeated recovery, older-version selection and mutation guards. `ActivationStateTests` covers every durable field, exact prior identity, root binding, undefined journal enums, digest lengths 63/64/65, owner-admission lengths 2,047/2,048/2,049, path lengths 511/512/513, executable sizes around 200,000,000, and zero/negative explicit ceilings. `InventoryAndPortTests` covers the zero/one/two active-row boundary, source check order, health counts, exact delete owners, fail-closed results and cancellation. Contracts tests continue to cover canonical versions, catalog package/note ceilings, identity composition and descriptor grammar. Test data is synthetic; no native execution is part of these pure contracts.

## NFC boundary and adoption

NFC keeps strict state/manifest/catalog DTOs and codecs, canonical wire schemas, product wording, exact protocol names, package trust and release authority, registry locators/replicas, retention and notification policy, deletion consent, firmware behavior and UI composition.

NFC consumes verified, versioned nupkg files under `vendor/nuget/` and uses exact `[x]` pins, lock files and locked restore. Source mapping restricts Core packages to that folder; `SOURCE.md` records the reviewed source and package SHA-256 hashes. Core and NFC release independently. Duplicate executable bodies are deleted only when the corresponding NFC adapter uses Core and preserves complete values, event traces and output bytes. UI-affecting adoption requires zero changed decoded pixels under the same recorded environment. The eight legacy font values remain unchanged. Bootstrap package wiring requires separate launcher-adoption authorization.

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

NFC retains strict manifest and admission schemas, product payload allowlists, release trust, firmware metadata and installation policy. NFC adopts relocated mechanisms in a separate pull request. It consumes verified, versioned nupkg files under `vendor/nuget/` with exact `[x]` package versions, locked restore and source mapping; `SOURCE.md` records the reviewed source and package SHA-256 hashes. NFC removes its generic reader only after callers consume Core and its original package, installation and product compatibility assertions pass with unchanged values and output bytes. Core reader tests do not establish NFC product or pixel parity.

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

| Bound | NFC value | Core contract |
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

NFC retains strict schemas, product payload roles and allowlists, wire grammar, release metadata, firmware data, trust and release authority. Adoption consumes independently versioned, verified nupkg files under `vendor/nuget/` with exact `[x]` versions, locked restore and source mapping; `SOURCE.md` records the reviewed source and package SHA-256 hashes. Original NFC schema, package, installation, values, traces and output-byte assertions must pass before deleting its relocated generic verifier and reader. UI-affecting adoption also preserves decoded pixels in the same recorded environment. Core synthetic tests do not establish NFC product or pixel parity, and this API provides no Bootstrap package wiring authority.

### Intentional differences from the frozen source

Core differs from the frozen NFC verifier in three places. Each one rejects input that the source handled differently. None accepts input that the source rejected.

- `VerifyAsync` checks that the package stream can read and seek, and that the candidate package size is within `MaximumPackageBytes`, before it reads. Either failure returns `PackageUnavailable`.
- When the package changes while it is read, the result is `PackageUnavailable`. The source returned `PackageMismatch`.
- A manifest file entry named `RELEASE-MANIFEST.json` or `SHA256SUMS.txt` is rejected as `InvalidPayload`.

## Raw state access and exact writer custody

The public API is in `Nvt.Core.Launcher.Persistence`. Existing read/load/save categories and `IVersionManagerStateReader` remain in `Nvt.Core.Launcher.Activation`; `ILauncherBootstrapStateStore` retains its original signatures and has no writer-acquisition member.

```csharp
FileSystemVersionManagerWriteLease.TryAcquireAsync(
    string statePath, TimeSpan waitTimeout, CancellationToken cancellationToken)
    // ValueTask<VersionManagerWriteLeaseResult>
VersionManagerStateFile(string path, int maximumBytes)
LauncherBootstrapStateFile(string versionManagerStatePath, string pathSuffix, int maximumBytes)
LauncherBootstrapStateFile.DerivePath(string versionManagerStatePath, string pathSuffix)
    // string
```

Both raw file collaborators expose `StatePathIdentity` and `ReadAsync(token) -> ValueTask<byte[]?>`. `VersionManagerStateFile` additionally exposes `TryAcquireWriteLeaseAsync(waitTimeout, token)` and `WriteAsync(bytes, token) -> ValueTask`. `LauncherBootstrapStateFile` exposes `TryWriteAsync(bytes, token) -> ValueTask<LauncherBootstrapStateSaveResult>`. NFC supplies its frozen app-state ceiling of **1,048,576 bytes**, launcher-state ceiling of **65,536 bytes**, and suffix `.launcher-bootstrap.v1.json`. Core requires explicit positive ceilings and a nonblank fixed suffix; it supplies no product path or ceiling defaults. For a fixed suffix, derivation appends it to the entire full app-state path, preserving injectivity across distinct canonical app-state paths.

`IVersionManagerStateStore` extends the unchanged reader port. It retains `TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken)`, `SaveAsync(VersionManagerState state, CancellationToken cancellationToken)` and the default `TrySaveAsync` implementation. Default save mapping propagates cancellation, converts `IOException`, `UnauthorizedAccessException` and `InvalidOperationException` to `VersionManagerStateSaveIssue.Unavailable`, and propagates other failures.

`VersionManagerWriteLeaseIssue` retains `None`, `Busy` and `Unavailable`. `VersionManagerWriteLeaseResult(issue, IDisposable? lease = null)` preserves the successful-result/one-disposable invariant and message `A successful writer lease must own exactly one handle.` `Issue` and `IsAcquired` describe acquisition. **Authority requires `HoldsStatePath(statePath)`**, which validates a nonblank argument and requires an undisposed result backed by the internal production custody, an open valid handle and ordinal equality of normalized exact state paths. Arbitrary disposables, foreign paths and disposed or closed custody grant no authority. `Dispose()` releases ownership once; historical `IsAcquired` remains true after disposal, as in the source.

Acquisition validates the nonblank path before rejecting negative waits. Zero makes an immediate attempt. Identity is the full path with its ending separator trimmed, then uppercased invariantly on Windows. The sibling key is `.{original-state-filename}.{first-24-lowercase-SHA256-characters}.writer.lock`, hashing UTF-8 identity bytes. The lock uses `OpenOrCreate`, `ReadWrite`, `FileShare.None`, buffer size 1 and `WriteThrough`. Retries remain 50 ms, each delay capped by the remaining wait. Only native sharing codes 32 and 33 yield `Busy` once elapsed time reaches the bound. Original path/directory, access and other I/O failures retain `Unavailable`; cancellation propagates. The final retry attempts opening before classifying a sharing violation at the deadline. Lock files may remain after disposal; their existence grants no authority.

Raw reads retain the source's existence and file-level reparse checks before opening with `Open`, `Read`, `FileShare.Read`, a 64 KiB buffer and `Asynchronous | SequentialScan`. They reject lengths below 1 or above the supplied ceiling before capture. Missing, linked, empty, oversized or changed-length files yield null; premature EOF remains `EndOfStreamException`. Complete held-stream reading uses only `Files.BoundedFileReader.ReadAndHashAsync` in capture mode, including its trailing-byte probe and final length/position checks. Cancellation propagates when reading admitted content. The strict NFC adapter distinguishes Missing before the raw read and maps null to Invalid; raw bytes alone confer no canonical-state authority. These raw checks do not supply the separate Files Windows stable-path custody mechanism.

App writes reject bytes above the explicit ceiling with `Version-manager state exceeds its bounded size.` Launcher writes return `Unavailable` for overflow and the original I/O/access/invalid-operation failure categories. Cancellation propagates. Both publish through `IO.AtomicOutput.WriteBytesAsync(path, bytes, token)`; Launcher contains no temporary-file write, flush, move or cleanup engine. The caller holds the same app-state writer across strict decoding, journal decisions, encoding, publication and the rest of its transaction. The launcher collaborator creates no second writer. AtomicOutput supplies publication mechanics, while activation and recovery journal semantics remain in Launcher.

NFC keeps strict app-state and launcher-state codecs, schema predicates, JSON depth ceilings **32** and **16**, root binding, default LOCALAPPDATA paths and product filenames. Before calling raw publication, its adapters retain the exact parent-path errors `Version-manager state has no parent directory.` and `Launcher state has no parent directory.` They validate and encode state before publishing it and preserve product-specific load/result mapping. No permissive `LocalJsonDocument` or default-accept authority adapter is used.

### Persistence test mapping

Frozen test paths are under `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/`; Core tests are under `tests/Nvt.Core.Tests/Launcher/Persistence/`.

| Frozen source case | Core case and retained boundary |
| --- | --- |
| `JsonVersionManagerStateStoreTests.SaveAndLoadRoundTrip` | `StateFileTests.SaveAndLoadRoundTrip`: exact synthetic document bytes, adapter-decoded values, no temporary residue and live caller custody; canonical JSON assertions remain in NFC. |
| `JsonVersionManagerStateStoreTests.CancelledSavePreservesPriorStateAndCleansTemporaryFile` | Same method in `StateFileTests`, for app and launcher raw access; prior complete bytes and synthetic values remain, temporary files are cleaned. |
| `JsonLauncherBootstrapStateStoreTests.StatePathMappingIsInjective` | Same method in `StateFileTests`, with explicit synthetic suffix, exact full-path append and neighboring canonical paths. |
| `FileSystemVersionManagerWriteLeaseTests.RecoveryCapabilityIsLiveExactAndNotForgeable` | Same class/method: equivalent spelling, foreign path, arbitrary disposable and disposed capability. |
| `FileSystemVersionManagerWriteLeaseTests.WindowsAbandonedProcessReleasesWriterForRestartConvergence` | Same class/method using `hold-lock`: 60-second startup budget, 10-second readiness bound, `LOCK_HELD` and ready marker, hard stop and 2-second reacquisition bound. The test writes its own abandoned residue; the child writes none. Residue grants no authority and survives later complete publication. |
| `JsonLauncherBootstrapStateStoreTests.NonCanonicalStateIsRejected` | Same method in `StateFileTests`: exact raw bytes reach a closed synthetic codec that rejects malformed shape; original JSON schema and wire cases remain in NFC. |

`StateFileTests` covers each product ceiling below/exact/above, smallest positive ceiling 1 with lengths 0/1/2, zero and negative parameters, blank paths/suffixes, empty/missing order, file links and real Windows sharing denial. `FileSystemVersionManagerWriteLeaseTests` covers real independent writers, canonical Windows case folding, exact hash keys, disposal, closed handles, physical exclusivity, cancellation and original result invariants. Per-call internal timing operations characterize zero and one-tick waits, 49/50/51 ms boundaries, exact deadline success and native codes 31/32/33/34 without changing global state.

`StatePublicationTests` uses the existing IO per-call physical stream seam to write an actual prefix, fail asynchronous or disk flush, and cancel after actual disk flush before move. It verifies prior complete bytes, cleanup and live writer custody. Other cases exercise real native move denial, failure after a successful replacement, native file-identity change during byte-identical replacement, and a deterministic publication gate with writer contention before and after publication. `StateStorePortTests` covers only default-port exception mapping; it supplies no physical write evidence. Native Windows cases explicitly skip elsewhere; unavailable symbolic-link creation explicitly skips. Synthetic fixtures live in unique system temporary folders.

### Persistence adoption rules

NFC consumes independently versioned, verified nupkg files under `vendor/nuget/` with exact `[x]` versions, locks, locked restore and package source mapping. `SOURCE.md` records the reviewed source and package SHA-256 hashes. Adoption uses packages without a ProjectReference to a Core checkout. Its strict codecs and state paths remain narrow adapters. Every launcher recovery or setup consumer shares this same live writer capability and checks its exact app-state path; it creates no second lock owner. NFC deletes relocated writer/raw-read/atomic-publication bodies only after callers use these Core owners and unchanged product schema, values, traces, byte outputs and recovery evidence pass. UI-affecting adoption also requires unchanged decoded pixels under the same environment and unchanged legacy font values. These APIs grant no Bootstrap package wiring or release authority.

## Held executable lease

`Nvt.Core.Launcher.Windows.StableManagedExecutableLaunchLease` is an internal adapter to the existing public `IManagedExecutableLaunchLease` port. It adds no public factory or native structure. Files owns all native path, tree, promotion and relative-write custody. The lease exposes the exact held `ExecutablePath`, its parent `WorkingDirectory`, and `TryValidateForStart()`. The caller keeps the lease alive through final validation and process creation; process creation belongs to Processes.

Internal acquisition and measurement require an explicit positive `maximumExecutableBytes`. NFC supplies its frozen 200,000,000-byte launcher ceiling through the descriptor-bound `ManagedLauncherIdentity.Create` contract. Application consumers retain their own admitted package ceiling; the launcher ceiling is not silently applied to unrelated application files. Invalid expected sizes or blank digests retain `Tampered` precedence before path acquisition. Unsafe, reparse or changed custody maps to `UnsafePath`; inaccessible, contended or unavailable custody maps to `Unavailable`.

`TryCreateAsync(ownedCustody, executableRelativePath, expectedSize, expectedSha256, maximumExecutableBytes, token)` consumes custody on every outcome. It checks cancellation, positive limits, exact length and the frozen PE predicates, hashes complete held bytes through `Files.BoundedFileReader.ReadAndHashAsync`, compares the digest ordinally, then revalidates the closed held tree. No private hash loop is introduced. Successful ownership transfers into the lease; failures and cancellation release both stream and custody. Complete-content reader EOF and final length/position checks remain in Files.

`TryCreateFromVerifiedTreeAsync` is the separate internal path for a caller that already verified complete package content under the same held tree. It retains exact length, PE and topology checks without rehashing already-proven content. The outer-launcher path hashes its own executable while holding closed topology for the surrounding declared tree; verification of other payload bytes belongs to full application activation. Neither internal path validates product JSON or replaces the mandatory NFC admission adapter. A captured topology alone does not authorize release content.

PE checks retain a minimum 64-byte DOS header, exact `MZ`, a signed little-endian offset at `0x3C`, offset at least 64 and at most length minus four, and the exact four-byte `PE\0\0` signature. `CopyToAsync` uses create-new output, the frozen 65,536-byte buffer, asynchronous flush and flush-to-disk while original content custody remains held. Measurement uses lowercase SHA-256 compatible with net8.0.

The source-to-Core lease test map is:

| Frozen source case | Core case in `StableManagedExecutableLaunchLeaseTests` |
| --- | --- |
| `FileSystemManagedVersionRepositoryTests:65 AcquiredApplicationLeaseDeniesExecutableSwapUntilReleased` | Same method; synthetic descriptor-relative executable swap remains denied until disposal |
| `FileSystemInstalledLauncherRepositoryTests:196 AddedChildAfterManifestProofFailsClosedAndReleasesCustody` | Same method; generic physical proof and release assertions |
| `AcquiredLauncherLeaseClosesAncestorAdmissionRace` | Same method and one/two ancestor levels |
| `SameLengthLauncherBytesChangedReturnsTamperedBeforeLeaseAdmission` | `SameLengthExecutableSwapFailsContentAdmission` |
| `DeclaredNonLauncherMemberChangedIsRejectedByApplicationActivationAfterLeaseAdmission` | `VerifiedTreeDoesNotRehashContentButOuterLauncherDoes` characterizes the generic split; full product activation assertions remain NFC |
| `RepositoryLeaseRejectsLateChildBeforeLauncherProcessStart` | `SharedProbeLateChildFailsFinalStartValidation` |

Additional cases cover exact and neighboring PE size/offset bounds, all signature bytes, 199,999,999/200,000,000/200,000,001-byte sparse executables, invalid limits at every acquisition and transferred-custody entry point, digest/size ordering, cancellation and no-replace copying. Real Windows child evidence uses the shared test probe: its entire framework-dependent output directory is copied and only its apphost is renamed to the descriptor path. One case starts that executable while custody stays held; a deterministic late-child gate rejects start before a child or marker exists.

NFC retains strict manifest/admission schemas, product names and safe-path policy, release-coupled identity authority, firmware, trust, release approval and product process orchestration. Adoption uses independently versioned nupkg files, exact `[x]` versions, locked restore, restricted source mapping and recorded source/package SHA-256. NFC removes its old lease adapter only after retained callers consume this port with the same held identity through start, original schema/product/value/trace/output assertions pass and required UI pixel comparisons remain identical. Core tests confer no Bootstrap package wiring authority.

## Activation and mutation coordination

The BCL-only `Nvt.Core.Launcher.Coordination` API contains:

- [ManagedActivationCoordinator](../../../src/Nvt.Core/Launcher/Coordination/ManagedActivationCoordinator.cs): `(managedRoot, stateStore, repository, process, readyDeadline = null)` and `RunAsync(token) -> ValueTask<ManagedLauncherResult>`.
- [LauncherBootstrapCoordinator](../../../src/Nvt.Core/Launcher/Coordination/LauncherBootstrapCoordinator.cs): `(managedRoot, statePath, appStateStore, launcherStateStore, repository, process, readyDeadline = null)` and `RunAsync(token) -> ValueTask<LauncherBootstrapResult>`.
- [ManagedMutationCoordinator](../../../src/Nvt.Core/Launcher/Coordination/ManagedMutationCoordinator.cs): explicit managed root and exact state path, the state/repository/fence ports, mandatory `IManagedPackageSelection` and mandatory `IManagedRetentionPolicy`. It exposes initialization, READY-qualified initialization, prepared install/delete, activation preparation/cancellation and retention acknowledgement. Results contain `ManagedMutationSnapshot`, with durable state, complete inventory and separate state/inventory issues.
- [ManagedVersionSeedBootstrapper](../../../src/Nvt.Core/Launcher/Coordination/ManagedVersionSeedBootstrapper.cs) and `ManagedVersionSeedPolicy`: explicit destination and packaged-seed ports, canonical single-admission seed policy and `EnsureInitializedAsync(writerLeaseTimeout, token)`.
- [ManagedApplicationStartupCoordinator](../../../src/Nvt.Core/Launcher/Coordination/ManagedApplicationStartupCoordinator.cs): the running version, mandatory READY writer and `IManagedApplicationInitialization`; `CompleteStartupAsync(token, isReadOnly = false)` returns the READY outcome and durable snapshot.
- [InstalledApplicationCoordinator](../../../src/Nvt.Core/Launcher/Coordination/InstalledApplicationCoordinator.cs): explicit `ProductDescriptor`, install root, state/repository/process ports and mandatory `IInstalledApplicationPresentation`. `StartAsync(token)` uses the same application supervisor. `ReadInstalledApplicationAsync(token)` reads the active installation; its version overload reads an exact admitted installed version.

`InstalledApplicationInfo` exposes product identity, installed version, verified executable path, display name, stable launch entry point and icon path. The executable comes from repository-held custody; presentation and shortcut paths come unchanged from the caller's mandatory adapter. Metadata reads verify exact admission, healthy inventory and executable custody without initialization writes or recovery. The upper layer creates or removes shortcuts. Core provides no shortcut writer or application registry. All ports for a named app must use that same product identity. The caller supplies the install root and update source; the latter arrives through validated durable state and current package selection. Core derives neither from product names.

`IManagedApplicationProcess` and `IManagedLauncherProcess` preserve exact executable-lease, READY result, admission and authoritative lifetime contracts. They delegate contained creation, inherited handles, Jobs, protocol decoding and cleanup to the process implementation. Coordination declares no native structures or execution tokens. A process port must enforce the READY budget from start entry; its frozen cleanup-confirmation extension remains at most ten seconds.

### Writer scopes and durable ordering

Application supervision holds the state store's exact writer receipt across load, root validation, complete inventory, executable custody, launch-journal save, READY and commit or rollback. The state-store adapter must acquire the production writer for its exact canonical application-state path. The default READY deadline is **20 seconds**, and the application writer wait is **five seconds**. Positive READY overrides are preserved unchanged; zero and negative overrides are rejected in the frozen constructor order.

Launcher startup uses the same application-state writer with a **250 ms** wait. It saves the requested and recorded launcher phases before process creation, releases the writer during nested READY and reacquires that writer before reloading both durable states. Commit requires the reloaded admission, root, active app and exact recorded launcher identity. Contention, changed authority and save failure remain typed outcomes. The launcher-state store exposes no second writer. A recorded fallback is never replaced by a directory scan or by an application candidate.

Every ordinary active-attempt guard requires authoritative `Exited` before it can be cleared. `Active` and `Unavailable` preserve the guard and prevent another launch. App and launcher journals retain the frozen cross-journal exclusion predicates, including the limited overlap between an active launcher guard and app candidate/rollback recovery. A normal failure after admission is separate from candidate rollback.

`ManagedMutationCoordinator` additionally checks `VersionManagerWriteLeaseResult.HoldsStatePath(statePath)` before loading or changing mutation authority. Arbitrary disposables, foreign-path capabilities and disposed receipts yield unavailable state. One mutation semaphore orders calls within an instance; the production application-state writer supplies cross-process exclusivity. No private lock-file owner, latest-snapshot save coordinator or undo service substitutes for these durable journals.

Install preserves prepare-save, repository promotion, complete inventory, retention advice and commit-save order. Failed or interrupted commits retain the prepared journal. Recovery admits only an exact healthy observed admission matching that journal; an absent install target clears the journal, and a mismatched target remains unadmitted. Delete preserves policy, explicit rollback-loss consent, launcher fallback-owner retirement, prepare-save, guarded filesystem delete, inventory and commit order. An already absent exact delete target converges as committed; recovery failure or unavailable inventory preserves the journal. Full durable source/registry authority survives both transitions.

`IManagedPackageSelection` must select the requested version from the caller's current validated catalog, returning null if source, active-version or catalog authority changed. Core rechecks the returned version and retains the package factory's mechanical identity/limit checks. Source checking, discovery supersession and source/session/UI snapshots remain in the upper layer; it supersedes discovery before entering a mutation. `IManagedRetentionPolicy` supplies the frozen NFC advice: successful update with more than **three healthy versions**, and reminder clearing at or below **three**. Core stores the reminder but supplies no retention threshold, automatic deletion or consent policy.

Ordinary initialization attempts the writer with zero wait. READY-qualified initialization uses the five-second application budget and reloads after the writer becomes available. Read-only initialization takes no writer and performs no prepared-mutation recovery. Startup reports READY first for every mode; only `Reported` selects the bounded writer wait, while read-only startup always selects the read-only path. Seed import validates its positive writer override before preflight, never replaces invalid or differently bound durable state, reloads after acquisition and accepts exactly one canonical admission and one healthy inventory row. It never discovers a seed by scanning directories.

### Coordination test mapping

Frozen paths are under `tests/NvtFwCombiner.Application.Tests/VersionManagement/`. Core paths are under `tests/Nvt.Core.Tests/Launcher/Coordination/`.

| Frozen source case | Core case |
| --- | --- |
| `ManagedActivationCoordinatorTests.cs:11 ReadyCommitsCandidate` | `ManagedActivationCoordinatorTests.ReadyCommitsCandidate` |
| `ManagedActivationCoordinatorTests.cs:33 FailureRollsBackExactlyOnce` | `ManagedActivationCoordinatorTests.FailureRollsBackExactlyOnce` |
| `ManagedActivationCoordinatorTests.cs:200 CandidateLaunchJournalFailureStartsNoProcess` | `ManagedActivationCoordinatorTests.CandidateLaunchJournalFailureStartsNoProcess` |
| `ManagedActivationCoordinatorTests.cs:277 ReadyCommitFailureRestartsDirectlyIntoRecordedRollback` | `ManagedActivationCoordinatorTests.ReadyCommitFailureRestartsDirectlyIntoRecordedRollback` |
| `ManagedActivationCoordinatorTests.cs:310 RollbackCommitFailureRestartsOnlyRecordedFallback` | `ManagedActivationCoordinatorTests.RollbackCommitFailureRestartsOnlyRecordedFallback` |
| `ManagedActivationCoordinatorTests.Concurrency.cs:10 ConcurrentLaunchersStartRequestedCandidateOnlyOnce` | `ManagedActivationCoordinatorTests.ConcurrentLaunchersStartRequestedCandidateOnlyOnce` |
| `ManagedActivationCoordinatorTests.ActiveLaunchAttemptRecovery.cs:9 ActiveTerminationUnconfirmedBlocksSecondRunThenRecoversAfterExit` | `ManagedActivationCoordinatorTests.ActiveTerminationUnconfirmedBlocksSecondRunThenRecoversAfterExit` |
| `LauncherBootstrapCoordinatorTests.cs:16 FirstVerifiedLauncherBecomesActiveOnlyAfterReadyAndDurableReload` | `LauncherBootstrapCoordinatorTests.FirstVerifiedLauncherBecomesActiveOnlyAfterReadyAndDurableReload` |
| `LauncherBootstrapCoordinatorTests.cs:364 EveryCandidateStateSaveFailureFailsClosed` | `LauncherBootstrapCoordinatorTests.EveryCandidateStateSaveFailureFailsClosed` |
| `VersionManagementExperienceTests.Transaction.cs:9 InstallCommitSaveFailureConvergesFromDurableJournalAfterRestart` | `ManagedMutationCoordinatorTests.InstallCommitSaveFailureConvergesFromDurableJournalAfterRestart` |
| `VersionManagementExperienceTests.DeleteRecoveryTransaction.cs:84 DeleteRecoveryCommitSaveFailureRemainsJournaledUntilNextRestart` | `ManagedMutationCoordinatorTests.DeleteRecoveryCommitSaveFailureRemainsJournaledUntilNextRestart` |
| Remaining generic cases in `ManagedActivationCoordinatorTests` and its concurrency, fail-closed, launch-lease and active-recovery partials | Same method names in `ManagedActivationCoordinatorTests` and `LauncherBootstrapCoordinatorTests` |
| `LauncherBootstrapCoordinatorTests` and `ActiveRecoveryMatrix` cases | Same method names in `LauncherBootstrapCoordinatorTests`; fixture builders/stores/process doubles retain source assertions |
| Generic `VersionManagementExperienceTests.Transaction` and `DeleteRecoveryTransaction` cases | Same method names in `ManagedMutationCoordinatorTests`; source/catalog policy uses synthetic selection ports |
| `ManagedVersionSeedBootstrapperTests` | Same method names in `ManagedVersionSeedBootstrapperTests` |
| `ManagedApplicationStartupCoordinatorTests` READY/read-only dispatch | Same method names in `ManagedApplicationStartupCoordinatorTests` |
| `ManagedApplicationStartupCoordinatorTests.ManagedReadyInitializationWaitsThenReloadsDurableState` and ordinary zero-wait contention | `ManagedMutationCoordinatorTests.ManagedReadyInitializationWaitsThenReloadsDurableState`, using the production writer |

`CoordinationCrashMatrixTests` crosses seven application states, five launcher states and all three lifetime results: **105 complete-state and trace cases**. It also covers READY followed by a crash before commit, both rollback READY commit failures, guard-clear save failures and unavailable inventory without losing either serialized journal. Its closed synthetic codec serializes every durable field to actual temporary state files; process traces prove writer retention for app READY and release/reacquisition for launcher READY. `ManagedMutationCoordinatorTests` adds actual writer capability rejection, read-only recovery exclusion, two-journal fencing, consent/retirement ordering, cancellation and callback identity checks.

`CoordinationBoundaryTests` covers default 20-second READY, five-second app writer and 250-ms launcher writer values; one-tick neighbors of the defaults; smallest positive overrides; zero/negative overrides; maximum `TimeSpan`; and canonical seed counts zero/one/two. These characterize coordination forwarding and check order. Native elapsed-time, handle and Job behavior belongs to the contained-process implementation. `InstalledApplicationCoordinatorTests` composes two synthetic apps and checks complete installed/presentation facts, source preservation, the shared READY path, read-only behavior, adapter failures and custody disposal. Runtime fixtures use unique system temporary folders. Synthetic coordination tests establish no product wire-schema, firmware or UI parity.

### Coordination adoption rules

NFC retains strict state/manifest/catalog codecs, product identity and payload policy, source/registry policy, discovery/session/UI composition, retention advice, consent, firmware, trust and release authority. Its adapters supply validated state and selected packages, the exact state-store writer, product-bound process/repository ports and presentation paths. Native process and physical repository implementations must be composed with these owners for complete engine behavior.

Adoption consumes independently versioned, verified nupkg files under `vendor/nuget/` with exact `[x]` pins, locks, locked restore and package source mapping. `SOURCE.md` records source and package SHA-256 hashes. NFC removes relocated supervisor and journal/mutation bodies only after its adapters use Core and the original complete values, process/writer traces, durable bytes and recovery assertions remain unchanged. UI-affecting adoption also preserves decoded pixels under the same environment and all eight legacy font values. This extraction grants no package publication or Bootstrap package-wiring authority.
