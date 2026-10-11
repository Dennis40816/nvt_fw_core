# Public API inventory

Core 0.9.0 keeps a reviewed list of every public `Nvt.Core` API in `src/Nvt.Core/PublicAPI.Unshipped.txt`. The 0.9.0 release step moves the approved entries to `PublicAPI.Shipped.txt`. The analyzer gate (RS0016/RS0017 as errors) follows in the last inventory slice, so a missing entry does not fail the build yet.

This page covers every public namespace of the four published projects: `Nvt.Core`, `Nvt.Core.Avalonia`, `Nvt.Core.Fonts` and `Nvt.Core.TestSupport`. The analyzer gate (RS0016 and RS0017 as errors) is on for all four.

## How the entries were produced

1. Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` 3.3.4 and two empty API files (`#nullable enable` only) to `Nvt.Core.csproj`. Restore.
2. Run `dotnet format analyzers src/Nvt.Core/Nvt.Core.csproj --diagnostics RS0016 --severity info --no-restore`. The RS0016 code fix writes the public symbols into `PublicAPI.Unshipped.txt`. The first pass wrote 1,648 lines. The final file lists 1,616 distinct entries.
3. Keep the entries whose declaring symbol is outside `Nvt.Core.Launcher` (745), sort them with ordinal comparison, and write LF line endings.
4. Rebuild with the analyzer: no RS0016 remains outside Launcher, and RS0017 is absent.
5. Remove the temporary project and lock changes. Nothing else in the repository changes.

The Launcher coordination, transport and repository slice (394 entries) was produced the same way, after the contracts, persistence and activation slice (477 entries) landed. The contracts slice was produced the same way, with one change. The build writes a SARIF log (`-p:ErrorLog=...%2Cversion=2.1`), and the `APIName` property of every RS0016 result is the exact entry text, including modifiers such as `static` and `const`. The analyzer reports one undeclared symbol for each declaration, so a record with a primary constructor needs a second pass. The slice was repeated until the build reported no RS0016 for these namespaces. Then RS0017 was absent, and 381 RS0016 results remained, all in `Nvt.Core.Launcher.Coordination`, `Transport` and `Repository`.

## Namespaces

Types and entries count the public API of the namespace. Consumers count source files that reference the namespace.

| Namespace | Types | Entries | In-repo consumers | External consumers | Verdict |
|---|---:|---:|---|---|---|
| `Nvt.Core.Csv` | 1 | 2 | Core tests only | none found | internalize-candidate: no consumer outside its tests |
| `Nvt.Core.Files` | 8 | 41 | 14 Core files (Launcher, Processes) | none found | keep |
| `Nvt.Core.IO` | 1 | 3 | 2 Core files | NFH (2 files) | keep |
| `Nvt.Core.Launcher.Activation` | 37 | 227 | 24 Core files (Coordination, Transport, Repository), other tests (1) | none found (NFC adopts the Launcher host in a later slice) | review: policy, decision and result types may not all need to be public once the Launcher host is adopted |
| `Nvt.Core.Launcher.Contracts` | 24 | 222 | 48 Core files (every Launcher namespace), other tests (5) | none found (NFC adopts the Launcher host in a later slice) | keep: the identity, admission and result types that every Launcher namespace shares |
| `Nvt.Core.Launcher.Persistence` | 6 | 28 | 17 Core files (Coordination, Transport, Repository), other tests (1) | none found (NFC adopts the Launcher host in a later slice) | keep: the state store contracts and the file-system store |
| `Nvt.Core.Launcher.Coordination` | 30 | 177 | 7 Core files, other tests (1) | none found (NFC adopts the Launcher host in a later slice) | review: coordinators and results are the host entry points. 15 of the 30 types have no other consumer yet |
| `Nvt.Core.Launcher.Repository` | 10 | 72 | 1 Core file, other tests (0) | none found (NFC adopts the Launcher host in a later slice) | review: the file-system repositories are the host entry points. all 10 types have no other consumer yet |
| `Nvt.Core.Launcher.Transport` | 26 | 145 | other tests (2) | none found (NFC adopts the Launcher host in a later slice) | review: the process and handoff adapters are the host entry points. 20 of the 26 types have no other consumer yet |
| `Nvt.Core.Lifecycle` | 3 | 15 | Core tests only | NFH (5 files) | keep |
| `Nvt.Core.Locale` | 2 | 32 | Core tests only | none found | internalize-candidate: no consumer outside its tests |
| `Nvt.Core.LogConsole` | 31 | 296 | Core tests only | none yet (NFH Console slices land in 0.9.1) | review: largest surface without a consumer today |
| `Nvt.Core.MessageCenter` | 10 | 64 | Avalonia (1), other tests (2) | none found | keep |
| `Nvt.Core.Persistence` | 2 | 13 | Core tests only | none found | internalize-candidate: no consumer outside its tests |
| `Nvt.Core.Processes` | 9 | 45 | 6 Core files (Launcher), other tests (1) | none found | keep |
| `Nvt.Core.Progress` | 7 | 48 | Avalonia (1), other tests (4) | NFH (4 files) | keep |
| `Nvt.Core.ReportList` | 4 | 18 | Avalonia (1), other tests (3) | none found | keep |
| `Nvt.Core.RuntimeQuery` | 20 | 132 | Avalonia (6), other tests (10) | NFH (15 files) | keep |
| `Nvt.Core.Shell` | 1 | 5 | Core tests only | none found | internalize-candidate: no consumer outside its tests |
| `Nvt.Core.SourceFileNavigation` | 2 | 13 | Core tests only | none found | internalize-candidate: no consumer outside its tests |
| `Nvt.Core.Startup` | 1 | 8 | Core tests only | none found | internalize-candidate: no consumer outside its tests |
| `Nvt.Core.Threading` | 1 | 4 | other tests (1) | none found (NFH uses `Nvt.Core.Avalonia.Threading`) | review |
| `Nvt.Core.Time` | 1 | 6 | Core tests only | none found | internalize-candidate: no consumer outside its tests |
| **Total** | **237** | **1,616** | | | |

`Nvt.Core.Files.Windows`, `Nvt.Core.Launcher.Verification` and `Nvt.Core.Launcher.Windows` have no public API.

## Published UI, font and test-support projects

The three projects have their own `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` next to their project files:

- `Nvt.Core.Avalonia`: 477 entries.
- `Nvt.Core.Fonts`: 3 entries.
- `Nvt.Core.TestSupport`: 50 entries.

| Namespace | Types | Entries | In-repo consumers | External consumers | Verdict |
|---|---:|---:|---|---|---|
| `Nvt.Core.Avalonia.Dialogs` | 2 | 8 | Avalonia tests (2) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.Focus` | 3 | 11 | Avalonia tests (5) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.Icons` | 1 | 72 | Avalonia tests (2) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.Inputs` | 1 | 36 | Avalonia tests (6) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.LogConsole` | 5 | 69 | Avalonia tests (17) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.MessageCenter` | 3 | 61 | Avalonia tests (2) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.Panels` | 2 | 51 | Avalonia tests (3) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.Primitives` | 3 | 28 | Avalonia tests (3) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.Progress` | 3 | 12 | Avalonia tests (5) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.ReportList` | 3 | 43 | Avalonia tests (6) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.RuntimeQuery` | 10 | 62 | Avalonia tests (10) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.Shell` | 1 | 3 | Avalonia tests (3) | none found | review: no tool consumes it yet. Adoption is part of the 1.0.0 plan |
| `Nvt.Core.Avalonia.Theme` | 5 | 17 | Avalonia tests (32) | NFH (7 files) | keep: NFH uses it |
| `Nvt.Core.Avalonia.Threading` | 1 | 4 | Avalonia tests (8) | NFH (22 files) | keep: NFH uses it |
| `Nvt.Core.Fonts` | 1 | 3 | Avalonia tests (1), Fonts tests (2) | NFH (1 files) | keep: NFH uses it |
| `Nvt.Core.TestSupport` | 7 | 50 | Avalonia tests (1), TestSupport tests (8), Core tests (14) | none found | keep: the shared test helpers; the Core tests use them |
| **Total** | **51** | **530** | | | |

## Types that only their own namespace and the Core tests reference

45 of the 66 public types in the three namespaces below have no other consumer today. This is expected: they are the entry points and results of the Launcher host that NFC adopts later (the H06 contract in NFC). The list is input for the 0.9.0 sign-off. Nothing was internalized here. A type leaves the list when the host design shows that it needs the type or when the owner decides to remove it.

- `Nvt.Core.Launcher.Coordination` (15): `IManagedApplicationInitialization`, `IManagedApplicationStartupCoordinator`, `IManagedPackageSelection`, `IManagedRetentionPolicy`, `ManagedActivationCoordinator`, `ManagedApplicationStartupCoordinator`, `ManagedApplicationStartupResult`, `ManagedLauncherOutcome`, `ManagedLauncherResult`, `ManagedMutationCoordinator`, `ManagedMutationSnapshot`, `ManagedVersionSeedPolicy`, `VersionDeleteOperationIssue`, `VersionDeleteOperationResult`, `VersionInstallOperationResult`
- `Nvt.Core.Launcher.Repository` (10): `ActiveLauncherAdmission`, `FileSystemInstalledLauncherRepository`, `FileSystemLauncherInstallationSelfTest`, `FileSystemManagedVersionRepository`, `ILauncherInstallationSelfTest`, `IManagedVersionAdmissionCodec`, `ImmutableBootstrapObservation`, `InstalledApplicationMetadata`, `LauncherInstallationSelfTestIssue`, `LauncherInstallationSelfTestResult`
- `Nvt.Core.Launcher.Transport` (20): `AnonymousPipeManagedApplicationProcess`, `AnonymousPipeManagedLauncherProcess`, `IImmutableBootstrapHandoff`, `IImmutableBootstrapLaunch`, `IImmutableBootstrapLeaseHandoff`, `IStableLauncherHandoff`, `ImmutableBootstrapAdmissionOutcome`, `ImmutableBootstrapAdmissionResult`, `ImmutableBootstrapCompletionOutcome`, `ImmutableBootstrapCompletionResult`, `ImmutableBootstrapStartIssue`, `ImmutableBootstrapStartResult`, `ImmutableBootstrapWaitBudget`, `InheritedPipeApplicationReadySignal`, `LauncherBootstrapRuntimeServices`, `LauncherReadyInheritance`, `LauncherReadyInheritanceOutcome`, `StableLauncherHandoff`, `StableLauncherStartOutcome`, `StableLauncherStartResult`

## The gate

`eng/code-health/projects.props` sets `HealthPublicApi` for the four projects. The shared health props then add `Microsoft.CodeAnalysis.PublicApiAnalyzers` and the two API files, and `TreatWarningsAsErrors` makes RS0016 (an undeclared public symbol) and RS0017 (a removed symbol that is still listed) fail the build.

- A new public member needs an entry in the project's `PublicAPI.Unshipped.txt` in the same pull request.
- The release step moves the approved entries from `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt`.
- A public member that goes away gets a `*REMOVED*` line in `PublicAPI.Unshipped.txt`, and a CHANGELOG entry under Breaking changes.

The RS0026 rule (no more than one overload with optional parameters) also became an error for `SignalWait.WaitAsync`. The instance overload no longer has a default token, and a parameterless overload replaced the default.

## How to read the verdicts

- External consumers come from `using` directives in the NFH clone. NFC references the Core packages only for fetch and hash checks and has no source use. NFU has none.
- `internalize-candidate` is a question for the 0.9.0 sign-off, not a decision. A candidate that the owner wants to keep as a published module stays in the list, and its entries stay in the file.
- Internalizing a namespace later means moving its entries to `*REMOVED*` lines in `PublicAPI.Unshipped.txt`, with a CHANGELOG entry under Breaking changes.
