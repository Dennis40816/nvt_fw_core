# Post-adoption tuning

Edit tokens to tune the controls together. Keep geometry out of style files.

| Control or role | Token file |
| --- | --- |
| CheckBox and RadioButton | `ChoiceTokens.axaml` |
| ListBoxItem and ComboBoxItem | `ListTokens.axaml` |
| MenuItem, ContextMenu and menu separators | `ListTokens.axaml` |
| Expander, ProgressBar, Separator and GridSplitter | `DividerTokens.axaml` |
| ToggleButton roles and ToggleSwitch | `ToggleTokens.axaml` |
| Shared selected colors and exterior or inset focus margins | `ControlTokens.axaml` |
| Shape-dependent corners | `ShapePill.axaml` and `ShapeSquare.axaml` |
| Shared row heights, fonts and focus thickness | `ThemeTokens.axaml` |

These files live in `src/Nvt.Core.Avalonia/Theme`.
The [Theme module](modules/Theme.md) lists defaults for both shapes.

Name new tokens `Nvt.<ControlOrFamily>.<Role>`.
Use a role such as `Nvt.CheckBox.CheckWidth`, not a number such as `Size12`.
Use `Thickness` for padding, margins and borders. Use `x:Double` for individual dimensions.
Put shape-dependent values in both shape dictionaries.
Keep equal defaults when a value does not depend on shape.
Use the existing resource-reference kind for the property. These controls use `DynamicResource` for tunable geometry.
Leave structural zeros, grid positions, single-line limits and stacking order in styles.

Change `Nvt.Focus.RingThickness` in `ThemeTokens.axaml` to change every keyboard focus ring with one value.
Change `Nvt.Controls.FocusRingMargin` for all exterior rings, or `Nvt.Controls.InsetFocusRingMargin` for all inset rings.
Override shared keys at the application resource root to tune every attached instance.
Change family tokens for narrower adjustments. Dynamic resources update without replacing templates.

Keep switch track, knob, travel and focus dimensions aligned when changing switch geometry.
`Nvt.Toggle.SwitchKnobTravel` sets both the knob canvas width and the checked position.
Native `ToggleSwitch` recalculates knob placement when its checked state changes.
`Nvt.Menu.PopupOffset` compensates for popup shadow margins on both axes.
Check submenu offsets when changing popup padding or shadow margins.

After a color change, check the contrast tables in [English](modules/Theme.md) and [Traditional Chinese](modules/Theme.zh-TW.md).
Run the whole Avalonia test project. Its state tests check both themes and both shapes.
Use detailed test output to review the measured contrast ratios.
Update both tables from those measurements.
Keep enabled text at least 4.5:1. Keep disabled text, active indicators and keyboard focus rings at least 3:1.

```text
dotnet build Nvt.Core.sln -warnaserror
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --logger "console;verbosity=detailed"
```

Set each renderer variable to an output directory before rendering.

| Renderer class | Redesign method | Environment variable | Sheets |
| --- | --- | --- | --- |
| `ChoiceStylesRenderer` | `RenderRedesignChoices` | `NVT_CHOICE_IMAGES_DIR` | CheckBox, RadioButton |
| `ListMenuStylesRenderer` | `RenderRedesignListsAndMenus` | `NVT_LIST_IMAGES_DIR` | List, dropdown rows, menu, context menu |
| `DividerStylesRenderer` | `RenderRedesignDividers` | `NVT_DIVIDER_IMAGES_DIR` | Expander, progress bar, splitter, separator |
| `ToggleStylesRenderer` | `RenderRedesignToggles` | `NVT_TOGGLE_IMAGES_DIR` | Switch and toggle roles |

```text
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --filter "FullyQualifiedName~RenderRedesign"
```

The renderers write 45 headless sheets at 100% scale.
They use `<control>-<pill|square>-<light|dark>.png` and `separator.png`.
Without these variables, renderer tests check layout without writing images.
For geometry-only changes, compare decoded pixels against the previous sheets. Every pixel must match.
