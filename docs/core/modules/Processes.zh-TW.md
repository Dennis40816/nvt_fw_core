[English](Processes.md)

# Processes

## 0.9.0 前的不相容變更

`ProcessInheritedHandle.EnvironmentVariable` 現為 `string?`。
未初始化的預設值包含 null 環境名稱與零控制代碼。
`ProcessLaunchGate.StartContained` 會在回呼或建立程序前拒絕該預設值。
通過建構函式與 `Parse` 驗證的名稱與控制代碼值維持原樣。

Contained launch 前，請透過建構函式或 `Parse` 建立繫結。
檢查可能未初始化的繫結時，請先防護環境名稱。
啟動期間請保留父程序擁有的原控制代碼。
測試涵蓋原生作業前拒絕預設值，以及有效繫結的不變行為。

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Infrastructure/ExternalTools/BoundedProcessOutputReader.cs`（完整讀取器及輸出 record）。
- `src/NvtFwCombiner.Infrastructure/ExternalTools/ExternalProcessResult.cs`（第 1–67 行：結果、清理列舉及例外；第 69–127 行的產品清理文字留在 NFC）。
- `src/NvtFwCombiner.Infrastructure/ExternalTools/ExternalProcessStartInfo.cs`（啟動要求，不含第 36–40 行的 `ToExecutedCommand()`）。
- `src/NvtFwCombiner.Infrastructure/ExternalTools/IExternalProcessRunner.cs`（完整介面）。
- `src/NvtFwCombiner.Platform/Processes/WindowsSynchronousReadCancellation.cs`（完整取消機制）。
- `tests/NvtFwCombiner.Infrastructure.Tests/ExternalTools/BoundedProcessOutputReaderTests.cs`（全部 11 個案例及完整輔助程式）。

- `src/NvtFwCombiner.Platform/Processes/ProcessLaunchGate.cs`（完整閘門及控制代碼 record，分成獨立的 Core 檔案）。
- `src/NvtFwCombiner.Platform/Processes/WindowsContainedProcessStarter.cs`（完整原生受控啟動器）。
- `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/ProcessLaunchGateTests.cs`（全部八個案例及輔助行為）。
- `tests/NvtFwCombiner.TestSupport/TempWorkspace.cs`（暫存路徑、位元組寫入及有界清理支援；產品前綴及儲存庫路徑轉接保留在 NFC）。

- `src/NvtFwCombiner.Infrastructure/ExternalTools/SystemExternalProcessRunner.cs`（完整 runner、清理時間、schedule、容量、階段及 seams；輔助型別分成獨立 Core 檔案）。
- `src/NvtFwCombiner.Infrastructure/ExternalTools/SystemExternalProcessRunner.Invocation.cs`（完整 invocation custody 及終止清理）。
- `tests/NvtFwCombiner.Infrastructure.Tests/ExternalTools/SystemExternalProcessRunnerTests.cs`（全部六個案例及程序身分／退出斷言；子程序 fixture 使用共用 test probe）。

- `tests/NvtFwCombiner.Infrastructure.Tests/ExternalTools/SystemExternalProcessRunnerLifetimeTests.cs`（25 個 runner、排程、容量及取消方法與其 nested helpers；清理文字方法保留於 NFC）。

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

僅使用 BCL、以 net8.0 為目標的 `Nvt.Core.Processes` 模組擁有外部程序契約及命令執行、有界 UTF-16 診斷、Windows 同步讀取取消及單一程序內啟動閘門。Launcher 使用受控建立，並擁有就緒協定及長期 Job。Launcher.Transport 繼續保有獨立的嚴格 UTF-8 行讀取器。

## API

```csharp
public readonly record struct ProcessInheritedHandle
{
    public ProcessInheritedHandle(string environmentVariable, IntPtr handle);
    public string? EnvironmentVariable { get; }
    public IntPtr Handle { get; }
    public static ProcessInheritedHandle Parse(string environmentVariable, string handle);
}

public static class ProcessLaunchGate
{
    public static Process? Start(ProcessStartInfo startInfo);
    public static Process? StartContained(
        ProcessStartInfo startInfo, IReadOnlyList<ProcessInheritedHandle> inheritedHandles);
    public static Process? StartContained(
        ProcessStartInfo startInfo, IReadOnlyList<ProcessInheritedHandle> inheritedHandles,
        Func<bool> validateImmediatelyBeforeStart);
    public static bool TryClearInheritance(IntPtr handle);
}

public interface IExternalProcessRunner
{
    ValueTask<ExternalProcessResult> RunAsync(
        ExternalProcessStartInfo startInfo, CancellationToken cancellationToken);
}

public sealed partial class SystemExternalProcessRunner : IExternalProcessRunner
{
    public SystemExternalProcessRunner();
    public ValueTask<ExternalProcessResult> RunAsync(
        ExternalProcessStartInfo startInfo, CancellationToken cancellationToken);
}

public sealed class ExternalProcessStartInfo
{
    public ExternalProcessStartInfo(
        string executablePath, string workingDirectory,
        IEnumerable<string> arguments, TimeSpan timeout);
    public string ExecutablePath { get; }
    public string WorkingDirectory { get; }
    public IReadOnlyList<string> Arguments { get; }
    public TimeSpan Timeout { get; }
}

public sealed record ExternalProcessResult(
    int ExitCode, bool TimedOut, string StandardOutput, string StandardError)
{
    public ExternalProcessCleanup Cleanup { get; init; } = ExternalProcessCleanup.Complete;
}

public enum ExternalProcessCleanup
{
    Complete, TerminationUnconfirmed, OutputStreamHeldOpen, OutputReadFailed
}

public sealed class ExternalProcessCleanupCapacityException : Exception
{
    public ExternalProcessCleanupCapacityException(int inUseInvocations, int limit);
    public int InUseInvocations { get; }
    public int Limit { get; }
}

public sealed class ExternalProcessStartFailedException : Exception
{
    public ExternalProcessStartFailedException(Exception startException);
}
```

供受控啟動及外部 runner 使用的內部契約為 `BoundedProcessOutputReader.ReadAsync(TextReader) : Task<string>`、`DrainProcessStreamAsync(TextReader, CancellationToken) : Task<BoundedProcessOutput>` 及 `DrainAsync(TextReader, CancellationToken) : Task<BoundedProcessOutput>`。`BoundedProcessOutput` 是內部 readonly record struct，包含 `string Text` 及 `bool ReachedEndOfStream`。`WindowsSynchronousReadCancellation` 維持 internal、sealed、partial、可釋放，並保留 `[SupportedOSPlatform("windows")]`。

指定的凍結來源沒有產品專屬的路徑或引數上限。正式 invocation 容量是八個的固定機制界限，由所有預設 runner 實例共用。讀取器、終止時間及暫停子程序終止確認的五秒界限均保持固定。啟動要求唯一必須為正的數值參數是 `timeout`；容量例外只記錄傳入整數，不加驗證。產品准入政策仍由主應用程式負責。

## 保留行為

| 機制 | 凍結契約 |
| --- | --- |
| 擷取容量 | 最多 65,536 個 UTF-16 字元。等於容量時保留完整輸出；超過一個字元時截斷。 |
| 前綴 | 代理字元調整前為 32,768 個字元。 |
| 尾端 | 使用池化的 32,768 字元環形緩衝區。保留尾端的額度為總容量扣除前綴及標記。 |
| 讀取緩衝區 | 每次固定要求 4,096 個字元。 |
| 標記 | 完整保留 `"\n...[process output truncated]...\n"`。 |
| 純量邊界 | 只在建立截斷輸出時移除前綴末端的高代理字元、略過尾端起點的低代理字元，並移除尾端末端的高代理字元。完整輸出逐字元保留。 |
| 計數及儲存 | 總字元計數在 `long.MaxValue` 飽和；兩個池化緩衝區歸還時均使用 `clearArray: true`。 |
| 程序排空 | 使用 `LongRunning | DenyChildAttach` 及 `TaskScheduler.Default`；啟動失敗以 faulted task 回傳。 |
| Windows 讀取 | 專用擁有執行緒使用 `THREAD_TERMINATE` 權限（`0x0001`）開啟自己的執行緒控制代碼，執行同步讀取並註冊停止取消。 |
| 取消競態 | 註冊前後均檢查停止；行內取消不影響其他 I/O。每 1 ms 重試 `CancelSynchronousIo` 直到讀取完成，下一次讀取或關閉控制代碼前先等待回呼結束。 |
| 停止排空 | 回傳已擷取文字及 `ReachedEndOfStream == false`。只有停止 token 已觸發時才抑制讀取取消。Windows 低 16 位元錯誤碼 995（`ERROR_OPERATION_ABORTED`）也只有停止時才如此處理。 |
| 其他平台 | 保留 `reader.ReadAsync`；此機制不會讓忽略停止的 reader 變得可中斷。 |
| 分析器及平台規則 | 保留來源的平台防護、平台屬性及有理由的 CA1031／CA1849 抑制。 |

啟動驗證順序維持執行檔路徑、工作目錄、引數序列，再檢查 timeout。路徑使用 `ArgumentException.ThrowIfNullOrWhiteSpace`，引數使用 `ArgumentNullException.ThrowIfNull`。非正 timeout 產生 `ArgumentOutOfRangeException`，保留 `ParamName == "timeout"`、實際值及 `Timeout must be positive.`。驗證完成後才列舉引數並複製到新陣列；不新增路徑或引數元素的正規化及驗證。

Cleanup 預設為 `Complete` 並參與結果相等性。只有完整清理才能把擷取文字當成成功結果或協定輸入；未完整清理仍可附帶錯誤診斷。觀察到直接子程序退出及兩個串流結束，不能證明所有後代程序均已停止：未持有任何串流的後代無法觀察。

容量拒絕訊息逐字保留：

```text
The external process runner is at its limit of {limit} invocations that are running or still cleaning up ({inUseInvocations} in use when the reservation was refused); a new run is refused. Restart the application.
```

啟動失敗訊息維持 `The external process could not be started ({startException.GetType().Name}).`，並保留原始例外實例作為 inner exception。傳入 null 啟動例外時，仍保留來源的解參照失敗。

## 測試及來源對應

讀取器及外部程序契約涵蓋 40 個已記錄 XML 文件的公開測試方法，靜態列舉為 95 個案例：11 個移植案例、15 個讀取器邊界方法（49 個案例）及 14 個契約方法（35 個案例）。全部輸入均為合成資料。可接受 token 的呼叫均直接使用 `TestContext.Current.CancellationToken` 或透過相連的停止來源傳入。

來源名稱、斷言、門檻及 `BeforeKernelReadReader`、`HeldOpenReader`、`CreatePattern`、`AssertWellFormedUtf16` 全數保留。三處來源 `Production.Drain` 改呼叫 `DrainProcessStreamAsync`。測試支援僅新增 Core namespace、明確 xUnit import、覆寫成員 XML 文件及測試 token 接線。

| 凍結來源案例 | Core 案例所在類別 `BoundedProcessOutputReaderTests` |
| --- | --- |
| `ProductionDrainStartupFailureReturnsFaultedTask` | `ProductionDrainStartupFailureReturnsFaultedTask` |
| `ProductionDrainAlreadyStoppedReturnsWithoutReading` | `ProductionDrainAlreadyStoppedReturnsWithoutReading` |
| `ProductionDrainStopsWhenCancellationPrecedesKernelRead` | `ProductionDrainStopsWhenCancellationPrecedesKernelRead` |
| `SmallOutputRemainsExact` | `SmallOutputRemainsExact` |
| `StoppedDrainKeepsCapturedTextWithoutEndOfStream` | `StoppedDrainKeepsCapturedTextWithoutEndOfStream` |
| `CompletedDrainReportsEndOfStream` | `CompletedDrainReportsEndOfStream` |
| `ExactCaptureLimitRemainsExact` | `ExactCaptureLimitRemainsExact` |
| `FirstTruncatedCharacterRetainsExactPrefixAndTail` | `FirstTruncatedCharacterRetainsExactPrefixAndTail` |
| `MultipleRingWrapsRetainExactOrderedTail` | `MultipleRingWrapsRetainExactOrderedTail` |
| `TruncationDoesNotRetainUnpairedHighSurrogateBeforeMarker` | `TruncationDoesNotRetainUnpairedHighSurrogateBeforeMarker` |
| `TruncationDoesNotRetainUnpairedLowSurrogateAtTailStart` | `TruncationDoesNotRetainUnpairedLowSurrogateAtTailStart` |

新增讀取器測試：

- `CaptureConstantsRetainFrozenValues` 固定容量及標記。
- `CaptureBoundariesRetainExactExpectedText` 涵蓋 0、1、32,767／32,768／32,769 及 65,535／65,536／65,537 字元。
- `ReadBufferBoundariesUseFixedReadRequests` 涵蓋 4,095／4,096／4,097 字元，並確認每次要求均為 4,096。
- `SingleAndChunkedSendsRetainIdenticalOutput` 比較完整及截斷輸出、1／17／4,095／4,096／4,097 字元分塊及多次環形繞回。
- `CompleteCapturePreservesSurrogatePairsAcrossBoundaries`、`TruncationPreservesPairsAroundReadAndPrefixBoundaries`、`TruncationPreservesPairsAroundTailStart`、`TruncationHandlesSurrogatesAtTailEnd` 涵蓋純量邊界，包括跨越尾端環形繞回的代理字元配對。
- `CompleteOutputPreservesSourceUtf16WithoutDecoding` 描述完整輸出中原始未配對代理字元的行為。
- `StoppedDrainRetainsTruncatedOutputWithoutEndOfStream` 使用確定性的全部文字已送出閘門。
- `DrainAsyncRejectsNullReaderWhenAwaited` 及 `ReadAsyncRejectsNullReaderWhenAwaited` 固定非同步 null 失敗。
- `UnrequestedReadCancellationPropagates`、`OperationAbortedRequiresWindowsAndStoppedToken`、`ProcessDrainUsesPlatformReadPath` 固定失敗篩選及讀取路徑。

新增契約測試：

- `StartInfoRejectsNullPaths`、`StartInfoRejectsBlankPaths`、`StartInfoRejectsNullArguments`、`StartInfoPreservesValidationOrder` 涵蓋建構失敗及列舉順序。
- `StartInfoRejectsNonpositiveTimeout`、`StartInfoAcceptsPositiveTimeout` 涵蓋負一 tick、零、正一 tick 及可表示的兩端極值。
- `StartInfoCopiesArguments` 涵蓋清單及陣列擁有權；`StartInfoPreservesUnboundedNonblankValues` 描述 511／512／513 字元及未變更的空白／null 引數元素。
- `ResultDefaultsToCompleteAndUsesValueEquality` 及 `ResultEqualityIncludesAllObservedValues` 涵蓋所有相等性組成。
- `CapacityExceptionPreservesPropertiesAndExactMessage` 涵蓋傳入容量八的前一個／邊界／後一個計數，以及不另驗證的零、負值及整數極值。
- `StartFailurePreservesTypeTextAndInnerException`、`StartFailureWithNullExceptionPreservesSourceFailure`、`CleanupEnumPreservesExactNamesAndOrder` 固定其餘契約。

kernel 讀取前的競態測試使用真正的 Windows 匿名管線及確定性閘門，非 Windows 透過 `Assert.Skip` 明確略過。略過不提供原生證據。以下受控啟動測試提供真正的子程序及繼承控制代碼證據；Job 成員關係屬於獨立的生命週期範圍。

## 受控建立行為

`Start` 與兩個 `StartContained` 多載透過同一個 private static `object` 鎖序列化。只有經過此 API 的啟動才參與閘門。一般啟動在鎖定前檢查 `startInfo`；受控啟動依序檢查 `startInfo`、`inheritedHandles` 及 `validateImmediatelyBeforeStart` 是否為 null，接著以 `ArgumentException`（`ParamName == "inheritedHandles"`）拒絕任何未初始化的控制代碼。這些檢查都在鎖定前完成。兩參數多載提供永遠回傳 true 的回呼。

`ProcessInheritedHandle` 依序拒絕空白名稱、含 `=` 的名稱，以及有號 `ToInt64()` 值非正的控制代碼。其餘名稱和值保持原樣。`Parse` 使用 `NumberStyles.None` 及 invariant culture；解析失敗保留 `ParamName == "handle"` 與 `Inherited handle must be a positive decimal value.`。解析成功的零值由建構函式拒絕。Record 相等性包含完整名稱及控制代碼；default record 保留 null／零欄位。

Windows 僅由一個 internal `WindowsContainedProcessStarter` 執行下列步驟：

1. 拒絕 shell、任何重新導向標準串流或非完整絕對執行檔路徑；接著檢查替代使用者名稱／密碼、混用 `Arguments` 與 `ArgumentList`，最後以 ordinal 忽略大小寫比較檢查重複綁定名稱。這些檢查都在複製控制代碼與最後驗證之前。
2. 依宣告順序複製原始控制代碼，保持相同存取權限並啟用繼承。使用 ordinal 忽略大小寫鍵複製環境，再將宣告名稱綁定到副本的 invariant 十進位值。呼叫者的環境不變。原始控制代碼仍由呼叫者擁有；需要時透過 `TryClearInheritance` 清除其繼承旗標。
3. 非空允許清單只初始化一個 `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`，其中只有這些副本，並啟用 extended startup information。空清單關閉繼承，使用一般 startup information。機制不新增控制代碼數量上限。
4. 環境項目以 ordinal 忽略大小寫排序，省略 null 值、保留空值及凍結的 NUL 結尾。執行檔及各個 `ArgumentList` 項目保留空格／tab／引號及反斜線 quoting 規則；沒有 argument list 時逐字附加原始 `Arguments`。
5. 準備原生緩衝區後，在仍持有閘門時執行最後 custody 驗證；拒絕時回傳 null 且不建立子程序。驗證例外在清理後原樣傳播。`CreateProcessW` 使用 suspended 及 Unicode environment 旗標，並依要求使用 no-window。先查找 managed process 並取得其 handle，再恢復原生執行緒。
6. 建立後、恢復前失敗時，釋放 managed process，以 exit code 1 終止暫停子程序，最多等待 5,000 ms 確認。確認終止後傳播原始失敗；終止或確認失敗時，以原生錯誤或有界 timeout 包裝原始失敗。
7. `finally` 依凍結順序關閉原生執行緒／程序控制代碼、刪除 attribute list、釋放 handle-list／environment／command-line 緩衝區，並釋放所有副本。凍結函式本身未釋放 attribute-list 的配置；此配置行為保持原樣。

原生拒絕訊息逐字保留：

- `Contained process starts require an absolute executable, shell disabled, and no redirected streams.`
- `Contained process starts do not support alternate credentials.`
- `Contained process starts cannot mix Arguments and ArgumentList.`
- `Inherited handle environment names must be unique.`（`ParamName == "inheritedHandles"`）。

建立後失敗保留 `Contained process creation failed and the suspended child could not be terminated.`、`Contained process creation failed and child termination could not be confirmed.` 及 `The suspended child did not terminate within the bounded confirmation deadline.`。其他原生失敗維持使用最後原生錯誤的 `Win32Exception`。

非 Windows 受控啟動在同一閘門內執行最後驗證，再呼叫 `Process.Start` 或回傳 null；不套用 Windows 限制及控制代碼允許清單。`TryClearInheritance` 在其他平台回傳 true；Windows 在 `SetHandleInformation` 前拒絕 0 與 -1。原生成員保留 Windows 平台屬性及 mutable-layout 警告抑制。Core 將 net9 鎖改為 `object`，並將相同 quoting 字元存入 static readonly 陣列以符合分析器要求。

### 與凍結來源的刻意差異

Core 在一處與凍結的 NFC 受控啟動不同：它拒絕來源接受或太晚才失敗的輸入。

- 未初始化的繼承控制代碼（null 名稱或零控制代碼）現在於所有平台丟出 `ArgumentException`。檢查在回呼與任何原生作業之前執行。
- 在 Windows，來源會嘗試複製零控制代碼並以 `Win32Exception` 失敗。在非 Windows，來源忽略該控制代碼並啟動程序。兩種情況現在都會丟出例外。
- 此檢查也早於原生設定檢查，因此 `UseShellExecute = true` 搭配預設控制代碼現在丟出 `ArgumentException`，不再是 `InvalidOperationException`。
- 有效繫結、回呼順序與其他所有拒絕條件維持凍結行為。

## 受控啟動測試對應

受控啟動新增 47 個已記錄 XML 文件的公開測試方法及 107 個靜態列舉案例：八個移植案例方法，以及以下輸入、閘門與準備邊界方法。

全部凍結閘門案例在 `ProcessLaunchGateTests` 保留原名。測試使用共用的 BCL-only probe，不複製產品 probe 專案。

| 凍結及 Core 案例 | 共用 probe 模式 |
| --- | --- |
| `ContainedChildExcludesUnstatedAmbientInheritableHandle` | `ambient-pipe` |
| `InvalidAllowlistHandleStartsNoChildAndDoesNotPoisonNextLaunch` | `ambient-pipe` |
| `InheritedHandleRejectsEveryNonPositiveValue` | 不建立子程序 |
| `ParallelContainedStartsDoNotCrossInheritHandles` | `contained-isolation` |
| `ContainedStartPreservesUnicodeArgumentsAndEnvironment` | `arguments-environment` |
| `FinalValidationRejectsChangeAfterNativePreparation` | `ambient-pipe` |
| `ValidationWaitsForGateAndRejectsChangedStateBeforeStart` | `arguments-environment` |
| `PostCreateFailureTerminatesSuspendedChildAndReleasesPhysicalPipe` | `contained-isolation` |

`ProcessSerialCollection` 序列化共用程序狀態的閘門測試。原有實體管線斷言、`first`／`second` 內容、Unicode 值及兩秒／200-ms 門檻保持原樣。其餘原本無界的 fixture 等待限制為 30 秒。僅限 Windows 的案例在其他平台使用 xUnit skip。

`ProcessInheritedHandleTests` 新增建構檢查順序、空白／等號名稱、未正規化的名稱、零／負值／正一控制代碼、有號指標極值、invariant 十進位語法、`long.MaxValue` 前一值／邊界／後一值及 record 相等性。啟動閘門沒有可調整的正值數字上限參數。

`ProcessLaunchGateBoundaryTests` 新增公開／內部 null 順序、一般 `exit` 啟動、拒絕且無 marker、回呼失敗與閘門重用、一般啟動序列化、原生驗證順序、default record 在回呼前即被拒絕、精確零／一／二控制代碼清單、相同原始控制代碼綁定、父環境不變、原生建立清理，以及無效／真實管線／非 Windows 的繼承清除。完整 probe 資料夾副本只重新命名 apphost 並測試原始 Arguments。

`WindowsContainedProcessStarterContractTests` 固定空／非空 command line、原始引數、零／一／二反斜線、空格／tab／引號／換行邊界、Unicode、ordinal 忽略大小寫環境排序、null 省略、空值、零／一／二環境項目及精確結尾。它固定 5,000-ms 確認常數；此期限沒有呼叫者可傳入的前一值／後一值。原生建立後案例證明暫停子程序不能執行，且其實體管線在原有兩秒門檻內關閉。

`OptionalPowerShellQuotedArguments` 獨立透過 PowerShell 測試 `two words`、`quote"inside` 及 `trail\`。執行檔不可用或 PowerShell 引數解讀不同時報告 skip，不修改受控啟動器。

## 外部命令執行

`SystemExternalProcessRunner` 是單次外部命令 invocation 的唯一擁有者。建立只使用 `ProcessLaunchGate.Start(CreateProcessStartInfo(startInfo))`；輸出使用既有的 `BoundedProcessOutputReader.DrainProcessStreamAsync`。Launcher 另外擁有 READY、ADMITTED、協定 Job 及准入期限。

`RunAsync` 依序檢查 null 輸入、呼叫者取消，再於建立程序前以原子方式保留容量。容量拒絕不啟動程序，並回報保留時觀察到的數量。所有啟動失敗均歸還槽位。`Win32Exception` 轉成 `ExternalProcessStartFailedException`；其他失敗原樣傳播。null 程序保留 `External process did not start.`。啟動資訊保留執行檔、工作目錄及每個有序的 `ArgumentList` 項目，停用 shell、不建立 console，並重新導向兩個輸出串流。

| 機制 | 凍結 runner 契約 |
| --- | --- |
| 正式容量 | 所有預設實例共用八個 invocation，包含 detached 清理；沒有公開容量設定。 |
| 終止總期限 | 終止訊號後五秒，由所有終止等待共用。 |
| held-output grace | 自然退出後兩秒。 |
| reader-stop reserve | 總期限內最後一秒。 |
| 取消回呼 | 只送出訊號，不執行 OS 終止或資源釋放。 |
| 終止 | 每次 invocation 一個背景工作，使用 `Kill(entireProcessTree: true)`。 |
| 退出及輸出 | 觀察 `WaitForExitAsync`，並行排空兩個串流，保留有界部分輸出。 |
| 正式 observer | null；排序及故障 seams 維持 internal。 |

兩個串流及退出觀察先啟動，之後才註冊取消並送出 `Started`。選取終止訊號時已觀察到的呼叫者取消，優先於同時發生的退出或 timeout。自然退出等待串流或取消直到 grace 時點；仍開啟的串流啟動終止，而沒有取消的 grace 到期送出 `OutputHeldAfterExit`。其他終止訊號立即啟動終止。終止、退出及串流 settlement 共用 reader-stop 時點。未完成的 reader 收到 `CancelAsync`，全部終止工作及取消派送共用最後期限。

清理優先順序維持 `TerminationUnconfirmed`、`OutputStreamHeldOpen`、`OutputReadFailed`，最後為 `Complete`。aggregate、Win32、invalid-operation 及 unsupported 終止失敗使用既有外部清理結果。Timeout 回傳 exit code -1 及 `TimedOut == true`；自然退出保留直接子程序的 exit code。取消使用呼叫者 token 並保留完整文字 `The external process run was canceled; observed cleanup: {cleanup}.`。

只要任何追蹤工作尚未完成，`Release` 就保留原有容量。完成後先觀察所有晚到的 fault，個別釋放 stdout、stderr、process、reader-stop source 及 exit-observation source，最後歸還容量。全部釋放成功送出 `ResourcesReleased`；任一釋放失敗送出 `ResourcesReleaseFailed`。net8.0 移植以 private `object` 作為 invocation gate，保留兩個 lock statement。Runner 不承接 Launcher Job 或 READY 狀態。未持有任何重新導向串流的後代仍不在其觀察契約內。

內部契約為 `ExternalProcessCleanupTiming`、`CleanupSchedule`、`ExternalProcessCapacity`、`ExternalProcessRunnerPhase` 及 `ExternalProcessRunnerSeams`。時間驗證依序保留：正值 deadline、正值 grace、正值 reserve、grace 加 reserve 不超過 deadline，最後 deadline 不超過 `int.MaxValue` 毫秒。總和超限維持 `ParamName == "HeldOutputGrace"`。Schedule 保留 `(long)(span.TotalSeconds * Stopwatch.Frequency)`，並以終止訊號建立絕對時間戳。內部測試容量必須為正；正式容量仍為八個。

## Runner 測試及來源對應

`SystemExternalProcessRunnerTests` 保留全部六個凍結案例的名稱、斷言及執行／退出觀察門檻。凍結類別沒有 collection attribute，這個選擇保持不變。共用 probe 契約為 [probe README](../../../tests/Nvt.Core.TestProbe/README.md)，不需私人子程序 harness 或修改 probe。

| 凍結案例及 Core 方法 | 共用 probe 及保留證據 |
| --- | --- |
| `CreateProcessStartInfoIsHeadlessAndShellFree` | 不建立子程序；旗標、執行檔、工作目錄及引數順序。 |
| `RunAsyncTranslatesOperatingSystemStartFailureToTypedExceptionAndReleasesCapacity` | 不存在的執行檔，再使用 `exit`；typed Win32 失敗、容量歸零及成功重用。 |
| `RunAsyncCancellationKillsChildProcessBeforeThrowing` | `tree-root-wait`；取消前擷取 root／child 的 PID 及 start-time 身分，30 秒執行 timeout，各程序三秒退出觀察。 |
| `RunAsyncTimeoutKillsChildProcessBeforeReturning` | `tree-root-wait`；十秒 timeout 前擷取身分，timeout 旗標、exit -1 及三秒退出觀察。 |
| `RunAsyncBoundsAndDrainsBothOutputStreams` | `dual-output-exit`；各串流 131,072 字元加 `OUT-END`／`ERR-END`，十秒 timeout，精確擷取長度、前綴、截斷標記及後綴斷言。 |
| `RunAsyncTimeoutRetainsBoundedPartialOutputAfterKill` | `dual-output-wait`；各串流 131,072 字元加 `OUT-PARTIAL-END`／`ERR-PARTIAL-END`，十秒 timeout（NFC 為兩秒；負載高時 probe 啟動可能超過一秒，probe 仍會等待 30 秒），exit -1、timeout 旗標、精確有界長度及後綴。 |

`SystemExternalProcessRunnerBoundaryTests` 新增 23 個方法及 56 個靜態列舉案例：

- 預設時間、八槽正式容量、null observer，以及全部 phase 名稱與值。
- 精確的 `Schedule(1000)` 時間戳及小數 tick 轉換，包含最大相容 deadline。
- 每個正值 timing 欄位及 private capacity 參數的零／負值；最小正值時間；驗證順序及來源的 overflow 行為。
- Grace 加 reserve 及最大 deadline 的前一 tick／精確邊界／後一 tick；建構函式時間驗證。
- 一、七、八、九及 `int.MaxValue` 的 private capacity，涵蓋精確准入、拒絕、觀察數量、歸還及重用。
- Null seams、null 輸入優先順序、空／滿容量下已取消的輸入，以及嘗試不存在執行檔前的 private 單槽拒絕。
- 精確啟動引數／工作目錄；真正的 `exit --exit-code 7`、phase 順序，以及五秒內觀察到容量歸還。
- 真正 stdout／stderr 的獨立路由，以及 65,535／65,536／65,537 擷取長度。
- 共用 OS 終止失敗歸類為 `TerminationUnconfirmed`、單一終止工作及 private capacity 歸還。

測試使用合成資料、獨立暫存資料夾中的 tree marker 及 test-context 取消 token。原生案例執行 Windows 程序，其他平台透過 xUnit skip 明確略過。Marker 輪詢只在 fixture 界限內重試短暫分享違規；PID 加 start time 避免緊急清理誤殺重用的 PID。這些測試描述外部命令行為；長期 Job 證據屬於 Launcher。

## Runner 生命週期測試對應

26 個凍結生命週期情境中，Core 包含其中 25 個。這些測試 runner、排程、容量及取消機制的方法，在 `SystemExternalProcessRunnerLifetimeTests` 保留原名及來源順序。NFC 保留 `CleanupDiagnosticsDescribeOnlyTheObservation` 及其三組 theory 案例，因為它們測試 NFC 的產品 formatter `ExternalProcessCleanupText.Describe`。Core 沒有清理文字 API，也不複製該 formatter；精確文字斷言留在 NFC。

| 凍結來源方法 | Core 方法或保留擁有者 | 共用 probe 及凍結證據 |
| --- | --- | --- |
| `CancelReturnsAtOnceAndRunEndsWithinDeadlineWhileTerminationBlocks` | 同名方法 | `silent-wait`；阻擋終止、取消在一秒內返回、有界執行及晚到釋放。 |
| `TimeoutWithBlockedTerminationReturnsUnconfirmedWithinDeadline` | 同名方法 | `silent-wait`；300-ms timeout、阻擋終止及未確認清理。 |
| `RefusedTerminationIsClassifiedAsUnconfirmed` | 同名方法 | `silent-wait`；aggregate、Win32、invalid-operation 及 unsupported 拒絕資料。 |
| `RefusedTerminationOnCancellationEndsCanceled` | 同名方法 | `silent-wait`；拒絕終止仍以呼叫者取消結束。 |
| `TerminationWithoutObservedExitIsBoundedAndUnconfirmed` | 同名方法 | `silent-wait`；忽略終止及單一有界清理期限。 |
| `CancellationRightAfterTimeoutSignalEndsCanceled` | 同名方法 | `silent-wait`；在 `TimeoutSignaled` 注入取消。 |
| `CancellationRightAfterExitSignalEndsCanceled` | 同名方法 | `exit`；在 `ExitSignaled` 注入取消。 |
| `CancellationAfterTerminalDecisionKeepsResult` | 同名方法 | `exit`；在 `Returning` 注入取消並保留結果。 |
| `UncooperativeReaderIsDetachedAtDeadlineAndObservedLater` | 同名方法 | `exit`；被阻擋的 reader 脫離、晚到失敗及釋放持有資源。 |
| `ReaderFaultWithoutCancellationIsOutputReadFailed` | 同名方法 | `exit`；reader 失敗以清理分類回報。 |
| `ReaderStartupFaultIsOutputReadFailed` | 同名方法 | `exit`；正式 drain 啟動失敗及單槽釋放。 |
| `ReaderFaultWithCancellationEndsCanceled` | 同名方法 | `silent-wait`；取消優先於 reader 失敗。 |
| `ExitObservationFaultIsUnconfirmedOrCanceled` | 同名方法 | `silent-wait`；原有兩組取消 theory 資料及退出觀察失敗。 |
| `HeldOutputAfterNaturalExitIsBoundedAndReported` | 同名方法 | `tree-root-exit`；正式時間及 output-held phase。 |
| `CancellationDuringHeldDrainEndsCanceledWithinDeadline` | 同名方法 | `tree-root-exit`；正式時間下於直接 root 退出時取消。 |
| `OrphanHoldingOutputAfterTimeoutIsBoundedAndReported` | 同名方法 | `orphan-chain-root`；leaf PID、`.middle` 及 `.ready`；三秒 timeout 前完成父程序退出交握。 |
| `OrphanHoldingOutputAfterExitStopsWithoutDetachingAndKeepsText` | 同名方法 | `orphan-chain-exit --stdout-text before-reader-stop`；middle 先退出，再由 root 以 code 0 自然退出，存活的 leaf 仍持有兩個串流。 |
| `DescendantWithoutRedirectedStreamIsNotObserved` | 同名方法 | `detached-descendant-root`；完整清理可與無法觀察的存活後代同時存在。 |
| `CleanupScheduleKeepsReaderStopWithinTheSingleDeadline` | 同名方法 | 不建立子程序；精確正式 schedule 及期限內的 reserve。 |
| `CleanupDiagnosticsDescribeOnlyTheObservation` | NFC 產品 formatter | 三組 theory 案例測試 `ExternalProcessCleanupText.Describe`；NFC 保留精確文字及禁止歸因於未觀察原因的兩個斷言。 |
| `CancelWithinGuardFailsPromptlyOnBlockingCancel` | 同名方法 | 不建立子程序；一秒阻擋取消 guard 及十秒回報上限。 |
| `DetachedCleanupIsBoundedAndRefusesNewRunsAtTheLimit` | 同名方法 | 先 `silent-wait` 再 `exit`；單槽拒絕及晚到終止後重用。 |
| `CapacityIsAHardCapUnderConcurrentStarts` | 同名方法 | `exit`；八個專用執行緒以 barrier 爭取三槽，共十輪。 |
| `TryReserveIsAtomicUnderContention` | 同名方法 | 不建立子程序；32 個專用競爭者、八槽及 200 輪。 |
| `DisposalFailureStillReturnsSlotAndSignalsFailure` | 同名方法 | `exit`；全部五次釋放、失敗通知及槽位重用。 |
| `DetachedDisposalFailureStillReturnsSlotAndSignalsFailure` | 同名方法 | `silent-wait`；脫離終止後執行五次釋放及失敗通知。 |

類別保留來源沒有 collection attribute 的選擇。所有原生案例需要真正的 Windows 程序，並在其他作業系統透過 xUnit skip 略過。Schedule、原子保留及阻擋取消 guard 案例可跨平台執行。`TestToken` 為 `TestContext.Current.CancellationToken`；排序由 phase hooks、阻擋終止、被阻擋的 readers 及專用執行緒 barrier 驅動。每個原有執行皆在 40 秒 watchdog 下等待，計時從觸發呼叫之前開始，包括 `Cancel()`。

| 凍結測試界限 | 值及意義 |
| --- | --- |
| Helper 生命週期 | 30 秒；共用 `silent-wait` 及後代模式的存活時間超過所有終止界限。 |
| Watchdog | 執行及 phase 等待均為 40 秒。 |
| 排程餘裕 | 選定清理 deadline 之外四秒。 |
| 取消返回 | 嚴格少於一秒，在 `Cancel()` 之前開始計時。 |
| Fast 清理 | Deadline 1,500 ms；held-output grace 300 ms；reader-stop reserve 500 ms。 |
| 正式清理 | Deadline 五秒；grace 兩秒；reserve 一秒。 |
| Timeout fixtures | 慢 runner 案例為 300 ms；orphan-chain 父程序退出交握為三秒；原有 30 秒及 60 秒啟動 timeout 全數保持不變。 |
| Guard fixture | 阻擋取消界限一秒；嚴格在十秒前回報。 |
| 容量競爭 | 三槽／八次啟動／十輪；八槽／32 個競爭者／200 輪。 |
| Marker 及競爭輪詢 | 原有就緒輪詢 100 ms、predicate 輪詢 20 ms。Probe 另使用文件指定的五秒交握及 10-ms 輪詢。 |

300-ms 慢 runner timeout 限制執行時間，並不證明 probe 已完成啟動。負載高的 Windows 主機可能需要超過一秒啟動 probe。三秒 orphan-chain 門檻還要求 timeout 前取得 leaf marker 並確認 middle 已實際退出；此門檻保持不變。兩個等待均不縮短或放寬。

來源 helpers `PhaseRecorder`、`TerminationSeam`、`ThrowingDisposal`、`FirstReader`、`CancelWithinAsync`、`SpinUntilAsync`、`HasExited`、`ReadPidAsync`、`WaitForFileAsync` 及 `KillById` 的機制保持不變。只有啟動設定以共用 probe 取代 shell scripts。Probe 輸入使用引數，markers 放在系統暫存目錄內的 Processes `TestWorkspace`。Orphan-chain marker 包含 leaf PID；`.middle` 包含 middle PID，而 `.ready` 在該程序退出後才出現。Fixtures 先等待 `.ready` 再讀取 PID，接著觀察 middle 退出，再等待執行結果。

`orphan-chain-exit` 在啟動 middle 前寫入並 flush `before-reader-stop`。Middle 啟動繼承 stdout 及 stderr 的 leaf 後退出；root 在 ready marker 出現後以 code 0 自然退出。Windows 上保留的完整文字為 `before-reader-stop\r\n`。執行以 `OutputStreamHeldOpen` 返回時 leaf 仍存活；reader stop 保留該行、不脫離，並釋放資源及容量。Fixture 在 `finally` 終止 leaf。

`Complete` 仍只觀察直接子程序退出及兩個串流結尾。未持有任一重新導向串流的 detached descendant 無法觀察，因此完整清理不證明整棵程序樹已空。清理不確定性的優先順序、取消優先順序、晚到失敗觀察、全部五次釋放嘗試、失敗通知及只在 settlement 後釋放均保持不變。

`SystemExternalProcessRunnerLifetimeBoundaryTests.DetachedReadersRetainExactCapacityUntilLateSettlement` 新增 12 個原生案例：容量一、二、三及八，各搭配晚到 reader 完成、失敗或取消。確定性 gates 保留真正已退出程序的資源。案例在容量前一個值准入、填滿精確容量、拒絕下一個請求且不增加槽位、只釋放已 settlement 的一槽、重用該槽，最後回到零。既有 `SystemExternalProcessRunnerBoundaryTests` 提供正值參數的零／負值案例、一 tick 最小值、grace 加 reserve 在 deadline 前一值／精確邊界／後一值，以及 deadline 在 `int.MaxValue` 毫秒前一值／精確邊界／後一值。固定 fixture 等待是排程界限，不是由呼叫者調整的正值上限參數。

## NFC 擁有權及採用

`ExternalProcessCleanupText`、issue codes、產品文字、`ToExecutedCommand` 稽核轉接、信任、manifest 驗證、staging、routing、韌體行為、schemas、golden 證據及 release authority 均留在 NFC。機制註解已不含私人紀錄識別碼。執行判斷、界限及訊息不變。

NFC 以獨立的 PR 採用本模組，與本次抽取分開。所有受控 launcher 呼叫者使用 `StartContained(startInfo, inheritedHandles, validateImmediatelyBeforeStart)`；一般外部工具啟動使用 `Start(startInfo)`。原始程序建立留在 Processes，不置入 Launcher。採用結案前，生命週期證據必須通過。

只有在所有呼叫者都改用驗證過的 Processes 套件，且採用案證明零差異（凍結行為、原生生命週期及必要的 UI 證據）之後，NFC 才刪除自己的通用外部 runner、invocation 輔助型別、讀取器、同步取消、啟動閘門、繼承控制代碼 record 及受控啟動器副本。NFC 的 Platform 副本要等剩餘的 Platform 呼叫者全部遷移，且主機結構試驗通過後才刪除。NFC 保留歷史 executor 證據。

UI 比較使用共用環境 manifest，並要求解碼後變更像素數為零。適用時，比較也涵蓋完整輸出位元組及事件軌跡，並記錄每個證據檔的 SHA-256。八個 legacy font 值保持不變。

NFC 在建置時透過自己的 `core-packages.json` 下載已驗證、版本化的 nupkg，採用精確 `[x]` 版本、source mapping、lock files 及 locked restore。清單記錄每個套件的 Release 標籤與 SHA-256。套件參照、版本鎖定、source mapping 及 lock files 由 NFC 擁有。NFC 不加入指向 Core checkout 的 ProjectReference。

Core 與 NFC 保持各自獨立的版本與發布。
