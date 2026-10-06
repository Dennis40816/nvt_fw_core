[English](Fonts.md) | [繁體中文](Fonts.zh-TW.md)

# Fonts

`Nvt.Core.Fonts` provides font role resources and an embedded Chinese fallback for `net10.0` and Avalonia 12.1.1.
The library is the Fonts module. Its namespace is `Nvt.Core.Fonts`.
It provides no styles, controls, icon name constants, or additional font registrations.

## Owner decisions

The owner approved the weekly Core scope on 2026-10-05.
On 2026-10-06, the owner selected a separate font package with its own version.
On the same day, the owner chose Normal (400) for Numbers. Cascadia Mono has no official static Medium (500) file.
Tool repositories therefore avoid storing the font files again with each Core release.

- Use only Inter, Cascadia Mono, Noto Sans TC, and Material Symbols Outlined.
- Use sizes 11, 13, 16, and 24.
- Embed fonts with pinned versions and static weights.
- Never ship Windows system fonts.
- Give each role one family, one size, and one weight. Each tool style applies all three together.
- Keep NVT FW Combiner's (NFC) legacy font resources in the Theme module unchanged.

Packaging follows in a separate pull request. It adds the package version, the pack step, and the license files in the package.

## Roles and resource keys

[`FontRoles.axaml`](../../../src/Nvt.Core.Fonts/FontRoles.axaml) contains the following roles.
Sizes use Avalonia device-independent pixels.

| Role | Use | Family | Size | Weight |
| --- | --- | --- | ---: | --- |
| Title | Page, modal, and launcher titles | Inter | 24 | SemiBold (600) |
| Heading | Section, panel, and card titles | Inter | 16 | SemiBold (600) |
| Body | Body text, descriptions, ordinary inputs, and options | Inter | 13 | Normal (400) |
| Caption | Secondary text, metadata, and notes | Inter | 11 | Normal (400) |
| Mono | Code, hex, addresses, IDs, and version strings | Cascadia Mono | 13 | Normal (400) |
| Numbers | Counts, progress, and statistics | Cascadia Mono | 13 | Normal (400) |
| BodyStrong | Primary action text, emphasis, and selected state | Inter | 13 | SemiBold (600) |
| CaptionStrong | Table headers, field labels, and status badges | Inter | 11 | SemiBold (600) |
| MonoStrong | Emphasized technical values and selected or changed hex | Cascadia Mono | 13 | SemiBold (600) |
| MonoCaption | Technical metadata and compact raw values | Cascadia Mono | 11 | Normal (400) |
| Icon | Glyph icons | Material Symbols Outlined | 16 | Normal (400) |

Use Mono for IDs and hex. Use Numbers for counts.
Every role has these three keys, with `<Role>` replaced by its exact name above:

| Key | Resource type | Meaning |
| --- | --- | --- |
| `Nvt.Font.<Role>.Family` | `FontFamily` | One family URI |
| `Nvt.Font.<Role>.Size` | `x:Double` | Role size |
| `Nvt.Font.<Role>.Weight` | `FontWeight` | Role weight |
| `Nvt.Font.Fallback.Cjk.Family` | `FontFamily` | Noto Sans TC fallback family |

Each family resource contains one family. No resource contains a fallback list.

| Family | Resource value |
| --- | --- |
| Inter | `avares://Avalonia.Fonts.Inter/Assets#Inter` |
| Cascadia Mono | `avares://Nvt.Core.Fonts/Assets/CascadiaMono#Cascadia Mono` |
| Noto Sans TC | `avares://Nvt.Core.Fonts/Assets/NotoSansTC#Noto Sans TC` |
| Material Symbols Outlined | `avares://Nvt.Core.Fonts/Assets/MaterialSymbolsOutlined#Material Symbols Outlined` |

The Inter asset URI resolves without `WithInterFont()`.
The package uses that URI and registers no Inter font collection.

## Frozen upstream baseline

This module uses upstream font assets. It extracts no NFC or NVT FW UTIL (NFU) code.
The following repository refs identify the upstream baseline. The asset hashes below pin the actual released bytes.

| Repository | Ref or version | Full commit SHA | Source paths |
| --- | --- | --- | --- |
| [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia/tree/e33eaed9c106846b200680751022385d9cc5dc6f/src/Avalonia.Fonts.Inter/Assets) | `Avalonia.Fonts.Inter` 12.1.1 | `e33eaed9c106846b200680751022385d9cc5dc6f` | `src/Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf`, `Inter-SemiBold.ttf` |
| [microsoft/cascadia-code](https://github.com/microsoft/cascadia-code/releases/tag/v2407.24) | `v2407.24` | `56bcca3f2c1e4cb19458954f0e2bb4635960df91` | `CascadiaCode-2407.24.zip`: `ttf/static/CascadiaMono-Regular.ttf`, `CascadiaMono-SemiBold.ttf` |
| [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk/releases/tag/Sans2.004) | `Sans2.004` | `523d033d6cb47f4a80c58a35753646f5c3608a78` | `19_NotoSansTC.zip`: `NotoSansTC-Regular.otf`, `NotoSansTC-Bold.otf` |
| [google/material-design-icons](https://github.com/google/material-design-icons/tree/737e3324305806514d7909874fa1818ae1808232/variablefont) | Font version 2.973 | `737e3324305806514d7909874fa1818ae1808232` | `variablefont/MaterialSymbolsOutlined[FILL,GRAD,opsz,wght].ttf` and its `.codepoints` file |

Inter remains in the pinned `Avalonia.Fonts.Inter` dependency. Core does not copy its files.
That package contains static Thin, Light, Regular, Medium, SemiBold, and Bold files.
The roles use its Regular and SemiBold files.
The [Inter font license](https://github.com/rsms/inter/blob/v3.19/LICENSE.txt) is SIL Open Font License 1.1.
The Avalonia package metadata declares MIT for the package code.

All paths in the following table start at `src/Nvt.Core.Fonts/`.

| Asset path | Family name ID 1 / 16 | Weight | Bytes | SHA-256 | Version and source | License |
| --- | --- | ---: | ---: | --- | --- | --- |
| `Assets/CascadiaMono/CascadiaMono-Regular.ttf` | Cascadia Mono | 400 | 575912 | `06520d032ec274fa5040b22c6f4a1d829081b24ba40b2da56dae89bf10c7b481` | v2407.24, release archive above | SIL OFL 1.1, unmodified |
| `Assets/CascadiaMono/CascadiaMono-SemiBold.ttf` | Cascadia Mono SemiBold / Cascadia Mono | 600 | 581840 | `8e04c1b811913a20773a3761d8994f15efe3029509c8d0556d74ae989558146d` | v2407.24, release archive above | SIL OFL 1.1, unmodified |
| `Assets/NotoSansTC/NotoSansTC-Regular.otf` | Noto Sans TC | 400 | 5683368 | `5bab0cb3c1cf89dde07c4a95a4054b195afbcfe784d69d75c340780712237537` | Sans2.004, release archive above | SIL OFL 1.1, unmodified |
| `Assets/NotoSansTC/NotoSansTC-Bold.otf` | Noto Sans TC | 700 | 5839972 | `55420b259eb119bf5f2a0aadba10cf9d736c12d64ab93e78546d69ef5f43558b` | Sans2.004, release archive above | SIL OFL 1.1, unmodified |
| `Assets/MaterialSymbolsOutlined/MaterialSymbolsOutlined-Regular.ttf` | Material Symbols Outlined | 400 | 1397116 | `e90300193fd701f4a4eb88d292699ce3b4237863c3f297da0eda866dfb415347` | 2.973, upstream commit above, static instance | Apache 2.0, modified |

## Known limits measured on 2026-10-06

- Avalonia 12.0.5 and 12.1.1 render variable fonts at their default instance and ignore the weight axis.
- Requests for a missing heavier weight can produce synthetic bold. This package ships static weights only.
- Cascadia Mono has no official static Medium (500) file. The first role table set Numbers to 500. The owner chose Normal (400) on 2026-10-06 for this reason. Numbers renders without simulation.
- Noto Sans TC has no static SemiBold (600) file. Chinese text in an Inter 600 role uses Noto Sans TC Bold (700), without simulation.
- Chinese text in an Inter 400 role uses Noto Sans TC Regular (400).
- Material Symbols provides only outlined, unfilled icons at weight 400, grade 0, and optical size 24 px.

The Icon role displays that fixed optical-size instance at size 16.
The upstream `.codepoints` file maps `home` to U+E9B2. The older code point U+E88A maps to the same glyph, and the text `home` shapes to that single glyph.
The Inter SemiBold file reports `Inter SemiBold` as its legacy family name. The requested family remains Inter.

## Tool adoption

1. Reference `Nvt.Core.Fonts` with the version selected by the integrator.
2. Merge `FontRoles.axaml` into the application's resources.
3. Call `WithNvtCoreFonts()` on the application's builder.
4. Apply each role's family, size, and weight in the tool's own styles.

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://Nvt.Core.Fonts/FontRoles.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

```csharp
using Nvt.Core.Fonts;

builder.WithNvtCoreFonts();
```

`WithNvtCoreFonts()` replaces `FontManagerOptions`.
It sets `FontFallbacks` to a list containing `NvtCoreFonts.CjkFallback`.
Avalonia tries Noto Sans TC for missing Chinese characters before system fonts.
If the tool owns its options, add the fallback to those options instead:

```csharp
options.FontFallbacks = [NvtCoreFonts.CjkFallback, .. (options.FontFallbacks ?? [])];
builder.With(options);
```

Place the Chinese fallback before other tool fallbacks. Keep the tool's other option values.
The following style belongs to the adopting tool:

```xml
<Style Selector="TextBlock.body">
  <Setter Property="FontFamily" Value="{StaticResource Nvt.Font.Body.Family}" />
  <Setter Property="FontSize" Value="{StaticResource Nvt.Font.Body.Size}" />
  <Setter Property="FontWeight" Value="{StaticResource Nvt.Font.Body.Weight}" />
</Style>
```

NFC keeps its legacy Theme resources.
Moving NFC to these roles requires a separate pull request with before and after images.
The owner approves the resulting look before adoption.

## Tests and zero-difference verification

[`FontTests`](../../../tests/Nvt.Core.Fonts.Tests/FontTests.cs) uses headless Avalonia with real Skia drawing.
The test application merges the dictionary and calls `AvaloniaTestHost.Build<FontsTestApplication>().WithNvtCoreFonts()`.
An isolated host also checks Inter without `WithInterFont()`.

- Check every resource value and each role's selected static font file.
- Check glyph typeface family names, weights, and absence of font simulations.
- Check Chinese fallback through a measured `TextBlock` and its shaped text runs.
- Check U+E9B2, its older alias U+E88A, and the single-glyph `home` ligature.
- Check each embedded asset's byte count and SHA-256 through `AssetLoader`.

Tests do not hash license text files. Checkout can change their line endings.
Run these commands with packages already restored:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Fonts.Tests/Nvt.Core.Fonts.Tests.csproj --no-build
dotnet test Nvt.Core.sln --no-build
```

For a tool's equivalent font substitution, run its existing resource, text layout, rendering, and screenshot tests before and after adoption.
Keep the same font bytes, Avalonia version, OS, DPI, theme, and sample text.
Compare selected font files, glyph IDs, weights, simulations, measurements, line breaks, and rendered pixels.
Include Latin text, Traditional Chinese, numbers, technical values, and icons.
For NFC, also run `AvaloniaApplicationResourceTests`, `StartupFocusTests`, `NavigationFocusIndicatorTests`, and its existing capture tests.
Do not refresh a baseline to hide differences.
Changing NFC's legacy look to these roles requires the separate owner review described above.
This task does not claim visual equivalence for that future change.

## License duties and updates

Ship the complete [`licenses`](../../../src/Nvt.Core.Fonts/licenses) folder with the tool.
List all four fonts in the tool's third-party notices.
Keep the Inter font license and the Avalonia dependency notices with the distribution.
Cascadia Mono and Noto Sans TC use SIL Open Font License 1.1 and remain unmodified.
Material Symbols uses Apache License 2.0.
Its [`NOTICE`](../../../src/Nvt.Core.Fonts/licenses/MaterialSymbolsOutlined/NOTICE) records the static-instance modification.

When updating a font:

1. Download the font from its source page above. Select static weights for Cascadia Mono and Noto Sans TC.
2. Record the version, repository ref, full commit SHA, source paths, byte counts, and new SHA-256 values.
3. For Material Symbols, rerun the exact command from NOTICE with its recorded fontTools version and `SOURCE_DATE_EPOCH`.
4. Check the fixed axes, family names, font weights, and icon ligatures. Update NOTICE if the source or command changes.
5. Update the hash tests and both module documents. Run the font tests and the solution tests.
6. Keep license files and third-party notices current. The integrator handles package publication in the packaging work.
