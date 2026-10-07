[English](Icons.md)

# Icons

Icons 提供 68 個具名 Material Symbols 字形與選用的 TextBlock 樣式。
應用程式可共用圖示名稱，不必複製碼位或圖示樣式。
命名空間為 `Nvt.Core.Avalonia.Icons`，位於 `Nvt.Core.Avalonia`。

- [`NvtIcons`](../../../src/Nvt.Core.Avalonia/Icons/NvtIcons.cs) 提供 C# 與 `x:Static` 使用的公開字串常數。
- [`IconResources.axaml`](../../../src/Nvt.Core.Avalonia/Icons/IconResources.axaml) 以 `Nvt.Icon.<Name>` 提供相同字形。
- [`IconStyles.axaml`](../../../src/Nvt.Core.Avalonia/Icons/IconStyles.axaml) 將圖示字型角色套用至 `TextBlock.nvtIcon`。

## 設定

參照 `Nvt.Core.Avalonia` 與 `Nvt.Core.Fonts`。
依照 [Fonts 設定](Fonts.zh-TW.md#工具採用方式) 完成字型設定，包括 `WithNvtCoreFonts()`。
合併兩個資源字典，再載入圖示樣式。

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

應用程式若已使用 `FontRoles.axaml`，只需合併一次。
使用既有 `actionIconButton` 角色時，另載入 [ButtonStyles.axaml](../../../src/Nvt.Core.Avalonia/Theme/ButtonStyles.axaml)。

## 使用方式

在 C# 使用常數：

```csharp
using Avalonia.Controls;
using Nvt.Core.Avalonia.Icons;

var icon = new TextBlock { Text = NvtIcons.Close };
icon.Classes.Add("nvtIcon");
```

在 XAML 使用 `x:Static` 取得具名字形。
按鈕應提供在地化的無障礙名稱與工具提示。

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

字形採用 `AutomationProperties.AccessibilityView="Raw"`。
螢幕閱讀器透過控制項樹取得按鈕的無障礙名稱。

周圍 XAML 若已使用資源鍵，可改用圖示資源：

```xml
<TextBlock Classes="nvtIcon" Text="{StaticResource Nvt.Icon.Settings}" />
<TextBlock Classes="nvtIcon small" Text="{StaticResource Nvt.Icon.Search}" />
<TextBlock Classes="nvtIcon large" Text="{StaticResource Nvt.Icon.Info}" />
```

| Classes | 字型大小資源 | 大小 |
| --- | --- | ---: |
| `nvtIcon` | `Nvt.Font.Icon.Size` | 16 |
| `nvtIcon small` | `Nvt.Font.Body.Size` | 13 |
| `nvtIcon large` | `Nvt.Font.Title.Size` | 24 |

所有大小皆使用 `Nvt.Font.Icon.Family` 與 `Nvt.Font.Icon.Weight`，字重解析為 Normal（400）。
樣式將字形置中，並停用文字裁切。
前景色繼承自父層，包含按鈕狀態與主題切換。
樣式只套用至具有 `nvtIcon` 類別的 TextBlock。

## 圖示表

每個常數包含一個 UTF-16 私用區字元。
資源使用相同的 PascalCase 名稱。
名稱依照固定版本的[上游碼位表](https://github.com/google/material-design-icons/blob/737e3324305806514d7909874fa1818ae1808232/variablefont/MaterialSymbolsOutlined%5BFILL%2CGRAD%2Copsz%2Cwght%5D.codepoints)。

| 名稱 | 碼位 | 意義 |
| --- | --- | --- |
| `Add` | U+E145 | 新增項目 |
| `AddCircle` | U+E990 | 新增群組項目 |
| `AreaChart` | U+E770 | 區域圖 |
| `ArrowDownward` | U+E5DB | 向下移動 |
| `Block` | U+F08C | 封鎖動作 |
| `CallSplit` | U+E0B6 | 分割路徑 |
| `Check` | U+E668 | 確認或完成 |
| `ChevronLeft` | U+E5CB | 向左導覽 |
| `ChevronRight` | U+E5CC | 向右導覽 |
| `Close` | U+E5CD | 關閉或取消 |
| `Construction` | U+EA3C | 工具與維護 |
| `ContentCopy` | U+E14D | 複製內容 |
| `ContentPaste` | U+E14F | 貼上內容 |
| `ContentPasteOff` | U+E4F8 | 停用貼上 |
| `CropSquare` | U+E3C6 | 單一視窗外框 |
| `DataObject` | U+EAD3 | 結構化資料 |
| `Delete` | U+E92E | 刪除項目 |
| `Download` | U+F090 | 下載內容 |
| `DriveFileMove` | U+E9A1 | 移動檔案 |
| `Edit` | U+F097 | 編輯內容 |
| `Error` | U+F8B6 | 錯誤狀態 |
| `ExpandLess` | U+E5CE | 收合內容 |
| `ExpandMore` | U+E5CF | 展開內容 |
| `FilterAlt` | U+EF4F | 篩選項目 |
| `FilterAltOff` | U+EB32 | 停用篩選 |
| `FilterNone` | U+E3E0 | 重疊視窗外框 |
| `FitScreen` | U+EA10 | 將內容符合檢視範圍 |
| `Folder` | U+E2C7 | 關閉的資料夾 |
| `FolderOpen` | U+E2C8 | 開啟資料夾 |
| `FormatListBulleted` | U+E241 | 項目符號清單 |
| `Fullscreen` | U+E5D0 | 進入全螢幕 |
| `FullscreenExit` | U+E5D1 | 離開全螢幕 |
| `History` | U+E8B3 | 近期活動 |
| `Info` | U+E88E | 資訊 |
| `Layers` | U+E53B | 圖層內容 |
| `Maximize` | U+E930 | 最大化範圍 |
| `Menu` | U+E5D2 | 開啟導覽 |
| `Merge` | U+EB98 | 合併路徑 |
| `Minimize` | U+E931 | 最小化範圍 |
| `MoreVert` | U+E5D4 | 更多動作 |
| `NotificationsOff` | U+E7F6 | 靜音通知 |
| `OpenInNew` | U+E89E | 在另一個視窗開啟 |
| `Palette` | U+E40A | 選擇色彩 |
| `Pause` | U+E034 | 暫停播放 |
| `PlayArrow` | U+E037 | 開始播放 |
| `PushPin` | U+F10D | 釘選項目 |
| `Refresh` | U+E5D5 | 重新整理內容 |
| `Remove` | U+E15B | 移除項目 |
| `Repeat` | U+E040 | 重複播放 |
| `RestartAlt` | U+F053 | 重新開始動作 |
| `Restore` | U+E8B3 | 還原先前狀態 |
| `RotateRight` | U+E41A | 順時針旋轉 |
| `Save` | U+E161 | 儲存內容 |
| `Search` | U+EF7A | 搜尋內容 |
| `Settings` | U+E8B8 | 設定選項 |
| `Shield` | U+E9E0 | 保護狀態 |
| `Shuffle` | U+E043 | 隨機排序 |
| `SkipNext` | U+E044 | 下一項目或影格 |
| `SkipPrevious` | U+E045 | 上一項目或影格 |
| `Stop` | U+E047 | 停止播放 |
| `Straighten` | U+E41C | 測量距離 |
| `Troubleshoot` | U+E1D2 | 診斷問題 |
| `Undo` | U+E166 | 復原動作 |
| `Upload` | U+F09B | 上傳內容 |
| `Videocam` | U+E04B | 影片內容 |
| `Visibility` | U+E8F4 | 顯示內容 |
| `VisibilityOff` | U+E8F5 | 隱藏內容 |
| `Warning` | U+F083 | 警告狀態 |

`History` 與 `Restore` 共用上游 U+E8B3 字形。
`Restore` 表示還原歷史狀態。
單一視窗外框使用 `CropSquare`，重疊視窗外框使用 `FilterNone`。

## 新增圖示

1. 從固定版本的上游碼位表選擇 Material Symbols 名稱。
2. 將名稱轉成 PascalCase，保留其標準碼位。
3. 新增常數、對應的 `Nvt.Icon.<Name>` 資源，以及 [codepoints-subset.txt](../../../tests/Nvt.Core.Avalonia.Tests/Icons/codepoints-subset.txt) 中的原始資料列。
4. 在兩份模組文件的表格加入名稱、碼位與意義。
5. 保持公開名稱少於 80 個，並執行圖示測試。
6. 執行文件檢查；必要時更新模組清單。

[圖示測試](../../../tests/Nvt.Core.Avalonia.Tests/Icons/IconTests.cs) 驗證字形覆蓋、資源一致性、來源碼位、編譯的使用範例、樣式、繼承與一般文字預設值。

```powershell
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter FullyQualifiedName~Icons
python tools/repo-checks/doc_sync.py --repo . --config tools/repo-checks/doc-sync.core.json --base origin/main --head HEAD --all-links
```

## 字型來源與授權

字形使用 `Nvt.Core.Fonts` 的 Material Symbols Outlined 2.973。
字型為上游提交 `737e3324305806514d7909874fa1818ae1808232` 產生的靜態實例。
固定軸值為 FILL 0、GRAD 0、opsz 24 與 wght 400。
[Fonts 模組](Fonts.zh-TW.md#固定的上游基準) 記錄來源與縮減細節。

Material Symbols 採用 Apache License 2.0。
發佈字型時，附上其 [LICENSE](../../../src/Nvt.Core.Fonts/licenses/MaterialSymbolsOutlined/LICENSE) 與 [NOTICE](../../../src/Nvt.Core.Fonts/licenses/MaterialSymbolsOutlined/NOTICE)。
字型套件負責維護這些檔案與字型角色。
