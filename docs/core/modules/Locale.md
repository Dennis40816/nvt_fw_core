[English](Locale.md) | [中文](Locale.zh-TW.md)

# Locale: Common text

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Core.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Localized.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Settings.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Report.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Navigation.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportWindowedListViewModel.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ReportPagedListViewModel.cs`

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

## API

[`CommonTextResources`](../../../src/Nvt.Core/Locale/CommonTextResources.cs) is in `Nvt.Core.Locale`.
It targets `net10.0`.
It depends only on the BCL.
`CommonLanguage` contains `English` and `TraditionalChinese`.
`For(language)` returns one lazy cached bundle per language.
`Language` records the selected language.
The bundle has 23 read-only string properties.
The bundle has three integer formatters.
There is no public constructor.
An unsupported language throws `ArgumentOutOfRangeException`.
The exception keeps the parameter name `language` and the supplied enum value.

## Source mapping

Unlisted common label names retain their source names.
Identical Cancel values share `CancelLabel`.
Identical Preferences values share `PreferencesLabel`.

| Frozen source member | Core member |
| --- | --- |
| `ShellLanguage.ChineseTraditional` | `CommonLanguage.TraditionalChinese` |
| `BackTooltip` | `BackLabel` |
| `OutputDeliveryCancelLabel`, `FirmwareNumberMismatchCancelLabel` | `CancelLabel` |
| `ExitConfirmLabel` | `ExitLabel` |
| `LeaveEditorConfirmLabel`, `OutputDeliveryConfirmLabel` | `ContinueLabel` |
| `NavigationClearCancelLabel` | `StayOnPageLabel` |
| `SettingsPreferencesTitle`, `LocalStatePreferencesLabel` | `PreferencesLabel` |
| `SystemThemeChoiceLabel`, `LightThemeChoiceLabel`, `DarkThemeChoiceLabel` | `SystemThemeLabel`, `LightThemeLabel`, `DarkThemeLabel` |
| `EnglishLanguageChoiceLabel`, `ChineseTraditionalLanguageChoiceLabel` | `EnglishLanguageLabel`, `TraditionalChineseLanguageLabel` |
| Windowed `PageStatus` interpolation | `FormatWindowStatus(first, last, total)` |
| Paged `PageStatus` interpolation | `FormatPagedStatus(visible, total)` |
| Paged `LoadMoreLabel` interpolation | `FormatLoadMore(next, remaining)` |

## Behavior and consumption

Both languages retain the frozen literals and punctuation.
The three formatters use the current culture at each call.
They interpolate supplied integers without validation or clamping.
They add no grouping format.
They impose no paging limit.
Inject the selected bundle into common text consumers.
The caller keeps paging state and argument validation.
The windowed caller selects `NoItemsLabel` when `VisibleCount == 0`.
Otherwise, it computes the checked first and last positions before formatting.
The paged caller always formats its visible and total counts.
It selects `AllItemsLoadedLabel` when `!(RemainingCount > 0)`.
Otherwise, it passes `Math.Min(pageSize, RemainingCount)` as `next`.
These predicates stay in the consumer.
Raw zero formatter arguments do not select the empty or all-loaded labels.

NFC keeps persisted preference parsing and culture selection.
NFC keeps product vocabulary and diagnostics formatters.
Message Center diagnostics and launcher messages remain in their product owners.
Later list consumers inject `ReportListLabels`.
Later Message Center consumers inject `IMessageCenterText`.
Those types are outside this module's API.
Delete a common-text adapter only after its callers use this bundle and adoption evidence passes.

## Verification and known differences

The Locale tests compare all 23 labels ordinally and as UTF-8 bytes in both languages.
They port the frozen cache identity and bilingual navigation label assertions.
They cover empty and one-item text.
They cover 63, 64, 65, 130, 10,000 and 10,001 counts.
They cover invalid enum values and integer extremes.
They check five cultures and a synthetic negative sign.
They check the exact public allowlist and absence of setters.
No literal or formatter behavior difference is intended.
Source member renames are listed above.

Run after the host's existing package restore:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

NFC adoption is a separate task.
Core tests do not establish pixel-identical NFC adoption.
