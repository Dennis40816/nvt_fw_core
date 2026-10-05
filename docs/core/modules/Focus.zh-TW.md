[English](Focus.md) | [中文](Focus.zh-TW.md)

# Focus

`src/Nvt.Core.Avalonia/Focus/` 的公開附加行為使用命名空間 `Nvt.Core.Avalonia.Focus`：

- `FocusOnRevealBehavior`：控制項附加到視覺樹或自身 `IsVisible` 變為 true 時，排程以 Tab 導覽方式移轉焦點；執行時再次確認行為仍啟用、有效可見／啟用且可取得焦點。
- `FocusToolTipBehavior`：Tab／方向鍵焦點開啟既有 tooltip，不移轉焦點。Escape、ComboBox 選取變更及下拉關閉會關閉並抑制 tooltip；失去焦點或指標離開恢復 tooltip 服務，指標進入時則僅在控制項未取得焦點時恢復服務。

凍結的父版本基準：NFC（`nvt_fw_combiner`）、ref `origin/1.2.x`、完整 commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`、Avalonia 12.0.5。抽取的來源路徑：

- `src/NvtFwCombiner.Presentation.Avalonia/Behaviors/FocusOnRevealBehavior.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/Behaviors/FocusToolTipBehavior.cs`

兩個行為檔僅變更命名空間並加入要求的版權標頭。以下凍結測試路徑的通用斷言已移植至 `tests/Nvt.Core.Avalonia.Tests/Focus/`：

- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Startup.cs` — `CatalogWarmupUsesAccessibleRetryableForegroundLoadingSurface` 的附加／排程焦點合約，改以執行階段驗證。
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.cs` — `IcDetailTooltipUsesOneNonInteractiveFocusAwareCard` 的 ComboBox tooltip 生命週期斷言，使用合成選項。
- `tests/NvtFwCombiner.UiSmoke.Tests/OutputDeliverySourceTooltipTests.cs` — `KeyboardFocusOpensDetailsWithoutDuplicateOrModalDismissal` 的 Tab／Shift+Tab、單一 tooltip、Escape 攔截、焦點保留及恢復斷言，使用合成列。

新增行為特徵測試涵蓋 null／附加屬性預設值、延後可用性檢查、停用與重新啟用、僅祖先揭露、重新附加、導覽方式、指標恢復及下拉關閉。產品內容、樣式與對話框／view model 留在 NFC。

還原套件後的 Core 驗證：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore -p:UseSharedCompilation=false -m:1 -nr:false
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

NFC 採用時，先以上述父版本 commit 凍結 UI 證據。以 Core 取代重複行為前後，皆以 `--no-restore` 建置並執行 `dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build`，包含 `XamlControlStyleContractTests`（上述合約及 tooltip 使用端）、`OutputDeliverySourceTooltipTests`、`SupportMatrixInteractionTests` 與 `StartupFocusTests`。僅調整採用所需的命名空間／來源位置參照，保留行為斷言與預期證據。比較焦點控制項與走訪順序、tooltip 開啟／服務狀態、Escape 處理；在相同 OS、字型、DPI 與主題下，將既有截圖逐像素比對，不得更新基準來接受差異。工具採用及這些產品層級比較仍待後續工作。
