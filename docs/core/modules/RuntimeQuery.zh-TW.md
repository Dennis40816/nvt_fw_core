[English](RuntimeQuery.md) | [中文](RuntimeQuery.zh-TW.md)

# RuntimeQuery

## 用途

`Nvt.Core.RuntimeQuery` 提供從 FreeformHelper（NFH）擷取的 JSON 封套與本機具名管道傳輸。每個連線依序處理一筆請求，使用位元組模式的非同步管道。不依賴 Avalonia 或 NLog，也不解讀產品命令。

## Commands and arguments

Core 現在提供命令路由、請求檢查與五個通用引數輔助方法。

| API | 契約 |
| --- | --- |
| `RuntimeQueryCommandRouter(handlers)` | 使用工具提供的委派字典。不新增命令，也不更改字典的鍵比較方式。 |
| `RegisteredCommands` | 建構時依字典列舉順序保存唯讀名稱清單。工具須先依登錄順序建立字典。 |
| `RouteAsync(commandText, args)` | 去除命令前後空白，以 invariant culture 轉為小寫。將原始引數字典傳給處理委派。 |
| `ExecuteAsync(request, expectedVersion)` | 先檢查 null，再以 ordinal 相等比較版本，最後路由命令。版本由工具提供。 |
| `RuntimeQueryArgumentParser.TryGetIntArg` | 以 invariant culture 解析整數，並檢查包含端點的範圍。 |
| `TryGetIntListArg` | 以逗號分割、去除項目前後空白及空項目，再依序檢查各整數。 |
| `TryGetDoubleArg` | 以 invariant culture 解析浮點數，允許千位分隔符。拒絕 NaN、無限值及範圍外數值。 |
| `TryGetStringArg` | 讀取非空白值，去除前後空白。 |
| `TryGetBoolArg` | 接受 true/false、1/0、on/off 與 yes/no，以 ordinal 規則比較且不區分大小寫。 |

處理委派型別保持為 `Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>`。
路由器原樣回傳處理委派的回應，並讓處理委派的例外向外傳遞。
Core 沒有版本常數。

未知命令回傳 `UNKNOWN_COMMAND`，訊息為 `Unknown query command '{commandText}'.`。
訊息保留原始命令文字及空白。null 命令文字顯示為空字串。
null 請求回傳 `INVALID_REQUEST`，訊息為 `Request is null.`。
版本不符回傳 `UNSUPPORTED_VERSION`，訊息為 `Unsupported request version '{request.Version}'. Expected '{expectedVersion}'.`。

缺少引數、null 值或空白值會回傳 false、預設輸出及 null 錯誤。
無效的已提供值會回傳 `INVALID_ARGUMENTS`，並保留凍結訊息。
整數清單保留順序及重複值。任一項目失敗時會清空輸出清單。
double 解析使用 invariant culture，但範圍訊息以目前文化格式化端點。
例如法文文化仍將 `1,5` 解析為 15，並將端點 1.5 格式化為 `1,5`。

### 凍結命令基準

此命令基準與下方傳輸基準分開記錄。

- 來源儲存庫：`Dennis40816/nvt-freeform-helper`。
- 來源 ref：`1.3.x`。
- 完整凍結 commit：`847cc4530ed098ceb56aa1bd8beda77bcd1ec227`。
- 擷取路徑：
  - `src/FreeformHelper.UI/Services/RuntimeQueryCommandRouter.cs`：全部 25 行。
  - `src/FreeformHelper.UI/Services/RuntimeQueryUseCase.cs`：第 51–92 行，提供登錄脈絡與請求檢查。
  - `src/FreeformHelper.UI/Services/RuntimeQueryArgumentParser.cs`：第 161–350 行的通用方法。
- 封套與版本參考：同一 commit 的 `src/FreeformHelper.UI/Services/RuntimeQueryProtocol.cs`。
- 測試參考：NFH 切換證據章節列出的全部 `RuntimeQueryUseCaseTests*.cs` 與 `RuntimeQueryIpcTests.cs`。

由 host 建立的 commit 訊息須包含此儲存庫、ref、完整 SHA 與擷取路徑。

### 驗證與切換

`RuntimeQueryCommandCases.cs` 集中保存可重用的輸入與字面預期值，不依賴 Core 封套型別。
`RuntimeQueryCommandRouterTests` 與 `RuntimeQueryArgumentParserTests` 對 Core 執行這些案例。
`RuntimeQuerySourceContractTests` 移植來源請求測試的路由邊界與處理委派例外契約。
合成處理委派取代產品測試設定。產品輸出斷言留在 NFH。
來源沒有直接針對路由器、請求檢查或通用引數方法的測試。

NFH 切換至 Core 時：

1. 以 `RuntimeQueryCommandRouter` 取代來源路由器。保留工具的處理委派表與各處理委派的檢查順序。
2. 在原本呼叫位置以 `router.ExecuteAsync(request, RuntimeQueryProtocol.Version)` 取代請求檢查。
3. 以 `RuntimeQueryArgumentParser` 取代五個通用方法。保留第 7–159 行的產品選取解析器。
4. 為來源型別與 Core 型別建立轉接，重用相同案例表。比較結果、輸出值、代碼與完整訊息。
5. 替換前後皆執行 NFH 切換證據章節列出的完整測試清單。
6. 測試名稱、數量、通過／失敗結果及略過結果必須相等。

凍結檔案共定義 24 個測試：19 個 use-case 測試與五個 IPC 測試。
每次採用驗證都須記錄完整測試清單。不得刪除或改名測試來接受差異。
來源請求檢查轉接使用其協定版本。其他預期版本案例驗證 Core 由呼叫端提供版本的契約。

命令列處理與 UI 執行緒步驟由後續任務加入。
本次擷取不在來源工具採用 Core。

## Command risk and confirmation

呼叫端啟用之前，確認防護保持關閉。
此行為於 2026-10-06 核准，沒有來源工具基準。

| 公開 API | 契約 |
| --- | --- |
| `RuntimeQueryCommandRisk` | 定義 `ReadOnly`、`ChangesState` 與 `WritesData`。 |
| `RuntimeQueryCommand(Name, Risk, Handler)` | sealed record，包含命令名稱、風險與現有的處理委派型別。 |
| `RuntimeQueryCommandRouter(commands, requireConfirmation)` | 由命令清單建立 ordinal 處理委派表。`RegisteredCommands` 保留登錄順序。 |

- `ReadOnly` 命令只讀取狀態。
- `ChangesState` 命令更改 UI 狀態，例如頁面或選取項目。不寫入檔案，也不更改資料。
- `WritesData` 命令寫入檔案或更改工具的資料。

清單建構子遇到 null 清單、命令、名稱或處理委派時，擲回 `ArgumentNullException`。
名稱重複時擲回 `ArgumentException`。
名稱若不同於去除前後空白並以 invariant culture 轉為小寫的結果，也會遭到拒絕。
字典建構子保留現有行為，永遠不要求確認。

使用 `requireConfirmation: false` 時，處理委派接收原始引數，包含任何 `confirm` 鍵。
NFH（FreeformHelper 工具）先以 false 切換至 Core。
啟用防護是另一項可見的獨立變更。

使用 `requireConfirmation: true` 時，`RouteAsync` 先尋找處理委派。
未知命令仍先回傳 `UNKNOWN_COMMAND`，再考慮確認檢查。
`ExecuteAsync` 仍先檢查 null 請求，再檢查版本，最後進行路由。

對 `WritesData`，路由器以 `RuntimeQueryArgumentParser.TryGetBoolArg` 讀取 `confirm`。
此方法接受 true/false、1/0、on/off 與 yes/no。
無效值會原樣回傳此方法的 `INVALID_ARGUMENTS` 錯誤。
缺少值、空白值或 false 會回傳 `CONFIRMATION_REQUIRED`，完整訊息如下：

```text
Command '{name}' writes files or changes data. Add --confirm to run it.
```

訊息使用正規化後的命令名稱。發生上述任一錯誤時，處理委派不會執行。
對每個風險等級，啟用的防護會先以 ordinal 規則移除 `confirm` 鍵，再呼叫處理委派。
其他鍵與值保持原樣，複製至新的 ordinal 字典。
沒有剩餘鍵時，處理委派接收 null。null 引數保持 null。
啟用防護後，處理委派不再接收 `confirm` 鍵。
現有命令列語法已將 `--confirm` 轉為 `"confirm": "true"`。

`RuntimeQueryConfirmationCases` 集中保存新增輸入與字面預期值。
`RuntimeQueryCommandConfirmationTests` 在防護關閉時，透過兩個建構子比較 `RuntimeQueryCommandCases` 的每一列。
測試驗證完整回應、處理委派呼叫、引數傳遞、登錄檢查與錯誤順序。
執行下方 RuntimeQuery 測試命令，驗證兩個建構子及啟用的防護。

要達成零差異，NFH 必須保持防護關閉，並執行下方現有的 NFH 切換檢查。
比較測試結果、stdout 與 stderr 位元組、管道框架、錯誤代碼與訊息，以及程序結束代碼。
獨立的防護變更須預期未確認的 `WritesData` 命令回傳 `CONFIRMATION_REQUIRED`，且處理委派引數不含 `confirm`。

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

伺服器處理委派為 `Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>>`。接收反序列化後的請求、設定的協定版本與關閉 token。空白行與格式錯誤的 JSON 由傳輸層拒絕；JSON null 與版本不符的請求會交給處理委派，與凍結傳輸一致。Core 現在提供 null／版本檢查與命令路由。工具保留處理委派表、產品解析器、命令列與 UI 派送。傳輸測試同時固定原有的 null 與版本錯誤封套。

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

NFH 保留 `freeformhelper.runtime.v1`、協定版本值、靜態 host 擁有權、`ShellViewModel`、`RuntimeQueryUseCase` 與 `Dispatcher.InvokeAsync` 整合。NFH 也保留處理委派表、產品解析器、選項解析、CLI 使用說明與輸出、結束代碼及逾時預設值。NFH 保留關閉政策、NLog、產品錯誤文字與診斷文字。產品資料與應用程式流程不屬於本模組。
