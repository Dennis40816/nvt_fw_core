[English](Testing.md) | [中文](Testing.zh-TW.md)

# Testing：與 NFC 相同的繪圖主機

[`AvaloniaTestHost`](../../../src/Nvt.Core.Avalonia/Testing/AvaloniaTestHost.cs) 與 NVT FW Combiner（NFC）凍結版本使用相同的 Inter、Skia、headless builder chain。只有 application 型別不同。

命名空間為 `Nvt.Core.Avalonia.Testing`。各測試專案直接編譯共用來源。執行階段程式庫排除此檔，讓 headless 與 xUnit 相依性留在測試端。本模組支援 dispatcher、版面、輸入、文字度量及畫面擷取測試。

## 凍結的 parent baseline

來源 repository 為 NFC（`nvt_fw_combiner`）。凍結 ref 為 `origin/1.2.x`，commit 為 `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。

本次以 `git show` 讀取該 commit 的下列檔案：

- `tests/NvtFwCombiner.UiSmoke.Tests/AvaloniaHeadlessTestApplication.cs`：builder 來源。
- `tests/NvtFwCombiner.UiSmoke.Tests/AvaloniaApplicationResourceTests.cs`：既有 UI 執行緒斷言。
- `tests/NvtFwCombiner.UiSmoke.Tests/packages.lock.json`：繪圖套件解析版本證據。

Core 測試保留 UI 執行緒斷言。產品資源檢查留在 NFC。來源沒有獨立的 bootstrap 測試。

| Builder 步驟 | NFC 凍結主機 | Core 主機 |
| --- | --- | --- |
| Application | `AppBuilder.Configure<App>()` | `AppBuilder.Configure<TApplication>()` |
| 字型 | `.WithInterFont()` | `.WithInterFont()` |
| Renderer | `.UseSkia()` | `.UseSkia()` |
| 平台 | `.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })` | `.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })` |

Core lock 檔為 [`tests/Nvt.Core.Avalonia.Tests/packages.lock.json`](../../../tests/Nvt.Core.Avalonia.Tests/packages.lock.json)。兩份 lock 檔的解析版本如下。這四個套件的 content hash 也完全相同。

| 套件 | NFC 凍結 lock | Core lock |
| --- | --- | --- |
| Avalonia.Skia | 12.0.5 | 12.0.5 |
| SkiaSharp | 3.119.4 | 3.119.4 |
| HarfBuzzSharp | 8.3.1.3 | 8.3.1.3 |
| Avalonia.Fonts.Inter | 12.0.5 | 12.0.5 |

## 零差異證據

[`AvaloniaRenderingTests`](../../../tests/Nvt.Core.Avalonia.Tests/Testing/AvaloniaRenderingTests.cs) 比較精確文字度量及畫面的原始像素。兩個 builder 使用相同的 `ThemeTestApplication` 與合成場景。

先以 NFC 凍結 chain 執行兩次獨立測試程序，只替換 application 型別。兩次結果完全相同。再以 Core builder 執行兩次獨立測試程序，重現相同結果。沒有不穩定的度量值。

基準環境為 Windows NT 10.0.26300.0、96 DPI。比對時保持相同 OS、fallback 字型、DPI 及主題。只釘 Inter 文字。Inter 是內嵌字型，數值不受系統字型影響。繁體中文會改用系統字型，各機器不同，等 font set 內嵌 Noto Sans TC 後再釘。

文字度量測試將 `FontFamily` 設為 `avares://Avalonia.Fonts.Inter/Assets#Inter`，關閉 layout rounding，並以無限可用大小測量各 `TextBlock`。下列精確 `DesiredSize` 使用裝置獨立像素：

| 文字 | 字型大小 | 寬度 | 高度 |
| --- | --- | --- | --- |
| Core 123 | 11 | 46.734375 | 13.3125 |
| Core 123 | 13 | 55.23153409090909 | 15.732954545454545 |
| Core 123 | 16 | 67.97727272727272 | 19.363636363636363 |
| Core 123 | 24 | 101.96590909090907 | 29.045454545454547 |

擷取畫面使用白色背景及四列黑色 Inter 文字。每列內容為 `Core 123`，字型大小依序為 11、13、16、24。Stack margin 為 12 像素，列間距為 6 像素。

- 畫面大小：320 × 240 像素。
- 畫面 DPI：96 × 96。
- 像素格式：RGBA8888。
- 雜湊輸入：`Bitmap.CopyPixels` 的原始列資料，stride 為 1,280 bytes，共 307,200 bytes。
- SHA-256：`19F40303C54B8F27E07EE66C7B8A312EF02BD4A73387F6D9ABB0291929DEE4E6`。

[`AvaloniaTestHostTests`](../../../tests/Nvt.Core.Avalonia.Tests/Testing/AvaloniaTestHostTests.cs) 也檢查啟動、dispatcher 執行緒歸屬、控制項固定位置大小、視窗關閉及鍵盤／文字輸入。輸入案例涵蓋 ASCII、繁體中文與 emoji。Theme 及 Focus 斷言維持原樣。

## 採用與驗證

測試專案需要 `Avalonia.Headless.XUnit` 及 `Avalonia.Skia`，並使用上列相同版本。保留 linked-source 結構：

```xml
<Compile Include="..\..\src\Nvt.Core.Avalonia\Testing\AvaloniaTestHost.cs"
         Link="Testing\AvaloniaTestHost.cs" />
```

每個測試 assembly 只註冊一次主機：

```csharp
[assembly: Avalonia.Headless.AvaloniaTestApplication(
    typeof(Nvt.Core.Avalonia.Testing.AvaloniaTestHost))]
```

需要專屬 application 的 assembly 改為註冊該類別。其無參數 builder 呼叫泛型主機：

```csharp
public static AppBuilder BuildAvaloniaApp() =>
    AvaloniaTestHost.Build<ThemeTestApplication>();
```

Core 只保留 `ThemeTestApplication` 的 assembly 註冊。該 application 透過共用 builder 載入已抽取的 theme 資源。

接觸 Avalonia 物件或 `Dispatcher.UIThread` 的測試使用 `[AvaloniaFact]` 或 `[AvaloniaTheory]`。測試自行載入所需資源，並在 `finally` 關閉視窗。主機不加入產品樣式、服務、預載、保存或桌面生命週期。

關閉 telemetry 後執行 Core 驗證：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter FullyQualifiedName~Testing --logger 'console;verbosity=detailed'
dotnet test Nvt.Core.sln --no-build
```

NFC 切換主機前，先對凍結 parent 執行未變更的 UI smoke suite：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build
```

NFC 的 adapter 呼叫 `AvaloniaTestHost.Build<App>()`，保留產品 application。切換後執行相同 suite，逐項比較精確文字度量、控制項位置大小、dispatcher 歸屬、焦點、鍵盤修飾鍵、收到的文字、擷取尺寸及像素雜湊。

須包含 `AvaloniaApplicationResourceTests`、`StartupFocusTests`、`NavigationFocusIndicatorTests` 及既有版面、擷取測試。既有截圖須與未變更的凍結基準比對。任何差異都阻擋採用，不得更新基準來接受差異。NFC 採用不在本次變更範圍內。
