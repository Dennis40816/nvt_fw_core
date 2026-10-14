# Third-party notices

These notices cover the files under the MIT License: `src/Nvt.Core.Avalonia/Theme`, `src/Nvt.Core.Avalonia/Icons` and `src/Nvt.Core.Fonts/FontRoles.axaml`. See [LICENSE-COMPONENTS.txt](LICENSE-COMPONENTS.txt), which the packages carry as `LICENSE` and which repeats the Avalonia text below. They refer to the following works.

## Avalonia (MIT)

The code depends on the `Avalonia` package. The control styles load after the Avalonia Fluent theme. Several control templates are modelled on the structure and the template part names of the Avalonia Fluent templates, such as `PART_ItemsPresenter`. Treat these style files as works that may contain portions of Avalonia. The Avalonia license text follows.

```text
The MIT License (MIT)

Copyright (c) AvaloniaUI OÜ
All Rights Reserved

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- Source of the license text: https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/licence.md

## Material Symbols (Apache License 2.0)

`Icons/IconResources.axaml` and `Icons/NvtIcons.cs` list 72 icon names and the Unicode code points of the Material Symbols Outlined font. The names and code points follow the upstream code point table of Google's `material-design-icons` repository at commit `737e3324305806514d7909874fa1818ae1808232` (font version 2.973). One entry differs: `Cancel` uses U+E5C9. U+E5C9 is a legacy alias of U+E888 and renders the same glyph in the font.

- Source: https://github.com/google/material-design-icons
- License: Apache License 2.0, https://www.apache.org/licenses/LICENSE-2.0
- The font is a separate work and is not under the MIT License. Its license and notice are in `src/Nvt.Core.Fonts/licenses/MaterialSymbolsOutlined/`.

## Font families named in the font role table

`src/Nvt.Core.Fonts/FontRoles.axaml` and `src/Nvt.Core.Avalonia/Theme/NfcLegacyFontTokens.axaml` name these families. Each family has its own license.

| Family | License | Upstream | Font file |
| --- | --- | --- | --- |
| Inter | SIL Open Font License 1.1 | https://github.com/rsms/inter | `Nvt.Core.Fonts` (via `Avalonia.Fonts.Inter`) |
| Cascadia Mono | SIL Open Font License 1.1 | https://github.com/microsoft/cascadia-code | `Nvt.Core.Fonts` |
| Noto Sans TC | SIL Open Font License 1.1 | https://fonts.google.com/noto/specimen/Noto+Sans+TC | `Nvt.Core.Fonts` |
| Material Symbols Outlined | Apache License 2.0 | https://github.com/google/material-design-icons | `Nvt.Core.Fonts` |
| Noto Sans CJK TC | SIL Open Font License 1.1 | https://github.com/notofonts/noto-cjk | not included |

The license texts of the included font files are in `src/Nvt.Core.Fonts/licenses/`.

`NfcLegacyFontTokens.axaml` also lists three Windows system fonts as fallbacks: Microsoft JhengHei UI, Segoe UI and Consolas. They are not included and not redistributed. They are proprietary Microsoft fonts that come with Windows and have their own license terms.
