[繁體中文](Icons.zh-TW.md)

# Icons

Icons provides 68 named Material Symbols glyphs and an opt-in TextBlock style.
Applications share icon names without copying codepoints or icon styles.
The namespace is `Nvt.Core.Avalonia.Icons` in `Nvt.Core.Avalonia`.

- [`NvtIcons`](../../../src/Nvt.Core.Avalonia/Icons/NvtIcons.cs) provides public string constants for C# and `x:Static`.
- [`IconResources.axaml`](../../../src/Nvt.Core.Avalonia/Icons/IconResources.axaml) provides the same glyphs under `Nvt.Icon.<Name>`.
- [`IconStyles.axaml`](../../../src/Nvt.Core.Avalonia/Icons/IconStyles.axaml) applies the icon font role to `TextBlock.nvtIcon`.

## Setup

Reference `Nvt.Core.Avalonia` and `Nvt.Core.Fonts`.
Follow the [Fonts setup](Fonts.md#tool-adoption), including `WithNvtCoreFonts()`.
Merge both resource dictionaries, then include the icon styles.

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://Nvt.Core.Fonts/FontRoles.axaml" />
      <ResourceInclude Source="avares://Nvt.Core.Avalonia/Icons/IconResources.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
<Application.Styles>
  <StyleInclude Source="avares://Nvt.Core.Avalonia/Icons/IconStyles.axaml" />
</Application.Styles>
```

Merge `FontRoles.axaml` once if the application already uses it.
Include [ButtonStyles.axaml](../../../src/Nvt.Core.Avalonia/Theme/ButtonStyles.axaml) when using the existing `actionIconButton` role.

## Usage

Use a constant in C#:

```csharp
using Avalonia.Controls;
using Nvt.Core.Avalonia.Icons;

var icon = new TextBlock { Text = NvtIcons.Close };
icon.Classes.Add("nvtIcon");
```

Use `x:Static` for a named glyph in XAML.
Give the button a localized accessible name and tooltip.

```xml
<Button xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:icons="using:Nvt.Core.Avalonia.Icons"
        Classes="actionIconButton"
        AutomationProperties.Name="Close"
        ToolTip.Tip="Close">
  <TextBlock Classes="nvtIcon" Text="{x:Static icons:NvtIcons.Close}" />
</Button>
```

The glyph uses `AutomationProperties.AccessibilityView="Raw"`.
Screen readers receive the button's accessible name through the control tree.

Use a resource when the surrounding XAML already uses resource keys:

```xml
<TextBlock Classes="nvtIcon" Text="{StaticResource Nvt.Icon.Settings}" />
<TextBlock Classes="nvtIcon small" Text="{StaticResource Nvt.Icon.Search}" />
<TextBlock Classes="nvtIcon large" Text="{StaticResource Nvt.Icon.Info}" />
```

| Classes | Font size resource | Size |
| --- | --- | ---: |
| `nvtIcon` | `Nvt.Font.Icon.Size` | 16 |
| `nvtIcon small` | `Nvt.Font.Body.Size` | 13 |
| `nvtIcon large` | `Nvt.Font.Title.Size` | 24 |

Every size uses `Nvt.Font.Icon.Family` and `Nvt.Font.Icon.Weight`, which resolves to Normal (400).
The style centers the glyph and disables text trimming.
Foreground inherits from the parent, including button states and theme changes.
The style applies only when the TextBlock has the `nvtIcon` class.

## Icon table

Each constant contains one UTF-16 private-use character.
Each resource uses the same PascalCase name.
Names follow the pinned [upstream codepoint table](https://github.com/google/material-design-icons/blob/737e3324305806514d7909874fa1818ae1808232/variablefont/MaterialSymbolsOutlined%5BFILL%2CGRAD%2Copsz%2Cwght%5D.codepoints).

| Name | Codepoint | Meaning |
| --- | --- | --- |
| `Add` | U+E145 | Add an item |
| `AddCircle` | U+E990 | Add within a group |
| `AreaChart` | U+E770 | Area chart |
| `ArrowDownward` | U+E5DB | Move downward |
| `Block` | U+F08C | Block an action |
| `CallSplit` | U+E0B6 | Split a path |
| `Check` | U+E668 | Confirm or complete |
| `ChevronLeft` | U+E5CB | Navigate left |
| `ChevronRight` | U+E5CC | Navigate right |
| `Close` | U+E5CD | Close or dismiss |
| `Construction` | U+EA3C | Tools and maintenance |
| `ContentCopy` | U+E14D | Copy content |
| `ContentPaste` | U+E14F | Paste content |
| `ContentPasteOff` | U+E4F8 | Disable pasting |
| `CropSquare` | U+E3C6 | Single window outline |
| `DataObject` | U+EAD3 | Structured data |
| `Delete` | U+E92E | Delete an item |
| `Download` | U+F090 | Download content |
| `DriveFileMove` | U+E9A1 | Move a file |
| `Edit` | U+F097 | Edit content |
| `Error` | U+F8B6 | Error status |
| `ExpandLess` | U+E5CE | Collapse content |
| `ExpandMore` | U+E5CF | Expand content |
| `FilterAlt` | U+EF4F | Filter items |
| `FilterAltOff` | U+EB32 | Disable filtering |
| `FilterNone` | U+E3E0 | Overlapping window outlines |
| `FitScreen` | U+EA10 | Fit content to view |
| `Folder` | U+E2C7 | Closed folder |
| `FolderOpen` | U+E2C8 | Open a folder |
| `FormatListBulleted` | U+E241 | Bulleted list |
| `Fullscreen` | U+E5D0 | Enter full screen |
| `FullscreenExit` | U+E5D1 | Exit full screen |
| `History` | U+E8B3 | Recent activity |
| `Info` | U+E88E | Information |
| `Layers` | U+E53B | Layered content |
| `Maximize` | U+E930 | Maximize extent |
| `Menu` | U+E5D2 | Open navigation |
| `Merge` | U+EB98 | Merge paths |
| `Minimize` | U+E931 | Minimize extent |
| `MoreVert` | U+E5D4 | More actions |
| `NotificationsOff` | U+E7F6 | Mute notifications |
| `OpenInNew` | U+E89E | Open another window |
| `Palette` | U+E40A | Choose colors |
| `Pause` | U+E034 | Pause playback |
| `PlayArrow` | U+E037 | Start playback |
| `PushPin` | U+F10D | Pin an item |
| `Refresh` | U+E5D5 | Refresh content |
| `Remove` | U+E15B | Remove an item |
| `Repeat` | U+E040 | Repeat playback |
| `RestartAlt` | U+F053 | Restart an action |
| `Restore` | U+E8B3 | Restore a previous state |
| `RotateRight` | U+E41A | Rotate clockwise |
| `Save` | U+E161 | Save content |
| `Search` | U+EF7A | Search content |
| `Settings` | U+E8B8 | Configure options |
| `Shield` | U+E9E0 | Protection status |
| `Shuffle` | U+E043 | Shuffle order |
| `SkipNext` | U+E044 | Next item or frame |
| `SkipPrevious` | U+E045 | Previous item or frame |
| `Stop` | U+E047 | Stop playback |
| `Straighten` | U+E41C | Measure distance |
| `Troubleshoot` | U+E1D2 | Diagnose a problem |
| `Undo` | U+E166 | Undo an action |
| `Upload` | U+F09B | Upload content |
| `Videocam` | U+E04B | Video content |
| `Visibility` | U+E8F4 | Show content |
| `VisibilityOff` | U+E8F5 | Hide content |
| `Warning` | U+F083 | Warning status |

`History` and `Restore` share the upstream U+E8B3 glyph.
`Restore` depicts history restoration.
Use `CropSquare` for a single window outline and `FilterNone` for overlapping window outlines.

## Adding an icon

1. Choose a Material Symbols name from the pinned upstream table.
2. Convert that name to PascalCase and retain its canonical codepoint.
3. Add the constant, matching `Nvt.Icon.<Name>` resource, and exact source line to [codepoints-subset.txt](../../../tests/Nvt.Core.Avalonia.Tests/Icons/codepoints-subset.txt).
4. Add the name, codepoint, and meaning to both module tables.
5. Keep the public set below 80 names and run the icon tests.
6. Run the documentation checker and update the module lists if needed.

The [icon tests](../../../tests/Nvt.Core.Avalonia.Tests/Icons/IconTests.cs) verify glyph coverage, resource parity, source codepoints, compiled consumers, styling, inheritance, and ordinary text defaults.

```powershell
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter FullyQualifiedName~Icons
python tools/repo-checks/doc_sync.py --repo . --config tools/repo-checks/doc-sync.core.json --base origin/main --head HEAD --all-links
```

## Font source and license

The glyphs use Material Symbols Outlined version 2.973 from `Nvt.Core.Fonts`.
The font is a static instance from upstream commit `737e3324305806514d7909874fa1818ae1808232`.
Its fixed axes are FILL 0, GRAD 0, opsz 24, and wght 400.
The [Fonts module](Fonts.md#frozen-upstream-baseline) records the source and reduction details.

Material Symbols uses Apache License 2.0.
Ship its [LICENSE](../../../src/Nvt.Core.Fonts/licenses/MaterialSymbolsOutlined/LICENSE) and [NOTICE](../../../src/Nvt.Core.Fonts/licenses/MaterialSymbolsOutlined/NOTICE) with the font.
The font package owns those files and the font roles.
