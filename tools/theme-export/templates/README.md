# Nvt.Core.Theme

Avalonia theme tokens, control styles and an icon name table. Light and dark palettes, shared radii and sizes, a keyboard focus ring and state styles for common controls.

License: MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## What is in this repository

| Part | Files | Use |
| --- | --- | --- |
| Tokens | `Theme/ThemeTokens.axaml` and the `*Tokens.axaml` files it merges | Colors, radii, sizes and focus ring. Light and dark |
| Styles | `Theme/*Styles.axaml` | Button, toggle, choice, form, list, menu, tab, expander, divider, scroll, text and progress |
| Shapes and rest fills | `Theme/ShapePill.axaml`, `ShapeSquare.axaml`, `RestFill*.axaml`, `ThemeShapes`, `ThemeRestFills` | Pill or square corners. Soft or no rest fill |
| Icons | `Icons/IconResources.axaml`, `Icons/IconStyles.axaml`, `NvtIcons` | 72 icon names mapped to Material Symbols code points |
| Font roles | `Fonts/FontRoles.axaml` | Names of the font roles. **No font files are included** |

Some resource keys use the prefix `Nfc` (for example `NfcAccentBrush`).

## Fonts

This package ships no font files. `Fonts/FontRoles.axaml` and `Theme/NfcLegacyFontTokens.axaml` only name the families they expect: Inter, Cascadia Mono, Noto Sans TC and Material Symbols Outlined, with Noto Sans CJK TC, Microsoft JhengHei UI, Segoe UI and Consolas as fallbacks. Get the fonts from their upstream projects or from your system, register them in your application, and keep their licenses. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The icon style needs Material Symbols Outlined.

Font addresses and registration:

- `Nvt.Font.NfcLegacy.Ui.Family` starts with `fonts:Inter#Inter`. A `fonts:` address needs the matching font collection to be registered by your application, for example `WithInterFont()` of the `Avalonia.Fonts.Inter` package, which has its own license. If it is not registered, Avalonia falls back to the next family in the list.
- The other roles name a plain family, such as `Inter` or `Material Symbols Outlined`. A plain name resolves only when the font is installed on the system. To use a font that your application bundles, override the role key in your application resources with an `avares://<your assembly>/<folder>#<family>` address.
- Without the Material Symbols Outlined font, the icon text shows as empty boxes or blank space.

## Use

Load the tokens as application resources and the styles after `FluentTheme`:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://Nvt.Core.Theme/Fonts/FontRoles.axaml" />
      <ResourceInclude Source="avares://Nvt.Core.Theme/Theme/ThemeTokens.axaml" />
      <ResourceInclude Source="avares://Nvt.Core.Theme/Icons/IconResources.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/ButtonStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/ToggleStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/ChoiceStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/FormStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/ListStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/MenuStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/TabStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/ExpanderStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/DividerStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/ProgressStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/TextStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Theme/ScrollStyles.axaml" />
  <StyleInclude Source="avares://Nvt.Core.Theme/Icons/IconStyles.axaml" />
</Application.Styles>
```

A button needs a role class, for example `Classes="actionGhost"`. A button without a role class falls back to Fluent. The seven `NfcAccent*` keys hold the accent colors of the primary button, progress bar, dividers, splitter preview and choice borders. Override them in application-scope Light and Dark resources to change these. The selected colors (`Nvt.Controls.Selected*`), the focus ring (`Nvt.Focus.RingBrush`) and the toggle colors (`Nvt.Toggle.*`) have their own keys and do not follow the accent. Override them too if you want one accent everywhere.

## Status and versions

The version is `0.1.0`. This repository is licensed under MIT, see [LICENSE](LICENSE). Issues are welcome. No support is promised.

The target framework is `net8.0` and `net10.0`. The package depends on Avalonia 12.1.1.
