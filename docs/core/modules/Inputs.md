[English](Inputs.md) | [中文](Inputs.zh-TW.md)

# Inputs

`NumberScrubber` is a decimal text input with a plain draggable border and optional Alt-wheel adjustment. Code is in `src/Nvt.Core.Avalonia/Inputs/`, namespace `Nvt.Core.Avalonia.Inputs`. It supports bounds, step snapping, read-only input and mixed values. The drag area has no glyph or icon dependency.

## Public API and use

The eleven original styled properties keep their names, types and defaults. `Value` keeps `BindingMode.TwoWay`; the other properties keep their default binding metadata.

| Property | Type | Default | Behavior |
| --- | --- | --- | --- |
| `Value` | `decimal` | `0m` | Current value; external assignments clamp without snapping. |
| `Minimum` | `decimal` | `decimal.MinValue` | Inclusive lower bound. |
| `Maximum` | `decimal` | `decimal.MaxValue` | Inclusive upper bound. |
| `SmallChange` | `decimal` | `1m` | Text snapping and wheel/drag step size; nonpositive values disable snapping and stepping. |
| `LargeChange` | `decimal` | `10m` | Retained property; the source has no large-step behavior. |
| `FormatString` | `string` | `"0.###"` | Invariant-culture display format; empty or whitespace uses invariant default formatting. |
| `RequireAltForWheel` | `bool` | `true` | Requires Alt for wheel adjustment. |
| `SnapToStep` | `bool` | `true` | Rounds text values to a multiple of `SmallChange`, and wheel/drag step counts to integers, away from zero at midpoints. |
| `ScrubPixelsPerStep` | `double` | `6.0` | Vertical pixels per drag step, with an effective minimum of 1. |
| `IsReadOnly` | `bool` | `false` | Makes the text box read-only, disables drag-area hit testing and rejects wheel input/new drags. |
| `IsMixed` | `bool` | `false` | Displays `*`; focus clears text without changing the value or this flag. |
| `ScrubHint` | `string?` | `null` | New property: drag-area tooltip; null or empty means no tooltip. Whitespace is retained. |

The host includes its normal Avalonia text-box theme, the existing `Theme/ThemeTokens.axaml` resource dictionary, and `Inputs/InputsStyles.axaml` styles, using `avares://Nvt.Core.Avalonia/` resource URIs. The styles retain the original base rules for the control and its `numberScrubber`, `numberScrubberInput`, `numberScrubberSpin` and `focused` classes. NFH's layout-context rules for `panelForm`, `panelFormField` and `settingsPage` stay in NFH. No additional theme keys are required.

Parsing uses `decimal.TryParse` with `NumberStyles.Float` and `CultureInfo.InvariantCulture`. Leading/trailing whitespace, signs and exponents are accepted; thousands separators and culture-specific decimal commas are rejected. Empty, invalid and decimal-out-of-range text leave `Value` unchanged. During editing, each parsable text change normalizes to the step and clamps immediately while keeping the typed text. Enter commits and refreshes text. Escape refreshes text from the current value and keeps all live value updates. Neither key ends editing. Lost focus commits, ends editing and refreshes text. `IsMixed` is never cleared automatically.

Wheel input uses the actual vertical delta, with optional Alt gating; zero vertical delta is ignored. Eligible nonzero wheel events are handled even when the rounded delta is zero or `SmallChange` is nonpositive. Dragging starts on left press, captures the pointer and focuses the text box. Upward motion increases the value from the drag's starting value; horizontal motion has no effect. Release or capture loss ends dragging; the focused class remains if the text box is focused. `LargeChange` is unused for all stepping.

## Frozen provenance

Parent repository: `Dennis40816/nvt-freeform-helper`; ref: `1.3.x`; full commit: `e01e07a361b8dc264a06b3741f40274feeeace2d`. The source uses Avalonia 11.3.12 and xUnit 2; Core uses Avalonia 12.1.1 and xUnit v3. All source reads were from this commit.

Extracted files:

- `src/FreeformHelper.UI/Controls/NumberScrubber.axaml`
- `src/FreeformHelper.UI/Controls/NumberScrubber.axaml.cs`
- `src/FreeformHelper.UI/Controls/NumberScrubber.Properties.cs`
- `src/FreeformHelper.UI/Controls/NumberScrubber.Input.cs`
- `src/FreeformHelper.UI/Controls/NumberScrubber.Scrub.cs`
- `src/FreeformHelper.UI/Controls/NumberScrubber.ValueFormatting.cs`
- `src/FreeformHelper.UI/Styles/Controls.Form.axaml` — the base NumberScrubber and part rules only. Its `panelForm` and `panelFormField` context rules stay in NFH.
- `src/FreeformHelper.UI/Styles/Controls.Settings.axaml` — not extracted. Its `settingsPage` context rules stay in NFH.

Supporting evidence: `src/FreeformHelper.UI/Styles/Tokens.axaml` supplies the frozen literal token values; `tests/FreeformHelper.Tests/UI/Smoke/HeadlessUiSmokeTests.cs` supplies the existing tooltip coverage. The entire frozen test tree was searched for `NumberScrubber`; its only reference is in `RightWorkflowStep3View_TargetCapTooltipUsesCurrentNotchAndEmsCaps`. That test checks the whole-control product tooltip, not the drag-area tooltip. Core ports its generic text/type/open assertions with synthetic text and independently tests `ScrubHint`.

## Style token mappings

Every NFH token mapped to an existing Core token is listed below. All Core resource references use `DynamicResource`; `ThemeTokens.axaml` is unchanged.

| NFH token | Core token |
| --- | --- |
| `BrushBgSurface` | `NfcSurfaceBrush` |
| `BrushBorderMuted` | `NfcBorderMutedBrush` |
| `BrushBgInteractivePressed` | `NfcSelectionSurfaceBrush` |
| `BrushAccent` | `NfcAccentBrush` |
| `BrushBorderStrong` | `NfcBorderBrush` |

## Verification and NFH adoption

Headless characterization tests in `tests/Nvt.Core.Avalonia.Tests/Inputs/` reuse the existing `ThemeTestApplication`. They pin literal values and display strings across `en-US`, `de-DE` and `zh-TW`, signs, exponents, whitespace, rejected separators, invalid/out-of-range input, midpoint snapping, finite bounds and both decimal endpoints. They cover live value-change sequences, Enter/Escape handling, real focus loss, mixed focus, wheel modifiers/fractional deltas, drag pixel steps, real pointer capture/loss, read-only behavior, separate tooltips, and compiled light/dark base styles.

Run with `AVALONIA_TELEMETRY_OPTOUT=1`:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Inputs"
```

At adoption, NFH sets `ScrubHint="Drag or Alt + mouse wheel to adjust"` and adopts Core's colors. It keeps only its own `panelForm`, `panelFormField` and `settingsPage` context styles and attaches before-and-after images. Run NFH's `HeadlessUiSmokeTests` suite, especially `RightWorkflowStep3View_TargetCapTooltipUsesCurrentNotchAndEmsCaps`, `SharedTooltipStyle_CanOpen_Headless`, `SharedTooltipStyle_StringTooltipUsesTooltipForegroundTextBlock_Headless`, and both `FreeformHelperView` layout cases. Reproduce Core's characterization cases against the frozen NFH control and the adopted Core control.

Compare literal `Value`, displayed text and ordered `ValueProperty` changes after each edit and dispatched event. Include `1.24`, `1.25`, `-1.25` at step `0.5`, bounds `-1.3..1.3`, `12.3456` with format `0.###`, both decimal endpoints with steps `1`, `0.1` and `10`, invalid separators, mixed focus, empty input, Enter, Escape and lost focus. Compare wheel deltas `0`, `0.49`, `0.5`, `-0.5`, `1`, `-1`, with/without Alt and both Alt policies; compare drag motions `±3`, `±6`, `±12` pixels at six pixels per step, capture loss, release, read-only and a read-only change during capture. Compare handled flags, focus/classes, capture target, read-only/hit-test states and unchanged mixed flags, including retained overflow exceptions.

Value and event behavior must remain identical. NFH UI snapshots may change due to the theme/font mapping and optional hint. The adoption PR attaches before-and-after images with the same OS, fonts, DPI, theme and control states; images supplement the behavior evidence. Adoption in NFH is outside this extraction.

## Known differences

- Namespace, copyright headers and public XML documentation are Core-specific. The partial-file split and numeric operations are retained.
- Avalonia 12 replaces `GotFocusEventArgs` with `FocusChangedEventArgs`; only the focus-handler parameter type is adapted. The binding-mode reference uses an imported `Avalonia.Data` namespace to avoid collision with `Nvt.Core.Avalonia`.
- The hardcoded product tooltip becomes the single optional `ScrubHint` property. The unused source XAML namespaces are omitted; no NFH icon resources are imported.
- Core's existing colors replace NFH's tokens through the mapping above, so visual snapshots can differ. No matching existing Core token fits the following typed values; they remain literals rather than introducing keys:

| NFH token | Retained literal |
| --- | --- |
| `FormControlHeight` | `30` |
| `BorderThin` | `1` |
| `RadiusMd` | `10` |
| `Inset6_2` | `6,2` |
| `BorderRightThin` | `1,0,0,0` |
| `SpinButtonWidth` | `6` |
| `RadiusRightMd` | `0,10,10,0` |

Source details retained beyond the task summary: normalization and wheel/drag decimal arithmetic can throw `OverflowException` before clamping; no saturating arithmetic is added. A read-only change during a captured drag does not cancel the drag, and programmatic text changes still update a focused read-only input. Formatting while still editing can queue another live parse: Enter or Escape on `12.3456` with snapping disabled and format `0.###` yields value/text `12.346` after queued text events; lost focus ends editing before those events and keeps value `12.3456` with text `12.346`. Bounds and step options are not given new validation.

## What stays in NFH

Product wording, product-specific tooltips, the `panelForm`, `panelFormField` and `settingsPage` context styles, UI snapshot baselines, all other controls and icons, settings and view-model bindings stay in NFH. This module adds no settings abstraction, icon system, other configurable text or adoption changes. No open API questions remain; NFH adoption evidence is still required in its own PR.
