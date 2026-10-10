[English](Threading.md) | [中文](Threading.zh-TW.md)

# Threading

## UiEventRunner

[`UiEventRunner`](../../../src/Nvt.Core/Threading/UiEventRunner.cs) 是 `Nvt.Core.Threading` 中不依賴 UI 框架的 sealed helper，目標為 `net10.0`，僅使用 BCL。Host 提供主要與緊急回報函式；runner 不依賴 Avalonia，也不使用全域回報狀態。

### 公開 API 與用法

~~~csharp
public UiEventRunner(Action<string, Exception> report, Action<string, Exception> fallbackReport);
public void Run(string operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
public Task RunAsync(string operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
~~~

同步事件處理函式可呼叫 `uiEvents.Run("CopyReport", token => CopyReportAsync(token), lifetimeToken)`。`Run` 委派給完整觀察結果的 `RunAsync`，在 delegate 交回尚未完成的工作時返回。兩者都在呼叫端執行緒與同步內容中開始，不將 action 排入背景執行緒；await 也保留該內容以回報失敗。

- delegate 同步擲出的例外與 faulted task 都會將原始操作名稱及例外交給主要回報函式一次。
- 僅當傳入的 token 已取消，且例外攜帶同一 token 時，取消才不回報。攜帶其他 token 的例外仍視為錯誤，即使兩個 token 都已取消。
- 若操作把傳入的 token 與其他來源連結（例如加入逾時），取消例外會攜帶連結後的 token，runner 會回報它。請在操作內捕捉，並在傳入的 token 確實是被取消的那一個時，用 `cancellationToken.ThrowIfCancellationRequested()` 重新擲出。
- 主要回報函式擲出例外時，fallback 會收到相同的操作名稱與原始操作例外一次。fallback 也擲出例外時，runner 會吞下該失敗。
- 回報函式、操作名稱與 action 為 null 時，都在公開呼叫邊界同步擲出 `ArgumentNullException`。允許空操作名稱，並原樣傳給回報函式。
- 操作本身負責 busy flag、取消來源、清理用的 `finally` 與成功狀態。觀察 task 完成不代表失敗的儲存或匯出成功。

### 原始碼採用與來源

此 helper 是 owner 於 2026-10-09 核准的 C# 健康度計畫第 7 節 H04 新增工作，並非從 app 凍結基準抽取。NFH 與 NFU 使用 Core 套件；NFC 複製 canonical 檔案並核對 SHA-256，不新增 Core 執行期相依。

定義 `NVT_CORE_SOURCE_CONSUMPTION` 可讓相同型別以 `internal` 編譯，命名空間仍為 `Nvt.Core.Threading`。Consumer CI 必須拒絕同時編譯副本與參考套件（包含間接相依）。採用套件時移除副本與 symbol。[原始碼採用指南及 manifest](../../../tools/source-consumption/README.md) 說明複製、LF 保留、驗證與更新方式。

### Runner 驗證

[`UiEventRunnerTests`](../../../tests/Nvt.Core.Tests/Threading/UiEventRunnerTests.cs) 涵蓋成功、兩種失敗形式、token 一致性與取消狀態、回報函式失敗隔離、在呼叫端內容立即執行、`Run` 尚有未完成工作時返回，以及 null 檢查。佇列式同步內容記錄 Post 例外；訊號等待有時間上限，不使用固定 sleep。[獨立原始碼採用測試專案](../../../tests/Nvt.Core.SourceConsumption.Tests/) 不參考 Core，直接編譯連結檔案，驗證 internal 可見性、SHA-256 與位元組長度。

~~~powershell
./tools/source-consumption/New-SourceConsumptionManifest.ps1 -Check
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Threading.UiEventRunnerTests"
dotnet test tests/Nvt.Core.SourceConsumption.Tests/Nvt.Core.SourceConsumption.Tests.csproj --no-build
~~~

App 事件處理函式遷移與 consumer CI 整合由各自儲存庫完成。

## 0.9.0 前的不相容變更

`UiThread.IsUiThreadThatRunsALoop` 已移除。
請使用 `TryGetRunningDispatcher` 取得已登錄的 dispatcher。
請使用 `IsCurrent` 檢查執行中的 UI 執行緒與目前應用程式存取權。
僅測試布林值時，可在測試內自行計算 `hasThreadAccess && dispatcherRunsLoops`。

NFH 必須在升級 Core 套件前更新 `CadLoadOverlayFrameYieldPolicyTests`。
將 helper 斷言改為區域判斷式或實際 dispatcher 證據。
使用已接受的套件重新建置既有 dispatcher 呼叫端。
登錄、替代查詢與 dispatcher 存取行為維持不變。
Core 測試現在透過實際 headless dispatcher 執行背景工作。

`src/Nvt.Core.Avalonia/Threading/` 的 `UiThread` 使用命名空間 `Nvt.Core.Avalonia.Threading`。它以程序內所有執行緒共用的靜態登錄保存可執行迴圈的 UI dispatcher，不依賴 Avalonia 的全域 dispatcher 欄位。登錄與查詢保留來源的 `Volatile.Write` 和 `Volatile.Read`。登錄沒有取消方法；由應用程式啟動流程負責登錄。

## 公開 API

靜態類別公開三個 dispatcher 方法：

```csharp
public static void RegisterRunningDispatcher(Dispatcher dispatcher);
public static bool TryGetRunningDispatcher(out Dispatcher? dispatcher);
public static bool IsCurrent(out Dispatcher? dispatcher, out Avalonia.Application? application);
```

- `RegisterRunningDispatcher` 遇到 null 時擲出 `ArgumentNullException`，參數名稱為 `dispatcher`。僅保存 `SupportsRunLoops` 為 true 的 dispatcher；其他登錄不改變既有值。
- `TryGetRunningDispatcher` 在沒有目前應用程式、沒有登錄或已登錄 dispatcher 不支援執行迴圈時，回傳 false 並將 out 值設為 null。其他情況回傳已登錄的 dispatcher，背景執行緒也可查詢。
- `IsCurrent` 僅在已登錄 dispatcher 支援執行迴圈、允許目前執行緒存取，且存在目前應用程式時回傳 true。執行緒存取失敗時，dispatcher out 值保留已登錄的 dispatcher，application out 值為 null。沒有目前應用程式時，兩個 out 值皆為 null。

## 來源

凍結的父版本基準：NFH、儲存庫 `Dennis40816/nvt-freeform-helper`、ref `1.3.x`、完整 commit `e01e07a361b8dc264a06b3741f40274feeeace2d`、Avalonia 11.3.12 與 xUnit 2。抽取路徑：

- `src/FreeformHelper.UI/Services/UiThread.cs`
- `tests/FreeformHelper.Tests/UI/TestHost/UiThreadTests.cs`

此 helper 及其既有 fallback 測試已移植至 Core。變更包含命名空間、公開可見性、版權／XML 文件、測試命名與初始化。公開成員 `IsUiThreadThatRunsALoop` 已移除（見已知差異）。

## 驗證

Core 使用 Avalonia 12.1.1 與 xUnit v3。`tests/Nvt.Core.Avalonia.Tests/Threading/` 的測試沿用既有 headless `ThemeTestApplication`，沒有新增應用程式 attribute 或 host。每個測試結束時保留目前 UI dispatcher 的登錄。Fallback 測試也會在 `finally` 還原 Avalonia 的全域欄位。

移植的測試暫時清空 `Dispatcher.s_uiThread`，確認仍可取得已登錄的 dispatcher，且不會重新填入該欄位。行為特徵測試涵蓋透過執行中的 dispatcher 排入背景工作、null 登錄與參數名稱、headless UI 執行緒上的 dispatcher 參考一致性、UI 執行緒存取成功，以及背景執行緒失敗時保留 dispatcher out 值、不回傳應用程式。

套件已還原後執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Avalonia.Tests.Threading"
```

NFH 採用時，先以上述父版本 SHA 凍結結果。切換至 Core 前後皆以 `--no-restore` 建置 NFH，並執行 `dotnet test tests/FreeformHelper.Tests/FreeformHelper.Tests.csproj --no-build --filter "FullyQualifiedName~FreeformHelper.Tests.UiThreadTests"`，再執行完整 `FreeformHelper.Tests` 專案以涵蓋使用端。保留既有 fallback 斷言，比較回傳 dispatcher 的參考、fallback 期間保持空值的全域欄位、null 登錄的例外／參數、其餘判斷式的布林結果，以及 UI／背景執行緒的回傳值與應用程式參考。在父版本 helper 與 Core 執行相同的行為特徵案例。不得更新預期證據以掩蓋差異。NFH 採用及其前後驗證仍不在本次範圍內。

## 已知差異（Known differences）

- Core 的 helper 為 public；NFH 的類別及成員為 internal。
- Core 沒有 `IsUiThreadThatRunsALoop`。它只供一個測試使用，受支援的判斷式是 `TryGetRunningDispatcher` 與 `IsCurrent`。NFH 在採用 Core 前保留本地副本，其布林真值表測試以本地判斷式測試的形式留在 NFH。
- Avalonia 12.1.1 仍有 `Dispatcher.s_uiThread`，因此保留原本以反射驗證 fallback 的情境。不檢查其他 dispatcher 私有成員，執行階段也不需要調整 Avalonia API。
- 測試改用 xUnit v3 與 Core 既有的 headless 應用程式，取代 xUnit 2、NFH bootstrap 及其 `HeadlessUiSerial` collection。測試方法名稱不含底線，以符合 Core analyzer。

## 留在 NFH 的內容

`tests/FreeformHelper.Tests/UI/TestHost/HeadlessDispatcherSetup.cs` 留在 NFH，包含平台初始化驗證與登錄。NFH 的啟動登錄、bootstrap、記錄、資源解析、runtime query、view／view model 使用端及其產品行為皆不變。本次抽取不在 NFH 採用 Core，也不移動其他 helper。
