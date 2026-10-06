[English](Progress.md) | [繁體中文](Progress.zh-TW.md)

# Progress

## 摘要

Progress 提供有範圍驗證的進度資料、NVT FW UTIL（NFU）一次只執行一個工作的背景服務，以及 Freeform Helper（NFH）的進度間隔限制。
模組僅使用 .NET 基礎類別庫，目標框架為 net8.0。
命名空間為 `Nvt.Core.Progress`。

各工具保留自己的更新頻率、進度類別、結果類別與呈現政策。
擁有者於 2026-10-06 核准此邊界。
模組不加入巢狀進度、佇列、排程器、取代模式、關閉框架、計時器、延後補送或 UI 程式碼。

## 凍結基準

以下提交固定本次抽取來源。
來源使用 `git show <sha>:<path>` 讀取。

| 工具 | 儲存庫 | Ref | 提交 |
| --- | --- | --- | --- |
| NVT FW UTIL（NFU） | `nvt-event-buffer-replay` | `origin/0.2.0` | `26d66bd377a4ad051392bd7cc7e9d1c2e6287dba` |
| Freeform Helper（NFH） | `nvt-freeform-helper` | `origin/1.3.x` | `4df72911867ad047b3217195d12223038a5781b7` |
| NVT FW Combiner（NFC） | `nvt_fw_combiner` | `origin/1.2.x` | `a67eaee35b1d7eda9157a82e880e98a70407e913` |

NFU 抽取與脈絡檔案：

- 引擎：`src/Nvt.Replay.Rendering/ExportJobService.cs`，讀取完整檔案。
- 全部七個來源測試：`tests/Nvt.Replay.Tests/ExportJobServiceTests.cs`。
- 僅供 UI 脈絡：`src/Nvt.Replay.Avalonia/MainWindow.Output.cs`，第 35、518 與 600–638 行。

NFU 引擎檔案在 `915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b` 完全相同。
本次抽取使用表格中的固定提交。

NFC 僅提供 `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ForegroundLoadingState.cs` 的比例規則。
該檔案的 `ValidateProgress` 方法定義驗證條件。

NFH 提供 `src/FreeformHelper.UI/ViewModels/FreeformHelperViewModel.Persistence.NotchExport.Helpers.cs` 的間隔限制。
方法為 `CreateNotchGenerationProgressReporter` 與 `CreateStep5GenerationProgressReporter`。

兩個回報器都先檢查有效性，再套用 120 ms 的間隔限制。
NFH 先正規化總數並限制已處理數，再依自己的資料計算 `isFinal`。
最終回報一定通過，並重設間隔起點。
捨棄的回報不會改變上次轉送時間。
原始程式將 `lastTick` 設為零，因此首筆回報在一般執行環境會通過。
Core 明確讓首筆回報通過，也涵蓋從零開始的測試時鐘。

凍結基準沒有直接測試這兩個回報器的間隔限制。
相關呼叫端測試包含最終進度送達與過期結果拒絕的斷言：

- `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.NotchExportCache.cs` 檢查完成階段的進度會送達呼叫端。
- `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.NotchGenerationStaleCompletion.cs` 檢查最終階段處理與過期結果拒絕。

Core 以合成的最終值與外部有效性檢查，保留上述通用斷言。
NFH 保留領域斷言、進度文字、版本檢查與 UI 派送。

## Progress data

[`ProgressUpdate`](../../../src/Nvt.Core/Progress/ProgressUpdate.cs) 是公開的 readonly record struct。
它儲存 `double? Fraction` 與 `string StepText`。
只有 `Fraction` 為 null 時，`IsIndeterminate` 才為 true。

NFC 會驗證比例，不會將比例限制到邊界值。
Core 使用相同條件：`fraction is < 0 or > 1 || double.IsNaN(fraction ?? 0)`。

| 輸入 | 行為 |
| --- | --- |
| `null` | 接受，表示進度未知。 |
| 零至一 | 接受並保留原值。 |
| 負零 | 接受並保留符號位元。 |
| 小於零或大於一 | 拋出 `ArgumentOutOfRangeException`。 |
| NaN、正無限大或負無限大 | 拋出 `ArgumentOutOfRangeException`。 |

例外訊息以 `Progress must be between 0 and 1.` 開頭。
建構、物件初始化與 record 複製都使用相同規則。
Core 原樣保留步驟文字，不加入文字驗證。

[`ProgressUpdateTests`](../../../tests/Nvt.Core.Tests/Progress/ProgressUpdateTests.cs) 涵蓋 null、零、一、中間比例與無效輸入。
測試也涵蓋負零、record 複製與原樣保留的步驟文字。

## Background jobs

[`BackgroundJobService<TProgress, TResult>`](../../../src/Nvt.Core/Progress/BackgroundJobService.cs) 擁有一個執行中的工作。
兩個型別參數都必須是參考型別。
工具可繼續使用既有的進度與結果類別。
`ProgressUpdate` 是獨立的值型別資料，不能直接作為此服務的型別參數。

[`BackgroundJobSnapshot<TProgress, TResult>`](../../../src/Nvt.Core/Progress/BackgroundJobSnapshot.cs) 儲存工作 ID、狀態、進度、結果與錯誤。
[`BackgroundJobHandle<TProgress, TResult>`](../../../src/Nvt.Core/Progress/BackgroundJobHandle.cs) 提供 ID 與完成工作。

| 狀態 | 意義 | `IsActive` |
| --- | --- | --- |
| `Idle` | 尚未啟動工作，ID 為零。 | False |
| `Running` | 操作執行中。 | True |
| `Cancelling` | 已要求取消，操作尚未完成。 | True |
| `Succeeded` | 操作回傳，且未要求取消。 | False |
| `Cancelled` | 操作在取消要求後完成。 | False |
| `Failed` | 操作拋出例外，且未要求取消。 | False |

`Start` 在接納工作前拒絕 null 操作。
已有執行中或取消中的工作時，它拋出 `InvalidOperationException`。
接納的工作依序取得經溢位檢查的 ID，從一開始。
被拒絕的啟動不消耗 ID。

`Start` 儲存呼叫端提供的初始進度，預設為 null。
它先通知初始 Running 快照，再以 `Task.Run` 啟動操作。
操作取得該工作的取消權杖與直接執行回呼的進度回報器。

`Cancel` 先設定 Cancelling 狀態並記錄取消要求，再通知觀察者與呼叫取消回呼。
沒有執行中的工作或已要求取消時，它回傳 false。
第一次要求取消且回呼正常完成時，它回傳 true。
取消回呼拋出例外時，`Cancel` 拋出 `AggregateException`，但取消要求仍然有效。

操作完成後，服務才清除執行中的工作。
取消會抑制回傳結果與操作錯誤。
若工作的權杖尚未取消，`OperationCanceledException` 會成為 Failed。
該例外本身攜帶的權杖不決定結果。

Cancelling 期間仍接受進度。
終止快照保留最後進度。
已完成工作的回報不能修改狀態，也不能通知舊觀察者。

服務在鎖外呼叫快照觀察者與取消回呼。
觀察者例外不改變工作結果。
觀察者可以讀取狀態、要求取消，並在終止通知時啟動下一個工作。
完成工作會等待終止觀察者返回。

服務保留 NFU 對取消來源釋放時機的協調。
操作可以在取消回呼執行期間完成，也可以在權杖取消呼叫開始前完成。
已完成的工作不能提早釋放尚未結束的 `Cancel` 呼叫所需的取消來源。

### 背景工作的零差異驗證

[`BackgroundJobServiceTests`](../../../tests/Nvt.Core.Tests/Progress/BackgroundJobServiceTests.cs) 移植全部七個 NFU 來源測試。
小型合成參考型別取代 replay 資料類別。

移植保留以下來源情境：

- 背景執行、進度回報與拒絕第二個執行中的工作。
- 可重複呼叫的取消與合作式操作的完成。
- 取消回呼在服務鎖外讀取狀態。
- 抑制取消後才回傳的結果。
- 拒絕舊工作的延遲進度。
- 可觀察的失敗與下一個工作的復原。
- 隔離觀察者例外。

特徵測試增加可控制的取消與觀察者時序：

- 取消回呼拋出例外，操作分別在回呼結束前與結束後完成。
- 不相關權杖的取消例外，涵蓋已要求與未要求取消工作的情況。
- Cancelling 期間的進度，以及完成後拒絕進度。
- 觀察者從另一執行緒讀取狀態、重入取消，並在終止通知時啟動下一個工作。
- 權杖取消前完成，以及取消後抑制操作錯誤。
- 精確的初始、進度與終止快照，以及工作 ID、null 值和觀察者順序。

測試以 `TaskCompletionSource` 控制時序。
測試不使用 `Thread.Sleep`、`Task.Delay` 或排程猜測。
服務不讀取時鐘。
相同閘門順序在假時鐘下仍產生精確快照，因此不需要新增時鐘 API。

來源比對確認引擎主體在套用下列必要差異後，與固定 NFU 來源相同。
比對排除註解與空白。
兩個固定 NFU 引擎版本也完全相同。

使用既有還原套件執行 Core 驗證：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build
```

2026-10-06 的驗證通過，建置沒有警告或錯誤。
全部 149 個 Core 測試通過，其中 55 個為 Progress 測試案例。
沒有失敗或略過的測試。

NFU 採用 Core 時，在切換包裝層前後執行以下測試：

- `tests/Nvt.Replay.Tests/` 中的 `ExportJobServiceTests` 與 `ReplayExportTests`。
- `AdvancedWorkspaceSnapshotTests`，包含 `Output_export_progress_uses_the_info_rail_without_reducing_preview_controls`。
- `MainWindowLayoutTests` 與 `tests/Nvt.Replay.Avalonia.Tests/` 的完整 UI 快照測試。

在各 NFU checkout 使用其既有還原套件：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Replay.Tests/Nvt.Replay.Tests.csproj --no-restore
dotnet build tests/Nvt.Replay.Avalonia.Tests/Nvt.Replay.Avalonia.Tests.csproj --no-restore
dotnet test tests/Nvt.Replay.Tests/Nvt.Replay.Tests.csproj --no-build --filter "FullyQualifiedName~ExportJobServiceTests|FullyQualifiedName~ReplayExportTests"
dotnet test tests/Nvt.Replay.Avalonia.Tests/Nvt.Replay.Avalonia.Tests.csproj --no-build
```

兩個版本使用相同的受控操作與觀察者傳遞方式。
透過 Core 參數傳入 NFU 既有的初始進度。
比較以下證據：

- 初始、進度、取消中與終止快照。
- 工作 ID、被拒絕的啟動與取消回傳值。
- 結果值、結果物件身分、錯誤型別、錯誤物件身分與被抑制的結果。
- 觀察者呼叫順序、取消期間接受的進度與忽略的延遲回報。
- 匯出輸出、manifest 內容、取消清理與既有 UI 快照。

UI 比對使用相同的 OS、執行階段、字型、DPI、佈景主題與快照輸入。
保留 NFU 的更新頻率、觀察者派送、狀態文字與過期快照檢查。
不得為了接受差異而更新核准快照。

NFU 採用驗證由採用工具執行。
本次工作沒有修改 NFU 或 NFC，也沒有執行其匯出或 UI 測試。

### 已知差異

Core 更改命名空間與工作型別名稱。
它以受限的泛型參考型別取代 replay 資料型別。
它加入要求的版權標頭與公開 API 文件。

Core 以參數接收初始進度，不建立 NFU 的匯出專用初始值。
NFU 保留 `StartReplayExport`、replay 資料類別、`Preparing export` 與所有匯出錯誤文字。
Core 的工作衝突訊息為 `A background job is already active.`。
NFU 可在包裝層保留原始訊息。

Core 新增要求的比例 record，不抽取 NFC 的前景 UI 狀態。
比例例外的參數名稱為 `fraction`，NFC 使用 `progress`。
驗證條件、拒絕行為與訊息文字保持相同。
NFC 的標題與詳細文字驗證留在 Core 之外。

Core 測試方法使用 PascalCase，以符合儲存庫分析器。
完成閘門取代 NFU 的無限等待，並移除移植斷言的時序競爭。
抽取沒有引入其他工作生命週期行為。

## Throttled progress

`ThrottledProgress<T>` 在 `src/Nvt.Core/Progress/ThrottledProgress.cs` 實作 `IProgress<T>`。

```csharp
public ThrottledProgress(
    IProgress<T> target,
    TimeSpan minimumInterval,
    Func<T, bool> bypass,
    TimeProvider? timeProvider = null);
```

`Report` 在以下任一條件成立時轉送資料：

- `bypass(value)` 回傳 true。
- 尚未轉送任何回報。
- 距上次轉送已達 `minimumInterval`。

每次轉送都更新時間戳記，也包含略過間隔限制的回報。
捨棄的回報直接遺失。
此類別不建立計時器、尾端回報或佇列。
零間隔會轉送每筆回報。
`target` 或 `bypass` 為 null 時，建構子擲出 `ArgumentNullException`。
負間隔會擲出 `ArgumentOutOfRangeException`。

預設時鐘為 `TimeProvider.System`。
間隔判斷使用 `GetTimestamp()` 與 `GetElapsedTime()`。
鎖保護間隔判斷與時間戳記。
目標回報器在鎖外執行。
目標回報器可能同時接收多個呼叫，因此由目標負責同步與派送。

### 驗證

套件還原完成後執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Progress.ThrottledProgressTests"
```

測試檔案為 `tests/Nvt.Core.Tests/Progress/ThrottledProgressTests.cs`。
手動 `TimeProvider` 不需要額外套件。
測試涵蓋首筆回報、119/120 ms 精確邊界、時間戳記精度、最終回報、間隔重設、捨棄值與零間隔。
並行測試檢查轉送數量，並證明阻塞中的目標回報器不會持有間隔鎖。
逾時只限制並行測試的失敗等待時間，所有間隔判斷都由手動時鐘決定。
參數測試檢查例外類型與參數名稱。

### NFH 採用時的零差異驗證

在呼叫 `ThrottledProgress` 前，保留既有有效性檢查。
無效回報不得進入節流器，即使該回報是最終回報。
使用 `TimeSpan.FromMilliseconds(120)`，並將 NFH 既有的 `isFinal` 計算傳入 `bypass`。
保留既有資料、UI 派送與目標回呼。

以相同假時鐘時間序列，比較兩個凍結回報器的間隔判斷與 Core。
原始時鐘起點須大於 120 ms，以重現一般環境的首筆回報行為。
逐筆比較轉送資料順序與轉送時間。
輸入須包含首筆回報、119 ms 捨棄、正好 120 ms 通過、最終回報、無效回報與最終回報之後的回報。
確認捨棄與無效回報不會改變下一次通過的邊界。
也執行上述 Core 測試。

NFH 採用前後，執行既有呼叫端測試，不修改預期結果：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build FreeformHelper.sln --no-restore
dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.FreeformHelperViewModelTests"
```

此篩選包含已檢視的快取與過期完成測試。
比較最終進度送達、過期結果拒絕、進度文字、對話框次數與輸出斷言。
在 NFH 或其他工具採用 Core，不屬於本次擷取範圍。

### 已知時鐘差異

原始程式使用 `Environment.TickCount64`，解析度為毫秒。
Core 使用 `TimeProvider` 時間戳記。
在實機上，接近 120 ms 邊界的回報可能產生不同的通過或捨棄結果。
零差異比較應使用精確的假時鐘時間序列。

## Progress UI

UI 控制項保留 NVT FW Combiner 的載入結構，以及由工具決定的動畫政策。
擁有者於 2026-10-06 核准此邊界。
程式庫使用 net10.0 與 Avalonia 12.0.5。
命名空間為 `Nvt.Core.Avalonia.Progress`。

### 凍結 UI 基準

使用 `git show <sha>:<path>` 讀取以下檔案。
這些基準也供主機撰寫提交訊息時記錄來源。

| 工具 | 儲存庫 | Ref | 提交 |
| --- | --- | --- | --- |
| NVT FW Combiner（NFC） | `nvt_fw_combiner` | `origin/1.2.x` | `a67eaee35b1d7eda9157a82e880e98a70407e913` |
| NVT FW UTIL（NFU） | `nvt-event-buffer-replay` | `origin/0.2.0` | `26d66bd377a4ad051392bd7cc7e9d1c2e6287dba` |
| Freeform Helper（NFH） | `nvt-freeform-helper` | `origin/1.3.x` | `4df72911867ad047b3217195d12223038a5781b7` |

NFC 提供外層結構、Padding 繫結、動畫政策與送達順序：

- `src/NvtFwCombiner.Presentation.Avalonia/Views/ForegroundLoadingSurface.axaml`。
- `src/NvtFwCombiner.Presentation.Avalonia/Views/ForegroundLoadingSurface.axaml.cs`。
- `src/NvtFwCombiner.Presentation.Avalonia/Resources/MainWindowSharedTemplates.axaml` 的 `ForegroundLoadingStatusTemplate`。
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ForegroundLoadingState.cs`。
- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/WorkflowInspectionLifecycle.cs` 的進度送達程式。

其他 UI 來源確認呈現政策由工具持有：

- NFU：`src/Nvt.Replay.Avalonia/MainWindow.Output.cs`，第 600–638 行。
- NFH：`src/FreeformHelper.UI/Views/WorkflowSteps/RightWorkflowStep5View.axaml` 的進度條。

已檢視的 NFC 測試來源：

- `tests/NvtFwCombiner.UiSmoke.Tests/ForegroundLoadingStateTests.cs`。
- `tests/NvtFwCombiner.UiSmoke.Tests/WorkflowInspectionLifecycleTests.cs`。
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.Startup.cs`。
- `tests/NvtFwCombiner.UiSmoke.Tests/XamlControlStyleContractTests.StandardFeedback.cs`。
- `tests/NvtFwCombiner.UiSmoke.Tests/ShellPreloadSessionTests.Presentation.cs`。
- `tests/NvtFwCombiner.UiSmoke.Tests/StartupFocusTests.cs`。

### UI 送達規則

[`UiProgress<T>`](../../../src/Nvt.Core.Avalonia/Progress/UiProgress.cs) 接收 `Action<T> publish`。
回呼為 null 時，建構子擲出 `ArgumentNullException`。
`Dispatcher.UIThread.CheckAccess()` 為 true 時，`Report` 立即執行 `publish(value)`。
否則，它以預設優先序將回呼 Post 至 `Dispatcher.UIThread`。

NFC 在擷取的呈現內容為 null，或等於目前同步內容時，立即執行 `Deliver`。
其他情況則 Post 至該同步內容。
Core 保留 NFC 的 UI 同步內容送達順序，並要求所有回呼在 UI 執行緒執行。
Core 不保留 NFC 未擷取同步內容時直接從背景執行緒送達的路徑。

Post 的回報依入列順序送達。
UI 執行緒的立即回報可能先於尚待派送的背景回報送達。
此配接器不加入佇列或節流。

NFU 建立 `Progress<ExportJobSnapshot>`，透過擷取的同步內容 Post 回呼。
NFU 在啟動或取消後，也會直接呈現目前快照。
回呼重新讀取權威快照，並拒絕不同工作 ID。
這些快照檢查與直接呈現仍由 NFU 持有。
若立即送達會改變既有順序，NFU 必須保留一律 Post 的觀察者。

### 進度條

[`ProgressIndicator`](../../../src/Nvt.Core.Avalonia/Progress/ProgressIndicator.cs) 繼承 `ProgressBar`。
唯一新增屬性為 `ProgressUpdate? Progress`。
已知比例會將 `Value` 設為該比例。
null 更新或 null 比例不改變 `Value`。

控制項不設定 `IsIndeterminate`。
NFC 執行中且未啟用減少動態效果時，`ShouldAnimate` 保持 true。
已知比例時也使用相同規則。
保留 NFC 的 `IsIndeterminate="{Binding ShouldAnimate}"` 繫結。

樣式鍵保持 `typeof(ProgressBar)`。
既有 `ProgressBar` 選取器與控制項佈景主題仍適用。
預設 `Maximum` 保持 Avalonia 的預設值。
NFC 與 NFH 必須保留明確的 `Maximum="1"`。

NFC 使用 `ProgressIndicator` 時，可以保留既有 `Value` 繫結。
工具提供 `ProgressUpdate` 時，再繫結 `Progress`。
NFU 保留自己的未知總數判斷與範圍。

### 載入表面

[`LoadingSurface`](../../../src/Nvt.Core.Avalonia/Progress/LoadingSurface.cs) 繼承 `ContentControl`，不新增屬性。
透過 `StyleInclude` 載入 `avares://Nvt.Core.Avalonia/Progress/ProgressStyles.axaml`。

樣式讓表面可取得焦點，並啟用 Core 既有的 `FocusOnRevealBehavior`。
範本包含遮罩 `Grid`，以及一個置中的內層 `ContentControl`。
遮罩使用 `{DynamicResource NfcModalScrimBrush}`，並阻擋指標輸入。
內層控制項接收表面的 `Content` 與 `ContentTemplate`。

內層範本元件名稱為 `PART_Content`。
工具以範本元件樣式提供寬度與 Padding。
NFC 保留以下值：

```xml
<Style Selector="progress|LoadingSurface /template/ ContentControl#PART_Content">
  <Setter Property="Width" Value="430" />
  <Setter Property="Padding" Value="28,26" />
</Style>
```

`progress` 前綴對應 `Nvt.Core.Avalonia.Progress`。
此方式不在 Core 新增尺寸或 Padding 屬性。
表面繼承的 `Padding` 不會轉送至內層元件。

NFC 資料範本保留 `Padding="{Binding $parent[ContentControl].Padding}"`。
最近的 `ContentControl` 仍為置中的內層元件。
因此，此繫結讀取的元素與 `28,26` 值都與凍結外層結構相同。

工具在表面設定 `AutomationProperties.Name`。
工具也持有可見性、文字、按鈕、命令、宣告、尺寸、陰影與產品樣式。
Core 不提供這些內容。

### UI 驗證

使用既有還原套件：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Progress"
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```

測試保留 NFC 可適用的動畫與外層結構斷言。
產品生命週期、文字與命令斷言仍留在 NFC。

[`UiProgressTests`](../../../tests/Nvt.Core.Avalonia.Tests/Progress/UiProgressTests.cs) 檢查 UI 執行緒送達、Post 順序與立即回報超前。
[`ProgressIndicatorTests`](../../../tests/Nvt.Core.Avalonia.Tests/Progress/ProgressIndicatorTests.cs) 檢查值、null 更新、動畫、預設值與繼承樣式。
[`LoadingSurfaceTests`](../../../tests/Nvt.Core.Avalonia.Tests/Progress/LoadingSurfaceTests.cs) 檢查樹、動態遮罩、指標阻擋、Padding、內容與顯示時焦點。

測試組件使用 `AvaloniaTestHost` 與真實 Skia 繪製。
`FrozenNfcLoadingSurface.axaml` 以測試命名空間與合成資料保留凍結外層結構。
成對的合成範本在基準使用 `ProgressBar`，在 Core 使用 `ProgressIndicator`。
兩者使用相同合成佈景主題，不使用動畫時鐘。
十四個比對以 640 × 360 像素與 96 DPI，逐一比較每個 RGBA 位元組。

2026-10-06 的驗證通過，建置沒有警告或錯誤。
全部 25 個 Progress UI 案例與全部 285 個 Avalonia 測試通過。
沒有失敗或略過的測試。

### NFC 採用時的零差異驗證

將 `ForegroundLoadingSurface` 替換為 `LoadingSurface`。
保留 NFC 的資料內容、Content、可見性繫結、自動化名稱與狀態範本。
以 `Content="{Binding}"` 傳入狀態，並保留既有 `ContentTemplate`。
載入 Core 樣式，並套用上述 NFC 內層寬度與 Padding。
保留所有狀態範本樣式、動作、宣告，以及 `ShouldAnimate` 繫結。

NFC 採用前後執行以下測試：

- `ForegroundLoadingStateTests`。
- `WorkflowInspectionLifecycleTests`。
- `ShellPreloadSessionTests` 與 `BuiltInBundlePreloadTests`。
- `XamlControlStyleContractTests`，包含 `CatalogWarmupUsesAccessibleRetryableForegroundLoadingSurface`。
- `StartupFocusTests` 與 `NavigationFocusIndicatorTests`。

逐像素比較未知進度、已知比例、失敗、重試、收合與完成。
也比較啟用減少動態效果時的已知比例。
使用相同 OS、字型、DPI、佈景主題、輸入與動畫擷取位置。
確認焦點目標、Padding、自動化名稱與過期進度拒絕都保持相同。
不得為了接受差異而更新核准快照。

Core 的合成比對不取代 NFC 產品快照檢查。
本次工作不包含採用或產品測試執行。
NFH 與 NFU 可以改變外觀，但其採用 PR 必須附上前後圖片。