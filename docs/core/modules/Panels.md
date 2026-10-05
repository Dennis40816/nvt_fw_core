[English](Panels.md) | [中文](Panels.zh-TW.md)
# Panels

## Purpose

`Nvt.Core.Avalonia.Panels` contains two layout controls extracted from NFH (FreeformHelper): a flat collapsible panel and a workspace with named content regions. Hosts supply the text and content. Product workflows remain in NFH.

Merge `avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml` into the host's resources and add this include to its styles:

```xml
<StyleInclude Source="avares://Nvt.Core.Avalonia/Panels/PanelsStyles.axaml" />
```

## Public API

`CollapsiblePanel` inherits `ContentControl`; inherited `Content` is its body.

| Property | Type | Default |
| --- | --- | --- |
| `Title` | `string` | Empty string |
| `IsExpanded` | `bool` | `true` |
| `DefaultExpanded` | `bool` | `true` |
| `IsCollapsible` | `bool` | `true` |
| `HeaderRight` | `object?` | `null` |

At initialization, an unset `IsExpanded` takes `DefaultExpanded`. An explicit value, including an explicit `true`, wins. Changing `DefaultExpanded` after initialization does not change expansion. Turning `IsCollapsible` off forces expansion and rejects later attempts to collapse. Turning it on again leaves the panel expanded until it is collapsed explicitly or through the header. The disabled header and hidden chevron follow NFH's non-collapsible behavior. The template has a toggle header containing the title, right-hand content and chevron, followed by the body; it uses no `Expander` or nested body card.

`WorkspaceShell` inherits `TemplatedControl`.

| Property | Type | Default / region |
| --- | --- | --- |
| `Title` | `string` | Empty string; main header title |
| `Subtitle` | `string` | Empty string; wrapping text below the title |
| `HeaderRight` | `object?` | `null`; top right of the header |
| `SummaryContent` | `object?` | `null`; below the header |
| `ToolbarContent` | `object?` | `null`; below the summary |
| `LeftContent` | `object?` | `null`; left main column |
| `RightContent` | `object?` | `null`; right main column |
| `FooterContent` | `object?` | `null`; below the main columns |
| `LeftColumnWidth` | `GridLength` | `2.2*` |
| `RightColumnWidth` | `GridLength` | `*` |

Each listed property has a corresponding public static `StyledProperty` field named `<Property>Property`. Both width properties bind to the main grid's column definitions and update after templating. The defaults preserve NFH's original proportions without product token names.

## Provenance

Frozen parent repository: `Dennis40816/nvt-freeform-helper`; ref: `1.3.x`; full commit: `e01e07a361b8dc264a06b3741f40274feeeace2d`. Source was read from that commit, not from the working tree.

| Extracted source path | Extracted portion |
| --- | --- |
| `src/FreeformHelper.UI/Controls/HidePanelBlock.axaml.cs` | Property defaults and initialization/change behavior, renamed `CollapsiblePanel` |
| `src/FreeformHelper.UI/Controls/ReviewWorkspaceShell.cs` | Workspace properties, renamed `WorkspaceShell` |
| `src/FreeformHelper.UI/Styles/Controls.Panel.axaml` | Both templates and their panel root, header, chevron and body rules |
| `src/FreeformHelper.UI/Styles/Controls.Core.axaml` | Only `workspaceTopLayerBox`, `workspaceMainTitle` and the toggle-button `SelectableTextBlock` interaction rule, scoped to the panel header |

`src/FreeformHelper.UI/Styles/Tokens.axaml` supplies the frozen values used for substitutions and width defaults. No other controls or product views are extracted.

## Token mapping

Every resource reference in `PanelsStyles.axaml` uses an existing `Nfc*` key through `DynamicResource`. `ThemeTokens.axaml` is unchanged.

| NFH token | Core token |
| --- | --- |
| `BrushBgSurfaceInset` | `NfcSurfaceSubtleBrush` |
| `BrushBgPanel` | `NfcSurfaceBrush` |
| `BrushBorder` | `NfcBorderBrush` |
| `BrushTextPrimary` | `NfcTextStrongBrush` |
| `BrushTextSubtle` | `NfcTextSecondaryBrush` |
| `BrushTextMuted` | `NfcTextMutedBrush` |
| `BrushBgInteractiveHover` | `NfcSelectionSurfaceBrush` |
| `BrushBgInteractivePressed` | `NfcSecondaryActionPressedBrush` |
| `RadiusSm` | `NfcCompactCornerRadius` |
| `Space2` | `NfcSpace2` |
| `Space12` | `NfcSpace12` |
| `Space16` | `NfcSpace16` |
| `FontSizeSectionTitle` | `NfcFontSize16` |

## Verification

The existing `ThemeTestApplication` runs the headless tests unchanged. All content is synthetic. `CollapsiblePanelTests` pins all defaults, unset and explicit initialization, clearing an explicit value before initialization, header keyboard toggling in both directions, both assignment orders for non-collapsible initialization, transitions between collapsible and non-collapsible states, body visibility, header-right visibility and flat layout. It also checks the vector's direction. `WorkspaceShellTests` checks every slot's visual region, row order, default proportions and custom/live column widths. `PanelsStylesTests` checks resource resolution in both themes, live theme/host overrides and the applicable source layout guards. Its source guards read the checked-out style file; runtime tests load the compiled style include.

The frozen test sources were searched for `HidePanelBlock`, `ReviewWorkspaceShell`, `panelBlock*`, `workspaceTopLayerBox` and `workspaceMainTitle`. No dedicated tests or direct references were found. Applicable concerns were ported from `tests/FreeformHelper.Tests/UI/Snapshots/UiLayoutGuardTests.cs`: no inline hex colors outside tokens, no duplicate property setters in a style, no scroll-width binding to `Bounds.Width`, and no default `Expander`. The headless layout concern in `UI/Smoke/WorkspaceViewsSmokeTests.cs` and `UI/Smoke/NotchExportSelectionWindowSmokeTests.cs` is covered using synthetic slots; their product setup stays in NFH. Product source-hash and rendered snapshot baselines are not copied into Core.

Run with `AVALONIA_TELEMETRY_OPTOUT=1`:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Panels"
```

### NFH adoption evidence

NFH adoption is outside this extraction. Its adoption PR verifies zero difference in expand/collapse behavior and content placement as follows:

1. Keep the default widths: `LeftColumnWidth=2.2*` and `RightColumnWidth=*` already reproduce `NotchExportMainLeftColumnWidth` and `NotchExportMainRightColumnWidth`.
2. Map NFH's token values to the Core keys above in the host's resource scope, including both themes, typography and corner radius. Shared keys affect their scope's other Core styles too; verify those consumers when choosing the scope.
3. Run the NFH test project with the existing `UiLayoutGuardTests`, `HeadlessUiSmokeTests`, `WorkspaceViewsSmokeTests`, `NotchExportSelectionWindowSmokeTests`, `UiVisualSnapshotTests` and `UiRenderedVisualSnapshotTests`. The relevant files are under `tests/FreeformHelper.Tests/UI/Snapshots/` and `UI/Smoke/`. Re-run the equivalent synthetic Panels characterizations after adoption as well. These existing broad tests do not replace the control-state comparisons.
4. Compare unset/explicit initial expansion, expanded/collapsed headers, keyboard and pointer toggles, disabling/re-enabling collapsibility, attempted collapse while non-collapsible, and populated/empty header-right content. Compare all workspace slots, wrapping subtitles, trimmed panel titles, header hover/pressed/disabled states and the two column proportions. Use the same OS, fonts, DPI, theme, window size and synthetic content before and after.
5. NFH UI snapshots may change, including the chevron, Core palette substitutions and the local header presenter. Attach before-and-after images to the adoption PR, review each visual change and update NFH's snapshot baselines only for approved changes. Expand and collapse behavior stays identical.

## Known differences

- Generic names replace `HidePanelBlock` and `ReviewWorkspaceShell`. The shell's only new properties are its two `GridLength` widths.
- NFH uses the icon font's `ExpandMore` and `ExpandLess` glyphs. Core draws a vector `Path`, down when collapsed and up when expanded, in the same 20-by-20 host. Its fixed 12-by-12 layout and 1.5 stroke replace the `IconSizeSm=12` font glyph; no suitable icon size/geometry token exists. There is no icon property or icon-font dependency.
- NFH relies on the host's `ToggleButton` template. Core supplies a minimal, bound `ContentPresenter` scoped to the panel header so these styles work with the existing headless application and without a new theme package. Style selectors are scoped to the two extracted controls. Header interaction and expansion binding remain the same; host-theme rendering may differ.
- NFH sets `IsHitTestVisible=False` and `Focusable=False` on every `SelectableTextBlock` inside a toggle button through a global rule. Core applies the same two setters only inside the panel header, so selectable header content does not block toggling.
- The controls set no font. The host's font applies, as with NFH's global text font rule, which stays in NFH.
- Core's mapped brush values can differ from NFH's palette. The mapping is semantic, not a claim of pixel equality. NFH supplies its own mapped values and reviews adoption images.
- Where no existing resource of the required type/value fits, the frozen geometry remains literal. Core spacing keys are `double`; using one directly as a `DynamicResource` for a `Thickness` property causes an invalid cast, so thickness values stay literal without adding converters or tokens.

| NFH token without a matching Core resource | Retained literal / replacement |
| --- | --- |
| `BrushTransparent` | `Transparent` |
| `InsetLeft10` | `10,0,0,0` |
| `Inset14` | `14` |
| `Space14` | `14` |
| `Inset12_10` | `12,10` |
| `Inset10` | `10` |
| `Inset8` | `8` |
| `Inset10_8` | `10,8` |
| `BorderThin` | `1` |
| `BorderBottomThin` | `0,0,0,1` |
| `BorderTopThin` | `0,1,0,0` |
| `RadiusTopMd` | `10,10,0,0` |
| `PanelBlockChevronHostSize` | `20` |
| `NotchExportMainLeftColumnWidth` | `LeftColumnWidth`, default `2.2*` |
| `NotchExportMainRightColumnWidth` | `RightColumnWidth`, default `*` |

NFH uses Avalonia 11.3.12 and xUnit 2; Core uses Avalonia 12.0.5 and xUnit v3. No control-code API adaptation was required. Core's headless key calls pass the physical key and key symbol required by Avalonia 12. Column layout assertions compare presenter bounds because `ColumnDefinition.ActualWidth` includes spacing in this version. The expansion logic is otherwise unchanged, and no behavior discrepancy with the task summary was found.

## What stays in NFH

Every other NFH control, its icon font, `FontIcon`, `IconGlyphs`, notch export views, product view models/workflows, token ownership and product snapshot baselines stay in NFH. Adoption, package/runtime alignment and approval of before-and-after images remain NFH work. There are no open implementation questions for this extraction.
