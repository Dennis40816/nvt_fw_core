# Core API XML docs audit

Branch: `cloud/core-api-docs`, based on `origin/main` at `0286a279e20882fed75efde8af4077438dba2a6e`.
Date: 2026-11-05. Scope: `src/Nvt.Core`, `src/Nvt.Core.Avalonia`, `src/Nvt.Core.Fonts` (313 `.cs` files).

## Result

**No public type or member in `src/` lacks an XML doc comment on `origin/main`.** There was nothing to write, so this branch changes no source file. Its only change is this note.

I did not invent doc edits to fill the task. The second step ("write the missing docs for the most used namespace") has no input, so it was skipped.

## Count by namespace

| Namespace | Public types and members without XML docs |
|---|---:|
| all namespaces in all three projects | **0** |

The scanner reported 34 raw hits, all of one kind: a secondary `partial` part of a public class whose declaration carries no `///`. For all 12 classes involved, exactly one part is documented, which is what the compiler requires. They are not gaps:

| Namespace | Class (partial parts without a doc) |
|---|---|
| `Nvt.Core.Launcher.Recovery` | `FileSystemManagedFirstInstallationRootMaterializer` (2), `FileSystemManagedSetupRecoveryExecution` (7) |
| `Nvt.Core.Launcher.Coordination` | `ManagedMutationCoordinator` (6), `LauncherBootstrapCoordinator` (1) |
| `Nvt.Core.Launcher.Integration` | `VersionManagementExperience` (5) |
| `Nvt.Core.Launcher.Repository` | `FileSystemManagedVersionRepository` (4) |
| `Nvt.Core.LogConsole` | `ConsoleLinkScanner` (3) |
| `Nvt.Core.Avalonia.LogConsole` | `ConsoleListView` (1), `ConsoleToolbar` (2) |
| `Nvt.Core.RuntimeQuery` | `RuntimeQueryCommandRouter` (1) |
| `Nvt.Core.Avalonia.RuntimeQuery` | `RuntimeQueryGenericCommands` (1) |
| `Nvt.Core.Processes` | `SystemExternalProcessRunner` (1) |

## Why zero is expected

`Directory.Build.props` sets `GenerateDocumentationFile=true` and `TreatWarningsAsErrors=true`. Neither `Directory.Build.props`, the `src` project files, `.editorconfig` nor the `HealthBaselineWarningIds` lists in `eng/code-health/projects.props` suppress CS1591 for the published projects. `grep` finds no `#pragma warning disable CS1591` in `src/`. A missing doc on a public symbol therefore fails the build, which matches the scan result. The 935 `pragma:CS1591` entries in `eng/code-health/baseline.json` all belong to `Nvt.Core.Tests`.

## Which namespace would have been chosen

`docs/core/api-inventory.md` counts external (NFH) consumers per namespace. By that table the apps use these most:

| Rank | Namespace | NFH files |
|---:|---|---:|
| 1 | `Nvt.Core.Avalonia.Threading` | 22 |
| 2 | `Nvt.Core.RuntimeQuery` | 15 |
| 3 | `Nvt.Core.Avalonia.Theme` | 7 |
| 4 | `Nvt.Core.Lifecycle` | 5 |
| 5 | `Nvt.Core.Progress` | 4 |

All five are fully documented. The inventory dates from the 0.9.0 sign-off, so the ranking may have moved. I did not re-count NFH usage, because this session has no access to the NFH repository.

## Method and limits

- `dotnet` is not installed in this container, so **no build or test was run**. No code changed, so the build result cannot differ from `main`.
- The scan used a heuristic Python scanner, not Roslyn. It tracks namespaces, brace depth, effective accessibility (public, or protected in an unsealed public type; interface members count as public) and a `///` block directly above the declaration, attributes allowed in between.
- Checks on the scanner:
  - With every `///` line stripped from a temporary copy of `src/`, it reported 1,604 undocumented symbols (types, constructors, methods, properties, fields, enum members, events, indexers, operators). This shows it detects members, so the real run's zero members is not a blind spot.
  - It does not model records' synthesized members or property accessors, so the count is lower than the roughly 2,800 `PublicAPI` entries and is not meant to match them.
- The authoritative confirmation is a normal `dotnet build` of the solution in a local session: CS1591 is an error there, so a clean build means zero missing docs. I could not run it here.
- `<inheritdoc/>` counts as documented, as it does for the compiler. There are 78 such comments (4 in `Nvt.Core.Avalonia.Theme`, none in the other four namespaces above).

## Possible follow-up (not done)

- Doc quality, not presence: 119 of 1,991 `<summary>` elements have three words or fewer. In the five most used namespaces only one does (`Nvt.Core.RuntimeQuery`: "The server started."). A review for summaries that restate the member name would need human judgement and is out of scope here.
