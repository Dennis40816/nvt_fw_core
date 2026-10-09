[English](Focus.md) | [中文](Focus.zh-TW.md)

# Focus

The public attached behaviors in `src/Nvt.Core.Avalonia/Focus/` use namespace `Nvt.Core.Avalonia.Focus`:

- `FocusOnRevealBehavior`: queues Tab focus on visual-tree attachment or when the target's own `IsVisible` becomes true, then checks that it is still opted in, effectively visible/enabled, and focusable.
- `FocusToolTipBehavior`: opens the existing tooltip on Tab/directional focus without transferring focus. Escape and ComboBox selection/dropdown closure close and suppress it; focus loss or pointer exit restores the tooltip service, and pointer entry restores it when the target is unfocused.

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`, Avalonia 12.0.5. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/Behaviors/FocusOnRevealBehavior.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/Behaviors/FocusToolTipBehavior.cs`

Only the namespace and required copyright header differ from those behavior files. Generic assertions were ported from these frozen test paths into `tests/Nvt.Core.Avalonia.Tests/Focus/`:

- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Startup.cs` — `CatalogWarmupUsesAccessibleRetryableForegroundLoadingSurface` attachment/queued-focus contract, exercised at runtime.
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.cs` — `IcDetailTooltipUsesOneNonInteractiveFocusAwareCard` ComboBox tooltip lifecycle assertions, using synthetic choices.
- `tests/NvtFwCombiner.UiSmoke.Tests/OutputDeliverySourceTooltipTests.cs` — `KeyboardFocusOpensDetailsWithoutDuplicateOrModalDismissal` Tab/Shift+Tab, single-open-tooltip, Escape consumption, retained focus and restoration assertions, using synthetic rows.

Additional characterization covers null/default attached properties, deferred availability checks, disabling and re-enabling, ancestor-only reveal, reattachment, navigation methods, pointer restoration and dropdown closure. Product content, styles and dialog/view models remain in NFC.

Core verification after restore:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore -p:UseSharedCompilation=false -m:1 -nr:false
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

For NFC adoption, freeze the UI evidence at the parent commit above. Before and after replacing the duplicate behaviors with Core, build with `--no-restore` and run `dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build`. This includes `XamlControlStyleContractTests` (the contracts listed above and tooltip consumers), `OutputDeliverySourceTooltipTests`, `SupportMatrixInteractionTests` and `StartupFocusTests`. Update only namespace/source-location references required by adoption; retain behavioral assertions and expected evidence. Compare focused elements and traversal order, tooltip open/service state, Escape handling, and the existing screenshots pixel for pixel with identical OS, fonts, DPI and theme. Do not refresh baselines to accept differences. Tool adoption and these product-level comparisons are still pending.

## Tooltip text wrapping and style

`ToolTipTextWrapping.Register()` converts subsequent nonblank string tips into wrapping `TextBlock` controls.
Null, empty, whitespace, and non-string tips stay unchanged.
The handler preserves string bindings with `SetCurrentValue`.
Call it before creating tooltip consumers. Repeated calls install one handler.
`ToolTipTextWrapping.Unregister()` removes the handler for tests and leaves existing text controls intact.
The registration lock protects subscription ownership. Assign tips on the UI thread.

Generated text binds its foreground and maximum width to dynamic resources.
Avalonia uses `Bind` with `DynamicResourceExtension` in the private `SetResourceReference` helper.
An existing tooltip follows theme changes and resource replacements without recreating its text.
`FocusToolTipBehavior` continues to control keyboard opening, Escape, and focus restoration.
Custom control tips keep their own content and local property values.

### Tooltip tokens

`Focus/ToolTipStyles.axaml` owns these tokens and the three tooltip style rules.
The brush aliases use the existing Core palette in separate Light and Dark dictionaries.
The style adds no literal colors and retains the Fluent tooltip template.
It sets the tooltip foreground, descendant text foreground, and template content-presenter foreground.

| Token | Light | Dark |
| --- | --- | --- |
| `Nvt.ToolTip.BackgroundBrush` | `NfcSurfaceBrush` (`#FFFFFF`) | `NfcSurfaceBrush` (`#111827`) |
| `Nvt.ToolTip.BorderBrush` | `NfcBorderSoftBrush` (`#94A3B8`) | `NfcBorderSoftBrush` (`#475569`) |
| `Nvt.ToolTip.ForegroundBrush` | `NfcTextBrush` (`#1E293B`) | `NfcTextBrush` (`#E2E8F0`) |
| `Nvt.ToolTip.CornerRadius` | 8 DIP cap: Pill 8, Square 6 | 8 DIP cap: Pill 8, Square 6 |
| `Nvt.ToolTip.Padding` | 8,4 DIP | 8,4 DIP |
| `Nvt.ToolTip.BorderThickness` | 1 DIP | 1 DIP |
| `Nvt.ToolTip.MaxWidth` | 320 DIP | 320 DIP |

Each corner uses the smaller of `Nvt.Shape.ControlCornerRadius` and `Nvt.ToolTip.CornerRadius`.
Both inputs remain dynamic, so runtime shape changes update existing tooltips.
`MaxWidth` limits both the tooltip and generated text. Padding and borders reduce the available text width.
Applications can override these tokens at their resource root.
Preserve the contrast floor when changing the palette.

| Text contrast against tooltip background | Light | Dark |
| --- | --- | --- |
| `Nvt.ToolTip.ForegroundBrush` on `Nvt.ToolTip.BackgroundBrush` | 14.629:1 | 14.390:1 |

Headless tests calculate sRGB relative luminance and assert at least 4.5:1 in both themes and shapes.
They also cover opening, long text, bound updates, registration cleanup, and live theme, shape, and token changes.
`ToolTipStylesRenderer` writes previews only when `NVT_TOOLTIP_IMAGES_DIR` is set.
It exports `tooltip-light.png`, `tooltip-dark.png`, `tooltip-square-light.png`, and `tooltip-before-light.png` at 1200 pixels wide.

### Load and register

Merge the existing theme tokens into application resources and include the tooltip style after Fluent.
The tooltip style is a separate include. `ThemeTokens.axaml` does not load it.

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://Nvt.Core.Avalonia/Focus/ToolTipStyles.axaml" />
</Application.Styles>
```

After loading application XAML, register before constructing views:

```csharp
using Nvt.Core.Avalonia.Focus;

ToolTipTextWrapping.Register();
```

Existing string tips assigned before registration remain unchanged until assigned again.
Keep `FocusToolTipBehavior.IsEnabled` on controls that need keyboard tooltip access.

### Source baseline and adoption

Source baseline: NFH, ref `origin/main`, commit `d6ceb2afb591162cbafc1e99eaf7891e6c4855e1`.
The extracted source and test ideas come from these files:

- `src/FreeformHelper.UI/Services/SharedToolTipStyleService.cs`.
- The final three tooltip rules in `src/FreeformHelper.UI/Styles/Controls.Overlay.axaml`.
- The tooltip style checks in `tests/FreeformHelper.Tests/UI/Snapshots/UiLayoutGuardTests.cs`.
- The tooltip-open tests in `tests/FreeformHelper.Tests/UI/Smoke/HeadlessUiSmokeTests.cs`.

NFH adoption requires these changes:

1. Load Core theme tokens and `Focus/ToolTipStyles.axaml` after Fluent.
2. Delete `SharedToolTipStyleService` and replace its registration with `ToolTipTextWrapping.Register()`.
3. Delete the three tooltip rules from `Controls.Overlay.axaml`. Keep its dialog button rules.
4. Replace the three brush-key references using this mapping and remove their obsolete local definitions.
5. Capture before-and-after Light and Dark images and run the existing tooltip smoke tests.

| Source brush key | Core token |
| --- | --- |
| `BrushTooltipBackground` | `Nvt.ToolTip.BackgroundBrush` |
| `BrushTooltipBorder` | `Nvt.ToolTip.BorderBrush` |
| `BrushTooltipForeground` | `Nvt.ToolTip.ForegroundBrush` |

Known differences: the source used white background and black text in both themes, a fixed 6 DIP radius, and foreground lookups.
Core uses the themed palette, a live shape radius capped at 8 DIP, dynamic foreground bindings, and a 320 DIP maximum width.
Product adoption and its before-and-after evidence remain pending.
