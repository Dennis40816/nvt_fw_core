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

可省略的診斷回呼為 `Action<RuntimeQueryDiagnostic, Exception?>`。事件涵蓋啟動、停止、管道建立失敗、連線失敗、請求失敗、處理委派失敗、關閉失敗與關閉逾時。呼叫端提供日誌文字與日誌相依套件。錯誤與診斷回呼必須迅速返回，且不得擲回例外。

Core 不提供產品預設的讀取限制、關閉上限、用戶端預算或協定識別。NFH 提供現有的 5,000 毫秒讀取逾時、1,500 毫秒預設 CLI 預算、協定版本、管道名稱、錯誤對應與日誌。凍結來源的關閉測試採用 1,500 毫秒完成上限；擷取的伺服器明確接收此上限。

管道寫入端使用精簡 JSON、不含 BOM 的 UTF-8，以及採用平台換行字元的 `StreamWriter.WriteLineAsync`（Windows 為 CRLF）。讀取端保留來源的 `Encoding.UTF8` 並停用編碼偵測。`StreamReader` 仍會消耗 UTF-8 編碼本身的前導碼；不偵測其他編碼。連線保持開啟但回應未以換行結尾時會逾時。未完成框架即關閉管道可能產生 IO 錯誤，包含讀取器／寫入器釋放期間，與來源一致。格式化輸出使用序列化器預設的平台換行字元（Windows 基準為 CRLF）；最後的輸出換行由呼叫端負責。

取消會停止等待忽略 token 的處理委派，但無法終止該委派自己的工作。關閉逾時透過診斷回報。呼叫端仍須負責處理委派的合作式清理。這些有時間上限的生命週期調整不改變請求或回應位元組。

## Pipe security

傳輸只接受同一部電腦上同一位使用者的程序。不開啟網路連接埠。

用戶端在 `PipeOptions.Asynchronous` 加上 `PipeOptions.CurrentUserOnly`。用戶端拒絕其他使用者擁有的管道。存取遭拒對應到既有的 `RuntimeQueryFailure.ClientError`，detail 保留例外訊息。

Windows 上的每個伺服器管道實例都透過內建的 `NamedPipeServerStreamAcl.Create` 指定 `PipeSecurity`：

- 只允許程序權杖的使用者安全識別碼（SID）取得 `PipeAccessRights.FullControl`，供建立管道與處理請求使用。
- 拒絕知名的 NETWORK SID（`S-1-5-2`）取得 `PipeAccessRights.FullControl`。網路登入無法連線，同一位使用者的網路登入亦同。
- 保護存取規則，避免繼承，且不加入其他允許項目。

管道擁有者使用權杖的擁有者 SID，以符合 .NET 用戶端的擁有權檢查。允許項目仍使用使用者 SID。

Windows 上只設定 `CurrentUserOnly` 無法阻擋遠端用戶端。執行階段在呼叫 `CreateNamedPipe` 前計算管道模式，但未加入 `PIPE_REJECT_REMOTE_CLIENTS`：

- [.NET 8.0.0，`NamedPipeServerStream.Windows.cs`，第 114–125 行](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs#L114-L125)。
- [.NET 10.0.0，同檔案第 118–129 行](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs#L118-L129)。
- [.NET 8.0.0 建構函式，第 33–36 行](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs#L33-L36) 拒絕同時指定安全設定與 `CurrentUserOnly`。[.NET 10.0.0](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs#L33-L36) 保留此檢查。

`NamedPipeServerStreamAcl.Create` 直接呼叫該建構函式。[API 備註](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstreamacl.create) 說明安全設定會被忽略，但執行階段實際拒絕此組合。
Windows 上的 .NET 8.0.31 測試確認此組合遭拒，並驗證連續兩個實例的存取規則。
因此 Windows 伺服器保留明確安全設定與 `Asynchronous`，不加入 `CurrentUserOnly`。拒絕 NETWORK 的規則限制連線只能來自本機，不新增 P/Invoke。
其他系統的伺服器保留 `Asynchronous | CurrentUserOnly`。遠端具名管道存取不適用於這些系統。Windows 專用呼叫皆以 `OperatingSystem.IsWindows()` 保護。

管道建立失敗時，伺服器透過診斷回呼送出一次 `RuntimeQueryDiagnostic.PipeCreationFailed` 及例外，然後結束執行迴圈。
這包含第二個伺服器使用相同名稱的情況。`Start()` 保留原有簽章，不會因這項失敗擲回例外。
第一個伺服器仍可回應請求。釋放仍受設定的關閉時間上限約束。

同一位使用者的呼叫端不會看到請求或回應位元組的改變。其他使用者的程序無法再連線。
名稱衝突現在會產生一次診斷事件。管道名稱、實例探索、選項及封套格式不變。

## 凍結來源

- 來源儲存庫：`Dennis40816/nvt-freeform-helper`。
- 來源 ref：`1.3.x`。
- 完整凍結 commit：`e01e07a361b8dc264a06b3741f40274feeeace2d`。
- 擷取的來源路徑：
  - `src/FreeformHelper.UI/Services/RuntimeQueryProtocol.cs`：封套與 JSON 設定，不包含產品識別常數。
  - `src/FreeformHelper.UI/Services/RuntimeQueryIpc.cs`：實例伺服器傳輸及用戶端 `SendRequest`／剩餘時間計算，不包含靜態 host 與 CLI 解析／輸出。
- 移植的傳輸測試來源：相同 commit 的 `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryIpcTests.cs`。

上述資訊記錄原始擷取。本次管道安全變更沒有來源工具基準，也未擷取工具程式碼。
前述執行階段證據支持新增的安全行為。在 NFH 採用 Core 仍是另一項工作。

## Core 驗證

所有測試資料皆為合成資料，並使用唯一管道名稱。測試位於 `tests/Nvt.Core.Tests/RuntimeQuery/`：

- `RuntimeQueryProtocolTests`：固定精簡與縮排請求、成功及失敗封套的 UTF-8 位元組字面預期值；驗證屬性順序、大小寫、字典鍵、null、跳脫與區分大小寫的反序列化。
- `RuntimeQueryIpcTests`：精確的精簡回應框架、含／不含前導碼的實際 UTF-8 請求、空白及格式錯誤請求、null／版本錯誤、可設定的讀取逾時、連線逾時後恢復、診斷及有時間上限的關閉。
- `RuntimeQueryIpcClientTests`：不含 BOM 的請求框架位元組字面預期值、null 引數、已接受但未回應及回應未以換行結尾的逾時案例、單一總逾時預算、空白／null／格式錯誤回應與中斷管道 IO 訊息傳遞。
- `RuntimeQueryTestValues`：呼叫端設定與凍結的 NFH 錯誤代碼／訊息。Core 正式程式碼不包含這些產品字串。
- `RuntimeQueryIpcSecurityTests`：執行中 Windows 管道的存取規則、安全設定與選項組合遭拒、用戶端存取遭拒的錯誤對應、名稱衝突診斷、持續回應及有時間上限的釋放。

Windows 安全測試在其他系統以 xUnit v3 `Assert.Skip` 回報略過。透過既有內部存取權取得作用中的管道，不新增公開 API。
存取規則測試使用 `PipesAclExtensions.GetAccessControl` 讀取連續兩個伺服器實例。新增的等待皆有明確時間上限。

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

採用本版本時，在同一位使用者下執行 Core 傳輸測試，以及工具既有的請求、回應、逾時與關閉測試。
比較採用前後的請求與回應位元組、CLI 輸出、錯誤文字及程序結束代碼。
另驗證預期變更：其他使用者無法連線，名稱衝突會產生一次 `PipeCreationFailed` 事件。
這些安全變更沒有來源工具基準。本次工作不執行 NFH 採用檢查。

## 留在 NFH 的內容

NFH 保留 `freeformhelper.runtime.v1`、協定版本值、靜態 host 擁有權、`ShellViewModel`、`RuntimeQueryUseCase` 與 `Dispatcher.InvokeAsync` 整合。NFH 也保留處理委派表、產品解析器、選項解析、CLI 使用說明與輸出、結束代碼及逾時預設值。NFH 保留關閉政策、NLog、產品錯誤文字與診斷文字。產品資料與應用程式流程不屬於本模組。

## UI thread and host

Core 現在於 `Nvt.Core.Avalonia.RuntimeQuery` 提供 UI 派送與實例 host。
工具保留命令處理委派、錯誤文字、啟動排程及結束事件。
新增的公開型別為 `RuntimeQueryUiThread` 與 `RuntimeQueryHost`。

| API | 契約 |
| --- | --- |
| `RuntimeQueryUiThread.Wrap(handler, error)` | 回傳相同委派簽章的伺服器處理委派。 |
| `RuntimeQueryHost(factory)` | 由 `Func<RuntimeQueryIpcServer>` 建立 sealed 實例 host。 |
| `RuntimeQueryHost.Start()` | 在鎖內建立並啟動一個伺服器。host 已持有伺服器時，重複啟動不產生額外作用。 |
| `RuntimeQueryHost.StopAsync()` | 在鎖內取出目前伺服器，再釋放它。沒有伺服器時立即完成。 |
| `RuntimeQueryFailure.DispatcherUnavailable` | 在 `Nvt.Core.RuntimeQuery` 的失敗列舉末端新增一個值。既有失敗值與診斷值皆不變。 |

`Wrap` 透過 `UiThread.TryGetRunningDispatcher` 取得 dispatcher。
它使用回傳 Task 的 `Dispatcher.InvokeAsync` 多載，保留來源的預設優先序。
請求、版本與取消 token 皆原樣傳遞，包含 null 請求與已取消的 token。
它回傳內部處理委派的回應，並讓例外向外傳遞。伺服器負責對應這些例外。
工具透過 `UiThread.RegisterRunningDispatcher` 登錄執行中的 dispatcher。

沒有執行中的 dispatcher 時，`Wrap` 以 null detail 對應 `DispatcherUnavailable`，並回傳失敗封套。
NFH 將此失敗對應為 `IPC_ERROR`，訊息為 `The UI dispatcher is unavailable.`。
NFH 將處理委派失敗對應為 `IPC_ERROR`，保留原始例外訊息。
這些對應由工具持有。Core 不提供這些失敗的產品錯誤文字。

`StopAsync` 在釋放前清除 host 持有的伺服器。後續 `Start` 會建立新伺服器，與凍結來源一致。
host 不加入排程、視窗事件、應用程式生命週期事件、靜態實例或選項。
工具決定啟動時機，並在結束時呼叫 `StopAsync`。

### 工具啟動與結束

NFH 是 FreeformHelper。NFC 是 NVT FW Combiner。
NFH 在主視窗 `Opened` 事件後，以 Background 優先序排入 host 啟動。
ApplicationIdle 備援路徑先檢查視窗可見性，再以 Background 優先序排入相同啟動流程。
NFH 在桌面 `Exit` 事件呼叫停止，不等待完成。
既有 `StartRuntimeIpcIfNeeded` 函式保留 shell 檢查及啟動防重複判斷。
以下簡短生命週期範例留在 NFH：

```csharp
mainWindow.Opened += (_, _) => Dispatcher.UIThread.Post(
    StartRuntimeIpcIfNeeded, DispatcherPriority.Background);
Dispatcher.UIThread.Post(() =>
{
    if (mainWindow.IsVisible)
    {
        Dispatcher.UIThread.Post(StartRuntimeIpcIfNeeded, DispatcherPriority.Background);
    }
}, DispatcherPriority.ApplicationIdle);
desktop.Exit += (_, _) => { _ = RuntimeQueryIpcHost.StopAsync(); };
```

NFC 只在寫出 READY 後啟動 host，維持原本的 READY 時間。
截圖模式、`--help` 及內部 probe 執行皆不啟動 host。
NFC 也在結束時呼叫 `StopAsync`。以下簡短範例表示啟動順序：

```csharp
WriteReady();
if (!screenshotMode && !helpRequested && !internalProbe)
{
    host.Start();
}
```

### 凍結 UI 與 host 基準

- 來源儲存庫：`nvt-freeform-helper`。
- 來源 ref：`origin/1.3.x`。
- 完整凍結 commit：`847cc4530ed098ceb56aa1bd8beda77bcd1ec227`。
- 擷取路徑：`src/FreeformHelper.UI/Services/RuntimeQueryIpc.cs`，第 11–48 行為 host，第 205–214 行為 UI 派送。
- 文件參考：`src/FreeformHelper.UI/App.axaml.cs`，第 82–137 行提供 NFH 啟動及結束時機。
- 測試來源：`tests/FreeformHelper.Tests/UI/Services/RuntimeQueryIpcTests.cs`，全部五個測試。

由 host 建立的 commit 訊息須記錄此儲存庫、ref、完整 commit 及全部三個檔案路徑。
本次擷取不修改 NFH 或 NFC。

### 測試與零差異切換

Core 移植三個 host 關閉測試，並透過真正的 UI 派送移植處理委派擲回例外的管道測試。
關閉測試保留 1500 毫秒完成上限。
已接受但未回應的案例已存在於 `Nvt.Core.Tests.RuntimeQuery.RuntimeQueryIpcClientTests.SendRequestWhenServerAcceptsButDoesNotRespondReturnsTimeout`。
此處不重複加入該傳輸測試。

新增測試也驗證 UI 存取權、原樣傳入的輸入、dispatcher 不可用、例外實例、重複啟動、重新啟動及並行啟動。
每個新管道測試皆使用自己的合成管道名稱。
全部新測試使用停用平行執行的 `RuntimeQuery` collection。
測試在 `finally` 還原靜態 `UiThread` dispatcher 登錄狀態，且只使用有時間上限的等待。

NFH 依下列步驟切換：

1. 保留靜態 host，讓它包裝一個 `RuntimeQueryHost` 實例。
2. 以既有設定及處理委派建立每個伺服器。使用 `RuntimeQueryUiThread.Wrap` 與 NFH 的錯誤對應包裝處理委派。
3. 保留既有啟動排程及桌面結束呼叫。
4. 替換前後皆執行全部五個 `RuntimeQueryIpcTests`。
5. 替換前後皆執行 NFH 完整測試套件，並保存完整測試清單。
6. 測試名稱、數量、通過／失敗結果及略過結果皆須相等。保留每個既有測試名稱。

五個凍結管道測試為：

- `StopAsync_WhenHostNotStarted_Completes`
- `StopAsync_WhenHostStarted_CompletesWithinTimeout`
- `StopAsync_WhenClientConnectedWithoutRequest_CompletesWithinTimeout`
- `SendRequest_WhenServerAcceptsButDoesNotRespond_ReturnsTimeout`
- `SendRequest_WhenRuntimeQueryThrows_ReturnsIpcErrorEnvelope`

Core 測試確立擷取的契約。NFH 採用 Core 時，由替換前後的執行結果確立零差異。
這些採用驗證留在 NFH 執行。

Core 驗證使用已還原的套件：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```
