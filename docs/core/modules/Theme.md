[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme (`Nvt.Core.Avalonia.Theme`)

Theme preserves the generic NVT FW Combiner (NFC) theme and its eight legacy font values.
The module provides `Theme/ThemeTokens.axaml`, `Theme/ButtonStyles.axaml` and `Theme/ActionRoleStyles.axaml`.
Merge the resource dictionary into application resources.
It also provides `UiResourceResolver` for controls that read theme resources in code. See [Resource resolver](#resource-resolver).
Include the styles at the host's existing button-style scope:

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
```

The module keeps existing `Nfc` resource keys for compatibility. The eight font values also have Core names. The module retains 51 brush keys in each of Light and Dark, 22 common radius/icon/spacing/font compatibility tokens, the semantic button template, and 78 style blocks. Generic roles include semantic, primary, secondary, danger, command, icon/copy, close, inline edit, breadcrumb, action/browse, file reveal, summary chips, and expandable rails with reduced motion. Values, template bindings, selector branches, transitions and cascade order are unchanged.

Excluded token families: `NfcKept`, `NfcReferenceInput`, `NfcControllerInput`, `NfcHex`, `NfcRequired`, `NfcMemory`, `NfcWorkflow`, `NfcWorkspace`, `NfcNav`, and `NfcReport`. Excluded selector branches: `settingsNavItem`, `messageCenterNavigationItem`, `activityFilter`, `sourceEditButton`, `version*`, `slotClearAction`, `outputRailAction`, and `outputNameEdit`. Mixed selectors retain only their generic branches. NFC retains these product resources and branches; reintegration must preserve their original cascade order.

## NFC legacy font extraction

`Theme/NfcLegacyFontTokens.axaml` preserves the eight font resources from NFC's `ThemeTokens.axaml`, lines 218–225, at the frozen commit below.
The dictionary contains exactly two family lists and six sizes in Avalonia device-independent pixels.
`Theme/ThemeTokens.axaml` merges this dictionary and exposes all eight original keys through `StaticResource` aliases.

| Core key | NFC compatibility key | Frozen value |
|---|---|---|
| `Nvt.Font.NfcLegacy.Ui.Family` | `NfcUiFontFamily` | `fonts:Inter#Inter, Microsoft JhengHei UI, Noto Sans CJK TC, Noto Sans TC, Segoe UI` |
| `Nvt.Font.NfcLegacy.Technical.Family` | `NfcTechnicalFontFamily` | `Cascadia Mono, Consolas` |
| `Nvt.Font.NfcLegacy.Size10` | `NfcFontSize10` | `10` |
| `Nvt.Font.NfcLegacy.Size11` | `NfcFontSize11` | `11` |
| `Nvt.Font.NfcLegacy.Size12` | `NfcFontSize12` | `12` |
| `Nvt.Font.NfcLegacy.Size13` | `NfcFontSize13` | `13` |
| `Nvt.Font.NfcLegacy.Size14` | `NfcFontSize14` | `14` |
| `Nvt.Font.NfcLegacy.Size16` | `NfcFontSize16` | `16` |

This extraction adds no fonts, role table, or fallback changes.
The family order and existing Inter registration remain unchanged.
The legacy resources serve existing NFC consumers.

For font-only adoption, NFC merges the Core font dictionary before defining its eight compatibility keys in its own resource dictionary.
Use the mapping above for every alias, as this example shows:

```xml
<ResourceDictionary.MergedDictionaries>
  <ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/NfcLegacyFontTokens.axaml" />
</ResourceDictionary.MergedDictionaries>
<StaticResource x:Key="NfcFontSize14" ResourceKey="Nvt.Font.NfcLegacy.Size14" />
```

NFC must retain `StaticResource NfcFontSize14` in these existing source files:

- `src/NvtFwCombiner.Presentation.Avalonia/Resources/MainWindowSharedTemplates.axaml`
- `src/NvtFwCombiner.Presentation.Avalonia/Resources/SettingsEventBufferFormatPageTemplate.axaml`

NFC must also preserve:

- Existing styles and inline values.
- Font sizes, weights, and inheritance rules.
- Static and dynamic resource lookup modes.
- Font assets, Inter package version, and fallback order.

## Frozen source and checks

Frozen parent: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`.
The full commit freezes the baseline even if the remote ref advances.
Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/Styles/ThemeTokens.axaml`
- `src/NvtFwCombiner.Presentation.Avalonia/Styles/MainWindowButtonStyles.axaml`

Generic assertions are ported from the same commit's:

- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.ThemeTokens.cs`
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Buttons.cs`
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Build.cs`
- `tests/NvtFwCombiner.Architecture.Tests/PresentationBoundaryTests.ShellSurface.cs` (font assertions only)

`tests/Nvt.Core.Avalonia.Tests/Theme/Baseline/*.xml` freezes the generic projection of those source resources. `ExtractedXamlMatchesFrozenBaseline` expands the eight font aliases before comparing the complete frozen XML trees. It pins every key, value, selector, setter, template binding, and transition in order. The baseline files remain unchanged. Characterization tests also check all compiled token values, both palettes, ordinary button geometry, primary/secondary/danger/action states, pointer versus keyboard focus, rail expansion and reduced motion. They preserve NFC's existing primary presenter behavior: later base setters override pressed/disabled presenter colors while descendant text still changes with the state. The same 22 runtime characterization cases passed against temporary copies of the full frozen NFC XAML; those copies are not retained.

`NfcLegacyFontTests` pins each Core font value to NFC's frozen value and checks all compatibility keys in Light and Dark.
It also loads the compiled font dictionary independently.
Compiled `StaticResource` and `DynamicResource` consumers retain their families, sizes, weights, wrapping, and bounds for English, Traditional Chinese, and technical text.
These headless checks do not prove desktop visual equality.

Verify Core with already restored packages:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore --disable-build-servers
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

For zero-difference adoption, run NFC's UI smoke and architecture suites at the frozen parent and after switching to Core.
Adjust source-text checks to follow resource includes and aliases while retaining every expected value.
Include these existing checks:

- `XamlControlStyleContractTests` for theme keys, font values, button states, borders, and reduced motion.
- `PresentationBoundaryTests` for shell font and size contracts.
- `MemorySourcePresentationTests` for the existing static size-14 consumers, English and Traditional Chinese, and disclosure interaction.
- `ReportChangesLayoutTests` and `HomeWorkflowCardVerticalAlignmentDiagnosticTests` for technical and UI font consumers.
- `NavigationFocusIndicatorTests`, `NavigationCheckedStateTests`, and existing modal keyboard-traversal tests.

Compare resolved family lists and all six sizes through both Core and NFC keys.
Also compare brush values, button geometry, presenter geometry, tab order, rail expansion, and reduced-motion behavior.
Compare actual screenshots, line breaks, text bounds, control sizes, and interactions before and after adoption.
Check English and Traditional Chinese, Light and Dark, and the affected hover, pressed, disabled, and keyboard-focus states.
Keep the same OS, installed font versions, font assets, DPI, window size, theme, and reduced-motion setting for each comparison.
Compare desktop captures pixel for pixel and retain the original baselines.
Restore and build NFC through its existing process before running:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build
dotnet test tests/NvtFwCombiner.Architecture.Tests/NvtFwCombiner.Architecture.Tests.csproj --no-build
```

Adopters: none.
NFC integration, product-suite verification, and desktop capture comparison remain pending outside this extraction.
This extraction changes no packages or shared configuration.

## Resource resolver

`UiResourceResolver` reads one theme resource for a control that draws in code. Each method takes the owner control, the key and a fallback value.

| Method | Accepted resource types |
|---|---|
| `GetBrush` | any `IBrush`, or a `Color` passed to the caller's brush factory |
| `GetColor`, `TryGetColor` | `Color`, or the color of a `SolidColorBrush` |
| `GetDouble` | `double`, `float` or `int` |
| `GetCornerRadius` | `CornerRadius` |
| `GetThickness` | `Thickness` |

A lookup has three steps:

1. It searches the owner and its styling parents with the owner's actual theme variant. A control outside any tree has a null variant, so the lookup uses `ThemeVariant.Default`.
2. If that fails and `UiThread.IsCurrent` succeeds, it searches the current application with the same variant.
3. Otherwise it returns the fallback.

A wrong resource type also returns the fallback. With the Default variant, keys that exist only in Light and Dark dictionaries are not found.
Call the resolver on the UI thread. It does not cache values or watch theme changes. A control that caches resolved values must refresh them when its theme variant changes.
Register the UI dispatcher with `UiThread.RegisterRunningDispatcher` at startup. Without the registration, step 2 is skipped.

### Resolver frozen source and checks

Frozen parent: NFH (`Dennis40816/nvt-freeform-helper`), ref `origin/1.3.x`, full commit `847cc4530ed098ceb56aa1bd8beda77bcd1ec227`.
Extracted source path: `src/FreeformHelper.UI/Services/UiResourceResolver.cs`.
The Core version keeps the NFH signatures and behavior. It changes only the namespace, the `UiThread` import, the visibility (public) and the documentation.

`tests/Nvt.Core.Avalonia.Tests/Theme/FrozenNfhUiResourceResolver.cs` keeps a frozen copy of the NFH file. Only its namespace, class name and `UiThread` import differ. Core's `UiThread` is the unchanged port of NFH's.
`UiResourceResolverTests` runs each of its 7 cases against both versions, and both must give the same results:

- Light and Dark for a control in a window, for a window key and an application key.
- Brush resources, brushes built from colors, and the factory call count.
- Colors from `Color` and `SolidColorBrush` only. An `ImmutableSolidColorBrush` returns the fallback.
- Number conversion and fallback for other types.
- Corner radius and thickness types.
- A control outside any tree: a null variant, application top-level keys found, Light and Dark keys not found.
- No registered dispatcher: the application step is skipped.

The test class runs in a collection without parallel tests, because one case clears the shared dispatcher registration.

For zero-difference adoption in NFH:

- Before NFH deletes its copy, the Core tests pass against both versions.
- The full NFH test list and outcomes match the frozen parent.
- NFH's `ui-visual-minimal-baseline.json` hashes and notch golden outputs stay unchanged.
- All NFH callers share one dispatcher registration. Either NFH's `UiThread` calls move to Core's `UiThread`, or NFH registers the dispatcher with both while both exist. About 20 NFH call sites read NFH's own `UiThread`, so registering only with Core's breaks them. Registering only with NFH's skips step 2 without an error.

Adopters: none. NFH adoption is pending.

## Action role styles

`Theme/ActionRoleStyles.axaml` defines the shared button roles for NFC, NFH and NFU. Core owns one definition of their look and behavior, and every tool uses it. A tool does not override these styles locally. When a tool needs a variant, Core adds a role.
The roles come from NFH's generic role classes, templates, states and layout. Their colors map to Core's existing Light and Dark palettes, so this is a token-mapped port with expected visible differences. A shared Core palette is being proposed separately. When the owner approves it, the mapped keys change in Core, and the tools change nothing.
An adopting tool merges `ThemeTokens.axaml` into its resources and includes `ActionRoleStyles.axaml` in its styles:

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ActionRoleStyles.axaml" />
```

Use these two files without `ButtonStyles.axaml`, which sets a global `Button` theme. Each `Button` or `ToggleButton` combines one layout primitive (`actionTextButton`, `actionIconButton`, `actionChip`) with one color role (`actionNeutral`, `actionPrimary`, `actionDanger`, `actionGhost`), or uses `actionChip chipAction`. `chipAction.active` represents selection. Passive badges use `Border.chipStatus`, optionally with `.warning`, `.danger` or `.success`.

The source order is retained. Buttons and toggles cover rest, `:pointerover`, `:pressed` and `:disabled`; toggles also cover `:checked`, `:checked:pointerover` and `:checked:pressed`. The primitives keep the Border plus unnamed ContentPresenter templates, centered content and clipping. Text/chip content keeps no wrapping, one line and character ellipsis. Status chips are passive: their modifiers change border/text colors, with no hover, pressed or checked rules.

### Action frozen source

Source repository: `Dennis40816/nvt-freeform-helper`, ref `origin/1.3.x`, full commit `6fe3c269ac86d7153f57466b56f959f36e86b9c4`. The full SHA pins the source independently of later ref changes. Read source paths:

- `src/FreeformHelper.UI/Styles/Controls.Action.axaml`
- `src/FreeformHelper.UI/Styles/Controls.Tab.axaml` (no additional rules for the retained role classes)
- `src/FreeformHelper.UI/Styles/Tokens.axaml`
- `docs/guides/ui-action-role-system.md`
- `tests/FreeformHelper.Tests/UI/Snapshots/UiLayoutGuardTests.cs` (generic template/layout/state and 4.5:1 contrast checks)

| Role / block | Source lines in `Controls.Action.axaml` at `6fe3c269` |
|---|---|
| `actionTextButton` layout | 5–13 (remove `actionButton` branches) |
| `actionIconButton` layout and templates | 15–65 (retain only the four color-role branches) |
| `actionChip` layout | 67–77 |
| Text/chip templates and text rules | 79–125 (remove `actionButton` branches) |
| `actionNeutral`, including text overrides | 144–210 |
| `actionPrimary`, including text overrides | 212–260 |
| `actionDanger`, including text overrides | 262–309 |
| `actionGhost`, including text overrides | 312–359 |
| Icon color-role overrides | 394–459 |
| `chipAction` and `.active` | 615–650 |
| `ToggleButton.chipAction` checked states | 697–701 |
| `Border.chipStatus` and modifiers/text rules | 704–744 |

The source's focus color setters inside these ranges are removed and replaced with the keyboard ring below. Product branches and `controls|FontIcon` rules are excluded.

### Action color mapping

The table lists every color token referenced by the retained rules, the two removed focus-only tokens, and the source's generic disabled contrast-check token. Several source tokens share a Core key. A source token may use different Core keys for rest, hover, pressed and disabled, following Core's existing state pairs.

| NFH brush / color token | Core key or literal |
|---|---|
| `BrushActionChipBackground` / `ColorActionChipBackground` | `NfcSurfaceSubtleBrush` |
| `BrushActionChipBorder` / `ColorActionChipBorder` | `NfcBorderBrush` |
| `BrushActionChipForeground` / `ColorActionChipForeground` | `NfcTextBrush` (rest); `NfcAccentStrongBrush` (hover/pressed); `NfcTextDisabledBrush` (disabled) |
| `BrushActionChipHover` / `ColorActionChipHover` | `NfcAccentSurfaceBrush` |
| `BrushActionChipPressed` / `ColorActionChipPressed` | `NfcSecondaryActionPressedBrush` |
| `BrushActionConsoleBackground` / `ColorActionConsoleBackground` | `Transparent` literal |
| `BrushActionConsoleCheckedBackground` / `ColorActionConsoleCheckedBackground` | `NfcSelectionSurfaceBrush` |
| `BrushActionConsoleForeground` / `ColorActionConsoleForeground` | `NfcTextBrush` |
| `BrushActionDangerBackground` / `ColorActionDangerBackground` | `NfcDangerSurfaceBrush` |
| `BrushActionDangerBorder` / `ColorActionDangerBorder` | `NfcDangerBorderBrush` |
| `BrushActionDangerForeground` / `ColorActionDangerForeground` | `NfcDangerTextBrush` |
| `BrushActionDangerHover` / `ColorActionDangerHover` | `NfcDangerSurfaceMutedBrush` |
| `BrushActionDangerPressed` / `ColorActionDangerPressed` | `NfcCriticalSurfaceBrush` |
| `BrushActionFocusBackground` / `ColorActionFocusBackground` | Removed focus color setter; no retained reference |
| `BrushActionFocusBorder` / `ColorActionFocusBorder` | Removed focus color setter; no retained reference |
| `BrushActionFocusForeground` / `ColorActionFocusForeground` | `NfcAccentStrongBrush` |
| `BrushActionGhostBackground` / `ColorActionGhostBackground` | `Transparent` literal |
| `BrushActionGhostBorder` / `ColorActionGhostBorder` | `Transparent` literal |
| `BrushActionGhostForeground` / `ColorActionGhostForeground` | `NfcTextSecondaryBrush` |
| `BrushActionGhostHover` / `ColorActionGhostHover` | `NfcAccentSurfaceBrush` |
| `BrushActionGhostPressed` / `ColorActionGhostPressed` | `NfcSecondaryActionPressedBrush` |
| `BrushActionInverseBackground` / `ColorActionInverseBackground` | `NfcAccentSurfaceBrush` |
| `BrushActionInverseForeground` / `ColorActionInverseForeground` | `NfcAccentStrongBrush` |
| `BrushActionInversePressedBackground` / `ColorActionInversePressedBackground` | `NfcSecondaryActionPressedBrush` |
| `BrushActionNeutralBackground` / `ColorActionNeutralBackground` | `NfcSurfaceBrush` |
| `BrushActionNeutralBorder` / `ColorActionNeutralBorder` | `NfcBorderBrush` |
| `BrushActionNeutralForeground` / `ColorActionNeutralForeground` | `NfcTextBrush` (rest); `NfcAccentStrongBrush` (hover/pressed) |
| `BrushActionNeutralHover` / `ColorActionNeutralHover` | `NfcAccentSurfaceBrush` |
| `BrushActionNeutralPressed` / `ColorActionNeutralPressed` | `NfcSecondaryActionPressedBrush` |
| `BrushActionPrimaryBackground` / `ColorActionPrimaryBackground` | `NfcAccentSurfaceBrush` |
| `BrushActionPrimaryBorder` / `ColorActionPrimaryBorder` | `NfcAccentBorderLightBrush` |
| `BrushActionPrimaryForeground` / `ColorActionPrimaryForeground` | `NfcAccentStrongBrush` |
| `BrushActionPrimaryHover` / `ColorActionPrimaryHover` | `NfcAccentSurfaceSubtleBrush` |
| `BrushActionPrimaryPressed` / `ColorActionPrimaryPressed` | `NfcAccentSurfaceBrush` |
| `BrushActionSelectedBackground` / `ColorActionSelectedBackground` | `NfcSelectionSurfaceBrush` |
| `BrushActionSelectedBorder` / `ColorActionSelectedBorder` | `NfcAccentBorderBrush` |
| `BrushActionSelectedForeground` / `ColorActionSelectedForeground` | `NfcTextStrongBrush` |
| `BrushActionTextHoverBackground` / `ColorActionTextHoverBackground` | `NfcAccentSurfaceBrush` |
| `BrushActionTextHoverBorder` / `ColorActionTextHoverBorder` | `NfcAccentBorderBrush` (hover); `NfcAccentBorderStrongBrush` (pressed) |
| `BrushActionTextHoverForeground` / `ColorActionTextHoverForeground` | `NfcAccentStrongBrush` |
| `BrushActionTextPressedBackground` / `ColorActionTextPressedBackground` | `NfcSecondaryActionPressedBrush` |
| `BrushBorderStrong` / `ColorBorderStrong` | `NfcAccentBorderBrush` (hover); `NfcAccentBorderStrongBrush` (pressed) |
| `BrushButtonDisabledForeground` / `ColorButtonDisabledForeground` | `NfcTextDisabledBrush` (generic NFH contrast check only) |
| `BrushButtonNeutralBackground` / `ColorButtonNeutralBackground` | `NfcSurfaceBrush` |
| `BrushButtonNeutralBorder` / `ColorButtonNeutralBorder` | `NfcBorderBrush` (rest); `NfcAccentBorderBrush` (hover); `NfcAccentBorderStrongBrush` (pressed) |
| `BrushButtonNeutralForeground` / `ColorButtonNeutralForeground` | `NfcTextBrush` (rest); `NfcAccentStrongBrush` (hover/pressed); `NfcTextDisabledBrush` (danger disabled setter) |
| `BrushButtonNeutralHover` / `ColorButtonNeutralHover` | `NfcAccentSurfaceBrush` |
| `BrushButtonNeutralPressed` / `ColorButtonNeutralPressed` | `NfcSecondaryActionPressedBrush` |
| `BrushDanger` / `ColorDanger` | `NfcDangerTextBrush` |
| `BrushStatusChipBackground` / `ColorStatusChipBackground` | `NfcSurfaceSubtleBrush` |
| `BrushStatusChipBorder` / `ColorStatusChipBorder` | `NfcBorderBrush` |
| `BrushStatusChipForeground` / `ColorStatusChipForeground` | `NfcTextBrush` |
| `BrushSuccess` / `ColorSuccess` | `NfcSuccessTextBrush` |
| `BrushTextMuted` / `ColorTextMuted` | `NfcTextDisabledBrush` |
| `BrushTransparent` | `Transparent` literal |
| `BrushWarning` / `ColorWarning` | `NfcWarningTextBrush` |

No retained NFH color key is unmapped. `BrushActionFocusBackground` and `BrushActionFocusBorder` are intentionally removed with the focus color setters. `BrushActionFocusForeground` remains in ghost hover/pressed rules. The four transparent source keys use the `Transparent` literal; no new color or size token is added apart from the specified focus resources.

### Action focus ring

Each layout primitive and color role sets `FocusAdorner` to null, disabling the default focus rectangle. Only `:focus-visible` supplies a ring through `FocusAdorner`. Pointer focus has no ring. Keyboard focus preserves background, border and foreground, including checked or active colors.

`Styles.Resources` in `ActionRoleStyles.axaml` owns these tokens:

| Token | Light | Dark |
|---|---|---|
| `Nvt.Focus.RingBrush` | `#4DA3FF` | `#4DA3FF` |
| `Nvt.Focus.DangerRingBrush` | `#FF6B6B` | `#FF6B6B` |
| `Nvt.Focus.RingThickness` | `2` (shared Thickness) | `2` (shared Thickness) |

Only `actionTextButton.actionDanger` and `actionIconButton.actionDanger` use the red ring, for both control types. All other combinations use blue, including `actionChip actionDanger`. The Border has `IsHitTestVisible="False"`, `AdornerLayer.IsClipEnabled="False"` and literal `Margin="-4"`: a 2 px border with a 2 px gap outside the button. Text buttons use Core's compact radius 6 and ring radius 8; icon buttons and chips keep the pill radius through `NfcPillCornerRadius`.

Using sRGB relative luminance, against Core's window background (`NfcAppBackgroundBrush`):

| Theme / background | Blue ring contrast | Red ring contrast |
|---|---:|---:|
| Light / `#F4F6FA` | 2.4266:1 | 2.5649:1 |
| Dark / `#0B1220` | 7.1312:1 | 6.7469:1 |

The Light contrast remains an owner question. It is part of the shared palette proposal. The selected colors are retained until then.

### Action known differences and checks

Literal source sizes are retained: `FormControlHeight` and `IconButtonSize` = 30; `Inset10_6` = `10,6`; `Inset8_4` = `8,4`; `InsetNone` and `BorderNone` = 0; `BorderControl` = 1.5; `OpacitySubtle` = 0.56; `WorkspaceSummaryChipMaxWidth` = 240. `RadiusPill` maps to `NfcPillCornerRadius` (999). The text primitive uses `NfcCompactCornerRadius` (6) rather than depending on a host radius; its ring adds the 2 px gap. Ring radius 8 and margin -4 are focus geometry literals. No retained rule sets a font family or size, so the port adds no Fonts project reference; source font weights remain unchanged.

The source cascade is preserved even where it differs from the general role guide:

- The later text-neutral rest rule overrides the earlier disabled foreground setter, so `actionTextButton actionNeutral` keeps `NfcTextBrush` when disabled.
- `.active` chip colors override earlier hover/pressed/disabled colors; disabled opacity is still 0.56.
- Checked text primary/danger/ghost buttons use the later text hover/pressed rules during interaction. Text-neutral checked buttons keep selected colors. Checked icon hover and pressed both use the mapped inverse hover pair.

`Baseline/ActionRoleStyles.xml` freezes the mapped projection and ring. `ExtractedXamlMatchesFrozenBaseline` compares the complete XML tree; the existing ThemeTokens and ButtonStyles baselines are unchanged. `ThemeContractTests` resolves references against Light and Dark, including the style-local focus dictionaries.

`ActionRoleStylesTests` loads only ThemeTokens and ActionRoleStyles. Headless checks cover both palettes and control types, state colors on controls/templates/text, active and checked cascades, dimensions/radii/clipping, passive status modifiers, real Tab traversal versus pointer focus, the live ring's brush/geometry/hit testing, the disabled default focus rectangle, and unchanged colors on focus. Every opened window closes in `finally`; there are no sleeps or network calls.

The generic NFH interactive text-pair check meets 4.5:1 in Light and Dark. The separate source disabled-neutral token-pair check fails for `NfcSurfaceBrush` / `NfcTextDisabledBrush`: Light **2.5640:1**, Dark **3.7277:1**. `DisabledNeutralTextContrastIsDocumentedException` pins these two values as a documented exception; colors and the 4.5:1 threshold for interactive text are unchanged.

Run with restored packages:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

Build: zero warnings and errors. Avalonia suite: 350 passed, 0 failed, 0 skipped, 350 total.

### Action adoption in NFC, NFH and NFU

Adoption is outside this port. Each tool adopts the roles in its own PR, includes the two files above, and removes its local button-role styles. A tool must not override the Core keys to recover its old palette. Each adoption PR attaches before/after images of the tool's main surfaces, with one image showing Tab focus, for the owner to approve the changed look.

NFH: Dark neutral text buttons visibly change from NFH's light surface (`#F4F7FC`) to Core's dark surface (`#111827`), and the keyboard focus ring appears. Other role colors follow the table, including icon inverse hover mapped to Core's secondary state pair.

These stay in NFH for now, because they are product-specific: `consoleHeaderAction`, `viewportOverlayAction`, `panelChromeToggle`, `dxfEditMiniAction`, `dangerTextButton`, the CAD-specific `chipAction` modifiers (`direct`, `transfer`, `linked`, `hidden`, `geometry`, `combined`, `duplicate`, `layer`, `incoming`, `outgoing`, `nocad`, `legacy`), tab styles, the unused `actionButton`, and the `controls|FontIcon` rules at source lines 127–141. Core adds no FontIcon control.

NFH's images cover its three rendered surfaces and the Dev page role matrix. NFH's non-UI test list and outcomes remain the same. Adoption, desktop images and owner approval are still pending.
