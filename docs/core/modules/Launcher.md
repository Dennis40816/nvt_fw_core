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
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagerStateStore.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateSourceRegistry.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedInstallationLayout.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedSetupTransactionDocuments.cs`

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Launcher.Contracts` and `Nvt.Core.Launcher.Activation` provide BCL-only `net8.0` values and ports for managed application and launcher activation. The module performs no filesystem or process access. Pure path normalization uses the current platform's path rules.

The extracted slices are version and content identities, descriptor and package-policy declarations, normalized package results, immutable application and launcher state, transition helpers, durable snapshot comparison, generic inventory, delete-owner protection, and repository/state-reader ports. `UpdateSourceRegistry.cs` contributes only `VersionSourceRegistryState`. `VersionManagementPolicy.cs` contributes inventory and generic delete decisions; its retention threshold, automatic deletion policy and discovery notifications remain in NFC. `LauncherMutationFence.cs` contributes protection values and its port; the experience partial remains in NFC. `VersionManagerStateStore.cs` contributes load/save result values and `IVersionManagerStateReader`; writer acquisition and live custody are separate persistence contracts. Native structures, execution tokens, ZIP plans and strict wire DTOs/codecs are outside this module.

## Contracts

[ProductDescriptor](../../../src/Nvt.Core/Launcher/Contracts/ProductDescriptor.cs) requires explicit product, runtime, registry, executable and protocol names, the Bootstrap filename and an archive-root callback. Executable paths are safe slash-separated relative paths; Bootstrap and evaluated archive roots are safe single names. Blank, rooted, traversal, alternate-stream, backslash, C0 control, invalid punctuation and Windows device-name inputs fail. Paths have at most 512 characters. DEL and C1 characters retain their frozen acceptance. Every archive-root callback result is checked. Configuration supplies no content trust.

[ManagedAppVersion](../../../src/Nvt.Core/Launcher/Contracts/ManagedAppVersion.cs) retains the canonical stable three-component parser, numeric ordering and invariant formatting. [UpdateCatalogVersionSnapshot.Create](../../../src/Nvt.Core/Launcher/Contracts/UpdateCatalogVersionSnapshot.cs) requires explicit positive package and UTF-8 release-note ceilings, UTC metadata, a structurally safe relative package path of at most 512 characters, positive package length within the supplied ceiling, lowercase SHA-256 digests, present notes within their ceiling and a defined notification policy. NFC's frozen ceilings are 134,217,728 package bytes and 65,536 release-note bytes. NFC also validates its strict ZIP and timestamp wire grammar.

Catalog admission identity remains `version|relative-package-path|invariant-package-size|package-sha256|release-manifest-sha256`. Configured roots, publication time, notes and notification policy remain outside that identity. `VerifiedUpdateCandidate` preserves version, admission identity and notes; `ManagedVersionAdmission` additionally preserves the exact manifest digest. Paths never infer admission.

[IProductPackagePolicy](../../../src/Nvt.Core/Launcher/Contracts/PackageContracts.cs) receives exact manifest bytes and returns product-admitted normalized facts. `archivePaths: null` retains installed-verification mode. Its mandatory NFC adapter validates strict schema, exact product/runtime values and closed payload before returning a `PackageManifest`. `PackageLauncher` is an unowned parsed declaration. No default-accept policy or fallback adapter is supplied.

[ManagedLauncherIdentity.Create](../../../src/Nvt.Core/Launcher/Contracts/ManagedLauncherIdentity.cs) requires the descriptor, an explicit positive executable ceiling no greater than 200,000,000 bytes, and every exact owner/version/hash/protocol/path/size field. NFC supplies 200,000,000 bytes. Protocol remains exactly `1`; the executable path matches the descriptor ordinally; the nonblank owner admission is at most 2,048 characters; both digests are lowercase SHA-256. `MatchesOwner` compares application version, admission string and manifest digest ordinally. `ManagedImmutableBootstrapIdentity.Create` retains its exact descriptor-bound root filename and 200,000,000-byte ceiling.

[ManagedPackageResults](../../../src/Nvt.Core/Launcher/Contracts/ManagedPackageResults.cs) preserves install, verification, executable-lease and installed-launcher issue values and success predicates, including `HasSupportedManagedLauncher`. `IManagedExecutableLaunchLease` supplies disposable custody, exact executable/working-directory values and final synchronous validation. This module declares no process starter or custody implementation.

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

NFC downloads versioned packages at build time through `core-packages.json` and uses exact `[x]` pins, lock files and locked restore. Source mapping restricts Core packages to the download folder. The manifest records each package's Release tag and SHA-256. Core and NFC release independently. Duplicate executable bodies are deleted only when the corresponding NFC adapter uses Core and preserves complete values, event traces and output bytes. UI-affecting adoption requires zero changed decoded pixels under the same recorded environment. The eight legacy font values remain unchanged. Bootstrap package wiring requires separate launcher-adoption authorization.
