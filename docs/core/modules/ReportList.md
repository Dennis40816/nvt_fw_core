[English](ReportList.md) | [中文](ReportList.zh-TW.md)

# ReportList

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportIndexedReadOnlyLists.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ResettableObservableCollection.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportWindowedListViewModel.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportPagedListViewModel.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/Resources/MainWindowReportTemplates.axaml:10-25` (paged pager only)
- `src/NvtFwCombiner.Presentation.Avalonia/Views/HexEditorPanel.axaml:11-35` (windowed pager only)

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

The module lives in `src/Nvt.Core/ReportList/`.
The namespace is `Nvt.Core.ReportList`.
It targets .NET 8.
It depends only on the BCL.
It owns all four generic collection types below.
The paging models live in `src/Nvt.Core.Avalonia/ReportList/`, namespace `Nvt.Core.Avalonia.ReportList`, targeting .NET 10.
They use CommunityToolkit.Mvvm 8.4.2 and the shared collections, with no Locale dependency.
The internal non-copying `ObjectReadOnlyList<T>` adapter comes from `ReportIndexedReadOnlyLists.cs:105-126`.
The two model algorithms are extracted in full; NFC retains their language mapping and supplies labels.

## Public API

`MemoizedIndexedReadOnlyList<T>(int count, Func<int, T> factory)` implements `IReadOnlyList<T>`.
`T` must be a reference type.
`Count` exposes the declared count.
`MaterializedCount` counts successful row creation.
Each index allocates its lazy holder on first access.
Concurrent readers share one published row per index.
Factory exceptions are cached per index.
A null factory result throws `InvalidOperationException` with `A report row factory returned null.`.
That exception is also cached.
The constructor rejects negative counts before checking the factory.
An invalid row index throws `ArgumentOutOfRangeException` for `index`.
Enumeration creates rows as they are reached.
`HasMaterializedReference` remains internal.

`FactoryReadOnlyList<T>(int count, Func<int, T> factory)` implements `IReadOnlyList<T>`.
It accepts reference types and value types.
`Count` exposes the declared count.
Every valid index access invokes the factory.
Factory results are not retained.
Null results are allowed.
Factory failures are retried on subsequent accesses.
The constructor checks the factory before rejecting a negative count.
An invalid row index throws `ArgumentOutOfRangeException` for `index`.
Enumeration invokes the factory as each row is reached.
There is no fixed page-size limit in this collection.

`IndexedReadOnlyList<T>(IReadOnlyList<T> source, IReadOnlyList<int> indices)` implements `IReadOnlyList<T>` and `IList`.
`T` must be a reference type.
The constructor checks `source` before `indices`.
It copies the selected indices.
It retains the source list.
It preserves index order and duplicates.
An invalid selected source index throws `ArgumentOutOfRangeException` for `indices`.
`Count` exposes the number of selected indices.
The indexer returns the original source row.
An invalid view position throws `IndexOutOfRangeException`.
Enumeration follows the selected order.
`IList.IndexOf` and `IList.Contains` never create memoized source rows.
Memoized lookup uses reference identity.
Other sources use `Equals`.
`IList.IsFixedSize` and `IList.IsReadOnly` are true.
`ICollection.IsSynchronized` is false.
`ICollection.SyncRoot` is the view itself.
Every `IList` mutation throws `NotSupportedException` with `The indexed report projection is read-only.`.
`ICollection.CopyTo` rejects a null array.
It copies through `Array.SetValue` in selected order.
It uses checked destination-index addition.
Array failures can leave a copied prefix.
An empty copy performs no destination-index or array-shape validation.

`ResettableObservableCollection<T>` inherits `ObservableCollection<T>`.
`ReplaceAll(IEnumerable<T> items)` retains the collection identity.
It rejects null input before checking reentrancy.
It clears the collection before enumerating the input.
It adds the input items without per-item notifications.
It raises `Count`, then `Item[]`, then one collection `Reset`.
Those notifications occur even for empty or identical replacements.
Replacing from the collection itself produces an empty collection.
Enumeration failure leaves the added prefix without replacement notifications.
Observer exceptions propagate at the notification that throws.
The inherited reentrancy rule is preserved.

### Paging models and labels

`ReportListLabels` is a sealed record containing immutable `NoItems`, `PreviousPage`, `NextPage`, and `AllItemsLoaded` strings.
Its `WindowStatus(first, last, total)`, `PagedStatus(visible, total)`, and `LoadMore(next, remaining)` delegates supply formatting.
The host supplies stable formatters. Both models reject a null label or formatter at construction with `ArgumentNullException`.
There is no mutable relocalization API.

Both sealed view models inherit Toolkit `ObservableObject` and expose:

```csharp
Create<T>(IReadOnlyList<T> items, int pageSize, ReportListLabels labels,
    bool loadInitialPage = true)
```

Each retains the supplied list without copying or enumerating it.
The host must keep its count stable and perform model access, commands and notifications on its UI thread.
Initial loading uses indexed reads only; deferred construction reads no rows.
`Items` is a stable `ReadOnlyObservableCollection<object>`.
Reference rows retain their source identity; value rows are boxed and null elements pass through.
`TotalCount` reads the source count; `VisibleCount` reads the visible collection count.
`pageSize` must be positive and has no fixed upper ceiling in either frozen model.
NFC supplies 8, 24 and 40 for accumulating report batches and 64 for changed-block windows; these policies remain host parameters.
Creation checks `items`, then `pageSize`, then the injected `labels` argument.
Invalid sizes retain the `pageSize` parameter name and actual value in `ArgumentOutOfRangeException`.

`ReportWindowedListViewModel` keeps one fixed window using `ResettableObservableCollection<object>.ReplaceAll`.
It exposes `PageIndex`, `PageCount`, `HasPreviousPage`, `HasNextPage`, `HasMultiplePages`, `PageStatus`, `PreviousPageLabel`, and `NextPageLabel`.
`PreviousPageCommand` and `NextPageCommand` are stable `IRelayCommand` instances.
The next command can load the first deferred window.
Empty sources have zero pages and use `NoItems`; a deferred nonempty window also uses `NoItems` until loaded.
`ShowItemAt(index)` rejects an index outside `[0, TotalCount)` using the frozen unsigned comparison.
The exception names `index` without storing its actual value.
It selects only the containing page and does no work when that page is already visible.
Row creation finishes before changing the page or replacing the window.
The new `PageIndex` is assigned before collection notifications.
Notification order is collection `Count`, `Item[]`, one `Reset`; model `VisibleCount`, `PageIndex`, `HasPreviousPage`, `HasNextPage`, `HasMultiplePages`, `PageStatus`; previous-command availability, then next-command availability.
There is no generated observable setter for `PageIndex`, because that would change this order.

`ReportPagedListViewModel` accumulates every loaded row and exposes `RemainingCount`, `HasMoreItems`, `PageStatus`, `LoadMoreLabel`, and a stable `IRelayCommand` named `LoadMoreCommand`.
`EnsureInitialPage()` loads only when `VisibleCount == 0 && TotalCount > 0`.
It is idempotent once any row is visible.
Status always uses `PagedStatus`, including empty sources.
The next label uses the minimum of page size and remaining count; the end label is `AllItemsLoaded`.
Every appended row raises collection `Count`, `Item[]`, and `Add` before the model raises `VisibleCount`, `RemainingCount`, `HasMoreItems`, `PageStatus`, `LoadMoreLabel`, then command availability.
Consumers see the growing prefix during each Add.
Source or observer failures retain the already appended prefix and propagate before later notifications.
Toolkit `RelayCommand.Execute` does not enforce `CanExecute`: direct end execution still performs the frozen checked addition and model notifications when it succeeds.
Window commands retain their own boundary predicates and do nothing at either end.

All frozen checked arithmetic and expression order remain unchanged.
Window end addition and cumulative batch addition are checked before clamping to the total count.
Window status retains the intermediate `first + VisibleCount - 1` overflow, including the final one-row window of an `int.MaxValue` source.
No model stores derived counts or availability flags; the window page index is the only independent mutable position.
Collection contents and page position have one model owner and UI-thread-only access.

### Pager templates

Load `avares://Nvt.Core.Avalonia/ReportList/ReportPagerTemplates.axaml` through a `ResourceInclude` at the caller's existing resource scope.
The dictionary contains exactly two `DataTemplate` resources:

| Key | Model | Tree |
| --- | --- | --- |
| `Nvt.ReportList.PagedPagerTemplate` | `ReportPagedListViewModel` | One two-column Grid, status TextBlock, and load-more Button. |
| `Nvt.ReportList.WindowedPagerTemplate` | `ReportWindowedListViewModel` | One two-row Grid, wrapping status TextBlock, and a two-column Grid with previous/next Buttons. |

The templates read the existing models; they own no paging state or commands.
The host supplies `captionText` and `semanticAction secondary` styles and the `Nvt.ReportList.WindowedSpacing` resource.
Both `Classes` values are unchanged from NFC: `captionText` on each status and `semanticAction secondary` on every pager button.
At the frozen commit, `Styles/MainWindowControlStyles.axaml:503` selects `TextBlock.captionText`; `Styles/MainWindowButtonStyles.axaml:33,45,48` selects `Button.semanticAction` and `:51,57,62,67` selects `Button.secondary` and its states.
These selectors continue to apply when NFC loads the shared templates; there is no class-name migration.
NFC supplies spacing at exactly 8 DIP, retaining both the row and button-column gaps.
The paged tree retains margin `0,8,0,0`, columns `*,Auto`, and a 10 DIP column gap.
The windowed tree retains centered wrapping status, its bound tooltip, and equal-width button columns.
Status automation names equal visible status text and `AutomationProperties.LiveSetting` is `Polite`.
Button automation names equal their bound visible labels, and command availability controls effective enabled state.
The paged end button remains visible with the all-items-loaded label; windowed endpoint buttons remain visible and disabled when unavailable.
The host controls windowed-pager visibility through `HasMultiplePages`, as the frozen caller does.
No template adds a visibility predicate, row renderer, UserControl wrapper, theme/font import, schema, or export command.

The host retains the existing Theme resource owner and all eight legacy font values.
The UI family chain remains `fonts:Inter#Inter, Microsoft JhengHei UI, Noto Sans CJK TC, Noto Sans TC, Segoe UI`; the technical chain remains `Cascadia Mono, Consolas`.
Sizes remain 10, 11, 12, 13, 14, and 16 DIP.
These templates do not adopt the new Core font roles or redefine a font resource.

## Tests and provenance

Model tests live in `tests/Nvt.Core.Tests/ReportList/`.
All test data is synthetic.
All three facts from `tests/NvtFwCombiner.UiSmoke.Tests/ReportIndexedReadOnlyListsTests.cs` are ported.
Their allocation bound is unchanged.
Their deterministic concurrent-reader synchronization is unchanged.
The single-Reset assertion comes from `tests/NvtFwCombiner.UiSmoke.Tests/ReportWindowedListViewModelTests.cs`.
Synthetic windows retain the 64-row boundary and the final two-row page.
Reset bounds come from `tests/NvtFwCombiner.UiSmoke.Tests/MemoryCoveragePublicationTests.cs`.
Observed-state identity assertions come from `tests/NvtFwCombiner.UiSmoke.Tests/MemoryCoverageStatePublicationTests.cs`.
Only their collection assertions are adapted.
Product models and golden fixtures are not imported.
Additional tests cover empty lists and single rows.
They cover boundary indices and invalid arguments.
They cover 64 and 65 rows.
Factory tests cover `int.MaxValue` without allocating rows.
They cover cached null failures and lookup without materialization.
They cover all `IList` mutation refusals and `CopyTo` failures.
They cover reset notification order and reentrancy.
They cover enumeration and observer failures.

Paging tests live in `tests/Nvt.Core.Avalonia.Tests/ReportList/` and use synthetic rows and literal injected labels.

| Frozen source assertion | Core mapping |
| --- | --- |
| `ReportWindowedListViewModelTests.NavigationReplacesTheCurrentFixedSizeWindow` | Same class and method; 64-row windows, 130 total rows, final two-row window, labels, commands and one Reset are retained. |
| `ReportWindowedListViewModelTests.DirectItemNavigationShowsOnlyTheContainingWindow` | Same class and method; 10,000 rows, direct index 9,999, 16-row final window, 80 factory calls and no work on reselection are retained. |
| `ReportWindowedListViewModelTests.NavigationLabelsFollowTheSelectedShellLanguage` | `ReportWindowedListViewModelTests.NavigationLabelsFollowTheInjectedLabels`; the exact bilingual assertions use injected values. |
| `ReportProjectionConcurrencyTests.ReportPerformance.cs:105-172` | `ReportListMechanismTests.LargeIndexedProjectionUsesBoundedDeferredAndMemoizedPages`; 1,000 rows, 40 groups, 8-row summaries, deferred 24-of-25 detail rows, cached identity and cumulative 16-group loading. Report JSON and product verdict assertions remain in NFC. |
| Shared memory Reset publication and observed-state identity | Existing `Nvt.Core.Tests.ReportList.ResettableObservableCollectionTests` retains the collection assertions and notification bounds. |

Direct `ReportPagedListViewModelTests` cover empty and deferred input, page size one, exact and partial pages, multiple loads, end commands, idempotence, source identity and failures.
Both model test classes cover zero and negative page sizes, the positive lower boundary, `int.MaxValue` and its neighbour, and argument validation order.
Window tests cover invalid indices, both valid index bounds, page-boundary neighbours and empty-list indices.
Count cases straddle host sizes 8, 24, 40 and 64, including second-page boundaries.
Overflow tests exercise intermediate addition below, at and above `int.MaxValue` without allocating enormous lists.
Tests assert complete notification traces, per-Add state, one Reset per replacement, no-op reselection, command and collection identity, and zero source enumeration.
`ReportListMechanismTests` also covers null elements, memoized window revisits, immutable label choices and null label members.
Custom labels differ from both frozen languages.

The compiled pager assertions are in `ReportPagerTemplateTests`:

| Frozen source assertion | Core mapping |
| --- | --- |
| `XamlControlStyleContractTests.Report.cs:101-106` | `PagedBindingsUpdateThroughTheBoundCommandAndKeepTheEndButtonVisible`: loaded status/label bindings, accessible names, Polite status, and visible disabled end button. |
| `XamlControlStyleContractTests.HexEditor.cs`, `HexViewport.cs`: windowed template, page content, commands, and caller visibility | `WindowedBindingsKeepWrappingTooltipNavigationAndDisabledEndpoints`, `WindowedPagerVisibilityBelongsToTheHost`: loaded fixed-window navigation, labels, tooltip, equal columns, spacing, and the caller's `HasMultiplePages` predicate. Product viewport, inspector and row assertions remain in NFC. |
| `ReportChangesLayoutTests`: same-theme/language rendered layout methodology | Fragment comparisons use the independent frozen pager slices and synthetic rows. Product range-card, scrollbar, byte viewport and report-loading assertions remain in NFC. |
| `AvaloniaHeadlessTestApplication.cs` | The existing shared `AvaloniaTestHost` and single test-assembly registration supply Inter, Skia and `UseHeadlessDrawing=false`; no second bootstrap is introduced. |

`DictionaryContainsExactlyTheTwoModelTemplates` checks resource count and model matching.
`LoadedControlsFollowCountBoundaries` covers empty and single-row inputs, page size one, and counts below, at and above the first and second boundaries for sizes 8, 24, 40 and 64.
`DeferredModelsLoadThroughTheCompiledCommandBinding` checks the deferred status and initial command.
`WindowedSpacingUsesTheHostResourceWithoutOwningADefault` checks both dynamic gaps and caller resource replacement.
The frozen comparison dictionary is copied directly from the two source slices, retaining original keys and `NfcSpace8`; only its model namespace is retargeted for compilation.
Its source is independent of the new template and does not include product row trees.

`ReportPagerCoreThemeGeometryTests` checks the independent frozen slices and shared templates under `ThemeTokens.axaml`, `ButtonStyles.axaml` and `ScrollStyles.axaml`, with Fluent supplying the base control themes.
Every comparison uses a 960 × 180 DIP window, explicit render scaling 1.0 (96 DPI), manual sizing and layout rounding.
The pager widths are 240 and 960 DIP; edge-input comparisons use 336 DIP inside the same fixed window.
Typography resolves through the shared Core Theme's `Nvt.Font.NfcLegacy.Ui.Family` / `NfcUiFontFamily` and size-13 resources.
Assertions check the resolved family, 13 DIP size, normal weight/style, both 8 DIP windowed gaps, paged margin and 10 DIP gap, equal star columns, and base-control padding, border, corner radius and minimum height.
The geometry label sets use short and long Latin text covered by bundled Inter; shaped glyphs must be present without font simulation, and each resolved font stream must match the bundled `Inter-Regular.ttf` SHA-256.
This avoids machine-dependent installed CJK fallback fonts while the compiled binding/accessibility tests retain English and Traditional Chinese labels.
Navigation comparisons include the first, middle and final states, disabled endpoints and reverse navigation; edge cases include empty, single-row, exact and adjacent pages and maximum batch sizes.
These checks establish Core Theme geometry; NFC supplies its own caption and secondary-button selectors at adoption.

After packages are restored, run from the repository root:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

## Known differences

The namespace changes to `Nvt.Core.ReportList`.
The four types become public.
Their constructors and required members become public.
The implementation is split into four source files.
Copyright headers and XML API documentation are added.
The predicates, literals, messages and method bodies retain the frozen behavior.
`ObjectReadOnlyList<T>` is internal to the Avalonia paging models and adds no public collection owner.
The two models and their creation/navigation members become public in `Nvt.Core.Avalonia.ReportList`.
Language branches become immutable injected labels and formatters; null labels and null label members are rejected after frozen item and page-size validation.
The frozen model notification sequence and Toolkit command behavior remain intact.
Pager templates are separate from these models.
Pager extraction renames only the two resource keys, the model namespace, and the windowed spacing resource key.
The original deferred resource scopes remain caller-owned; no resource is loaded eagerly by Core.
The templates introduce no C# state fields and require no changes to the paging models.

## NFC ownership and adoption

NFC retains report row models and window navigation policy.
NFC retains memory coverage projection and interaction state construction.
The shared reset collection serves `MergeCoverageSegments`.
It also serves `ReplaceCoverageSegments` and `CtrlRamOverview`.
MessageCenter's passive activity projection stays outside this module.
Later consumers must use one shared reset implementation.
NFC adoption must pin the exact Core revision or package version.
NFC downloads verified versioned packages at build time through `core-packages.json`.
Use exact `[x]` versions, locked restore, and source mapping restricted to the package download folder.
The manifest records each package's Release tag and SHA-256; Core and NFC retain independent versioned releases.
Delete NFC's local generic collections, paging models and object adapter only after all their callers use the pinned packages and equivalent executable checks preserve the frozen values, identities, materialization and notifications.
Retarget every report and shared memory Reset consumer to the single collection owner.
NFC keeps its separate label factory, ShellLanguage mapping, row factories, report DTOs, schema, export, async providers, report history and product navigation policy.
MessageCenter keeps its separate report history table.
Run NFC's existing functional and publication tests against the frozen baseline.
Compare complete values, row identity, materialization and notifications.
Core pager tests cover compiled structure, bindings, commands, accessibility and geometry under the Core Theme.
NFC takes pixel evidence at adoption using its zero-difference UI snapshot rule: the before/after NFC screens must have zero changed decoded pixels.
Core fragment tests do not establish that product UI result and do not load NFC styles or compare decoded NFC pixels.
Compare under the same OS, resolved fonts, DPI, theme, renderer, viewport, motion, input, time and IDs, and record each artifact's SHA-256.
Compare complete values, event traces and output bytes where applicable, and preserve the eight legacy font values.
NFC's existing product image producers are in `tests/NvtFwCombiner.UiSmoke.Tests/` and save frames when `NFC_VISUAL_OUTPUT_DIR` is set:

| NFC test | Captured screens at the frozen baseline |
| --- | --- |
| `ReportChangesLayoutTests.ChangedRangeCardsReserveAStableScrollbarGutter` | Report modal Changes workspace at 1440 × 900 in Light/Dark and English/Traditional Chinese; the light-English CRC-cause view is also captured. |
| `ReportHistoryControlTests.DpWarningHistoryShowsRecordedLengths` | Report history and the opened report review at 1536 × 864, light English. |
| `ReportHistoryControlTests.HistoryTrashDeletesOnlyTargetAndPersists` | Report history rows at widths 1920 and 1024, height 850, across the declared theme/language pairs. |
| `RunReportsListTests.RunReportsShowsDirectListAndFullWidthNavigation` | Message Center Run Reports list at 1635 × 962 light English and 1024 × 768 dark Traditional Chinese. |

For pager adoption, NFC's before/after snapshot pairs compare the actual Report modal paged collections (output-difference summaries and Audit lists, including loaded-more and all-loaded states) and the Hex Editor changed-block fixed-window list (first, middle and final windows, both disabled endpoints, and the host's hidden zero/one-page pager).
Use the same theme/language and environment for each pair and require zero changed decoded pixels.
The frozen NFC tree does not contain a dedicated image-producing test for those two pager-state matrices; the broader captures above do not establish their pixel parity.
NFC owns those adoption captures alongside `XamlControlStyleContractTests.ReportDetailCollectionsUseBoundedPagerBindings`, `XamlControlStyleContractTests.HexEditorInspectorUsesCompactTopAlignedLayout`, `ReportWindowedListViewModelTests` and `RunAndHexEditorTests.HexEditorBoundsFragmentedChangedBlockProjection`.
The latter tests provide caller/model contracts rather than saved UI snapshots.

NFC keeps the existing `ReportPagerTemplate` alias in its report dictionary and `HexEditorChangedBlockPagerTemplate` alias in the Hex Editor resource scope until all callers can use the shared keyed templates at those same deferred scopes.
Delete only the two local pager trees after actual alias-loaded comparisons following NFC's final shared UI imports preserve exact control trees, measurements, automation, command behavior and zero changed decoded pixels.
Keep the source row templates and all product-specific caller bindings in NFC.
Record comparison artifact SHA-256 hashes and the common environment manifest; fragment tests do not establish full product parity.
