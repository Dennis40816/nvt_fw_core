[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme (`Nvt.Core.Avalonia.Theme`)

Theme preserves the generic NVT FW Combiner (NFC) theme and its eight legacy font values.
The module provides `Theme/ThemeTokens.axaml` and `Theme/ButtonStyles.axaml`.
Merge the resource dictionary into application resources.
It also provides `Theme/ScrollStyles.axaml`. See [Scroll styles](#scroll-styles).
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

## Scroll styles

`Theme/ScrollStyles.axaml` gives every scroll bar the same look, and adds two opt-in classes that keep content inside the viewport width.
The styles change parts of the Fluent `ScrollBar` template, so a tool that includes them must use the Fluent theme. NFC and NFH both do.
Include the file at the position where the tool's own scroll styles were, so that the style order stays the same:

```xml
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml" />
```

What the styles do:

- Every `ScrollViewer`, and every templated control through the attached `ScrollViewer.AllowAutoHide`, keeps its scroll bars visible.
- A vertical bar is 14 px wide, and a horizontal bar is 14 px high. The track is transparent.
- The thumb is 6 px thick, centred in the bar, pill-shaped, and uses `NfcTextDisabledBrush`. It changes to `NfcTextMutedBrush` while the pointer is over the bar or the thumb.
- The track rectangle and the line buttons are fully transparent. The line buttons still take space.
- `ScrollViewer.viewportBoundScroll` stretches its content and removes its padding. Inside it, `Border.viewportBoundContent` takes the viewport width, so the vertical bar never covers or clips the content. Use both with `HorizontalScrollBarVisibility="Disabled"`.

Which rules need the Fluent template:

- The thumb, track rectangle, line button and thumb-border rules select Fluent template parts (`/template/ Thumb`, `/template/ Rectangle#TrackRect`, `/template/ RepeatButton` and the thumb's `Border`). Under another theme they match nothing.
- The bar size, bar background and auto-hide rules set properties on the `ScrollBar` and `ScrollViewer` themselves. They apply under any theme.
- The viewport-bound classes bind to `ScrollViewer.Viewport`. The 14 px lane they leave for the vertical bar comes from the Fluent `ScrollViewer` template.

### Scroll frozen sources and checks

The file is built from two frozen sources, with every selector, setter, value and the order unchanged:

- NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `085f71cfaf9d1f592759d1c58b5bdc4f7b572902`, `src/NvtFwCombiner.Presentation.Avalonia/Styles/MainWindowControlStyles.axaml`, lines 117-185. NFC's `ScrollViewer#SupportMatrixScroll` rule at line 186 is product-specific and stays in NFC.
- NFH (`nvt-freeform-helper`), ref `origin/1.3.x`, full commit `847cc4530ed098ceb56aa1bd8beda77bcd1ec227`, `src/FreeformHelper.UI/Styles/Controls.Scroll.axaml`, lines 12-19 (the two viewport-bound classes only).

Checks:

- `ExtractedXamlMatchesFrozenBaseline` compares the file with `Baseline/ScrollStyles.xml`, which was built from the same source lines.
- `ScrollStylesTests` checks that every resource key exists in both themes, that the viewport-bound classes come last, the bar size and auto-hide setters in both orientations, and auto-hide on scroll viewers and templated controls.
- Under the Fluent template, `ScrollStylesTests` also checks both orientations in Light and Dark: the 14 px lane, the centred 6 px thumb, the hidden track and line buttons, the hover brush, dragging the thumb, and paging with a click on the hidden track. A `ListBox`'s inner scroll viewer also keeps its bars visible. It checks that viewport-bound content takes the viewport width. It also checks that the width settles in the same layout pass each time the vertical bar appears or disappears, and that it does not oscillate.
- The test project references `Avalonia.Themes.Fluent` 12.1.1 for these checks. Only the scroll tests load `FluentTheme`, in their own windows. The packages gain no dependency.
- The same seven runtime cases for NFC's rules passed against a temporary copy of the full frozen NFC file. The copy left out three styles that select an NFC view type, because Core cannot compile them. The copy is not retained.

For zero-difference adoption:

- NFC replaces lines 117-185 with the include at the same position. NFC must stay pixel-identical: run its UI smoke tests, and compare desktop captures before and after with the same OS, fonts, DPI and theme.
- NFH will look different: a 14 px lane with a 6 px thumb replaces its 2.5 px bar. NFH attaches before/after images. Its non-UI tests must keep the same list and outcomes.
- NFH keeps `scrollDevCandidate`, `workspaceDataList`, `workspaceGroupStripScroll` and `DevScrollPreviewHeight`.

Adopters: none yet.
