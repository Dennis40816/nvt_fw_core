[English](Shell.md)

# Shell

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Presentation.Avalonia/ViewModels/ShellNavigationViewModel.cs`：第 15 行的歷史初始化、第 47 行的 `CanGoBack`、第 115 行的上一頁目標，以及第 171–203 行的完成導覽與回復機制。第 74–85 與 107–119 行的命令流程界定轉接邊界；其中的防護條件與捷徑保留在 NFC。

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Shell` 提供同步、僅依賴 BCL 的導覽歷史輔助類別，頁面識別由宿主定義。
它不依賴產品、Toolkit 或 Avalonia，也不公開可變動的歷史集合。本次擷取涵蓋
歷史與啟用失敗回復。頁面承載、工作區組合及預載排程屬於其他機制。

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

## 驗證

完成鎖定還原後，從方案根目錄執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

Core 測試涵蓋歷史機制。NFC 在採用時對凍結父版本證明產品與像素一致。

## NFC 所有權與採用

NFC 保留 `ShellPage`、頁面識別與工廠、韌體防護、首頁返回不動作政策、同頁命令捷徑、
確認與離開狀態、來源清除政策、文字、命令、麵包屑、啟動包裝及 MessageCenter facade。
`MainWindowViewModel.Context.cs` 保留產品頁面啟用與成功時的
`Navigation.UpdateState()` 更新；`MainWindowViewModel.Construction.cs` 保留產品組合。

NFC 以自己的獨立 PR 採用本模組。該 PR 使用 `vendor/nuget/` 中已驗證且具版本的 `Nvt.Core` nupkg，
搭配精確 `[x]` 版本、鎖定還原、套件來源對應，以及 `SOURCE.md` 中的來源與套件 SHA-256。
套件參照與鎖定檔由 NFC 擁有。套件與可執行行為都符合凍結父版本後，NFC 只刪除本地泛型歷史儲存與
完成回復方法本體。NFC 保留自己的產品轉接層，並在相同的已記錄環境下證明 UI 快照完全一致。
