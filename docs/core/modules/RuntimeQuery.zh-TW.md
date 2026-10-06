[English](RuntimeQuery.md) | [中文](RuntimeQuery.zh-TW.md)

# RuntimeQuery

## 用途

`Nvt.Core.RuntimeQuery` 提供從 FreeformHelper（NFH）擷取的 JSON 封套與本機具名管道傳輸。每個連線依序處理一筆請求，使用位元組模式的非同步管道。不依賴 Avalonia 或 NLog，也不解讀產品命令。

## 公開 API

| API | 契約 |
| --- | --- |
| `RuntimeQueryRequest(Version, Command, Args)` | 請求欄位依此順序排列；引數可以是 null。 |
| `RuntimeQueryError(Code, Message)` | 由呼叫端擁有的錯誤代碼與訊息。 |
| `RuntimeQueryResponseEnvelope(Ok, Data, Error)` | 回應欄位依此順序排列；`Success(data)` 與 `Failure(code, message)` 保留明確的 null 屬性。 |
| `RuntimeQueryProtocol.CompactJsonOptions` | 屬性名稱採 camelCase、保留 null、使用預設 JSON 跳脫規則、反序列化區分大小寫、不縮排。字典鍵保留原本大小寫。 |
| `RuntimeQueryProtocol.PrettyJsonOptions` | 相同設定，另使用兩個空白縮排與序列化器預設的平台換行字元。用於呼叫端輸出，不用於管道訊息。 |
| `RuntimeQueryIpcServer(...)` | 實例接收管道名稱、協定版本、以毫秒計的正數讀取與關閉逾時、錯誤回呼、診斷回呼及請求處理委派。 |
| `Start()` / `DisposeAsync()` | 啟動一次；執行期間重複啟動與重複釋放皆不產生額外作用。釋放後啟動會擲回例外。釋放會關閉作用中的管道、取消傳輸與處理委派的等待，並限制等待取消回呼與執行迴圈的時間。 |
| `RuntimeQueryIpcClient.SendRequest(pipeName, request, timeoutMs, error)` | 同步送出一筆請求，使用涵蓋連線、寫入與讀取的正數總逾時預算。開始寫入／讀取計時前扣除連線耗時，保留來源的整數毫秒取整與至少一毫秒的剩餘預算。 |

伺服器處理委派為 `Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>>`。接收反序列化後的請求、設定的協定版本與關閉 token。空白行與格式錯誤的 JSON 由傳輸層拒絕；JSON null 與版本不符的請求會交給處理委派，與凍結傳輸一致。null／版本與命令驗證仍留在 NFH 的 `RuntimeQueryUseCase`，於 UI 派送後執行。合成測試處理委派重現現有驗證封套，不將產品驗證移入 Core。

錯誤回呼為 `Func<RuntimeQueryFailure, string?, RuntimeQueryError>`。`InvalidJson`、`HandlerError`、`IoError` 與 `ClientError` 的 detail 是原樣傳遞的例外訊息；其他情況為 null。Core 將回傳的錯誤包裝成失敗封套。`RuntimeQueryFailure` 除了這些失敗，還包含 `RequestTimeout`、`EmptyRequest`、`EmptyResponse`、`InvalidResponse`、`ConnectionTimeout` 與 `ClientTimeout`；列舉名稱不是線上傳輸的錯誤代碼。

可省略的診斷回呼為 `Action<RuntimeQueryDiagnostic, Exception?>`。事件涵蓋啟動、停止、連線失敗、請求失敗、處理委派失敗、關閉失敗與關閉逾時。呼叫端提供日誌文字與日誌相依套件。錯誤與診斷回呼必須迅速返回，且不得擲回例外。

Core 不提供產品預設的讀取限制、關閉上限、用戶端預算或協定識別。NFH 提供現有的 5,000 毫秒讀取逾時、1,500 毫秒預設 CLI 預算、協定版本、管道名稱、錯誤對應與日誌。凍結來源的關閉測試採用 1,500 毫秒完成上限；擷取的伺服器明確接收此上限。

管道寫入端使用精簡 JSON、不含 BOM 的 UTF-8，以及採用平台換行字元的 `StreamWriter.WriteLineAsync`（Windows 為 CRLF）。讀取端保留來源的 `Encoding.UTF8` 並停用編碼偵測。`StreamReader` 仍會消耗 UTF-8 編碼本身的前導碼；不偵測其他編碼。連線保持開啟但回應未以換行結尾時會逾時。未完成框架即關閉管道可能產生 IO 錯誤，包含讀取器／寫入器釋放期間，與來源一致。格式化輸出使用序列化器預設的平台換行字元（Windows 基準為 CRLF）；最後的輸出換行由呼叫端負責。

取消會停止等待忽略 token 的處理委派，但無法終止該委派自己的工作。關閉逾時透過診斷回報。呼叫端仍須負責處理委派的合作式清理。這些有時間上限的生命週期調整不改變請求或回應位元組。

## 凍結來源

- 來源儲存庫：`Dennis40816/nvt-freeform-helper`。
- 來源 ref：`1.3.x`。
- 完整凍結 commit：`e01e07a361b8dc264a06b3741f40274feeeace2d`。
- 擷取的來源路徑：
  - `src/FreeformHelper.UI/Services/RuntimeQueryProtocol.cs`：封套與 JSON 設定，不包含產品識別常數。
  - `src/FreeformHelper.UI/Services/RuntimeQueryIpc.cs`：實例伺服器傳輸及用戶端 `SendRequest`／剩餘時間計算，不包含靜態 host 與 CLI 解析／輸出。
- 移植的傳輸測試來源：相同 commit 的 `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryIpcTests.cs`。

由 host 建立的 commit 訊息必須包含此儲存庫、ref、完整 SHA 與上述來源路徑。本次擷取不更新凍結基準，也不在 NFH 採用 Core。

## Core 驗證

所有測試資料皆為合成資料，並使用唯一管道名稱。測試位於 `tests/Nvt.Core.Tests/RuntimeQuery/`：

- `RuntimeQueryProtocolTests`：固定精簡與縮排請求、成功及失敗封套的 UTF-8 位元組字面預期值；驗證屬性順序、大小寫、字典鍵、null、跳脫與區分大小寫的反序列化。
- `RuntimeQueryIpcTests`：精確的精簡回應框架、含／不含前導碼的實際 UTF-8 請求、空白及格式錯誤請求、null／版本錯誤、可設定的讀取逾時、連線逾時後恢復、診斷及有時間上限的關閉。
- `RuntimeQueryIpcClientTests`：不含 BOM 的請求框架位元組字面預期值、null 引數、已接受但未回應及回應未以換行結尾的逾時案例、單一總逾時預算、空白／null／格式錯誤回應與中斷管道 IO 訊息傳遞。
- `RuntimeQueryTestValues`：呼叫端設定與凍結的 NFH 錯誤代碼／訊息。Core 正式程式碼不包含這些產品字串。

來源 `RuntimeQueryIpcTests` 的五個案例皆已移植：未啟動即停止、等待連線時停止、已連線但閒置時停止、已接受請求但未回應，以及處理請求時擲回例外。靜態 host／UI 測試設定改為實例伺服器與合成的擲回例外委派；保留原始處理錯誤代碼與訊息。另以忽略取消及同步阻塞的處理委派驗證設定的關閉行為與管道釋放。

字面預期值固定序列化與管道框架契約，不只驗證往返結果。格式錯誤的 JSON 與 IO 錯誤會依來源行為保留序列化器／作業系統例外訊息；測試在相同執行環境比較傳遞的 detail。文字可能隨執行環境或作業系統語系變動。

使用已還原的套件，設定 `AVALONIA_TELEMETRY_OPTOUT=1` 後執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.RuntimeQuery"
```

## NFH 切換時的證據

採用 Core 是獨立的 NFH 變更。保留凍結行為，並在替換重複傳輸實作前後執行以下全部檢查：

- `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryIpcTests.cs`（`RuntimeQueryIpcTests`），包含無介面 UI 處理失敗案例及所有關閉／逾時案例。
- 每個 `RuntimeQueryUseCaseTests` partial 檔案：
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryUseCaseTests.cs`
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryUseCaseTests.ExportAndStatus.cs`
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryUseCaseTests.NotchAndPad.cs`
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryUseCaseTests.Simulation.cs`
- 使用相同合成應用程式狀態與引數比較 CLI 輸出：預設格式化輸出、`--json-pretty`、`--json-compact`、help、代表性成功命令、無效／缺少的命令與選項、明確指定的 `--timeout-ms`、沒有執行中實例、未回應請求及處理委派失敗。比較 stdout 與 stderr 位元組、屬性順序、跳脫、null、內部與最後換行、精確錯誤代碼／訊息及程序結束代碼。兩次執行的動態狀態必須固定。
- 對相同合成封套，將原始請求與回應框架與凍結傳輸比較，包含格式錯誤／空白請求及閒置讀取逾時。將 NFH 現有設定與錯誤對應提供給 Core。
- 已知差異：請求或回應是格式正確、但形狀不對的 JSON（例如 `42` 或 `[]`）時，序列化器的訊息會寫出 Core 的型別名稱，例如 `Nvt.Core.RuntimeQuery.RuntimeQueryRequest`。凍結來源寫的是 `FreeformHelper.UI.Services.RuntimeQueryRequest`。錯誤代碼不變。要保留凍結文字，NFH 的錯誤對應在 `InvalidJson` 與 `ClientError` 的細節中把 `Nvt.Core.RuntimeQuery.` 換成 `FreeformHelper.UI.Services.`。否則由採用 PR 記錄這項差異。

Core 特徵測試通過可確立擷取的傳輸契約；NFH 的產品測試及 CLI／框架比較則確立採用時的零差異。本次擷取不執行上述 NFH 採用檢查。

## 留在 NFH 的內容

NFH 保留 `freeformhelper.runtime.v1`、協定版本值、靜態 host 擁有權、`ShellViewModel`、`RuntimeQueryUseCase`、`Dispatcher.InvokeAsync` 整合、null／版本驗證、命令登錄與驗證、選項解析、CLI 使用說明與輸出、結束代碼、逾時預設值、關閉政策、NLog 及全部產品錯誤／診斷文字。產品資料與應用程式流程不屬於本模組。
