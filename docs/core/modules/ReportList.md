[English](ReportList.md) | [中文](ReportList.zh-TW.md)

# ReportList

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportIndexedReadOnlyLists.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ResettableObservableCollection.cs`

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

The module lives in `src/Nvt.Core/ReportList/`.
The namespace is `Nvt.Core.ReportList`.
It targets .NET 8.
It depends only on the BCL.
It owns all four generic collection types below.

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

## Tests and provenance

Tests live in `tests/Nvt.Core.Tests/ReportList/`.
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

After packages are restored, run from the repository root:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

Verification used the local working tree based on Core `b98099a43553f4d04f084a3a33bc027bd9a8c95c`.
The build passed.
It reported 0 warnings.
It reported 0 errors.
All 84 ReportList test cases passed in the full solution run.
The Core test project passed 377 tests.
The Avalonia test project passed 260 tests.
No tests failed.
No tests were skipped.

## Known differences

The namespace changes to `Nvt.Core.ReportList`.
The four types become public.
Their constructors and required members become public.
The implementation is split into four source files.
Copyright headers and XML API documentation are added.
The predicates, literals, messages and method bodies retain the frozen behavior.
`ObjectReadOnlyList<T>` is reserved for the later Avalonia model extraction.
This module adds no UI.

## NFC ownership and adoption

NFC retains report row models and window navigation policy.
NFC retains memory coverage projection and interaction state construction.
The shared reset collection serves `MergeCoverageSegments`.
It also serves `ReplaceCoverageSegments` and `CtrlRamOverview`.
MessageCenter's passive activity projection stays outside this module.
Later consumers must use one shared reset implementation.
NFC adoption must pin the exact reviewed Core revision or package version.
Delete NFC's local copies only after all four generic collection consumers use that revision.
Keep the reserved object view until its separate extraction is adopted.
Run NFC's existing functional and publication tests against the frozen baseline.
Compare complete values, row identity, materialization and notifications.
NFC UI adoption still requires zero changed decoded pixels.
Core tests alone do not establish that UI result.
