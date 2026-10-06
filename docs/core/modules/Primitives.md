[English](Primitives.md) | [中文](Primitives.zh-TW.md)

# Primitives

## Purpose and public API

Three small controls live in `src/Nvt.Core.Avalonia/Primitives/`, namespace `Nvt.Core.Avalonia.Primitives`. This module contains layout, section framing and informational labels only.

| Control | Public properties and defaults | Behavior |
|---|---|---|
| `BalancedWrapPanel` (sealed `Panel`) | `ItemSpacing = 0`, `LineSpacing = 0`, `BalanceLastRow = true` | Measures every child with infinite space, wraps visible children at their desired sizes, and optionally moves the preceding row's last child before a single-child final row when it fits. Only the last two rows participate. |
| `SectionFrame` (`ContentControl`) | `Title = string.Empty`; inherited `Content` | A selectable title between divider lines, followed by a stretched content presenter. The title and content remain bound to their properties. |
| `InfoLabel` (sealed `UserControl`) | `Text = string.Empty`, `Tip = string.Empty`, `TipTargetClass = null` | Selectable wrapped text, with the tooltip on the label itself. A configured class also receives the tooltip on the nearest matching visual ancestor. |

Each property has a corresponding styled property field. `SectionFrame` retains the source's inherited content behavior. `InfoLabel` applies nonblank tips on attachment to the visual tree and when `Tip` changes. The method skips empty or whitespace tips, but the label's own tooltip follows `Tip` through the same XAML binding as the source. Null or empty `TipTargetClass` limits application to the label. Matching starts at the visual parent, skips nonmatching ancestors and stops at the first matching control. Changing `TipTargetClass` alone does not apply a tip, and detaching or changing targets does not clear previously applied ancestor tips.

Load `Theme/ThemeTokens.axaml` into the application's resources and add the default styles:

```xml
<StyleInclude Source="avares://Nvt.Core.Avalonia/Primitives/PrimitivesStyles.axaml"/>
```

`BalancedWrapPanel` needs no default style. Hosts supply their own text, content and product-specific classes.

## Provenance

Frozen parent baseline: repository `Dennis40816/nvt-freeform-helper`, ref `1.3.x`, full commit `e01e07a361b8dc264a06b3741f40274feeeace2d`. Source uses Avalonia 11.3.12 and xUnit 2; Core uses Avalonia 12.1.1 and xUnit v3.

Extracted file paths at that commit:

- `src/FreeformHelper.UI/Controls/BalancedWrapPanel.cs`
- `src/FreeformHelper.UI/Controls/PadInfoSectionFrame.cs`
- `src/FreeformHelper.UI/Controls/SettingsInfoLabel.axaml`
- `src/FreeformHelper.UI/Controls/SettingsInfoLabel.axaml.cs`
- `src/FreeformHelper.UI/Styles/Controls.PadInfo.axaml` — the `PadInfoSectionFrame` template and its divider-line/title rules only.
- `src/FreeformHelper.UI/Styles/Controls.Settings.axaml` — the label alignment and `settingsInfoLabelText` rules only.

`src/FreeformHelper.UI/Styles/Tokens.axaml` was read to identify the frozen token values; its resources are not imported. `BalancedWrapPanel`'s measure, arrange, row construction and balancing algorithm is unchanged. The other controls receive generic names, namespace and internal style classes. The fixed ancestor class is replaced by the requested `TipTargetClass` property.

## Token mappings

Every source token used by the extracted template and label rules is listed here. All Core resource references use `DynamicResource` and refer to existing keys in `Theme/ThemeTokens.axaml`; no keys were added.

| NFH token | Core resource or literal | Use |
|---|---|---|
| `Space8` | `NfcSpace8` | Section stack spacing and header column spacing, both 8 by default. |
| `Space1` | Literal `1` | Divider height; no existing Nfc key represents 1. |
| `BrushWhite` | `NfcSurfaceBrush` | Divider background; the closest existing light surface brush. |
| `BrushTextPrimary` | `NfcTextStrongBrush` | Section title and label foreground. |
| `BrushPadInfoPanelBackground` | `NfcSurfaceBrush` | Section title background; the closest existing surface role. |
| `Inset8_4` | Literal `8,4` | Title padding; Nfc spacing doubles do not supply a matching `Thickness` token. |

## Verification

Tests in `tests/Nvt.Core.Avalonia.Tests/Primitives/` reuse the existing `ThemeTestApplication`. They cover property defaults, compiled styles in both themes, dynamic resources, title/content/text bindings, selectable wrapped text, and tooltip timing, nearest-ancestor search, missing/disabled targets, blank tips and reattachment.

A frozen-commit `git grep` of the three control names found only `tests/FreeformHelper.Tests/UI/Snapshots/UiLayoutGuardTests.cs`: `CriticalScrollContainers_UseViewportBoundedWidth` checks the product's badge-panel use, the `BalanceLastRow` style setter and the absence of a wrap panel in the product toolbar. The portable style-property contract is exercised by `BalanceLastRowCanBeSetFromStyles`; product view/class assertions remain in NFH. No existing direct behavioral tests for these three controls were found at the baseline.

The rectangle expectations were reasoned from the frozen algorithm before running the Core tests. Rectangles below are `(x, y, width, height)` in original child order. Unless indicated otherwise, item spacing is 5, line spacing is 7 and balancing is enabled. Desired size is the panel's measured size; child sizes are synthetic.

| Case | Available width; child sizes | Literal expected rectangles; desired size |
|---|---|---|
| One row | 100; `20x10, 30x20, 10x15` | `(0,0,20,10), (25,0,30,20), (60,0,10,15)`; `70x20` |
| Several rows, exact fit | 65; `30x10, 30x20, 30x15, 30x25, 30x12, 30x18` | `(0,0,30,10), (35,0,30,20), (0,27,30,15), (35,27,30,25), (0,59,30,12), (35,59,30,18)`; `65x77` |
| Single-child last row, balanced | 70; `20x10, 20x12, 20x30, 20x15` | `(0,0,20,10), (25,0,20,12), (0,19,20,30), (25,19,20,15)`; `45x49` |
| Same input, balancing disabled | 70; same children | `(0,0,20,10), (25,0,20,12), (50,0,20,30), (0,37,20,15)`; `70x52` |
| Only final two rows balance | 70; seven `20x10` children | `(0,0,20,10), (25,0,20,10), (50,0,20,10), (0,17,20,10), (25,17,20,10), (0,34,20,10), (25,34,20,10)`; `70x44` |
| Moved child cannot fit | 60; `20x10, 30x20, 60x15` | `(0,0,20,10), (25,0,30,20), (0,27,60,15)`; `60x42` |
| Preceding row has one child | 50; `40x10, 40x20`; line spacing 3 | `(0,0,40,10), (0,13,40,20)`; `40x33` |
| Zero-size and collapsed children | 100; `20x10, 0x0, collapsed 100x100, 30x15` | `(0,0,20,10), (25,0,0,0), (0,0,0,0), (30,0,30,15)`; `60x15` |
| Infinite available width | Infinity; `20x10, 30x20, 40x5, 10x15`; arrange width 200 | `(0,0,20,10), (25,0,30,20), (60,0,40,5), (105,0,10,15)`; `115x20` |
| Arrange width differs from measure | Measure 100, arrange 65; three `30x10` children | `(0,0,30,10), (0,17,30,10), (35,17,30,10)`; measured `100x10` |

For example, the four-child balanced case first produces rows `[1,2,3]` and `[4]`. Moving child 3 yields row heights 12 and 30, so the second row starts at `12 + 7 = 19` and desired height is `12 + 7 + 30 = 49`. Without balancing, row heights stay 30 and 15, so the second row starts at 37 and desired height is 52. Visible zero-size children count toward spacing; collapsed children are excluded. Empty and collapsed-only panels measure `0x0`. Tests also pin measurement invalidation when any layout property changes.

Run after restore:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Primitives"
```

### NFH adoption evidence

Adoption is a separate NFH change. Set `InfoLabel.TipTargetClass` to `settingsFieldTile` and map NFH's tokens to the Nfc keys above in the host's resource integration. Preserve the original product classes and consumers. Run `UiLayoutGuardTests`, especially `CriticalScrollContainers_UseViewportBoundedWidth`, `CriticalDynamicTextBindings_UseWrapOrTrimmingContract`, `HoverAffordances_KeepContrastTooltipsAndInputBorderScope`, and the two `PadInfoPopover` guards, before and after adoption:

```powershell
dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.UiLayoutGuardTests"
```

Build the NFH test project before each test run. Update only source/type references required by adoption; keep its product assertions. Compare each rectangle and desired size in the table using the same synthetic child inputs against both frozen NFH and Core. Also compare actual badge child rectangles at the same viewport widths, section title/divider/content bounds, and label text bounds under identical OS, fonts, DPI and theme. Compare property values, text/content binding updates, and label/ancestor tooltip values and lifecycle directly. Non-UI behavior stays identical.

NFH UI snapshots may change because of the documented resource mappings and Avalonia version. The adoption PR attaches before-and-after images in both themes and identifies those differences; Core tests alone do not certify NFH visual adoption.

## Known differences

- `PadInfoSectionFrame` becomes `SectionFrame`; `SettingsInfoLabel` becomes `InfoLabel`. Internal settings/pad-info style classes become generic and their selectors are scoped to these controls.
- Ancestor tooltip propagation is opt-in through `TipTargetClass`, default null. NFH restores its original class search by setting `settingsFieldTile`.
- NFH colors map to Core's existing palette. `BrushWhite` and the translucent `BrushPadInfoPanelBackground` both map to `NfcSurfaceBrush`; this changes dark divider color and title-background opacity. A single shared resource key cannot preserve those two distinct source brushes simultaneously. Primary text colors also differ.
- Divider height `1` and title padding `8,4` retain frozen values as literals because no matching Nfc keys exist. The source's static spacing lookup becomes a dynamic lookup of `NfcSpace8`.
- The code compiles against Avalonia 12.1.1 instead of 11.3.12; no other control API adaptation was required. Headless tests use xUnit v3 instead of xUnit 2. Product-level rendering compatibility still needs NFH adoption evidence.

## What stays in NFH and open questions

Product names, settings classes, pad-info wording, badge/toolbar styles, views, view models and UI snapshots remain in NFH. All other controls, including `FontIcon`, `LoadingSpinner`, `HidePanelBlock`, `ReviewWorkspaceShell`, `NumberScrubber`, `WorkspaceHeader` and dialogs, are outside this module. NFH adoption is not performed here.

No implementation questions remain. NFH's Avalonia upgrade compatibility and its before-and-after adoption images remain product-level verification work.
