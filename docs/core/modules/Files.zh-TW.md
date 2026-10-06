[English](Files.md) | [中文](Files.zh-TW.md)

# Files

`Nvt.Core.Files` 依照事先量得的串流長度讀取完整內容、計算 SHA-256，並可選擇回傳內容位元組。讀完指定長度後，最多再讀取一個尾端位元組，因此串流持續增長也不會造成無限制的讀取。

凍結的父版本基準：NFC（`nvt_fw_combiner`）、ref `origin/1.2.x`、完整 commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。抽取的來源路徑：

- `src/NvtFwCombiner.Infrastructure/Files/FileContentSnapshotInspector.cs` （第 86 到 181 行）
- `src/NvtFwCombiner.Application/Ports/ISelectedFileContentInspector.cs` （第 26 到 106 行）

讀取迴圈、多讀一個位元組的檢查，以及最後的長度與位置檢查，都和這些行相同。例外訊息把「Selected file」改成「File」。NFC 改用這個模組時，會保留自己的例外型別與訊息。測試從 `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileContentSnapshotInspectorTests.BoundedIdentity.cs` 移植，改用合成的資料流。

## 公開 API

```csharp
public enum FileCaptureMode { IdentityOnly, CaptureBytes }
public enum FileChangeKind { Unspecified, ShortRead, Growth, Shrinkage, PositionChanged }
public sealed class FileSizeLimitExceededException : Exception
public sealed class FileChangedDuringReadException : IOException
public readonly record struct BoundedReadResult(long Length, byte[] Sha256, byte[]? Bytes);
public static ValueTask<BoundedReadResult> BoundedFileReader.ReadAndHashAsync(
    Stream stream, long observedLength, FileCaptureMode mode, CancellationToken cancellationToken);
```

`FileSizeLimitExceededException` 提供 `(long observedBytes, long maximumBytes)` 與 `(long observedBytes, long maximumBytes, bool isCaptureStorageLimit)` 建構式，並公開唯讀屬性 `ObservedBytes`、`MaximumBytes`、`IsCaptureStorageLimit`。兩個參數的建構式將旗標設為 `false`。所有建構式都先檢查觀測長度不可為負值，再檢查上限必須大於零。

`FileChangedDuringReadException` 提供 `(FileChangeKind changeKind = FileChangeKind.Unspecified)` 建構式，以及唯讀屬性 `ChangeKind`。

## 使用方式與所有權

呼叫端應提供位於內容起點的可讀串流，以及呼叫前量得的完整長度。主應用程式負責管理與釋放串流；讀取器不會重設位置或關閉串流。本模組不負責開啟路徑或決定呼叫端的大小上限。

```csharp
BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
        stream, observedLength, FileCaptureMode.IdentityOnly, cancellationToken)
    .ConfigureAwait(false);
```

`Length` 等於傳入的觀測長度。`Sha256` 包含 32 個原始雜湊位元組。`IdentityOnly` 回傳的 `Bytes` 為 `null`；`CaptureBytes` 回傳與雜湊內容一致的位元組陣列，空內容則回傳空陣列。回傳的陣列可修改，並由呼叫端持有。雜湊描述實際讀到的位元組；長度與位置檢查無法偵測所有長度不變的內容修改。

## 讀取規則

1. 依序檢查串流不可為空、長度不可為負值，以及模式必須是已定義值。接著檢查取消要求，最後檢查擷取儲存上限。未定義模式會拋出參數名稱為 `mode` 的 `ArgumentOutOfRangeException`。
2. 當模式是 `CaptureBytes` 且觀測長度超過 `Array.MaxLength` 時，在讀取前拒絕請求。僅計算識別資訊的模式使用長整數計數器，不受此擷取儲存上限限制。
3. 建立增量 SHA-256 雜湊與 64 KiB 緩衝區。擷取模式配置 `new byte[checked((int)observedLength)]`，並直接讀入該陣列；僅計算識別資訊的模式重複使用緩衝區。
4. 在讀完觀測長度前，每次要求最多 64 KiB，且不超過剩餘長度。每次讀取前後都檢查取消要求。部分正數讀取會繼續累積；讀到零個位元組則以 `ShortRead` 拒絕。
5. 檢查取消要求，最多讀取一個尾端位元組，再次檢查取消要求。若讀到尾端內容，則以 `Growth` 拒絕。
6. 對可搜尋串流，先比較最終長度：較小為 `Shrinkage`，較大為 `Growth`。接著要求最終位置等於觀測長度，否則為 `PositionChanged`。對不可搜尋串流，絕不存取 `Length` 或 `Position`。
7. 所有檢查通過後才回傳結果。讀取器中的所有 await 都使用 `ConfigureAwait(false)`。取消要求仍以 `OperationCanceledException` 傳遞；失敗時不回傳部分雜湊或擷取位元組。

## 例外訊息

- 呼叫端上限：`File length {observedBytes} exceeds the resolved maximum {maximumBytes} bytes.`
- 擷取儲存上限：`File length {observedBytes} exceeds the capture storage limit {maximumBytes} bytes.`
- 串流變更：`File length changed during complete-content read.`

## 測試

`BoundedFileReaderStreamTests` 包含 24 個公開測試方法，共 47 個測試案例，全部使用合成資料。內部輔助類別 `GeneratedReadStream` 按需產生 `0xA5` 位元組，記錄讀取請求、支援部分讀取，並可模擬長度、位置與取消狀態的變化，不需儲存大型內容。

長整數計數器測試分別讀取 2,147,483,665 與 4,294,967,313 個產生的位元組，並比對固定 SHA-256 值。這些案例可能各需數秒。其他測試以 `(byte)(index % 251)` 產生 0 與 131,089 個位元組，驗證兩種模式的結果與 `SHA256.HashData` 一致。

### 測試對照

| 原串流測試 | Core 串流測試 |
| --- | --- |
| `HashExactLengthAsyncRejectsGrowthWithOneByteProbe` | `ReadAndHashAsyncRejectsGrowthWithOneByteProbe` |
| `HashExactLengthAsyncRejectsShortReadAsContentChange` | `ReadAndHashAsyncRejectsShortReadAsContentChange` |
| `HashExactLengthAsyncPropagatesCancellation` | `ReadAndHashAsyncPropagatesCancellationForCancelledToken` |
| `HashExactLengthAsyncAccumulatesPartialReadsAcrossBufferBoundary` | `ReadAndHashAsyncAccumulatesPartialReadsAcrossBufferBoundary` |
| `ReadAndHashExactLengthAsyncUsesLongCountersWithoutPayload` | `ReadAndHashAsyncUsesLongCountersWithoutPayload` |
| `ReadAndHashExactLengthAsyncRejectsUnrepresentableCaptureBeforeReading` | `ReadAndHashAsyncRejectsUnrepresentableCaptureBeforeReading` |
| `ReadAndHashExactLengthAsyncCapturesPartialReads` | `ReadAndHashAsyncCapturesPartialReads` |
| `ReadAndHashExactLengthAsyncRejectsFinalPositionChange` | `ReadAndHashAsyncRejectsFinalPositionChange` |
| `ReadAndHashExactLengthAsyncSupportsNonSeekableStream` | `ReadAndHashAsyncSupportsNonSeekableStream` |
| `ReadAndHashExactLengthAsyncRejectsGrowthDuringRead` | `ReadAndHashAsyncRejectsGrowthDuringRead` |
| `ReadAndHashExactLengthAsyncRejectsShortReadDuringRead` | `ReadAndHashAsyncRejectsShortReadDuringRead` |
| `ReadAndHashExactLengthAsyncRejectsFinalLengthChange` | `ReadAndHashAsyncRejectsFinalLengthChange` |
| `InspectionRejectsInvalidPayloadModeBeforeReading`（僅串流部分） | `ReadAndHashAsyncRejectsInvalidModeBeforeReading` |
| `InspectionPropagatesCancellationBeforeOpeningOrReading`（僅串流部分） | `ReadAndHashAsyncPropagatesCancellationBeforeReading` |
| `ReadAndHashExactLengthAsyncPropagatesCancellationMidRead` | `ReadAndHashAsyncPropagatesCancellationMidRead` |

新增測試方法：

- `ReadAndHashAsyncHashesCompleteStreamWithExplicitPayloadMode`
- `ReadAndHashAsyncRejectsNullStream`
- `ReadAndHashAsyncRejectsNegativeObservedLength`
- `ReadAndHashAsyncValidatesArgumentsBeforeCancellation`
- `FileSizeLimitExceededExceptionRejectsInvalidLengths`
- `FileSizeLimitExceededExceptionReportsCallerLimit`
- `FileSizeLimitExceededExceptionReportsExplicitLimit`
- `FileChangedDuringReadExceptionDefaultsToUnspecified`
- `FileChangedDuringReadExceptionExposesChangeKind`

## 離線驗證

在方案根目錄設定 `AVALONIA_TELEMETRY_OPTOUT=1` 後執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
dotnet test Nvt.Core.sln --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Files"
```
