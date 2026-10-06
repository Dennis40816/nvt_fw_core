[English](Processes.md)

# Processes

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Infrastructure/ExternalTools/BoundedProcessOutputReader.cs`（完整讀取器及輸出 record）。
- `src/NvtFwCombiner.Infrastructure/ExternalTools/ExternalProcessResult.cs`（第 1–67 行：結果、清理列舉及例外；第 69–127 行的產品清理文字留在 NFC）。
- `src/NvtFwCombiner.Infrastructure/ExternalTools/ExternalProcessStartInfo.cs`（啟動要求，不含第 36–40 行的 `ToExecutedCommand()`）。
- `src/NvtFwCombiner.Infrastructure/ExternalTools/IExternalProcessRunner.cs`（完整介面）。
- `src/NvtFwCombiner.Platform/Processes/WindowsSynchronousReadCancellation.cs`（完整取消機制）。
- `tests/NvtFwCombiner.Infrastructure.Tests/ExternalTools/BoundedProcessOutputReaderTests.cs`（全部 11 個案例及完整輔助程式）。

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

此切片將僅使用 BCL、以 net8.0 為目標的外部程序契約、有界 UTF-16 診斷輸出讀取器及 Windows 同步讀取取消機制移入 `Nvt.Core.Processes`。Processes 是這些 Core 機制的唯一擁有者。受控啟動、runner 實作及子程序／Job 生命週期證據由後續 Processes 切片負責。Launcher.Transport 繼續保有獨立的嚴格 UTF-8 行讀取器。

## 契約收據

```csharp
public interface IExternalProcessRunner
{
    ValueTask<ExternalProcessResult> RunAsync(
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

指定的凍結來源沒有產品專屬的路徑、引數或程序容量准入上限，只有下列固定讀取機制界限。啟動要求唯一必須為正的數值參數是 `timeout`；容量例外只記錄傳入整數，不加驗證。產品准入政策仍由主應用程式及後續 runner 契約負責。

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

共有 40 個已記錄 XML 文件的公開測試方法，靜態列舉為 95 個案例：11 個移植案例、15 個讀取器邊界方法（49 個案例）及 14 個契約方法（35 個案例）。全部輸入均為合成資料。可接受 token 的呼叫均直接使用 `TestContext.Current.CancellationToken` 或透過相連的停止來源傳入。

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

kernel 讀取前的競態測試使用真正的 Windows 匿名管線及確定性閘門，非 Windows 透過 `Assert.Skip` 明確略過。略過不提供原生證據。子程序、繼承控制代碼隔離及 Job 證據仍由後續啟動及生命週期切片負責。

## NFC 擁有權及採用

`ExternalProcessCleanupText`、issue codes、產品文字、`ToExecutedCommand` 稽核轉接、信任、manifest 驗證、staging、routing、韌體行為、schemas、golden 證據及 release authority 均留在 NFC。機制註解已不含私人紀錄識別碼。執行判斷、界限及訊息不變。

NFC 以獨立的 PR 採用本模組，與本次抽取分開。受控啟動及外部 runner 建立在本契約之上。採用結案前，生命週期證據必須通過。

只有在所有呼叫者都改用驗證過的 Processes 套件，且採用案證明零差異（凍結行為、原生生命週期及必要的 UI 證據）之後，NFC 才刪除自己的通用讀取器及同步取消副本。NFC 的 Platform 副本要等剩餘的 Platform 呼叫者全部遷移，且主機結構試驗通過後才刪除。NFC 保留歷史 executor 證據。

UI 比較使用共用環境 manifest，並要求解碼後變更像素數為零。適用時，比較也涵蓋完整輸出位元組及事件軌跡，並記錄每個證據檔的 SHA-256。八個 legacy font 值保持不變。

NFC 在建置時透過自己的 `core-packages.json` 下載已驗證、版本化的 nupkg，採用精確 `[x]` 版本、source mapping、lock files 及 locked restore。清單記錄每個套件的 Release 標籤與 SHA-256。套件參照、版本鎖定、source mapping 及 lock files 由 NFC 擁有。NFC 不加入指向 Core checkout 的 ProjectReference。

Core 與 NFC 保持各自獨立的版本與發布。兩階段獨立審查都涵蓋抽取 PR 及採用 PR 的精確 head。
