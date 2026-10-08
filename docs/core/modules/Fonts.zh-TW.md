[English](Fonts.md) | [繁體中文](Fonts.zh-TW.md)

# Fonts

## 0.9.0 前的不相容變更

`NvtCoreFonts.CjkFallback` 每次存取都回傳新的 `FontFallback`。每個 builder 或 options 物件只讀取一次並重複使用該實例，不要在迴圈內讀取。
每個 builder 都取得自己的可變 fallback 實例。
內嵌字型 URI 與 `WithNvtCoreFonts()` 行為維持不變。

請為每個 builder 或自行擁有的 `FontManagerOptions` 取得一個 fallback。
僅調整該實例，不要依賴不同屬性存取之間的參考相等性。
測試確認修改一個 fallback 不會影響其他實例或後續存取。

`Nvt.Core.Fonts` 提供字型角色資源與內嵌中文字型後備，使用 `net10.0` 與 Avalonia 12.1.1。
此函式庫就是 Fonts 模組，命名空間為 `Nvt.Core.Fonts`。
模組不提供樣式、控制項、圖示名稱常數或其他字型註冊。

## 擁有者決定

擁有者於 2026-10-05 核准本週 Core 範圍。
擁有者於 2026-10-06 決定將字型放入獨立套件，並使用獨立版本。
同一天，擁有者決定 Numbers 使用 Normal（400），因為 Cascadia Mono 官方沒有靜態 Medium（500）檔案。
工具儲存庫因此不必在每次 Core 發行時重複保存字型檔案。

- 僅使用 Inter、Cascadia Mono、Noto Sans TC 與 Material Symbols Outlined 四個家族。
- 字型大小僅使用 11、13、16 與 24。
- 內嵌字型採固定版本與靜態字重。
- 不隨工具發行 Windows 系統字型。
- 每個角色包含一個家族、一個大小與一個字重。工具樣式必須一起套用三者。
- NVT FW Combiner（NFC）的舊字型資源留在 Theme 模組，維持不變。

打包另開 pull request 處理，加入套件版本、打包步驟與套件內的授權檔案。

## 角色與資源鍵

[`FontRoles.axaml`](../../../src/Nvt.Core.Fonts/FontRoles.axaml) 定義下列角色。
大小使用 Avalonia 的裝置獨立像素。

| 角色 | 用途 | 字型家族 | 大小 | 字重 |
| --- | --- | --- | ---: | --- |
| Title | 頁面、模態視窗與啟動器標題 | Inter | 24 | SemiBold（600） |
| Heading | 區段、面板與卡片標題 | Inter | 16 | SemiBold（600） |
| Body | 內文、說明、一般輸入與選項 | Inter | 13 | Normal（400） |
| Caption | 次要文字、中繼資料與註記 | Inter | 11 | Normal（400） |
| Mono | 程式碼、十六進位、位址、ID 與版本字串 | Cascadia Mono | 13 | Normal（400） |
| Numbers | 計數、進度與統計 | Cascadia Mono | 13 | Normal（400） |
| BodyStrong | 主要操作文字、強調與選取狀態 | Inter | 13 | SemiBold（600） |
| CaptionStrong | 表格標頭、欄位標籤與狀態徽章 | Inter | 11 | SemiBold（600） |
| MonoStrong | 強調的技術數值、選取或變更的十六進位 | Cascadia Mono | 13 | SemiBold（600） |
| MonoCaption | 技術中繼資料與緊湊原始數值 | Cascadia Mono | 11 | Normal（400） |
| Icon | 字形圖示 | Material Symbols Outlined | 16 | Normal（400） |

ID 與十六進位使用 Mono。計數使用 Numbers。
每個角色都有下列三個鍵，將 `<Role>` 換成上表中的精確角色名稱：

| 鍵 | 資源型別 | 意義 |
| --- | --- | --- |
| `Nvt.Font.<Role>.Family` | `FontFamily` | 單一家族 URI |
| `Nvt.Font.<Role>.Size` | `x:Double` | 角色大小 |
| `Nvt.Font.<Role>.Weight` | `FontWeight` | 角色字重 |
| `Nvt.Font.Fallback.Cjk.Family` | `FontFamily` | Noto Sans TC 後備家族 |

每個家族資源只包含一個家族，不包含後備清單。

| 字型家族 | 資源值 |
| --- | --- |
| Inter | `avares://Avalonia.Fonts.Inter/Assets#Inter` |
| Cascadia Mono | `avares://Nvt.Core.Fonts/Assets/CascadiaMono#Cascadia Mono` |
| Noto Sans TC | `avares://Nvt.Core.Fonts/Assets/NotoSansTC#Noto Sans TC` |
| Material Symbols Outlined | `avares://Nvt.Core.Fonts/Assets/MaterialSymbolsOutlined#Material Symbols Outlined` |

Inter 資源 URI 不需要 `WithInterFont()` 即可解析。
套件使用此 URI，不註冊 Inter 字型集合。

## 固定的上游基準

本模組使用上游字型資產，未擷取 NFC 或 NVT FW UTIL（NFU）的程式碼。
下表記錄上游儲存庫與 ref。後面的資產雜湊固定實際發行位元組。

| 儲存庫 | Ref 或版本 | 完整提交 SHA | 來源路徑 |
| --- | --- | --- | --- |
| [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia/tree/e33eaed9c106846b200680751022385d9cc5dc6f/src/Avalonia.Fonts.Inter/Assets) | `Avalonia.Fonts.Inter` 12.1.1 | `e33eaed9c106846b200680751022385d9cc5dc6f` | `src/Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf`、`Inter-SemiBold.ttf` |
| [microsoft/cascadia-code](https://github.com/microsoft/cascadia-code/releases/tag/v2407.24) | `v2407.24` | `56bcca3f2c1e4cb19458954f0e2bb4635960df91` | `CascadiaCode-2407.24.zip`：`ttf/static/CascadiaMono-Regular.ttf`、`CascadiaMono-SemiBold.ttf` |
| [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk/releases/tag/Sans2.004) | `Sans2.004` | `523d033d6cb47f4a80c58a35753646f5c3608a78` | `19_NotoSansTC.zip`：`NotoSansTC-Regular.otf`、`NotoSansTC-Bold.otf` |
| [google/material-design-icons](https://github.com/google/material-design-icons/tree/737e3324305806514d7909874fa1818ae1808232/variablefont) | 字型版本 2.973 | `737e3324305806514d7909874fa1818ae1808232` | `variablefont/MaterialSymbolsOutlined[FILL,GRAD,opsz,wght].ttf` 與對應的 `.codepoints` 檔案 |

Inter 留在固定版本的 `Avalonia.Fonts.Inter` 相依套件內，Core 不複製其字型檔案。
該套件包含靜態 Thin、Light、Regular、Medium、SemiBold 與 Bold 檔案。
角色使用其中的 Regular 與 SemiBold。
[Inter 字型授權](https://github.com/rsms/inter/blob/v3.19/LICENSE.txt) 為 SIL Open Font License 1.1。
Avalonia 套件中繼資料將套件程式碼授權列為 MIT。

下表的所有路徑皆相對於 `src/Nvt.Core.Fonts/`。

| 資產路徑 | 家族名稱 ID 1 / 16 | 字重 | 位元組 | SHA-256 | 版本與來源 | 授權 |
| --- | --- | ---: | ---: | --- | --- | --- |
| `Assets/CascadiaMono/CascadiaMono-Regular.ttf` | Cascadia Mono | 400 | 575912 | `06520d032ec274fa5040b22c6f4a1d829081b24ba40b2da56dae89bf10c7b481` | v2407.24，上述發行壓縮檔 | SIL OFL 1.1，未修改 |
| `Assets/CascadiaMono/CascadiaMono-SemiBold.ttf` | Cascadia Mono SemiBold / Cascadia Mono | 600 | 581840 | `8e04c1b811913a20773a3761d8994f15efe3029509c8d0556d74ae989558146d` | v2407.24，上述發行壓縮檔 | SIL OFL 1.1，未修改 |
| `Assets/NotoSansTC/NotoSansTC-Regular.otf` | Noto Sans TC | 400 | 5683368 | `5bab0cb3c1cf89dde07c4a95a4054b195afbcfe784d69d75c340780712237537` | Sans2.004，上述發行壓縮檔 | SIL OFL 1.1，未修改 |
| `Assets/NotoSansTC/NotoSansTC-Bold.otf` | Noto Sans TC | 700 | 5839972 | `55420b259eb119bf5f2a0aadba10cf9d736c12d64ab93e78546d69ef5f43558b` | Sans2.004，上述發行壓縮檔 | SIL OFL 1.1，未修改 |
| `Assets/MaterialSymbolsOutlined/MaterialSymbolsOutlined-Regular.ttf` | Material Symbols Outlined | 400 | 1397116 | `e90300193fd701f4a4eb88d292699ce3b4237863c3f297da0eda866dfb415347` | 2.973，上述上游提交，靜態實例 | Apache 2.0，已修改 |

## 2026-10-06 實測的已知限制

- Avalonia 12.0.5 與 12.1.1 都以可變字型的預設實例繪製，忽略字重軸。
- 要求不存在的較重字重時，Avalonia 可能套用合成粗體。因此本套件只提供靜態字重。
- Cascadia Mono 官方沒有靜態 Medium（500）檔案。第一版角色表的 Numbers 為 500，擁有者因此於 2026-10-06 改定為 Normal（400）。Numbers 不套用模擬。
- Noto Sans TC 沒有靜態 SemiBold（600）檔案。Inter 600 角色中的中文使用 Noto Sans TC Bold（700），不套用模擬。
- Inter 400 角色中的中文使用 Noto Sans TC Regular（400）。
- Material Symbols 只提供 outlined、未填色、字重 400、grade 0、光學大小 24 px 的固定樣式。

Icon 角色以大小 16 顯示該固定光學大小實例。
上游 `.codepoints` 檔案將 `home` 對應至 U+E9B2。較舊的碼位 U+E88A 對應到同一個字形，文字 `home` 也會塑形成這個單一字形。
Inter SemiBold 檔案回報的舊式家族名稱為 `Inter SemiBold`，要求的家族仍為 Inter。

## 工具採用方式

1. 參考 `Nvt.Core.Fonts`，版本由整合者選定。
2. 將 `FontRoles.axaml` 合併至應用程式資源。
3. 在應用程式 builder 呼叫 `WithNvtCoreFonts()`。
4. 在工具自己的樣式內，一起套用角色的家族、大小與字重。

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

`WithNvtCoreFonts()` 會取代 `FontManagerOptions`。
方法將 `FontFallbacks` 設為包含 `NvtCoreFonts.CjkFallback` 的清單。
角色家族缺少中文字元時，Avalonia 先嘗試 Noto Sans TC，再嘗試系統字型。
工具若自行管理選項，應直接將後備加入自己的選項：

```csharp
options.FontFallbacks = [NvtCoreFonts.CjkFallback, .. (options.FontFallbacks ?? [])];
builder.With(options);
```

將中文後備放在其他工具後備之前，保留工具的其他選項值。
下列樣式屬於採用工具：

```xml
<Style Selector="TextBlock.body">
  <Setter Property="FontFamily" Value="{StaticResource Nvt.Font.Body.Family}" />
  <Setter Property="FontSize" Value="{StaticResource Nvt.Font.Body.Size}" />
  <Setter Property="FontWeight" Value="{StaticResource Nvt.Font.Body.Weight}" />
</Style>
```

NFC 保留舊的 Theme 資源。
將 NFC 移至這些角色，必須另開 pull request，提供修改前後圖片。
擁有者核准畫面外觀後才能採用。

## 測試與零差異驗證

[`FontTests`](../../../tests/Nvt.Core.Fonts.Tests/FontTests.cs) 使用 headless Avalonia 與真正的 Skia 繪製後端。
測試應用程式合併字典，並呼叫 `AvaloniaTestHost.Build<FontsTestApplication>().WithNvtCoreFonts()`。
獨立 host 另外檢查未呼叫 `WithInterFont()` 時的 Inter 解析。

- 檢查每個資源值與各角色實際選取的靜態字型檔案。
- 檢查字形字型的家族名稱、字重與未套用模擬的狀態。
- 透過量測後的 `TextBlock` 與其塑形文字 run 檢查中文後備。
- 檢查 U+E9B2、較舊的別名 U+E88A，以及 `home` 的單字形連字。
- 透過 `AssetLoader` 檢查每個內嵌資產的位元組數與 SHA-256。

測試不雜湊授權文字檔案，因為 checkout 可能改變換行格式。
套件已還原後，執行下列命令：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Fonts.Tests/Nvt.Core.Fonts.Tests.csproj --no-build
dotnet test Nvt.Core.sln --no-build
```

工具進行等效字型替換時，採用前後都執行既有的資源、文字排版、繪製與截圖測試。
維持相同的字型位元組、Avalonia 版本、作業系統、DPI、佈景與範例文字。
比較字型檔案、字形 ID、字重、模擬、量測值、換行與繪製像素。
案例須包含拉丁文字、繁體中文、數字、技術數值與圖示。
NFC 也需執行 `AvaloniaApplicationResourceTests`、`StartupFocusTests`、`NavigationFocusIndicatorTests` 與既有畫面擷取測試。
不得更新基準來掩蓋差異。
將 NFC 的舊外觀改為這些角色，仍需前述獨立擁有者審查。
本工作不宣稱未來的外觀變更具有視覺零差異。

## 授權義務與更新

工具必須隨附完整的 [`licenses`](../../../src/Nvt.Core.Fonts/licenses) 資料夾。
工具的第三方聲明必須列出四個字型家族。
發行內容也須保留 Inter 字型授權與 Avalonia 相依套件聲明。
Cascadia Mono 與 Noto Sans TC 使用 SIL Open Font License 1.1，檔案未修改。
Material Symbols 使用 Apache License 2.0。
其 [`NOTICE`](../../../src/Nvt.Core.Fonts/licenses/MaterialSymbolsOutlined/NOTICE) 記錄靜態實例修改方式。

更新字型時：

1. 從上方來源頁下載字型。Cascadia Mono 與 Noto Sans TC 選用靜態字重。
2. 記錄版本、儲存庫 ref、完整提交 SHA、來源路徑、位元組數與新的 SHA-256。
3. Material Symbols 使用 NOTICE 中的精確命令，並使用所記錄的 fontTools 版本與 `SOURCE_DATE_EPOCH`。
4. 檢查固定軸值、家族名稱、字重與圖示連字。來源或命令改變時，更新 NOTICE。
5. 更新雜湊測試與兩份模組文件。執行字型測試與整個 solution 的測試。
6. 同步維護授權檔案與第三方聲明。套件發佈由整合者於打包工作中處理。
