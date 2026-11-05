# core-unreachable-pressed

Branch: `cloud/core-unreachable-pressed`, started from `origin/fix/console/state-layer` at `84765d31`.
The task named PR 269 head `065bf050`. That commit does not exist in this clone, so the branch tip above was used.

## Changes

- `src/Nvt.Core.Avalonia/Theme/FormStyles.axaml:227`: the selector `TextBox:pressed:not(:disabled), NumericUpDown:pressed:not(:disabled), ComboBox:pressed:not(:disabled)` is now `ComboBox:pressed:not(:disabled)`. The setters are unchanged.
- `src/Nvt.Core.Avalonia/Theme/FormStyles.axaml:236` and `:299` are untouched. The ComboBox open rule and the spinner button rule still use the form pressed tokens, so the tokens stay.
- `tests/Nvt.Core.Avalonia.Tests/Forms/FormStylesTests.cs:87-91`: `FormStatesAndErrorClassResolveTokens` forced `:pressed` on TextBox and NumericUpDown and expected the pressed fill and border. That test relied on the dead selector. It now expects the pressed brushes only for ComboBox. The Pressed state also sets hover, so TextBox and NumericUpDown expect the hover brushes.
- `docs/core/modules/Theme.md:1486` and `docs/core/modules/Theme.zh-TW.md:1485`: both said TextBox has no native `:pressed` and the state sheet forces it to preview the retained rule. Both now say TextBox and NumericUpDown never get `:pressed` on the editable area, and only ComboBox and the spinner buttons use the pressed fill. The task expected the docs to already say this, but they did not.

## Not changed

- `eng/code-health/baseline.json` is untouched, so no entry was added or changed. The edited test method is not in the baseline. The file-level `IDE0005` entry for `FormStylesTests.cs` is keyed on the using directives, which were not edited.
- `FormTokens.axaml` and the form contrast tests still have live consumers and are unchanged.
- `FormsTestHost.SetState` and `FormSheets` still set `:pressed` on the control. That is harmless for ComboBox and the spinner buttons, and no longer previews anything for TextBox and NumericUpDown.
- Theme.md line 1482 ("Controls support rest, hover, pressed or open ...") was left as is.

## What ran

- `dotnet` is not installed in this container, so nothing was built and no tests were run. The edit and the updated test expectation are unverified.
- Searches were done with grep: `:pressed` selectors in the repo, `Form.Pressed*` consumers, and the baseline for the edited files.
- Please run the Forms and Theme tests (`FormStylesTests`, `FormsContrastTests`, `InteractionContrastTests`) and the code-health baseline check locally before adopting.
