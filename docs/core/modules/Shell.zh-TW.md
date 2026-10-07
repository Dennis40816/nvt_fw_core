[English](Shell.md)

# Shell

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`：第 15 行的 `_pageHistory` 初始化、第 47 行的 `CanGoBack`、第 115 行的前一筆項目運算式（公開為 `BackTarget`），以及第 171–203 行含啟用失敗回復的 `CompleteNavigation`。
- `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`：僅擷取第 758–764 行的 `LoadContent`，改名為 `EnsureContent`。頁面註冊與 `ApplyDeferredShellContent` 呼叫端保留在 NFC。
- `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.StartupWarmup.cs`：僅擷取第 42–59 行的 `MaterializeContent`。暖機清單、排程、執行閒置檢查、進度及追蹤包裝保留在 NFC。

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Shell` 提供同步、僅依賴 BCL 的導覽歷史輔助類別，頁面識別由宿主定義。
它不依賴產品、Toolkit 或 Avalonia，也不公開可變動的歷史集合。
`Nvt.Core.Avalonia.Shell` 的 `PageHost` 提供兩個同步 `ContentControl` 輔助方法。
Shell 邊界涵蓋導覽歷史、啟用失敗回復及此頁面承載介面。呼叫端註冊自己的頁面，
並提供識別、承載控制項、範本與順序；API 不假定任何產品頁面清單。
工作區組合與預載排程由宿主應用程式負責。

已接受的 Shell 範圍為導覽歷史與既有 PageHost 介面。下列保留接縫是在此範圍內
由產品負責的功能。此清冊不新增執行階段 API、控制項採用或可執行測試。

## Core 契約來源

| 契約 | Core 來源 | 合併來源 |
| --- | --- | --- |
| `NavigationHistory<TPage>` | `src/Nvt.Core/Shell/NavigationHistory.cs` | Core #54，合併提交 `d21934353a87926fb0ab4720f7a5921c48644c76`。 |
| `PageHost.EnsureContent` 與 `PageHost.MaterializeContent` | `src/Nvt.Core.Avalonia/Shell/PageHost.cs` | Core #77，合併提交 `968c14c8d3abcd814cb78d376c6d35475bb66160`。 |

凍結來源 `ShellNavigationViewModel.cs:74–85` 的前進命令與 `107–119` 的返回命令
界定歷史完成方法的呼叫端。NFC 保留同頁捷徑、返回許可與受防護的請求。
Core 接收擷取的目標、返回旗標及選用的完成動作；開頭列出的來源片段是已擷取的機制。

## 公開 API

```csharp
namespace Nvt.Core.Shell;

public sealed class NavigationHistory<TPage> where TPage : notnull
{
    public NavigationHistory(TPage home, Func<TPage> selectedPage,
        Action<TPage> activate, Action stateChanged);
    public bool CanGoBack { get; }
    public TPage BackTarget { get; }
    public void CompleteNavigation(TPage target, bool isBack,
        Action? afterActivation = null);
}
```

```csharp
namespace Nvt.Core.Avalonia.Shell;

public static class PageHost
{
    public static void EnsureContent(ContentControl host, bool shouldLoad,
        object content);
    public static void MaterializeContent(ContentControl host, object dataContext);
}
```

`ContentControl` 是 Avalonia 既有的控制項。PageHost 不新增控制項、註冊表、
dispatcher 或排程器。呼叫端負責 UI 執行緒存取，並決定執行狀態是否已閒置，
適合進行實體化。

建構時只存入一筆注入的首頁項目，不讀取選取狀態，也不呼叫回呼。
空回呼依 `selectedPage`、`activate`、`stateChanged` 的順序拒絕。
頁面值以 `EqualityComparer<TPage>.Default` 比較，不進行正規化或產品頁面驗證。
`CanGoBack` 的條件精確為 `history.Count > 1`。`BackTarget` 傳回前一筆項目；
沒有該項目時擲出 `InvalidOperationException`。

凍結的歷史機制沒有可設定的正數限制、頁面識別長度限制或歷史筆數上限。
固定機制邊界為一筆歷史；返回完成不會移除首頁項目，因此零筆狀態無法到達。
沒有產品上限移至 Core。

## 保留的完成與回復流程

1. 啟用前先讀取選取的來源，再擷取目前歷史快照。前進完成會再次讀取選取狀態，
   用於加入項目的判斷。這些讀取與歷史變動發生在啟用的 `try` 區塊外。
2. 返回完成只在歷史超過一筆時移除最後一筆。前進完成只在目前選取與目標不同時
   加入目標。此條件比較選取狀態，而非最後一筆歷史項目。
3. 即使目標與選取相等，仍啟用傳入的目標，再呼叫存在的 `afterActivation`。
   成功狀態更新由 `activate` 負責；Core 成功時不額外呼叫 `stateChanged`。
4. 歷史在啟用前變動，因此同步重入的更新可看到變動後的返回可用性與目標。
   回呼可完成巢狀導覽；外層失敗時仍還原其原始歷史快照。
5. 啟用或完成動作失敗時，先讀取選取狀態，僅在選取頁面不同時重新啟用擷取的來源。
   回復啟用仍看到變動後的歷史，因為歷史稍後才還原。
6. 回復啟用成功，或不需要重新啟用時，還原先前歷史、呼叫 `stateChanged`，
   再以不帶運算式的 `throw` 重新擲出原始例外。
7. 回復時的選取讀取或啟用失敗，會在還原歷史及呼叫 `stateChanged` 前取代原始例外。
   `stateChanged` 失敗則在歷史已還原後取代原始例外。沒有改變此順序的額外清理。

宿主在要求確認前擷取 `BackTarget`，並將該目標傳給完成方法。
完成方法不會在確認關閉通知重入後重新計算目標。即使重入改變歷史，仍依原始
返回條件移除最後一筆。回復來源為呼叫完成時讀取的選取狀態；宿主可另外保留
較早的來源，供清除動作使用。呼叫不具同步保護，應由宿主的導覽執行緒執行。

## 保留的頁面承載行為

`EnsureContent` 先檢查 `shouldLoad`。若為 false，不存取宿主；若為 true，
唯一操作是 `host.Content ??= content`。既有非 null 內容保留原識別；Core
不明確建構範本、不改動 `ContentTemplate`，也不指派控制項的 `DataContext`。
Avalonia 仍處理屬性正常的通知與呈現。

`MaterializeContent` 在進入時若 `host.Content` 非 null，立即返回。
否則直接呼叫 `host.ContentTemplate?.Build(dataContext)`，不檢查 `Match`。
缺少範本或建構結果為 null 時，呼叫 `EnsureContent(host, true, dataContext)`，
並保留範本。備援路徑的 null 合併指派也會保留重入建構所載入的內容。

建構出非 null 控制項時，先指派完全相同的 `dataContext`，接著清除宿主的
`ContentTemplate`，最後指派 `Content`。宿主自己的 `DataContext` 不變。
後續一般呼叫保留已發布的內容，不再建構；先延遲載入或先暖機皆如此。

建構與屬性通知同步執行。沒有重入防護，也不會在成功建構後再次檢查內容：
內容仍為 null 時，巢狀實體化可以再次建構，外層成功呼叫最後發布。
清除範本的通知同樣可在最後指派前載入暫時內容。這些行為保留凍結來源的指派語意。

建構及指派例外原樣傳出，不包裝，也不回復。指派失敗會停止後續步驟，
保留已完成的變動。API 不新增參數驗證：執行時 null 宿主在 false 延遲載入時
不被存取，其他情況擲出 `NullReferenceException`。執行時 null 內容或內容物件
可被接受；null 備援結果讓內容維持 null，允許再次實體化。
成功建構的控制項依傳入值接收 null 內容物件。

凍結的 PageHost 方法沒有數值限制、正數限制參數、產品上限或訊息常值。
其邊界為兩種載入許可值、null／非 null 內容，以及缺少／null／非 null 建構結果。
沒有 NFC 上限被移轉、放寬或新增。

## 來源與 Core 測試對照

所有 Core 測試均使用合成識別、回呼及確定性的同步控制點。
軌跡斷言依序比較每次記錄的選取讀取、啟用、成功更新、完成動作及還原更新。

| 凍結來源證據 | `tests/Nvt.Core.Tests/Shell/` 中的 Core 測試 |
| --- | --- |
| `ShellNavigationSystemTests.NavigationTransaction.cs:15` 的前進／返回案例 | `FailedActivationKeepsSourceInputsAndRestoresRetryableHistory`：保留來源輸入、原始失敗訊息、失敗時零次清除、重試一次清除、來源識別及後續返回目標。 |
| `ShellNavigationSystemTests.NavigationTransaction.cs:86` | `PostActivationSourceClearFailureRollsBackDestinationAndHistory`：來源重新啟用、保留輸入、原始失敗訊息、失敗時零次成功清除、重試一次成功清除及歷史還原。 |
| 歷史、相同目標完成與返回目標片段 | `ForwardAndBackCompletionPreserveHistoryAndCallbackOrder`、`EqualTargetCompletionActivatesWithoutAddingHistory`、`EqualReferencePageValuesStillActivateTheSuppliedTarget`。 |
| 一筆歷史邊界與缺少返回項目 | `ConstructorSeedsHomeWithoutInvokingCallbacks`、`ForwardAndBackCompletionPreserveHistoryAndCallbackOrder`、`BackCompletionWithOnlyHomeStillActivatesCapturedTarget`：一、二、三筆，再返回一筆；不會出現零筆。 |
| 額外啟用與回復特性 | `PostActivationBackFailureRestoresSourceBeforeHistory`、`FailedActivationAfterSelectionReactivatesSourceAndRestoresHistory`、`FailedEqualTargetActivationRestoresStateWithoutReactivation`、`RollbackUsesDefaultPageEqualityInsteadOfReferenceIdentity`。 |
| 例外順序 | `RollbackActivationFailureInterruptsHistoryRestoration`（選取發布前／後）、`StateChangedFailureReplacesOriginalFailureAfterHistoryRestoration`、`SelectionReadFailureBeforeActivationLeavesHistoryUntouched`（第一次／第二次讀取）、`RollbackSelectionReadFailureInterruptsHistoryRestoration`。 |
| 重入狀態觀察與巢狀完成 | `HostRefreshObservesChangedHistoryBeforeCompletionActions`、`ActivationRefreshCanCompleteNestedNavigation`、`OuterFailureRestoresHistoryFromBeforeNestedNavigation`。 |
| 確認關閉前擷取目標的契約 | `CapturedBackTargetCompletesAfterConfirmationCloseReentry`、`CapturedBackFailureRestoresSourceObservedAfterConfirmationCloseReentry`。 |
| 選取判斷與讀取順序 | `ForwardAppendComparesSelectionInsteadOfLatestHistoryEntry`、`ForwardAppendUsesSecondSelectionRead`。 |
| 新注入介面與識別邊緣輸入 | `ConstructorRejectsNullCallbacksInParameterOrder`、`StringPageIdentitiesRoundTripWithoutNormalization`、`NumericPageIdentitiesHaveNoPositiveLimitPolicy`：空回呼、空字串／空白／控制字元／Unicode 識別、零、負數及整數端點。 |

`NavigationClear.cs`、`NavigationClearModal.cs` 及 `SettingsNavigation.cs`
中的產品斷言仍是 NFC 證據。Core 不重製韌體、目錄、模態、焦點、麵包屑或像素測試資料。

PageHost 測試透過既有連結來源的 `AvaloniaTestHost` 及測試組件的單一註冊，
使用合成物件、控制項與範本。屬性回呼提供確定性的順序及重入控制點，不使用計時等待。

| 凍結來源證據或邊界 | `tests/Nvt.Core.Avalonia.Tests/Shell/PageHostTests*.cs` 中的 Core 測試 |
| --- | --- |
| `MainWindow.axaml.cs:758–764`，true／false 許可與既有內容邊界 | `EnsureContentUsesOnlyLazyAdmission`、`EnsureContentRetainsExistingContent`、`HelpersRetainNonNullEdgeValues`、`EnsureContentDoesNotAssignControlDataContext`、`EnsureContentAcceptsNullContentWithoutSealingTheHost`。 |
| `MainWindow.StartupWarmup.cs:44–54`，既有內容及兩種備援路徑 | `MaterializeContentRetainsExistingContent`、`MaterializeContentPreservesFallbackIdentities`。 |
| `MainWindow.StartupWarmup.cs:49,56–58`，完全相同的內容物件、一次建構及指派順序 | `MaterializeContentBuildsOnceAndAssignsInFrozenOrder`、`MaterializeContentDoesNotConsultTemplateMatch`。 |
| 先延遲載入與先暖機；拒絕延遲載入 | `LazyFirstAndWarmupFirstRetainTheirFirstContent`、`DeniedLazyAdmissionStillAllowsWarmup`。 |
| 執行時 null 宿主、內容及內容物件邊緣輸入 | `NullHostFollowsTheFrozenPredicateOrder`、`EnsureContentAcceptsNullContentWithoutSealingTheHost`、`MaterializeContentNullFallbackRemainsRetryable`、`MaterializeContentAssignsNullDataContextToBuiltControl`。 |
| 建構失敗與三個指派失敗邊界 | `BuildFailurePropagatesUnchangedAndAllowsRetry`、`AssignmentFailurePreservesPartialStateAndOrder`、`EnsureContentAssignmentFailurePropagatesUnchanged`、`FallbackAssignmentFailurePropagatesUnchanged`。 |
| 延遲載入及實體化發布時重入 | `LazyAssignmentReentryRetainsPublishedContent`、`MaterializedContentReentrySeesCompletedAssignments`。 |
| 成功／null Build 重入、巢狀建構、內容物件／範本通知重入與重入建構失敗 | `BuildReentryPreservesSuccessfulAndFallbackAssignmentRules`、`BuildReentryCanMaterializeAgainBeforeOuterPublication`、`DataContextReentryRetainsTheNestedContextChange`、`TemplateClearReentryRunsBeforeOuterContentAssignment`、`BuildFailureRetainsReentrantHostChanges`。 |
| `XamlControlStyleContractTests.Startup.cs` 的 `MainWindowDefersInactivePageAndModalContent` 與 `MainWindowWarmsCommonPagesAfterFirstFrameWithoutLoadingModals` | 方法本體的來源檢查對應至上述延遲載入及直接範本建構／發布測試。NFC 保留頁面承載註冊、延遲資源位置、暖機頁面順序、首幀時機、執行閒置控制及追蹤斷言。 |

`ShellPreloadSessionTests.Presentation.cs`、`ShellPreloadSessionTests.Cancellation.cs`、
`WindowLifetimeTests.Ready.cs` 及 `ShellScreenInventoryTests.cs` 的產品預載、取消、
視窗生命週期、頁面組合、版面、焦點及像素斷言保留在 NFC。
其中門檻與排程預算不屬於 PageHost。Core 以特性測試描述已擷取的方法，
不移轉這些產品測試套件。

不可變的 NFC 父版本證據涵蓋導覽、防護與確認重入、焦點、延遲內容識別、暖機順序
及啟動區段。NFC 採用時透過實際產品轉接層，比較歷史與 PageHost，使用固定的
父版本觀察值及不變的情境驅動程式。保留接縫仍由產品測試負責；方法證據不認證
更廣的工作區框架。

## 驗證

完成鎖定還原後，從方案根目錄執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

Core 測試涵蓋歷史與 PageHost 機制。NFC 在採用時對凍結父版本證明產品與像素一致。

## NFC 保留的工作區與預載清冊

下列每個路徑都相對於開頭指定的凍結 NFC 儲存庫。「NFC 擁有者」欄列出負責該接縫
的保留元件。所有判斷式、限制、訊息及產品測試預期值都由原有擁有者保留。
NFC 保留韌體、產品文字、schema、信任及發行權限。

| 保留接縫 | NFC 擁有者 | 來源路徑與片段 | 保留在 NFC 的原因 |
| --- | --- | --- | --- |
| 韌體防護與命令許可 | NFC 導覽轉接層：`ShellNavigationViewModel` 與 `MainWindowViewModel` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`：`NavigateToPage`、`GoBack`、`RequestNavigation`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Navigation.cs`：`HasPageSelectedFiles`。 | 已選韌體／編輯器輸入、不符狀態失效、同頁捷徑及首頁返回不動作都是產品政策。 |
| 確認、離開、來源清除與關閉通知重入 | NFC `ShellNavigationViewModel` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`：`RequestExitConfirmation`、`RefreshConfirmation`、`ConfirmNavigationAndClear`、`CancelNavigationClear`、`PendingNavigation`。 | NFC 在確認前擷取目的地，在關閉確認前擷取清除動作的來源，並負責忽略後續請求、離開覆蓋、取消時重新啟用、文字及通知。Core 在重入後完成傳入目的地。 |
| 麵包屑、標籤與命令 | NFC 導覽呈現 | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`：`RefreshNavigationTrail`、`NavigationPath`、`UpdateState`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationEntryViewModel.cs`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Navigation.cs`：`PageLabel`。 | 首頁／目前頁面的路徑、標籤驗證、在地化標籤、Toolkit 命令及命令通知都是產品呈現。 |
| 頁面識別與工廠 | NFC Shell 組合 | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.cs`：`ShellPage`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellViewModelFactory.cs`：`Create`；`src/NvtFwCombiner.Presentation.Avalonia/MainWindow.StartupFactory.cs`：`CreateStartupViewModel`。 | NFC 定義自己的頁面、宿主服務、版本標籤、啟動語言及偏好套用；Core 接受宿主定義的識別。 |
| 產品頁面組合與啟用 | NFC `MainWindowViewModel` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Construction.cs`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Context.cs`：`ApplySelectedPage`。 | 此處組合韌體／工作流程服務、報告、設定、執行工作階段及 MessageCenter。產品啟用負責驗證與還原工作流程內容，並在成功時執行 `Navigation.UpdateState()`。 |
| 工作區版面與產品範本 | NFC `MainWindow` XAML | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml`：資源引入、Shell 網格、頁面範本、動作列、載入／狀態表面及模態承載控制項。 | 幾何、祖先結構、可見性、資源位置、手勢、焦點及可及性都是 NFC 不變的 UI 契約。 |
| 頁面承載註冊與延遲載入許可 | NFC `MainWindow` | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`：第 734–756 行的 `ApplyDeferredShellContent` 與 `ViewModel_OnPropertyChanged`。 | NFC 選擇每個承載控制項、內容物件及產品可見性條件。只有第 758–764 行的 `LoadContent` 移至 `PageHost.EnsureContent`。 |
| 延遲設定與保留的編輯器狀態 | NFC `DeferredShellState` 與 Shell 轉接層 | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/DeferredShellState.cs`：`EnsureSettings`、`GetHexEditorWorkspace`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Navigation.cs`：`OpenSettings`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.HexEditor.cs`：`ShowHexEditor`。 | NFC 負責設定載入時機、快取的編輯器識別、檔案工作階段工廠、訂閱及保留輸入。 |
| 預載工作階段與排程政策 | NFC `ShellPreloadSession` | `src/NvtFwCombiner.Presentation.Avalonia/ShellPreloadSession.cs`：階段／嘗試記錄、`RunCatalogAsync`、`RunOptionalStagesCoreAsync`、報告與環境／診斷鏈、重試／略過／取消／排空、進度驗證及發布。 | 必要目錄載入許可、選用相依性、世代、工作預算、排空逾時、減少動態效果及在地化狀態都是產品生命週期政策。 |
| 啟動預載工作與就緒 | NFC `MainWindow` 與啟動組合 | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`：`RunStartupPreloadAsync`、`RunRequiredPreloadAsync`、`PresentPreloadStage`、`ApplyPreloadStage`、重試／略過／取消處理常式及 `ApplyShellInteractionState`。 | NFC 提供歷史／報告／診斷／檢視工作，在必要狀態發布後啟用 Shell、套用啟動選項，並負責狀態與焦點。 |
| 暖機頁面清單與執行閒置排程 | NFC `MainWindow` 暖機 | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.StartupWarmup.cs`：第 10–40 行的 `WarmDeferredShellAsync` 與第 61–142 行的排程／計時包裝。 | 五個承載控制項的順序、內容物件、進度、UI dispatcher 優先序、執行閒置等待、世代檢查、取消及追蹤名稱保留在本地。只有第 42–59 行的 `MaterializeContent` 移轉。 |
| 應用程式啟動包裝 | NFC `App` | `src/NvtFwCombiner.Presentation.Avalonia/App.axaml.cs`：`SetStartup`、`Initialize`、`OnFrameworkInitializationCompleted`。 | NFC 負責啟動狀態、編譯應用程式資源、偏好、桌面生命週期、主視窗建立及擷取結束政策。 |
| 桌面啟動包裝 | NFC `DesktopApplication` | `src/NvtFwCombiner.Presentation.Avalonia/DesktopApplication.cs`：`Run`、`DispatchLaunch`、`PrepareStartup`、`BuildAvaloniaApp`。 | 命令列驗證、公開請求完成、受保護啟動輸入、本地狀態組合、追蹤選用、字型及桌面生命週期都是應用程式選擇。 |
| 視窗啟動與生命週期包裝 | NFC `MainWindow` | `src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`：建構式、`OnOpened`、`RunStartupAfterOpenedAsync`、`OnClosing`、`OnClosed`、`Dispose`。 | NFC 負責首幀延後、視窗發布／許可、輸入載入、離開確認、取消及關閉／排空協調。 |
| 啟動區段建構與計時轉接層 | NFC 啟動診斷 | `src/NvtFwCombiner.Presentation.Avalonia/StartupTraceSession.cs`：`Create`、`MarkProfileAdmission`、`Mark`、`Complete` 及提供者轉接層；`src/NvtFwCombiner.Presentation.Avalonia/StartupTraceFileSink.cs`：`TryWrite`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.SystemActivity.cs`：`RecordStartupDuration`。 | NFC 提供里程碑／profile 對應、結束與預載區段、診斷 schema、時間／配置量轉接層及活動文字。這些包裝不屬於 Shell 的兩個內容方法。 |
| MessageCenter 型別化 facade | NFC `MessageCenterViewModel` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MessageCenterViewModel.cs`：型別化服務／報告繫結、在地化投影、啟動更新、環境發布及匯出內容。 | facade 串接產品診斷、韌體就緒、報告所有權、文字及命令，不將這些概念加入 Shell API。 |
| MessageCenter 產品範本與文字 | NFC `MessageCenterModal` 與 `ShellTextResources` | `src/NvtFwCombiner.Presentation.Avalonia/Views/MessageCenterModal.axaml`；`src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml`：`MessageCenterModalHost`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.MessageCenter.cs`；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellTextResources.Localized.cs`：MessageCenter 標籤。 | 報告表格、儲存挑選器事件、活動版面、產品標籤／格式化方法、模態祖先結構及可及性保留在 NFC。 |
| 報告組合與檔案挑選器轉接層 | NFC 報告呈現與 `MainWindow` | `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Construction.cs`：`Reports` 建構；`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/MainWindowViewModel.Report.cs`；`src/NvtFwCombiner.Presentation.Avalonia/MainWindow.Report.cs`：`LoadReportJsonCoreAsync`、`ApplyStartupReportAsync`、`ApplyLaunchPage`。 | 報告語意、模態／toast 整合、檔案許可、啟動參數、投影世代及焦點返回都是產品行為。 |
| 報告與偏好持續化轉接層 | NFC 本地狀態儲存與視窗組合 | `src/NvtFwCombiner.Presentation.Avalonia/ReportHistoryFileStore.cs`；`src/NvtFwCombiner.Presentation.Avalonia/ShellPreferenceFileStore.cs`；`src/NvtFwCombiner.Presentation.Avalonia/MainWindow.axaml.cs`：持續化建構、`Reports_OnPropertyChanged`、`PostLocalStateSaveOutcome` 及偏好儲存繫結。 | NFC 負責 schema、路徑、位元組／保留預算、備援、快照擷取、重試通知及關閉儲存政策。泛型編解碼／儲存協調有獨立的 Persistence 邊界。 |

韌體回呼仍由 NFC 的 `WorkflowSessionPresentationViewModel` 負責：
`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/WorkflowSessionPresentationViewModel.Slots.cs`
包含 `HasSelectedInputs` 與 `ClearSelectedInputs`；
`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/WorkflowSessionPresentationViewModel.FirmwareNumberMismatch.cs`
包含 `InvalidateFirmwareNumberMismatch`；
`src/NvtFwCombiner.Presentation.Avalonia/ViewModels/WorkflowSessionPresentationViewModel.WorkflowContext.cs`
包含 `ValidatePageActivation` 及工作流程啟用／還原。導覽繫結與 `ApplySelectedPage`
委派給這些產品擁有者。

保留的預載證據為
`tests/NvtFwCombiner.UiSmoke.Tests/ShellPreloadSessionTests.cs`、
`tests/NvtFwCombiner.UiSmoke.Tests/ShellPreloadSessionTests.Presentation.cs` 及
`tests/NvtFwCombiner.UiSmoke.Tests/ShellPreloadSessionTests.Cancellation.cs`。
這些測試涵蓋型別化結束發布、必要階段互動、相依性、重試世代、取消、遲到進度，
以及有界排空後的實際完成。其來源與上述導覽、視窗生命週期、組合、版面及焦點
套件一起保留為 NFC 證據。

## 既有 Panels 比較

比較來源為 Core 快取的 `origin/main`，完整提交
`b98099a43553f4d04f084a3a33bc027bd9a8c95c`，其中包含 Core #30 的 Panels
合併（`2af6b30f181a1c0006d6070339edc90366afbf1e`）。其控制項、樣式、測試及
[Panels 模組契約](Panels.zh-TW.md) 是 NFC 保留組合的比較候選。

| 候選 | 比較提交的來源 | 適用契約與比較限制 |
| --- | --- | --- |
| `Panels.WorkspaceShell` | `src/Nvt.Core.Avalonia/Panels/WorkspaceShell.cs`；`src/Nvt.Core.Avalonia/Panels/PanelsStyles.axaml`；`tests/Nvt.Core.Avalonia.Tests/Panels/WorkspaceShellTests.cs`。 | 以範本配置標題、摘要、工具列、兩個主要欄位及頁尾，內容由呼叫端提供；預設欄寬為 `2.2*` 與 `*`。測試描述各插槽、列順序及即時欄寬更新。此版面不提供 NFC 頁面識別、防護、延遲承載控制項、覆蓋層或預載排程。 |
| `Panels.CollapsiblePanel` | `src/Nvt.Core.Avalonia/Panels/CollapsiblePanel.cs`；`src/Nvt.Core.Avalonia/Panels/PanelsStyles.axaml`；`tests/Nvt.Core.Avalonia.Tests/Panels/CollapsiblePanelTests.cs`。 | 平面切換標題與本文：未設定的初始展開使用 `DefaultExpanded`，明確值優先，停用摺疊時強制展開。測試描述鍵盤／指標切換、可見性及標題樣式。其幾何、向量箭頭與樣式選擇不證明等同 NFC 的產品面板或預載狀態 `Expander`。 |

Shell 不採用 Panels 控制項，也不替換 NFC 工作區標記。日後採用 Panels 時，
必須另有明確範圍，並對相同凍結 NFC 父版本證明行為、物件識別、焦點、可及性、
版面及解碼 UI 像素零差異。Panels 來源與合成測試無法提供該產品證據；其原宿主
接受視覺差異的政策不放寬 NFC 不變的快照契約。

## NFC 採用規則

歷史與 PageHost 是已接受的目前移轉。更廣的移轉需要新的明確擁有者範圍，並在
實作前固定來源片段、寫入路徑及相依性。此清冊保留產品所有權，不授權更廣刪除。
共用專案、根目錄計畫、元件清單、版本、參照及鎖定檔由既有整合擁有者負責。

NFC 以自己的獨立 PR 採用本模組。建置時透過 `core-packages.json`（Core #61）下載已驗證且具
版本的 `Nvt.Core` 與 `Nvt.Core.Avalonia` nupkg，使用精確 `[x]` 版本、鎖定還原
及限定至下載資料夾的來源對應。清單記錄每個套件的 Release 標籤與 SHA-256。
Core 與 NFC 維持各自的版本化發行。
共用套件參照、版本、來源對應及鎖定檔由 NFC 擁有，不加入指向 Core checkout 的 ProjectReference。

NFC 在改接這些方法前，須取得確切已審查模組套件的來源與行為證據。Core 擷取與 NFC
採用各自保留模組 PR 及獨立發行；NFC 採用目標為下一個相容的 1.2.x 修補版本。
此清冊不改變套件閉包。

NFC 改接兩個方法的呼叫端；只有套件來源及完整可執行值、物件識別與事件軌跡符合
凍結父版本後，才刪除本地兩個方法本體、泛型歷史儲存及完成回復方法本體。
NFC 保留產品轉接層，並在相同記錄的 OS、解析字型、DPI、主題、renderer、viewport、
motion、輸入、時間及 IDs 下證明解碼 UI 像素零差異，記錄比較產物的 SHA-256。
八個舊字型值保持不變；新的 Core 字型角色不屬於此次採用。
像素比較要求相同解碼尺寸及最大通道差異為零，不使用裁切、遮罩、正規化或容差。
