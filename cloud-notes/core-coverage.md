# Nvt.Core.Avalonia: control and test coverage

Analysis only. Snapshot: `origin/main` at `0286a27` (2026-11-05). No source or test file was changed, and no test was run. Every cell below comes from reading the style and test code.

## Method and limits

- **Inventory.** Every style or template file under `src/Nvt.Core.Avalonia` (`Theme/`, `Focus/`, `Inputs/`, `Panels/`, `Primitives/`, `Progress/`, `Dialogs/`, `Icons/`, `LogConsole/`, `ReportList/`) and every custom control class in them. The test project has 157 `.cs` files. It has 733 `[AvaloniaFact]`/`[AvaloniaTheory]` attribute lines and 222 `[Fact]`/`[Theory]` lines (non-UI tests included).
- **Three kinds of test.**
  - Contract tests: no literal colors or corners, resources resolve, transitions exist, runtime shape and dictionary replacement. Files: `Choice/Toggle/ListMenu/DividerContractTests`, `ThemeContractTests`, `PanelsStylesTests`, `ToolTipStylesTests`.
  - Contrast tests: `PaletteContrastTests` (all three tool accents, light and dark), `InteractionContrastTests`, `FormsContrastTests`, plus contrast checks inside the `*StylesTests` files.
  - Headless render tests: `*StylesRenderer`, `RedesignSheets`, `FormSheets`.
- **The render tests are not visual regression tests.** They check that every control sits inside the host bounds and that `CaptureRenderedFrame()` returns a frame of the expected width. They write PNG files only when an `NVT_*_IMAGES_DIR` variable is set. No control family compares an image or hash against a baseline. The only pixel hash in the project is `AvaloniaRenderingTests`, and it covers Inter text only.
- **Cell values.**
  - `C` covered: a test asserts the painted or behavioral result for that state.
  - `P` partly: only token-level, only one variant of several, only a structural check, or only through another control's test.
  - `N` not covered, though the style defines the state.
  - `–` the style has no such state, so none is expected.
  - `†` the state is set with a forced pseudo-class instead of real pointer input, but the painted values are asserted. This applies to the Button and Tab hover and pressed cells.
- **"Selected"** means `:selected`, `:checked`, `IsChecked` or `IsExpanded`.
- **App usage is from Core's own documents, not from app code.** This session could only read `nvt_fw_core`, so NFC, NFH and NFU were not inspected. The sources are `ROADMAP.md`, `docs/components.md`, the `Theme.md` role-mapping and adopter tables, and `CHANGELOG.md`. "Documented" means a Core document names the app as a user or adopter. "Planned" means only the owner decision of 2026-10-06 applies (one shared style per basic control across apps). To get real counts, run a search for the control classes and element names in the three app repositories before acting on the ranking.

## Matrix 1: control styles (`Theme/`, `Focus/`)

Columns: **Re** rest, **Ho** hover, **Pr** pressed, **Di** disabled, **Fo** focus, **Se** selected, **RM** reduced motion.

| Control and style | Apps (documented + planned) | Tests | Re | Ho | Pr | Di | Fo | Se | RM | Gaps |
|---|---|---|---|---|---|---|---|---|---|---|
| `Button`/`ToggleButton` roles `actionPrimary`, `actionNeutral`, `actionDanger`, `actionGhost`, `actionGhost.flat(.active)`, `actionIconButton`, `chipAction(.active)` | NFC, NFH, NFU (role maps for 59, 105 and 22 classes) | `ButtonThemeTests`, `ThemeContractTests`, `ControlTuningTests`, `PaletteContrastTests`, `InteractionContrastTests` | C | C† | C† | C | C (real Tab and pointer) | C (checked × hover, pressed, disabled) | C | `:flyout-open` is **N** here; it is only touched by one Console test. Label-on-fill ratio per painted state is **P** (token pairs only). |
| `Border.chipStatus` (+ `danger`, `success`, `warning`) | NFH | `ButtonThemeTests.StatusChipsKeepPassiveColorsAndLayout` | C | – | – | C | – | – | – | none |
| `ToggleButton` `toggleSwitch`/`ToggleSwitch`, `toggleSegment` (+ group), `toggleTab`, `toggleIcon`, `toggleSoft` (+ `danger`) | NFH documented; NFC and NFU planned | `Toggle*Tests`, `ToggleStylesRenderer`, `ToggleSoftRenderer`, `InteractionContrastTests` | C | C | C | C | C | C | **N** | The style file has no `reducedMotion` rule, although it sets 150 ms brush transitions and a knob slide. `ResourcesResolveAndMotionUsesTransitions` only pins that the transitions exist. This is a source gap as well as a test gap. |
| `CheckBox`, `RadioButton` (+ `compact`) | NFC, NFH | `Choice*Tests`, `RestFillTests`, `ControlRedesignTests` | C | C | C | C | C | C (checked, indeterminate) | C | none |
| `TextBox` (+ `formEmbedded`, multiline, `error`, read-only) | NFH, NFC (overlap noted), NFU planned | `FormStylesTests`, `FormsContrastTests`, `FormStylesRenderer` | C | C | C | C | C | – | P (`TextBox` only) | none beyond RM |
| `NumericUpDown` + `RepeatButton.formSpin` | same | same | C | C (body) | C (body) | C | C (ring follows spinner placement) | – | **N** | `RepeatButton.formSpin` hover, pressed and disabled fills are **P** (native spinning is tested, painted spin fills are not). The style file has `reducedMotion` rules for `NumericUpDown` and `formSpin`; no test asserts them. |
| `ComboBox` (+ editable, `dropdownopen`) | same | same | C | C | C (pressed and open) | C | C | – | **N** | `reducedMotion` rule exists, not asserted. |
| `TabControl`/`TabItem` (4 strip placements) | NFH, NFU (Settings tab list), NFC planned | `TabStylesTests`, `FormsContrastTests`, `FormStylesTests.ReducedMotion…` | C | C† | C† | C | C | C (× hover, pressed, disabled) | C | none |
| `ListBox`, `ListBoxItem`, `ComboBoxItem` (+ `compact`) | NFC, NFH, NFU | `ListMenuStylesTests`, `ListMenuContractTests`, `RestFillTests` | C | C | C | C | C | C | C | none |
| `Menu`, `MenuItem`, `ContextMenu`, menu `Separator` | NFC, NFH, NFU | `ListMenuStylesTests`, `ListMenuContractTests` | C | C | C | C | C | P | C | `MenuItem:open` parent fill: only popup geometry is asserted. `MenuFlyoutPresenter` style is exercised only by Console tests. |
| `Expander` (+ `section`, up, left, right) | NFH, others planned | `DividerStylesTests`, `RestFillTests`, `DividerContractTests` | C | C | C | C | C | C (expanded) | C | none |
| `ProgressBar` (+ `thin`, `thick`, vertical, indeterminate) | NFC, NFH, NFU (Progress module) | `DividerStylesTests`, `DividerContractTests`, `ProgressIndicatorTests` | C | – | – | **P** | – | – | C | `ProgressBar:disabled` has a token contrast check and a render-sheet row; the painted indicator is not asserted. |
| `GridSplitter` | NFH, others planned | `DividerStylesTests`, `DividerContractTests` | C | C | C | C | C | – | C | none |
| `Separator`, `Border.divider` (+ `strong`, `vertical`) | all | `DividerStylesTests.SeparatorAndBorderDividersMatch` | C | – | – | – | – | – | – | none |
| `ScrollBar`, `ScrollViewer`, `viewportBound*` classes | NFC, NFH documented; NFU planned | `ScrollStylesTests`, `PaletteContrastTests` (thumb tokens) | C | C (real pointer) | – | – | – | – | – | No render sheet. No disabled style exists. |
| Text classes `title` … `monoCaption`, `muted` | all | `TextStylesTests`, `FormsContrastTests` | C | – | – | – | – | – | – | none |
| `ToolTip` + string wrapping, `FocusToolTipBehavior`, `FocusOnRevealBehavior` | NFC, NFH | `ToolTipStylesTests`, `ToolTipTextWrappingTests`, `Focus*BehaviorTests` | C | – | – | – | C | – | – | none |
| `TextBlock.nvtIcon` (+ `small`, `large`) | all | `IconTests` | C | – | – | C (inherits foreground) | – | – | – | none |

## Matrix 2: custom controls and templates

| Control | Apps | Tests | Re | Ho | Pr | Di | Fo | Se | RM | Gaps |
|---|---|---|---|---|---|---|---|---|---|---|
| `NumberScrubber` (`Inputs/`) | NFH (source of the control, adoption pending); `CHANGELOG.md` records no inspected dependency in NFC, NFH or NFU | `NumberScrubberTests.*` (6 files, about 38 tests) | C | **N** | **N** | **N** | C (`focused` class and painted fill) | – | – | Behavior is thorough. The style defines no hover, pressed or disabled look. It uses a hard-coded `CornerRadius="10"` (the frozen NFH `RadiusMd`, see `Inputs.md`), so it ignores `ThemeShape`, and it uses `Nfc*` tokens instead of `Nvt.Form.*`. |
| `CollapsiblePanel` (`Panels/`) | NFH | `CollapsiblePanelTests`, `PanelsStylesTests` | C | C | C | P (`IsEnabled` only) | P (inherits the `actionGhost` ring) | C (expanded) | **N** | The host window sets `reducedMotion` only to make tests deterministic. |
| `WorkspaceShell`, `SectionFrame`, `InfoLabel`, `BalancedWrapPanel` | NFH | one test file each | C (layout) | – | – | – | – | – | – | Layout and template only. |
| `ConfirmDialog`, `WarningDialog` | NFH | `ConfirmDialogTests`, `WarningDialogTests` | C | – | – | – | – | – | – | Buttons inherit the action roles, so states belong to Matrix 1. |
| `LoadingSurface`, `ProgressIndicator` | NFC, NFH, NFU | `LoadingSurfaceTests`, `ProgressIndicatorTests` | C | – | – | – | C (focus on attach and reveal) | – | C | none |
| `ReportPager` templates | NFC | `ReportPagerTemplateTests`, `ReportPager*GeometryTests` | C | – | – | C (endpoints) | – | – | C | Buttons inherit the action roles. |
| Console: `ConsoleListView`, `ConsoleRowPresenter` | NFH | 49 files in `LogConsole/` | C | C (link hover) | – | C (disabled contrast group) | C (ring inside viewport) | **P** | – | Selection is covered as behavior and automation state. The painted selected-row fill has no assertion. |
| Console: toolbar, header, empty state, classes `consoleAction`, `consoleLevelItem`, `consoleSearchFlag`, `consoleSearch`, `consoleToggle` | NFH | `ConsoleToolbarTests`, `ConsoleInteractionStateTests`, `ConsoleResourceTests` | C | P | **N** | P | P | P | P (two buttons) | None of these style classes is named in any test. `ConsoleInteractionStateTests` pins flyout-open, checked-fill and no-fill-when-disabled through the toolbar. |

Out of scope, with no control styles: `MessageCenter`, `Shell/PageHost`, `RuntimeQuery`, `Threading`, `Testing`, and the `ReportList` view models.

## Cross-cutting facts

- Shape (Pill and Square) is covered for Button, Toggle, Choice, Form, Tab, List, Menu, Expander, ProgressBar and GridSplitter through the `*ShapeChangesAtRuntime` tests and `*Renderer` sheets. Dark and light are covered everywhere except where noted.
- Reduced motion has a `.reducedMotion` rule in nine style files (Button, Form, Expander, Choice, List, Menu, Tab, Progress, Divider). Toggle and Scroll have none. Scroll sets no transitions, so it does not need one. Toggle does need one.
- `ButtonThemeTests` and `TabStylesTests` set `:pointerover` and `:pressed` directly. Real pointer input is used for Button focus, Toggle, Choice, Form, List and Menu.

## Ranked gaps

Ranking key: documented apps first, then planned apps, then how many defined states are `N` or `P`.

| # | Gap | Apps | States affected |
|---|---|---|---|
| 1 | No baseline image or hash for any rendered state sheet. Painted regressions that no assertion names go unnoticed. | all 3 | all |
| 2 | `Button` roles: `:flyout-open` is not tested in the Theme tests. | NFC, NFH, NFU | pressed-like open state |
| 3 | `Button` roles: contrast of the label on the painted fill is not measured per state, including the disabled fill. Only token pairs are checked. | NFC, NFH, NFU | hover, pressed, disabled, checked |
| 4 | `ProgressBar:disabled`: the painted indicator is not asserted. | NFC, NFH, NFU | disabled |
| 5 | `Menu`/`MenuFlyoutPresenter`: the `:open` parent fill and the flyout presenter style have no Theme-level assertion. | NFC, NFH, NFU | selected-like open state |
| 6 | Form controls: reduced motion is asserted only for `TextBox`. `NumericUpDown`, `ComboBox` and `formSpin` have rules but no assertion. Spin button hover, pressed and disabled fills are not asserted. | NFH, NFC; NFU planned | reduced motion, hover, pressed, disabled |
| 7 | Toggle family: no `reducedMotion` rule although the style animates. | NFH; NFC, NFU planned | reduced motion |
| 8 | `NumberScrubber`: no hover, pressed or disabled look and no test for them. It ignores `ThemeShape`. | NFH | hover, pressed, disabled |

Remaining gaps, not in the top 8: Console toolbar style classes (NFH only); `CollapsiblePanel` disabled and reduced motion (NFH only); selected-row fill in the Console list (NFH only); Tab and Button hover and pressed with real pointer input (low risk, the selectors are Avalonia built-ins).

## Test plan for the top 8

All new UI tests follow `docs/core/testing.md`: `[AvaloniaFact]` or `[AvaloniaTheory]`, a shared headless session, no `Task.Delay`, PascalCase names, one behavior per test, and a negative case where a policy test could pass vacuously (rule 12). Reuse `FormsTestHost`, `ChoiceTestHost.Contrast` and `ResourceColor`, `ToggleTestHost`, and `DividerTestHost`.

1. **Rendered baseline (gap 1).** Decision needed from the owner first: `docs/components.md` lists NFH's average-hash snapshot check (candidate 8) and NFC's exact pixel capture as the two options, and says NFH's Apply mode should be removed or locked before extraction. Proposal: one `RenderedSheetBaselineTests` class that renders the existing state sheets (`FormSheets`, `ToggleStylesRenderer`, `DividerStylesRenderer`, `ListMenuStylesRenderer`, `ChoiceStylesRenderer`) at scale 1 with the embedded fonts and compares a 16×16 average hash with a Hamming distance limit, with hashes in a committed JSON file. Negative case: change one state token in a copy of the resources and assert the check fails. Effort: L. Risk: font and rendering differences across OS, so run it only on the CI Windows runner and mark it `[Trait("Category", "Slow")]` with reason `ui-render`.
2. **Button `:flyout-open` (gap 2).** Add `FlyoutOpenMarksTheRoleAsActive` to `ButtonThemeTests`. For each role in the selector (`actionGhost`, `actionNeutral`, `chipAction`, `actionIconButton`), attach a `Flyout`, open it, and assert the painted background, border and foreground equal the documented active values, in both themes, and that `actionPrimary` and `actionDanger` are unaffected. Negative case: a closed flyout keeps the rest values. Effort: S.
3. **Button painted contrast (gap 3).** Add `EveryPaintedStateMeetsContrastFloors` to `ButtonThemeTests`. Reuse `SetState`, then compute the ratio of `Foreground` over `Background` for each role × state × theme × control type: 4.5:1 for enabled text, 3:1 for disabled text and for border-over-surface. Negative case: override one fill with a low-contrast color and assert the helper fails. Effort: S.
4. **ProgressBar disabled (gap 4).** Add `DisabledBarPaintsTheDisabledIndicator` to `DividerStylesTests`. Set `IsEnabled = false` on determinate and indeterminate bars, for thin, default and thick, in both themes and shapes. Assert `PART_Indicator` uses `Nvt.Progress.DisabledIndicatorBrush` and the track keeps its fill. Effort: S.
5. **Menu open and flyout presenter (gap 5).** Add `OpenSubmenuParentKeepsTheOpenFill` and `MenuFlyoutPresenterUsesThePopupSurface` to `ListMenuStylesTests`. First: open a submenu with `IsSubMenuOpen = true` and assert the parent body fill equals the documented open fill while the pointer is elsewhere. Second: show a `MenuFlyout` and assert the presenter background, border, corner radius and padding use the menu tokens, including after a runtime shape change. Effort: S to M.
6. **Form reduced motion and spinner fills (gap 6).** Extend `ReducedMotionDisablesFormAndTabTransitions` into a theory over `TextBox`, `NumericUpDown`, `ComboBox` and `RepeatButton.formSpin`, on the control and on an ancestor, and assert transitions return when the class is removed. Add `SpinnerButtonsPaintHoverPressedAndDisabled` that moves the real pointer over `PART_IncreaseButton` and `PART_DecreaseButton` and asserts the painted fills against the `formSpin` tokens, both spinner placements. Effort: M.
7. **Toggle reduced motion (gap 7).** Two steps. (a) Source change, which needs owner sign-off because it is a style addition: add `.reducedMotion` selectors to `ToggleStyles.axaml` that clear the brush transitions, the `PART_MovingKnobs` transition and `ToggleSwitch.KnobTransitions`, following the pattern in `ChoiceStyles.axaml`. (b) Tests: add `ReducedMotionRemovesToggleTransitions` to `ToggleContractTests`, a theory over every role and `ToggleSwitch`, on the control and on an ancestor, asserting `Transitions`, `KnobTransitions` and the knob grid's transitions are null and return when the class is removed. This test fails on current `main`, so it doubles as the regression test (rule 11). Effort: S for tests, S for the style.
8. **NumberScrubber states (gap 8).** Decide first whether hover, pressed and disabled looks are wanted; `Inputs.md` records the current look as frozen NFH values, so this may be intentional. If yes: add the styles using `Nvt.Form.*` tokens and `ThemeShape`, then `DisabledScrubberUsesTheDisabledPalette` and `HoverAndPressedShowOnlyOnTheScrubArea` in `NumberScrubberTests.Display`, plus a shape test next to `ControlRedesignTests`. If no: add one test that pins the current look (rest, focused, disabled equals enabled) so the choice is visible. Effort: M.

Suggested order by value per effort: 2, 3, 4, 7, 5, 6, 8, then 1 after the owner decision.
