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
