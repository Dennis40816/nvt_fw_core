[English](Theme.md) | [中文](Theme.zh-TW.md)

# Theme (`Nvt.Core.Avalonia.Theme`)

The generic NFC theme is available as `Theme/ThemeTokens.axaml` and `Theme/ButtonStyles.axaml`. Merge the resource dictionary into application resources and include the styles at the host's existing button-style scope:

```xml
<ResourceInclude Source="avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml" />
<StyleInclude Source="avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml" />
```

Resource keys keep their `Nfc` names for compatibility. The module retains 51 brush keys in each of Light and Dark, 22 common radius/icon/spacing/font tokens, the semantic button template, and 78 style blocks. Generic roles include semantic, primary, secondary, danger, command, icon/copy, close, inline edit, breadcrumb, action/browse, file reveal, summary chips, and expandable rails with reduced motion. Values, template bindings, selector branches, transitions and cascade order are unchanged.

Excluded token families: `NfcKept`, `NfcReferenceInput`, `NfcControllerInput`, `NfcHex`, `NfcRequired`, `NfcMemory`, `NfcWorkflow`, `NfcWorkspace`, `NfcNav`, and `NfcReport`. Excluded selector branches: `settingsNavItem`, `messageCenterNavigationItem`, `activityFilter`, `sourceEditButton`, `version*`, `slotClearAction`, `outputRailAction`, and `outputNameEdit`. Mixed selectors retain only their generic branches. NFC retains these product resources and branches; reintegration must preserve their original cascade order.

Frozen parent: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/Styles/ThemeTokens.axaml`
- `src/NvtFwCombiner.Presentation.Avalonia/Styles/MainWindowButtonStyles.axaml`

Generic assertions are ported from the same commit's:

- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.ThemeTokens.cs`
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Buttons.cs`
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Build.cs`

`tests/Nvt.Core.Avalonia.Tests/Theme/Baseline/*.xml` freezes the generic projection of those source resources. `ExtractedXamlMatchesFrozenBaseline` compares the complete XML trees, pinning every key/value, selector, setter, template binding and transition in order. Characterization tests also check all compiled token values, both palettes, ordinary button geometry, primary/secondary/danger/action states, pointer versus keyboard focus, rail expansion and reduced motion. They preserve NFC's existing primary presenter behavior: later base setters override pressed/disabled presenter colors while descendant text still changes with the state. The same 22 runtime characterization cases passed against temporary copies of the full frozen NFC XAML; those copies are not retained.

Verify Core with already restored packages:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore --disable-build-servers
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

For zero-difference adoption, first run NFC's `NvtFwCombiner.UiSmoke.Tests` at the frozen parent, then rerun the same suite after switching its generic resource/style includes to Core. This includes `XamlControlStyleContractTests` (theme keys, button states, borders and reduced motion), `NavigationFocusIndicatorTests`, `NavigationCheckedStateTests`, and the existing modal keyboard-traversal tests. Compare resolved brush values, button/presenter bounds and geometry, hover/pressed/disabled/focus states, tab order and Light/Dark desktop captures. Keep OS, font assets (including the existing Inter setup), DPI, window size, theme and reduced-motion settings identical; compare the original captures pixel for pixel and never refresh them to accept differences. Restore/build the tool beforehand using its existing process, then run:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build
```

Adopters: none. NFC integration, full product-suite verification and desktop capture comparison remain pending outside this extraction; no packages or shared configuration were changed.
