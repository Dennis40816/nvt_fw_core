[English](Lifecycle.md) | [中文](Lifecycle.zh-TW.md)

# Lifecycle

## 0.9.0 前的不相容變更

`UndoService.TryPop` 現宣告 `[NotNullWhen(true)] out UndoAction? action`。
空堆疊仍回傳 false 與 null。
成功彈出仍提供最新動作，不會執行它。
在成功分支內使用動作，無須 null 抑制：

```csharp
if (undoService.TryPop(out UndoAction? action))
{
    action.Undo();
}
```

請依條件式註記重新建置呼叫端。
保留空堆疊處理與呼叫端負責執行的行為。

`src/Nvt.Core/Lifecycle/`（命名空間 `Nvt.Core.Lifecycle`）提供不依賴 UI 的更新合併與復原堆疊。
兩個輔助類別都使用呼叫端提供的委派，僅依賴 .NET，不包含 Avalonia、
dispatcher 或 NFH 型別。

## 公開 API 與行為

- `CoalescedRefresh(Action<Action> schedule, Action refresh)` 不接受 null
  委派。`Request()` 可從任何執行緒安全呼叫，並在排入的回呼開始執行前合併
  多次要求。回呼會先清除排程旗標，再呼叫 `refresh`，因此在更新期間重入的
  要求會排入另一個回呼。
- `CoalescedRefresh.Reset()` 清除旗標，讓下一次要求可以重新排程。
  它**不會**取消已排入的回呼；該回呼仍會執行更新。
  更新動作必須能容許擁有者放手之後仍被呼叫。
- 排程例外會從 `Request()` 傳出，而且不會清除旗標。
  後續要求仍會被合併，直到 `Reset()` 或已排入的回呼清除旗標。
  不會自動重試。
- `UndoService.CanUndo` 表示堆疊是否含有項目。
  `Push(Action undo, string description)` 儲存項目，但不執行動作。
  `TryPop([NotNullWhen(true)] out UndoAction? action)` 依後進先出順序移除最新項目，但不執行動作。
  空堆疊會傳回 `false`，並將 `action` 設為 null。true 結果保證動作非 null。
  堆疊未提供同步保護。
- `UndoAction(string Description, Action Undo)` 是 sealed record。
  堆疊原樣保留描述與委派，由呼叫端執行 `Undo`。

## 凍結來源

- 儲存庫：`Dennis40816/nvt-freeform-helper`
- Ref：`1.3.x`
- 完整 commit SHA：`e01e07a361b8dc264a06b3741f40274feeeace2d`
- 擷取的實作檔案：
  - `src/FreeformHelper.UI/Services/CoalescedRefresh.cs`
  - `src/FreeformHelper.UI/Services/UndoService.cs`
- 移植的更新測試：
  - `tests/FreeformHelper.Tests/UI/Services/CoalescedRefreshTests.cs`
- 復原行為參考：
  - `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.CommandsAndUndo.CadEditing.cs`
    （`Undo_RevertsDisplayToggleChange`、`DeleteSelectedCadPadsCommand_CanUndoToRestoreHiddenPads`、
    `ClearCombinedCadPadsCommand_CanUndoBackToCombinedState`）
  - `tests/FreeformHelper.Tests/UI/ViewModels/FreeformHelperViewModelTests.CommandsAndUndo.GeometryTransforms.cs`
    （`RotateSelectedCadPadsCommand_RotatesSelectedPadAndCanUndo`、
    `OffsetSelectedCadOutputFwDiffIndicesCommand_ShiftsSelectedVisibleCadDiffsAndCanUndo`）

原始抽取變更僅限命名空間、將 `CoalescedRefresh` 設為 public、著作權標頭及 API
文件。方法主體與復原 record 宣告保留凍結來源的實作。四個更新測試均已移植；
reset 測試重新命名，以描述重新排程而不暗示取消。
復原測試使用合成值改寫狀態還原與 `CanUndo` 的斷言，並明確由呼叫端執行
彈出項目的委派。

## 驗證

套件還原完成後，在 Core 儲存庫根目錄執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Lifecycle"
```

測試涵蓋突發要求合併、完成後重新排程、重入、reset 允許再次排程、已排入回呼
在 reset 後仍會執行，以及排程例外使旗標維持設定直到 reset 的行為。
復原測試涵蓋由呼叫端還原狀態、後進先出順序、剩餘項目與 `CanUndo`、push 或
pop 均不執行動作、空堆疊 pop 傳回 null，以及原樣保留描述與委派。

### NFH 採用時的零差異檢查

NFH 採用 Core 屬於另一項變更。在替換 NFH 輔助類別之前及之後，於 NFH
儲存庫根目錄使用相同環境執行既有測試：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build FreeformHelper.sln --no-restore
dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.CoalescedRefreshTests|FullyQualifiedName~FreeformHelper.Tests.FreeformHelperViewModelTests"
```

view-model 篩選涵蓋所有 `CommandsAndUndo*.cs` 測試及既有的設定載入測試。
對照凍結基準，比較相同測試名稱、數量與斷言結果：回呼佇列與更新次數、
原始狀態還原、`CanUndo` 與復原命令可用性、未變動的狀態字串，以及既有的
復原抑制與設定載入行為。另執行上述 Core Lifecycle 行為刻畫測試。
對於相同且具代表性的 request/reset/pop 序列，比較排入的回呼、更新次數、
彈出動作順序、描述、`CanUndo` 及呼叫端執行後的效果。
保留凍結的預期值，不以更新預期值的方式接受差異。

## 保留在 NFH 的內容

復原抑制、設定載入政策、領域專用復原資料、狀態字串、命令通知及 dispatcher
選擇，均保留在 NFH 的 view model 與 UI 服務中。這兩個輔助類別不管理上述
政策。本次擷取不修改 NFH，也不在 NFH 中採用 Core。
