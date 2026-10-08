[English](RuntimeQuery.md) | [中文](RuntimeQuery.zh-TW.md)

# RuntimeQuery

## 用途

`Nvt.Core.RuntimeQuery` 提供從 FreeformHelper（NFH）擷取的 JSON 封套與本機具名管道傳輸。每個連線依序處理一筆請求，使用位元組模式的非同步管道。不依賴 Avalonia 或 NLog，也不解讀產品命令。

## 取消與遷移

RuntimeQuery 使用單一執行委派，將取消 token 從 IPC 傳遞到路由、啟動、UI 派送與截圖擷取。
`InvocationHandler` 已移除。
共用 JSON 預設選項在首次使用前即為唯讀。
傳輸位元組與格式化輸出的預設值保持不變。

- `Handler` 接收呼叫時機、引數與取消 token。
- `FromArgs` 從引數與 token 委派建立命令，忽略呼叫時機。
- 字典路由器的委派接收引數與取消 token。

將同一個 token 傳給 `ExecuteAsync`、`RouteAsync` 與 `ExecuteStartupPhaseAsync`。
這些方法的 token 可省略，預設為 `default`。
UI 包裝器在派送前檢查取消，並在排入佇列的工作開始前再次檢查。
啟動執行器在每個命令開始前檢查取消。
已開始的處理委派自行採用合作式取消。
忽略 token 的處理委派仍回傳原本的回應。
已提交變更的清理流程不得使用已取消的請求 token。
Core 保留無關的例外，且仍執行已核准的關閉動作。
預設截圖在繪製前與檔案提交前檢查取消，暫存檔清理一定執行。
替代擷取接收呼叫的 token，自行負責取消與已提交檔案的清理。

舊版登錄使用兩個委派：

```csharp
var command = new RuntimeQueryCommand("resize", RuntimeQueryCommandRisk.ChangesState, LegacyHandler)
{
    InvocationHandler = ApplyAsync
};
```

新版登錄只提供一個接收呼叫時機的處理委派：

```csharp
var command = new RuntimeQueryCommand("resize", RuntimeQueryCommandRisk.ChangesState,
    (invocation, values, token) => ApplyAsync(invocation, values, token));
```

執行不需要呼叫時機時，使用只接收引數與 token 的工廠：

```csharp
var command = RuntimeQueryCommand.FromArgs("probe", RuntimeQueryCommandRisk.ReadOnly,
    (values, token) => ProbeAsync(values, token),
    startupPhase: RuntimeQueryStartupPhase.AfterStartup);
var router = new RuntimeQueryCommandRouter([command], requireConfirmation: false);
var handler = RuntimeQueryUiThread.Wrap(router.ExecuteAsync, MapTransportError);
await router.ExecuteStartupPhaseAsync(startup.Calls, RuntimeQueryStartupPhase.AfterStartup, cancellationToken);
```

自訂輸出時，建立可修改的選項複本：

```csharp
var options = RuntimeQueryProtocol.CreatePrettyJsonOptions();
options.PropertyNameCaseInsensitive = true;
```

`CreateCompactJsonOptions` 與 `CreatePrettyJsonOptions` 回傳共用預設選項的獨立可修改複本。
自訂選項不影響傳輸序列化或其他複本。

更新每一個 RuntimeQuery 使用端的 `DesktopRuntimeQuery` 工廠及路由包裝器。
將你的進入點收到的 token 傳給 `Router.ExecuteAsync(request, version, token)`，或直接使用路由器的方法群組。token 來自 IPC 伺服器的處理委派；沒有伺服器時，來自你自己的進入點。
傳入啟動 token 給 `ExecuteStartupPhaseAsync` 是選用的。啟動期間取消會擲回 `OperationCanceledException`，已執行命令的結果會遺失。工具若要保留視窗在啟動時關閉的舊結果，就不要傳啟動 token。
關閉期間，IPC 伺服器把處理委派擲出的任何 `OperationCanceledException`（含無關 token 的）都當成關閉取消，不回報 `HandlerFailed`，也不寫出回應。
更新 `AppearanceLaunchCommands`、啟動派送與處理委派測試的簽章。
命令結果、確認資訊傳遞與傳輸位元組保持不變。
另外兩個已檢視的工具不需原始碼遷移。
由整合者鎖定已接受版本、更新套件雜湊、重新產生鎖定檔，並以鎖定模式還原。

## Commands and arguments

Core 現在提供命令路由、請求檢查與五個通用引數輔助方法。

| API | 契約 |
| --- | --- |
| `RuntimeQueryCommandRouter(handlers)` | 使用工具提供的委派字典。不新增命令，也不更改字典的鍵比較方式。 |
| `RegisteredCommands` | 建構時依字典列舉順序保存唯讀名稱清單。工具須先依登錄順序建立字典。 |
| `RouteAsync(commandText, args, cancellationToken)` | 去除命令前後空白，以 invariant culture 轉為小寫。將原始引數字典傳給處理委派。token 已取消時，呼叫會在任何處理委派執行前擲回 `OperationCanceledException`。 |
| `ExecuteAsync(request, expectedVersion, cancellationToken)` | 先檢查 null，再以 ordinal 相等比較版本，最後路由命令。版本由工具提供。 |
| `RuntimeQueryArgumentParser.TryGetIntArg` | 以 invariant culture 解析整數，並檢查包含端點的範圍。 |
| `TryGetIntListArg` | 以逗號分割、去除項目前後空白及空項目，再依序檢查各整數。 |
| `TryGetDoubleArg` | 以 invariant culture 解析浮點數，允許千位分隔符。拒絕 NaN、無限值及範圍外數值。 |
| `TryGetStringArg` | 讀取非空白值，去除前後空白。 |
| `TryGetBoolArg` | 接受 true/false、1/0、on/off 與 yes/no，以 ordinal 規則比較且不區分大小寫。 |

字典處理委派的型別為 `Func<IReadOnlyDictionary<string, string>?, CancellationToken, Task<RuntimeQueryResponseEnvelope>>`。
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
2. 在原本呼叫位置以 `router.ExecuteAsync(request, RuntimeQueryProtocol.Version, cancellationToken)` 取代請求檢查。
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
預設情況下，啟用的防護會先以 ordinal 規則移除各風險等級命令的 `confirm` 鍵，再呼叫處理委派。
其他鍵與值保持原樣，複製至新的 ordinal 字典。
沒有剩餘鍵時，處理委派接收 null。null 引數保持 null。
設定 `ReceivesConfirmation = true` 的命令保留原始執行期間引數，包含 `confirm`。
此屬性預設為 false，不改變 `WritesData` 檢查或啟動引數處理。
現有命令列語法已將 `--confirm` 轉為 `"confirm": "true"`。

`RuntimeQueryConfirmationCases` 集中保存新增輸入與字面預期值。
`RuntimeQueryCommandConfirmationTests` 在防護關閉時，透過兩個建構子比較 `RuntimeQueryCommandCases` 的每一列。
測試驗證完整回應、處理委派呼叫、引數傳遞、登錄檢查與錯誤順序。
執行下方 RuntimeQuery 測試命令，驗證兩個建構子及啟用的防護。

要達成零差異，NFH 必須保持防護關閉，並執行下方現有的 NFH 切換檢查。
比較測試結果、stdout 與 stderr 位元組、管道框架、錯誤代碼與訊息，以及程序結束代碼。
獨立的防護變更須預期未確認的 `WritesData` 命令回傳 `CONFIRMATION_REQUIRED`。
除非命令透過 `ReceivesConfirmation` 選擇接收，處理委派引數不含 `confirm`。

## Startup entry

工具只定義一次命令，並從啟動引數及 RuntimeQuery 請求使用同一份定義。
擁有者於 2026-10-06 核准此新行為。
此行為沒有來源工具基準，也沒有擷取的來源路徑。

| 公開 API | 契約 |
| --- | --- |
| `RuntimeQueryStartupPhase` | `None` 不新增啟動選項。`BeforeFirstFrame` 僅供啟動使用。`AfterStartup` 也接受 RuntimeQuery 請求。`BeforeFirstFrameAndRuntime` 同時接受早期啟動與執行期間的請求。 |
| `RuntimeQueryInvocation` | 識別 `Startup` 或 `Runtime` 呼叫時機。 |
| `RuntimeQueryCommand.Handler` | 唯一的處理委派接收呼叫時機、引數與取消 token。 |
| `RuntimeQueryCommand.StartupPhase` | 可省略的中繼資料。預設為 `None`，現有登錄維持原有行為。 |
| `RuntimeQueryCommand.StartupValueKey` | 一個啟動值的引數鍵。預設為 null，表示旗標，處理委派接收 null 引數。 |
| `RuntimeQueryCommand.StartupValidator` | 可省略的驗證委派，型別為 `Func<IReadOnlyDictionary<string, string>?, RuntimeQueryResponseEnvelope?>?`。有效時回傳 null，無效時回傳失敗。 |
| `RuntimeQueryStartupCall(Command, Args)` | 一次已辨識的命令出現。`Phase` 來自命令定義。包含有問題的呼叫，並保留命令列順序。 |
| `RuntimeQueryStartupIssue(Option, Message)` | 選項名稱及完整問題訊息。 |
| `RuntimeQueryStartupParseResult(Calls, RemainingArguments, Issues)` | 一次解析的全部呼叫、工具剩餘引數及問題。 |
| `RuntimeQueryStartupCallResult(Call, Response)` | 已執行的呼叫及原樣回傳的回應。 |
| `RuntimeQueryCommandRouter.ParseStartupArguments(arguments)` | 依路由器登錄的命令及確認設定解析原始引數。不執行處理委派。 |
| `RuntimeQueryCommandRouter.ExecuteStartupPhaseAsync(calls, phase, cancellationToken)` | 透過路由器依命令列順序執行一個階段。包含第一個失敗回應，然後停止。 |

例如，將 `theme` 登錄為 `AfterStartup`，並使用引數鍵 `value`：

```csharp
var command = RuntimeQueryCommand.FromArgs(
    "theme",
    RuntimeQueryCommandRisk.ChangesState,
    ApplyThemeAsync,
    startupPhase: RuntimeQueryStartupPhase.AfterStartup,
    startupValueKey: "value",
    startupValidator: ValidateTheme);
var router = new RuntimeQueryCommandRouter([command], requireConfirmation: false);
var startup = router.ParseStartupArguments(args);
```

工具可在 `ApplyThemeAsync` 中呼叫 `ValidateTheme`，讓值的規則只存在於一個函式。
驗證委派不得有副作用，也不得執行命令。

兩個入口都以 ordinal 鍵 `"value"`，將 `"dark"` 傳給同一個處理委派：

```text
--theme dark
query theme --value dark
```

Core 接受以下啟動形式：

- 值選項使用 `--name value` 或 `--name=value`。
- 旗標使用 `--name`。
- 值可以包含 `=`，且文字保持原樣。

Core 只接收 `--` 加上已登錄且階段不是 `None` 的命令名稱。
名稱以 ordinal 相等比較，不改變大小寫。
工具保留既有選項的解析器。
Core 依原始順序，原樣傳遞所有其他引數。
例如，`--page home --theme dark --load-report a.json` 留下 `--page home --load-report a.json` 給工具。
Core 不為 `page` 或 `help` 等通用命令新增啟動階段。
工具若未登錄任何啟動階段，行為完全不變。
解析器傳遞每一個引數，包含 `--confirm`。

解析器回報以下完整語法訊息：

- 兩種形式中缺少值、空值或空白值：`--{name} requires a value.`。
- 旗標含等號：`--{name} does not take a value.`。
- 選項重複：`--{name} is given more than once.`。

下一個 token 若以 `--` 開頭，就不是值，解析器不會消耗它。
每個沒有語法問題的呼叫，Core 都只呼叫一次啟動驗證委派。
驗證失敗會成為問題，訊息保持原樣。
解析器一次回傳全部呼叫及問題，讓工具檢查選項之間的規則。

有登錄啟動命令且啟用確認防護時，Core 將 `--confirm` 保留為啟動旗標。
此旗標確認全部 `WritesData` 呼叫，也包含出現在旗標之前的呼叫。
未確認時，每個此類呼叫新增 `--{name} writes files or changes data. Add --confirm to use it.`。
防護關閉時，`--confirm` 原樣傳給工具。
處理委派不會收到保留的啟動確認旗標。

工具在主視窗顯示之前執行 `BeforeFirstFrame`。
影響第一個畫面的設定使用此階段。
工具在自身啟動流程結束之後執行 `AfterStartup`。
Core 不提供視窗事件或 UI 派送。
階段執行略過執行期間的啟動專用檢查，並保留已啟用的確認防護。
工具須先檢查解析問題及選項之間的規則，再呼叫任一階段。

執行期間的請求依序檢查 null、版本、未知命令、啟動專用狀態及確認。
`RouteAsync` 與 `ExecuteAsync` 都以 `STARTUP_ONLY` 拒絕 `BeforeFirstFrame`，完整訊息如下：

```text
Command '{name}' can be used only at startup.
```

嚴格模式由工具決定。
自動化執行遇到啟動問題時，以結束代碼 64 拒絕執行，且不開啟 UI。
啟動驗證委派讓工具在開啟任何視窗之前拒絕無效值。
互動執行透過工具自己的 UI 顯示問題。
Core 只回傳問題及回應，不定義結束代碼常數。

`RuntimeQueryStartupCases` 集中保存輸入與字面預期輸出。
`RuntimeQueryStartupParserTests` 與 `RuntimeQueryStartupTests` 驗證解析、執行期間錯誤順序、相同的處理委派引數及階段執行。
使用已還原的套件執行下方 RuntimeQuery 測試命令。
要達成零差異，不登錄啟動階段，並逐項比較傳遞後的引數與原始清單。
以兩份清單執行工具既有的啟動解析器測試。
比較解析結果及完整錯誤訊息，包含既有的 `page`、`help` 與報告選項。
登錄啟動階段會新增行為，工具須另行測試後才採用。
本任務不修改任何工具儲存庫。

### 第一個畫面之前及執行期間

命令若需要在第一次排版之前執行，也需要在執行期間使用，請選擇 `BeforeFirstFrameAndRuntime`。
新的列舉成員附加於既有成員之後，保留原有數值。
兩個早期階段值都選擇相同的第一個畫面之前執行批次。
混合的早期命令保留命令列順序。
呼叫端須在顯示視窗或開始任何排版之前執行此批次。

`Handler` 在啟動執行時接收 `RuntimeQueryInvocation.Startup`，執行期間路由時接收 `RuntimeQueryInvocation.Runtime`。
它也接收引數與取消 token。
`ReceivesConfirmation` 控制執行期間傳入的引數。
啟動驗證委派仍在解析期間執行，不呼叫處理委派。

啟動解析器依既有的選項、值鍵、驗證及確認規則辨識新階段。
通用 help 依登錄順序列出這些命令。
啟動選項清單選取 `StartupPhase` 不是 `None` 的命令。

`--window-size` 命令可在處理委派內重用尺寸驗證委派。
此 Avalonia 範例在 Show 之前設定 Width 及 Height。
只有執行期間的呼叫才更新排版。

```csharp
RuntimeQueryResponseEnvelope? ValidateWindowSize(IReadOnlyDictionary<string, string>? values)
{
    if (RuntimeQueryArgumentParser.TryGetIntListArg(values, "size", 1, 8192, out var dimensions, out var error)
        && dimensions.Count == 2)
    {
        return null;
    }

    return error ?? RuntimeQueryResponseEnvelope.Failure("INVALID_ARGUMENTS", "Use width,height with two positive dimensions.");
}

Task<RuntimeQueryResponseEnvelope> ApplyWindowSizeAsync(
    RuntimeQueryInvocation invocation, IReadOnlyDictionary<string, string>? values, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    if (ValidateWindowSize(values) is { } failure)
    {
        return Task.FromResult(failure);
    }

    _ = RuntimeQueryArgumentParser.TryGetIntListArg(values, "size", 1, 8192, out var dimensions, out _);
    mainWindow.Width = dimensions[0];
    mainWindow.Height = dimensions[1];
    if (invocation == RuntimeQueryInvocation.Runtime)
    {
        mainWindow.UpdateLayout();
    }

    return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
}

var windowSize = new RuntimeQueryCommand(
    "window-size", RuntimeQueryCommandRisk.ChangesState,
    ApplyWindowSizeAsync,
    StartupPhase: RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime,
    StartupValueKey: "size",
    StartupValidator: ValidateWindowSize);
var router = new RuntimeQueryCommandRouter([windowSize], requireConfirmation: false);
var startup = router.ParseStartupArguments(args);
if (startup.Issues.Count > 0)
{
    return;
}

var before = await router.ExecuteStartupPhaseAsync(startup.Calls, RuntimeQueryStartupPhase.BeforeFirstFrame, cancellationToken);
if (before.Any(result => !result.Response.Ok))
{
    return;
}

mainWindow.Show();
await router.ExecuteStartupPhaseAsync(startup.Calls, RuntimeQueryStartupPhase.AfterStartup, cancellationToken);
```

兩種形式都使用 `size` 引數鍵：

```text
--window-size 1280,720
query window-size --size 1280,720
```

呼叫端持有視窗，並在 UI 執行緒派送執行期間的請求。
`BeforeFirstFrame` 仍只供啟動使用，執行期間仍回傳 `STARTUP_ONLY`。
`AfterStartup` 保留較晚的啟動批次及執行期間路由。
`None` 保留執行期間路由，不新增啟動選項。

## 公開 API

| API | 契約 |
| --- | --- |
| `RuntimeQueryRequest(Version, Command, Args)` | 請求欄位依此順序排列；引數可以是 null。 |
| `RuntimeQueryError(Code, Message)` | 由呼叫端擁有的錯誤代碼與訊息。 |
| `RuntimeQueryResponseEnvelope(Ok, Data, Error)` | 回應欄位依此順序排列；`Success(data)` 與 `Failure(code, message)` 保留明確的 null 屬性。 |
| `RuntimeQueryProtocol.CompactJsonOptions` | 唯讀預設選項。屬性名稱採 camelCase、保留 null、使用預設 JSON 跳脫規則、反序列化區分大小寫、不縮排。字典鍵保留原本大小寫。 |
| `RuntimeQueryProtocol.PrettyJsonOptions` | 唯讀預設選項。相同設定，另使用兩個空白縮排與序列化器預設的平台換行字元。用於呼叫端輸出，不用於管道訊息。 |
| `RuntimeQueryIpcServer(...)` | 實例接收管道名稱、協定版本、以毫秒計的正數讀取與關閉逾時、錯誤回呼、診斷回呼及請求處理委派。 |
| `Start()` / `DisposeAsync()` | 啟動一次；執行期間重複啟動與重複釋放皆不產生額外作用。釋放後啟動會擲回例外。釋放會停止接受新連線，並取消尚未送出請求行的連線。已讀到請求的連線可在關閉時間上限內完成處理並寫出回應。超過上限後，釋放會向處理委派發出合作式取消通知並關閉管道。 |
| `RuntimeQueryIpcClient.SendRequest(pipeName, request, timeoutMs, error)` | 同步送出一筆請求，使用涵蓋連線、寫入與讀取的正數總逾時預算。開始寫入／讀取計時前扣除連線耗時，保留來源的整數毫秒取整與至少一毫秒的剩餘預算。 |

伺服器處理委派為 `Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>>`。接收反序列化後的請求、設定的協定版本與關閉 token。空白行與格式錯誤的 JSON 由傳輸層拒絕；JSON null 與版本不符的請求會交給處理委派，與凍結傳輸一致。Core 現在提供 null／版本檢查與命令路由。工具保留處理委派表、產品解析器、命令列與 UI 派送。傳輸測試同時固定原有的 null 與版本錯誤封套。

錯誤回呼為 `Func<RuntimeQueryFailure, string?, RuntimeQueryError>`。`InvalidJson`、`HandlerError`、`IoError` 與 `ClientError` 的 detail 是原樣傳遞的例外訊息；其他情況為 null。Core 將回傳的錯誤包裝成失敗封套。`RuntimeQueryFailure` 除了這些失敗，還包含 `RequestTimeout`、`EmptyRequest`、`EmptyResponse`、`InvalidResponse`、`ConnectionTimeout` 與 `ClientTimeout`；列舉名稱不是線上傳輸的錯誤代碼。

可省略的診斷回呼為 `Action<RuntimeQueryDiagnostic, Exception?>`。事件涵蓋啟動、停止、管道建立失敗、連線失敗、請求失敗、處理委派失敗、關閉失敗與關閉逾時。呼叫端提供日誌文字與日誌相依套件。錯誤與診斷回呼必須迅速返回，且不得擲回例外。

Core 不提供產品預設的讀取限制、關閉上限、用戶端預算或協定識別。NFH 提供現有的 5,000 毫秒讀取逾時、1,500 毫秒預設 CLI 預算、協定版本、管道名稱、錯誤對應與日誌。凍結來源的關閉測試採用 1,500 毫秒完成上限；擷取的伺服器明確接收此上限。

管道寫入端使用精簡 JSON、不含 BOM 的 UTF-8，以及採用平台換行字元的 `StreamWriter.WriteLineAsync`（Windows 為 CRLF）。讀取端保留來源的 `Encoding.UTF8` 並停用編碼偵測。`StreamReader` 仍會消耗 UTF-8 編碼本身的前導碼；不偵測其他編碼。連線保持開啟但回應未以換行結尾時會逾時。未完成框架即關閉管道可能產生 IO 錯誤，包含讀取器／寫入器釋放期間，與來源一致。格式化輸出使用序列化器預設的平台換行字元（Windows 基準為 CRLF）；最後的輸出換行由呼叫端負責。

伺服器會持續等待已開始的處理委派，讓它自行完成清理。關閉時會發出 token 取消通知，超過時間上限後關閉管道並回報 `ShutdownTimedOut`。忽略 token 的處理委派會執行到自行返回，因為 Core 無法終止它的工作。該處理委派返回前不會回報 `Stopped`，診斷事件也可能在 `DisposeAsync` 返回之後才出現。呼叫端仍須負責處理委派的合作式清理。這些有時間上限的生命週期調整不改變請求或回應位元組。

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
名稱衝突現在會產生一次診斷事件。執行多個副本時，請參閱 [Per-window pipes](#per-window-pipes)。

## Command line

Core 現在提供 query 命令列前端，保留凍結來源的行為。
工具保留管道名稱、協定版本、依原順序排列的命令清單，以及用戶端錯誤文字。
Core 負責解析、JSON 輸出、結束代碼，以及預設用戶端逾時。

新增的公開型別是 `RuntimeQueryCommandLine`。
唯一入口方法是 `TryHandleQueryCommand(args, pipeName, protocolVersion, supportedCommands, error, output, out exitCode)`。
錯誤對應使用既有的 `Func<RuntimeQueryFailure, string?, RuntimeQueryError>` 契約。
工具將 `Console.Out` 傳入輸出 writer。

- 只有第一個引數等於 `query` 時才處理，忽略大小寫。
- 未處理的引數不產生輸出，結束代碼為 0。
- 解析器去除命令前後空白，再使用 invariant culture 轉成小寫。
- 使用說明與不支援命令的錯誤，依提供的原順序列出命令。
- 預設輸出格式化 JSON。最後一個 `--json-pretty` 或 `--json-compact` 決定回應格式。
- 解析錯誤一律輸出格式化的 `INVALID_ARGUMENTS` JSON，結束代碼為 2。
- 回應的 `ok` 為 true 時結束代碼為 0，否則為 1。
- 前端只呼叫一次 `WriteLine`，寫入序列化的 JSON。

只有第一個 `=` 的位置大於 2 時，選項才在該處分割。
否則 `--` 後的完整 token 就是鍵。
缺少值時使用 `"true"`，包含下一個 token 以 `--` 開頭的情況。
鍵不區分大小寫。重複鍵保留第一次的拼法與最後一次的值。
`--timeout-ms` 只供用戶端使用，預設為 1500，接受 1 到 120000 的整數。
沒有剩餘命令引數時，請求的引數為 null。

### 命令列基準

- 來源儲存庫：`nvt-freeform-helper`（NFH）。
- 正式程式碼 ref：`origin/1.3.x`。
- 凍結正式程式碼 commit：`847cc4530ed098ceb56aa1bd8beda77bcd1ec227`。
- 擷取路徑：`src/FreeformHelper.UI/Services/RuntimeQueryIpc.cs`，第 239–289 與 376–463 行。
- 呼叫端參考：相同 commit 的 `src/FreeformHelper.UI/Program.cs`，約第 37 行。
- 特徵測試 ref：`test/1.3.x/runtimequery-characterization`，NFH pull request 47。
- 凍結特徵測試 commit：`464ecf4d98095ac26b195046bfb50ef66679286f`。
- 特徵測試路徑：
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryCharacterizationCommandLineTests.cs`
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryCharacterizationSubject.cs`

### 命令列驗證

`RuntimeQueryCommandLineCases` 將全部 40 個凍結輸入與預期輸出放在同一個可共用的測試表。
另外四列涵蓋 `--=x`、`--a=b=c`、空命令，以及前後有空白的命令。
凍結的 query 單獨一列涵蓋 `query` 後沒有引數的情況。
測試使用 `Environment.NewLine` 建立精確的 stdout 字串與請求框架，逐字比較。
三個逾時案例依來源測試方式檢查私有解析器。
Program 案例重現呼叫端指定結束代碼的流程，不啟動 UI。
全部管道測試共用一個停用平行執行的 collection。
連線測試使用唯一管道名稱，所有等待都有時間上限。
產品管道名稱只用於不會連線的測試資料。

設定 `AVALONIA_TELEMETRY_OPTOUT=1`，使用已還原的套件：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.RuntimeQuery"
```

### 零差異切換

NFH 保留凍結順序的命令清單，以及既有的用戶端錯誤文字。
在 `Program.cs` 將前端呼叫換成 Core 入口：

```csharp
if (RuntimeQueryCommandLine.TryHandleQueryCommand(
    args, PipeName, ProtocolVersion, SupportedCommands, ClientError, Console.Out, out var cliExitCode))
{
    Environment.ExitCode = cliExitCode;
    return;
}
```

範例中的設定名稱代表工具既有的設定值與用戶端錯誤對應。
切換前後都執行全部 40 個 `RuntimeQueryCharacterizationCommandLineTests` 案例。
使用 `RuntimeQueryCommandLineCases`，讓兩個前端執行相同資料列。
比較 stdout 位元組與結束代碼，兩者都必須相等。
工具的 `INSTANCE_NOT_RUNNING` 文字繼續由其用戶端錯誤對應提供。
Core 繼續使用既有的用戶端傳輸與 JSON 選項。
本次工作不變更來源工具。

## Per-window pipes

每視窗模式讓每個執行中的工具程序持有自己的管道。
owner 在 2026-10-06 核准這項行為。
由 host 建立的 commit 訊息須包含 `new behavior, no source baseline`。

| 公開成員 | 契約 |
| --- | --- |
| `RuntimeQueryWindowPipes.BuildName(baseName, processId)` | 回傳 `{baseName}.{processId}`，使用 invariant 十進位數字。拒絕 null 或空白基底名稱，以及非正數程序 ID。 |
| `RuntimeQueryCommandLine.TryHandlePerWindowQueryCommand(args, baseName, protocolVersion, supportedCommands, error, output, out exitCode)` | 先選擇執行中的視窗，再呼叫既有用戶端。其他參數與 `TryHandleQueryCommand` 相同。 |
| `RuntimeQueryFailure.ServerNotFound` | 找不到視窗時，以 null detail 呼叫工具的錯誤對應。命令列回傳結束代碼 1。 |

例如，兩個視窗分別使用 `sample.runtime.v1.123` 與 `sample.runtime.v1.456`。
若程序 456 啟動較晚，`sample query help` 會選擇其管道。
使用 `sample query help --pid 123` 可選擇另一條管道。

探索只在 Windows 執行，列舉 `\\.\pipe\`。
候選名稱必須以 `{baseName}.` 開頭，並以正十進位程序 ID 結尾，後方不得有其他字元。
探索忽略 `{baseName}.12x`、`{baseName}.` 及 `{baseName}x.1` 等名稱。
程序已結束或無法讀取啟動時間時，跳過該候選。
其他系統不回傳候選。

選擇規則只使用程序 ID 與啟動時間：

- 未指定 `--pid` 時，選擇啟動時間最晚的程序。
- 啟動時間相同時，選擇較大的程序 ID。
- 指定 `--pid` 時，只選擇該程序 ID。找不到時回傳 `ServerNotFound`，不改選其他視窗。
- 沒有候選時，回傳 `ServerNotFound`。

新入口接受 `--pid 123` 與 `--pid=123`，如同 `--timeout-ms`，只供用戶端使用。
其值必須為正整數。
缺少值或值無效時，輸出格式化的 `INVALID_ARGUMENTS` JSON，結束代碼為 2。
精確訊息為 `--pid must be a positive process ID.`。
請求不包含 `--pid` 與 `--timeout-ms`。
其他解析、輸出格式、回應處理及結束代碼皆保留固定名稱模式的行為。

固定名稱模式不變。
`TryHandleQueryCommand` 仍傳送至指定管道，並將 `--pid` 視為一般命令引數。
第二個伺服器使用相同固定名稱時，仍回報 `PipeCreationFailed`，第一個伺服器繼續回應。
NFH 先以固定名稱模式零差異採用 Core。
後續另一個 pull request 才切換每視窗模式，執行以下變更：

1. 使用 `RuntimeQueryWindowPipes.BuildName(baseName, Environment.ProcessId)` 建立各伺服器名稱，再傳入既有伺服器建構函式。
2. 將命令列呼叫改為 `TryHandlePerWindowQueryCommand`，並提供相同基底名稱。
3. 加入工具自己的 `ServerNotFound` 錯誤對應，並測試新的視窗選擇行為。

在 Windows 上，兩個用戶端入口處理 `focus` 時，都使用已連線管道的伺服器程序 ID。
用戶端透過 [GetNamedPipeServerProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid) 讀取該 ID。
寫入請求前，先對該程序呼叫 [AllowSetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow)。
Windows 呼叫失敗不會停止請求。
請求位元組與回應處理皆不變。

測試涵蓋純函式選擇規則、精確命令列輸出、真正的 Windows 管道，以及透過內部測試接點觀察前景權限呼叫。
舊入口仍執行全部 40 個凍結命令列案例及既有邊界案例。
管道測試使用唯一名稱、有時間上限的等待，以及既有停用平行執行的 collection。
雙程序探索測試使用共用 test probe 的 `silent-wait` 模式，並在 `finally` 終止子程序。

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
請求、版本與取消 token 皆原樣傳遞，包含 null 請求。
派送前與佇列工作開始前皆檢查取消。
它回傳內部處理委派的回應，並讓例外向外傳遞。伺服器負責對應這些例外。
工具透過 `UiThread.RegisterRunningDispatcher` 登錄執行中的 dispatcher。

沒有執行中的 dispatcher 時，`Wrap` 以 null detail 對應 `DispatcherUnavailable`，並回傳失敗封套。
NFH 將此失敗對應為 `IPC_ERROR`，訊息為 `The UI dispatcher is unavailable.`。
NFH 將處理委派失敗對應為 `IPC_ERROR`，保留原始例外訊息。
這些對應由工具持有。Core 不提供這些失敗的產品錯誤文字。

`StopAsync` 在釋放前清除 host 持有的伺服器。後續 `Start` 會建立新伺服器，與凍結來源一致。
停止期間再次呼叫 `StopAsync` 會回傳同一個 task，所有呼叫者都等待同一次釋放。
host 不加入排程、視窗事件、應用程式生命週期事件、靜態實例或選項。
工具決定啟動時機，並在結束時呼叫 `StopAsync`。

`StopAsync` 立即停止接受新連線，並取消尚未讀到請求行的連線。
已讀到請求的連線可在伺服器的關閉時間上限內完成處理委派、寫出並清空回應緩衝區。
超過上限後，伺服器向處理委派發出合作式取消通知、關閉管道，並回報 `ShutdownTimedOut`。

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

## Generic commands

工具可將六個通用命令與產品命令一起登錄。
這是新增行為，沒有來源基準（new behavior, no source baseline）。
Core 只回傳命令 record，不啟動伺服器。
六個命令的啟動階段皆為 `None`，沒有啟動選項。
`--page`、`--help` 及其他啟動引數仍由工具持有。

通用 exit 設定 `RuntimeQueryCommand.ReceivesConfirmation` 為 true，讓處理委派在兩種路由器設定下都收到 `confirm`。
此 init 屬性位於 `Nvt.Core.RuntimeQuery`，其他命令的預設值為 false。
要求確認時，路由器仍先驗證 `WritesData`，再呼叫處理委派。
未選擇接收的命令仍會移除 `confirm`。
關閉確認防護時，路由器對兩種 `ReceivesConfirmation` 設定都原樣傳遞全部引數。
此屬性不改變啟動解析或執行。

公開型別皆位於 `Nvt.Core.Avalonia.RuntimeQuery`：

| 公開型別 | 契約 |
| --- | --- |
| `RuntimeQueryGenericCommands` | `Create(options)` 依下表順序回傳唯讀清單。工具選擇要登錄的 record。 |
| `RuntimeQueryGenericCommandOptions` | 不可變 record，包含工具識別、命令查詢、視窗查詢、導覽、結束決策與關閉動作。 |
| `IRuntimeQueryNavigation` | 提供 `Pages`、`CurrentPage` 及同步的 `SwitchPage(name)`。 |
| `RuntimeQueryPageResult` | 定義 `Switched`、`NeedsConfirmation` 與 `Rejected`。 |
| `RuntimeQueryExitResult` | 定義 `Closing`、`NeedsConfirmation` 與 `Rejected`。 |
| `RuntimeQueryExitRequest(Confirmed)` | 將解析後的確認值傳給工具的結束決策。缺少或空白確認值表示 false。 |
| `RuntimeQueryScreenshotResult` | 替代擷取回傳 `Success(pixelWidth, pixelHeight, fileSize)` 或 `Failure(code, message)`。檔案大小以位元組計。 |
| `RuntimeQueryGenericFailureCodes` | 下列失敗代碼常數，包含共用的 `INVALID_ARGUMENTS`。`USER_CONFIRMATION_REQUIRED` 表示工具的決策。路由器的 `WritesData` 防護使用 `CONFIRMATION_REQUIRED`。 |

| 名稱 | 風險 | 引數 | 成功資料 | 失敗代碼 |
| --- | --- | --- | --- | --- |
| `help` | `ReadOnly` | 無 | `{ commands: [{ name, risk }] }`，或工具原樣提供的文字資料 | 無 |
| `ping` | `ReadOnly` | 無 | `{ toolName, version, processId }` | 無 |
| `focus` | `ChangesState` | 無 | `{ focused: true }` | `NO_MAIN_WINDOW` |
| `page` | `ChangesState` | 無，或 `--name <page>` | 清單：`{ pages, currentPage }`。切換：`{ currentPage }` | `INVALID_ARGUMENTS`、`UNKNOWN_PAGE`、`USER_CONFIRMATION_REQUIRED`、`PAGE_REJECTED` |
| `screenshot` | `ChangesState` | `--path <file.png>` | `{ path, pixelWidth, pixelHeight, fileSize }` | `INVALID_ARGUMENTS`、`FILE_EXISTS`、`NO_MAIN_WINDOW`、`CAPTURE_UNAVAILABLE`，或替代擷取原樣回傳的失敗 |
| `exit` | `ChangesState` | 設定 `DecideExitRequest` 時可選用 `--confirm` | `{ closing: true }` | `INVALID_ARGUMENTS`、`USER_CONFIRMATION_REQUIRED`、`EXIT_REJECTED` |

Help 以字串回傳風險名稱，並保留登錄順序。
工具透過 `GetCommands` 提供清單，因為登錄會在建立命令後才完成。
設定 `HelpText` 即可原樣回傳文字，包含空白及空字串。
FreeformHelper 可用此方式保留自己的說明文字。
Ping 使用工具提供的名稱與版本，並加入 `Environment.ProcessId`。

工具以 `GetMainWindow` 提供 focus 與預設擷取使用的主視窗。
Focus 先將最小化視窗還原為 `Normal`，再呼叫 `Activate()`。
用戶端在送出請求前授予前景權限。
沒有主視窗時回傳 `NO_MAIN_WINDOW`，訊息為 `The main window is not available.`。

工具依自身順序提供頁面名稱，並持有目前頁面。
Core 使用 `StringComparer.Ordinal` 比較名稱，再要求 `SwitchPage` 執行導覽。
Core 不預設頁面名稱或啟動目標。
例如 NVT FW Combiner 可省略 settings，因為 settings 開啟的是對話框。
工具回傳以下決策：

- `Switched`：回傳新的目前頁面。請求目前頁面時成功，且不改變狀態。
- `NeedsConfirmation`：頁面保持不變，回傳 `USER_CONFIRMATION_REQUIRED`。工具不得開啟對話框。
- `Rejected`：回傳 `PAGE_REJECTED`，例如已有對話框阻止導覽。

已提供的 name 值為空白或 null 時，回傳 `INVALID_ARGUMENTS`，訊息為 `Argument '--name' requires a page name.`。
未知名稱回傳 `UNKNOWN_PAGE`，訊息為 `Unknown page '{name}'. Valid pages: {names}.`。
有效名稱保留工具順序，以逗號及一個空白分隔。
現有 query 解析器會將沒有值的選項編碼為字串 "true"。
處理委派無法區分沒有值的 --name 與明確指定的 true 頁面名稱。
`query page --name` 會回傳 `UNKNOWN_PAGE`，除非工具有名為 `true` 的頁面。
確認訊息為 `Page switching requires confirmation.`。拒絕訊息為 `The page switch was rejected.`。

Screenshot 不要求 `--confirm`，即使路由器已啟用確認防護。
擁有者將其指定為 `ChangesState`，因為擷取只接受絕對路徑，且永不覆寫檔案。
此明確例外讓 screenshot 不屬於 `WritesData`。
Core 要求完整絕對路徑，且副檔名必須為 `.png`，不區分副檔名大小寫。
Core 先正規化絕對路徑並檢查檔案是否存在，再執行擷取或查詢主視窗。
無效或缺少路徑時回傳 `INVALID_ARGUMENTS`，訊息為 `Argument '--path' must be an absolute path ending in '.png'.`。
目的地已有檔案或資料夾時回傳 `FILE_EXISTS`，訊息為 `The screenshot file already exists.`，且內容保持不變。
上層資料夾不存在時回傳 `INVALID_ARGUMENTS`，訊息為 `The folder for '--path' does not exist.`。

預設擷取先更新視窗排版。視窗最小化或沒有大小時，回傳 `CAPTURE_UNAVAILABLE`，訊息為 `The main window has no visible size to capture.`。否則以目前視窗大小及縮放比例繪製 `RenderTargetBitmap`。
PNG 先寫入目的資料夾內的暫存檔。
Core 不覆寫地移至最終名稱，失敗時刪除暫存檔。
若目的檔在移動前出現，Core 回傳相同的 `FILE_EXISTS` 失敗。
其他擷取例外會向外傳遞。

設定 `CaptureScreenshot` 可使用工具自己的非同步擷取流程。
NVT FW Combiner 可用此方式接上正式擷取。
委派接收正規化後的絕對路徑與呼叫的取消 token。
委派負責等待影格、排版、擷取及不覆寫的寫入。
Core 在呼叫委派前不更新排版，也不取得主視窗。
成功時回傳 `RuntimeQueryScreenshotResult.Success`，包含已儲存影像的像素尺寸及檔案大小。
失敗時回傳 `Failure`，原樣保留工具的代碼及訊息，例如 `PROTECTED_PATH` 或 `FILE_EXISTS`。
委派必須以不覆寫的移動處理 Core 檢查後才出現的目的檔。
委派例外原樣向外傳遞。

設定 `DecideExitRequest`，讓同步的結束決策讀取 `RuntimeQueryExitRequest.Confirmed`。
設定後，Core 呼叫此委派，取代 `DecideExit`。
未設定時，Core 呼叫既有 `DecideExit`，忽略全部引數，包含無效的 `confirm` 值。
既有建構子簽章及舊版結束決策保持不變。
兩個委派都必須直接決策，不得開啟對話框。

使用 `DecideExitRequest` 時，Core 透過 `RuntimeQueryArgumentParser.TryGetBoolArg` 解析 `confirm`，並忽略其他引數。
缺少或空白確認值表示 `Confirmed = false`。
命令列解析器將 `--confirm` 轉為 `confirm=true`，表示 `Confirmed = true`。
明確指定 `confirm=false` 表示 `Confirmed = false`。
布林解析器也接受 1、0、on、off、yes 及 no，不區分大小寫。
無效值原樣回傳路由器的 `INVALID_ARGUMENTS` 封套，且不呼叫任一委派。
`requireConfirmation` 為 true 或 false 時，這些值及回應都相同。

NVT FW Combiner 可依下列規則實作結束決策：

- 未確認：回傳 `NeedsConfirmation`，不開啟對話框。
- 已確認：回傳 `Closing`，略過工具自身的關閉確認。
- 已在關閉中，或開啟了模態對話框：回傳 `Rejected`。

Core 處理以下三種結果：

- `Closing`：產生 `{ closing: true }`，並將 `Close` 以 Background 優先序排入 UI dispatcher。
- `NeedsConfirmation`：回傳 `USER_CONFIRMATION_REQUIRED`，訊息為 `Exit requires confirmation.`。不關閉，也不開啟對話框。
- `Rejected`：回傳 `EXIT_REJECTED`，訊息為 `Exit was rejected.`。不關閉。

委派回傳 `Closing` 後，Core 先回傳回應，再於 UI 執行緒執行排程中的 `Close` 動作。
工具必須在關閉流程中等待 `RuntimeQueryHost.StopAsync` 完成，才能結束程序。
伺服器讓回應在關閉時間上限內完成寫入並清空緩衝區。
這讓 Core 在程序結束前送出回應，避免關閉流程截斷回應。
工具回傳 `Closing` 後必須執行關閉。
關閉動作必須略過自身的確認，且不得再否決已核准的決策。

常數包含 `NO_MAIN_WINDOW`、`USER_CONFIRMATION_REQUIRED`、`PAGE_REJECTED`、`UNKNOWN_PAGE`、`INVALID_ARGUMENTS`、`FILE_EXISTS`、`CAPTURE_UNAVAILABLE` 及 `EXIT_REJECTED`。
工具的擷取失敗可自行提供其他代碼，不需要 Core 對應。

例如，將選用的通用命令與產品命令一起登錄：

```csharp
IReadOnlyList<RuntimeQueryCommand> registered = [];
var options = new RuntimeQueryGenericCommandOptions(
    "Example tool", "1.0", () => registered, () => mainWindow,
    navigation, DecideExit, CloseNormally)
{
    DecideExitRequest = request => DecideExitWithConfirmation(request.Confirmed),
    CaptureScreenshot = CaptureProductionAsync
};
var generic = RuntimeQueryGenericCommands.Create(options);
registered = [.. generic.Where(command => command.Name != "help"), .. productCommands];
var router = new RuntimeQueryCommandRouter(registered, requireConfirmation: true);
var handler = RuntimeQueryUiThread.Wrap(
    (request, version, token) => router.ExecuteAsync(request, version, token), MapTransportError);
```

此範例選用五個通用命令，help 留在工具。
若要登錄通用 help，加入其 record，並可設定 `HelpText`。
完成的 `registered` 清單包含每個產品命令與風險。
工具將包裝後的處理委派交給既有伺服器設定。

Headless 測試驗證完整回應資料、訊息、導覽決策及延後關閉。
Exit 測試涵蓋兩種路由器設定、委派優先順序、解析失敗、舊版決策，以及回應先於 `Close` 執行。
固定視窗內容驗證兩種縮放比例的 PNG 尺寸，以及成功和失敗後的暫存檔清理。
測試也驗證替代擷取前的路徑檢查、委派自行排版，以及工具失敗與例外的原樣傳遞。

使用已還原的套件，設定 `AVALONIA_TELEMETRY_OPTOUT=1` 後執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```
