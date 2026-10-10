# Public API inventory

Core 0.9.0 keeps a reviewed list of every public `Nvt.Core` API in `src/Nvt.Core/PublicAPI.Unshipped.txt`. The 0.9.0 release step moves the approved entries to `PublicAPI.Shipped.txt`. The analyzer gate (RS0016/RS0017 as errors) follows in the last inventory slice, so a missing entry does not fail the build yet.

This page covers the namespaces that are not `Nvt.Core.Launcher*`. The Launcher entries follow in two slices, and `Nvt.Core.Avalonia`, `Nvt.Core.Fonts` and the TestSupport project in the last slice.

## How the entries were produced

1. Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` 3.3.4 and two empty API files (`#nullable enable` only) to `Nvt.Core.csproj`. Restore.
2. Run `dotnet format analyzers src/Nvt.Core/Nvt.Core.csproj --diagnostics RS0016 --severity info --no-restore`. The RS0016 code fix writes every public symbol into `PublicAPI.Unshipped.txt`: 1,648 entries.
3. Keep the entries whose declaring symbol is outside `Nvt.Core.Launcher` (745), sort them with ordinal comparison, and write LF line endings.
4. Rebuild with the analyzer: no RS0016 remains outside Launcher, and RS0017 is absent.
5. Remove the temporary project and lock changes. Nothing else in the repository changes.

## Namespaces

Types and entries count the public API of the namespace. Consumers count source files that reference the namespace.

| Namespace | Types | Entries | In-repo consumers | External consumers | Verdict |
|---|---:|---:|---|---|---|
| `Nvt.Core.Csv` | 1 | 2 | Core tests only | none found | internalize-candidate: no consumer outside its tests |
| `Nvt.Core.Files` | 8 | 41 | 14 Core files (Launcher, Processes) | none found | keep |
| `Nvt.Core.IO` | 1 | 3 | 2 Core files | NFH (2 files) | keep |
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
| **Total** | **104** | **745** | | | |

`Nvt.Core.Files.Windows` has no public API.

## How to read the verdicts

- External consumers come from `using` directives in the NFH clone. NFC references the Core packages only for fetch and hash checks and has no source use. NFU has none.
- `internalize-candidate` is a question for the 0.9.0 sign-off, not a decision. A candidate that the owner wants to keep as a published module stays in the list, and its entries stay in the file.
- Internalizing a namespace later means moving its entries to `*REMOVED*` lines in `PublicAPI.Unshipped.txt`, with a CHANGELOG entry under Breaking changes.
