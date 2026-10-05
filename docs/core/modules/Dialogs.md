[English](Dialogs.md) | [中文](Dialogs.zh-TW.md)

# Dialogs

## Purpose and public API

`src/Nvt.Core.Avalonia/Dialogs/` contains two small windows in namespace `Nvt.Core.Avalonia.Dialogs`:

- `ConfirmDialog()` and `ConfirmDialog(string title, string message, string confirmText, string cancelText, bool emphasizeCancel = false, string? confirmTip = null, string? cancelTip = null)`. Confirm completes `ShowDialog<bool>(owner)` with `true`; Cancel completes it with `false`. Cancel emphasis adds `danger` and displays the close icon. Each null tip means that button has no tooltip.
- `WarningDialog()` and `WarningDialog(string title, string message)`. The button text is `OK`; clicking it calls `Close()` and completes `ShowDialog(owner)`.

Both windows retain selectable titles and messages, wrapping messages, fixed 360 × 170 dimensions, `CanResize = false`, and `WindowStartupLocation = CenterOwner`. The parameterless constructors load the same layout with empty content. As in NFH, the supplied title is the selectable content title; it does not set `Window.Title`.

Each window includes `DialogsStyles.axaml` locally. The application loads `avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml` into its resources and supplies its normal window/button control themes and base button/danger styling. Core's existing `Theme/ButtonStyles.axaml` can supply button styling. No application theme, dialog service, options record, or additional configuration is introduced.

## Provenance

Frozen parent baseline: repository `Dennis40816/nvt-freeform-helper`, ref `1.3.x`, full commit `e01e07a361b8dc264a06b3741f40274feeeace2d`, Avalonia 11.3.12 and xUnit 2. Extracted paths:

- `src/FreeformHelper.UI/Views/ConfirmDialog.axaml`
- `src/FreeformHelper.UI/Views/ConfirmDialog.axaml.cs`
- `src/FreeformHelper.UI/Views/WarningDialog.axaml`
- `src/FreeformHelper.UI/Views/WarningDialog.axaml.cs`
- `src/FreeformHelper.UI/Styles/Controls.Overlay.axaml` — only the five `confirmDialog*` rules; no tooltip styles.
- `src/FreeformHelper.UI/Styles/Tokens.axaml` — only values referenced by these windows and rules, including `DialogWidth`, `DialogHeight`, and `ConfirmDialog*`.

Existing evidence comes from `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.CommandsAndUndo.InputAndExport.cs`, method `ExportDxfLayerImageCommand_WhenDxfMissing_ShowsWarningDialog`. It captures a warning request rather than creating a dialog. Core ports only its warning title and message assertions using the frozen caller text from `src/FreeformHelper.UI/ViewModels/FreeformHelperViewModel.DxfEditing.Export.cs`; command, status, and view-model behavior stay in NFH. There are no direct dialog tests in the supplied source coverage.

The callers were inspected for context only: `src/FreeformHelper.UI/Views/FreeformHelperView.Pickers.cs`, `LeftDxfPanel.axaml.cs`, `NotchExportSelectionWindow.axaml.cs`, and `SettingsWindow.axaml.cs`.

## Resource mappings

Every extracted NFH resource mapped to an existing Core resource is listed below. All Core references use `DynamicResource`; `ThemeTokens.axaml` is unchanged.

| NFH token | Core token | Use |
| --- | --- | --- |
| `BrushBgSurface` | `NfcSurfaceBrush` | Window background |
| `BrushTextPrimary` | `NfcTextStrongBrush` | Selectable title |
| `BrushTextSubtle` | `NfcTextBrush` | Selectable message |
| `BrushWhite` | `NfcDangerTextBrush` | Cancel vector stroke |
| `BrushWarning` | `NfcWarningAccentBrush` | Warning vector stroke |
| `Space8` | `NfcSpace8` | Action/header spacing |
| `ConfirmDialogDangerContentSpacing` | `NfcSpace8` | Cancel icon/text spacing |

The font-based `IconGlyphs.Close` is replaced by `NfcCloseIconGeometry` through `DynamicResource`.

## Verification

Core uses Avalonia 12.0.5, xUnit v3, and the unchanged `ThemeTestApplication` headless host. `tests/Nvt.Core.Avalonia.Tests/Dialogs/` covers selectable supplied text (ordinary, empty, Unicode, multiline), both actual `ShowDialog<bool>` results, ownership, emphasized and ordinary cancel states, parameterless loading, independent optional/null/empty tips, frozen window/action layout, dynamic brush changes, warning content, and OK closure through the actual modal task. The warning-content case is the scoped port of existing NFH evidence; the remaining cases characterize the frozen dialogs.

After the existing package restore, run:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Dialogs"
```

For NFH adoption:

1. At the Embed DXF call site in `FreeformHelperView.Pickers.cs`, pass `confirmTip: "Embed DXF into the project file."` and `cancelTip: "Cancel and keep external DXF reference."`. Other callers choose their own tips or leave them null. The frozen dialog applies these product tips to every instance; Core deliberately makes them caller supplied.
2. Adopt Core's colors. Do not override `Nfc*` keys with NFH values at application scope. The cancel icon uses Core's danger text color, which matches the `Button.danger` style in Core's `Theme/ButtonStyles.axaml`. NFH's own `Button.danger` rule paints a red surface, so the adoption images must show that the emphasized cancel icon stays visible.
3. Run the Core Dialogs group above and NFH's full existing test project with `dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build`. Specifically retain `FreeformHelper.Tests.FreeformHelperViewModelTests.ExportDxfLayerImageCommand_WhenDxfMissing_ShowsWarningDialog`, including its command/status assertions. Run it alone with `--filter "FullyQualifiedName~ExportDxfLayerImageCommand_WhenDxfMissing_ShowsWarningDialog"` when diagnosing adoption.
4. Compare supplied title/message/button text, the two DXF tips, `danger` and icon visibility, 360 × 170 dimensions, resize/startup settings, Confirm = true, Cancel = false, and OK closure. Check the Embed DXF, reset-DXF-edits, reset-settings, and warning callers. These dialog results stay identical.
5. Capture before-and-after images on the same OS, fonts, DPI, and theme, using the frozen baseline. NFH UI snapshots may change, including the icons and mapped colors. The adoption PR attaches both images and explains those known visual differences; it retains behavioral expectations rather than refreshing them to accept result changes.

NFH adoption and product image comparisons are outside this extraction task.

## Known differences

- Namespaces, copyright headers, and XML API documentation follow Core conventions. No dialog runtime API adaptation was needed for Avalonia 11.3.12 → 12.0.5; XAML loading, `ShowDialog`, and `Close` retain their behavior. Tests use xUnit v3 and the existing Core headless application.
- The two hard-coded DXF tips become optional constructor arguments, defaulting to null. The original four-string constructor call and optional emphasis argument remain source compatible.
- The cancel font glyph becomes a stroked `Path` using `NfcCloseIconGeometry`, with a 24 × 24 box and a rounded 2-unit stroke instead of a bold font glyph. The warning font glyph becomes an outline `Path` with literal geometry `M10 1L19 18H1Z M10 6V11 M10 14V15`, a 20 × 20 box, a 2-unit stroke, and `NfcWarningAccentBrush`. No icon font, `FontIcon`, or `IconGlyphs` is extracted.
- Core's default palette differs from NFH's. In particular, `BrushWhite` maps to the theme-dependent `NfcDangerTextBrush` so the icon remains visible on Core's danger-button surface; its default color is no longer constant white. NFH adopts Core's colors and reviews the images. Scalar spacing uses dynamic resources instead of NFH's static resources.
- The following NFH values have no matching typed/semantic Core key and remain exact literals. Core has scalar spacing keys, but no corresponding `Thickness` resources for the insets. No new theme keys are added.

| NFH token | Retained literal |
| --- | --- |
| `DialogWidth` | `360` |
| `DialogHeight` | `170` |
| `Inset16` | `16` (uniform padding) |
| `InsetTop8` | `0,8,0,0` |
| `InsetTop14` | `0,14,0,0` |
| `Inset14_8` | `14,8` |
| `FontSizeLg` | `15` |
| `IconSizeLg` | `20` (warning vector box) |
| `ConfirmDialogDangerIconSize` | `24` (cancel vector box) |
| `ConfirmDialogActionMinHeight` | `36` |
| `ConfirmDialogActionHeight` | `40` |

## What stays in NFH

DXF wording, command decisions, settings/reset/export behavior, view models, application theme/base control styles, tooltip styling, product snapshots, and adoption remain in NFH. `CadPadDialog`, `RegularPadDialog`, all other views/controls, the icon font, `FontIcon`, and `IconGlyphs` are outside this module. No source repository files are changed.
