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
