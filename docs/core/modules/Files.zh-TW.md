[English](Files.md)

# Files

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.Infrastructure/Files/FileSystemPathGuard.cs` (complete file; renamed `RootedPathGuard`)
- `src/NvtFwCombiner.Infrastructure/Files/RegularFileGuard.cs` (complete file)
- `src/NvtFwCombiner.Infrastructure/Files/FileContentSnapshotInspector.cs` (lines 37-80: path opening; lines 86-181: existing stream read)
- `src/NvtFwCombiner.Application/Ports/ISelectedFileContentInspector.cs` (lines 26-106: existing result, mode, change-kind, and exception contracts)
- `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileSystemPathGuardTests.cs` (all four methods and eight relative-path cases)
- `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileContentSnapshotInspectorTests.cs` (the five file-inspection tests mapped below)
- `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileContentSnapshotInspectorTests.BoundedIdentity.cs` (stream tests plus complete-file hashes and path validation/cancellation slices)
- `tests/NvtFwCombiner.TestSupport/TempWorkspace.cs` (unique workspace, byte writing, and bounded Windows disposal slices)
- `tests/NvtFwCombiner.TestSupport/RepositoryPaths.cs` (`NormalizeRelativePath` only)

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Files` 解析根目錄路徑、拒絕非一般檔案、依明確位元組上限開啟檔案，並以既有有界 SHA-256 串流讀取器讀取完整內容。串流迴圈、尾端位元組探測及最終長度/位置檢查未變。產品提示與模型留在 NFC。

## 公開 API

```csharp
public enum FileCaptureMode { IdentityOnly, CaptureBytes }
public enum FileChangeKind { Unspecified, ShortRead, Growth, Shrinkage, PositionChanged }
public sealed class FileSizeLimitExceededException : Exception
public sealed class FileChangedDuringReadException : IOException
public readonly record struct BoundedReadResult(long Length, byte[] Sha256, byte[]? Bytes);
public static ValueTask<BoundedReadResult> BoundedFileReader.ReadAndHashAsync(
    Stream stream, long observedLength, FileCaptureMode mode, CancellationToken cancellationToken);
public static ValueTask<BoundedReadResult> BoundedFileReader.ReadFileAsync(
    string path, IReadOnlyList<string>? allowedRoots, long maximumBytes,
    FileCaptureMode mode, CancellationToken cancellationToken);
public static class RootedPathGuard
{
    public static string ResolveRoot(string rootDirectory);
    public static string ResolveExistingRoot(string rootDirectory);
    public static string ResolveExistingFileUnderRoots(string path, IReadOnlyList<string> allowedRoots);
    public static string ResolveFileUnderRoots(string path, IReadOnlyList<string> allowedRoots, bool mustExist);
    public static string ResolveExistingRelativeFileUnderRoot(string relativePath, string rootDirectory);
    public static string ResolveFileNameUnderRoot(string fileName, string rootDirectory);
    public static bool IsUnderRoot(string fullPath, string root);
}
public static partial class RegularFileGuard
{
    public static void RequirePath(string path);
    public static void RequireOpenHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, string displayPath);
    public static (long Device, long Inode)? ReadUnixIdentity(string path);
}
```

`FileSizeLimitExceededException` 提供 `(long observedBytes, long maximumBytes)` 與 `(long observedBytes, long maximumBytes, bool isCaptureStorageLimit)` 建構式，並公開唯讀屬性 `ObservedBytes`、`MaximumBytes`、`IsCaptureStorageLimit`。兩個參數的建構式將旗標設為 `false`。所有建構式都先檢查觀測長度不可為負值，再檢查上限必須大於零。

`FileChangedDuringReadException` 提供 `(FileChangeKind changeKind = FileChangeKind.Unspecified)` 建構式，以及唯讀屬性 `ChangeKind`。

既有 Core 讀取例外使用一般檔案措辭。NFC 採用轉接器保留原選取檔案例外型別、訊息及模式映射。

## 使用方式與所有權

呼叫端應提供位於內容起點的可讀串流，以及呼叫前量得的完整長度。主應用程式負責管理與釋放串流；讀取器不會重設位置或關閉串流。串流入口使用呼叫端持有的串流；下述路徑入口自行開啟及釋放串流，並檢查呼叫端明確提供的大小上限。

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

## 根目錄與一般檔案契約

`ResolveRoot` 會建立缺少的目錄；`ResolveExistingRoot` 要求目錄已存在。兩者皆拒絕既有祖先目錄中的重新解析點，並回傳帶尾端目錄分隔符的路徑。Windows 使用 `OrdinalIgnoreCase`，其他平台使用 `Ordinal`。包含關係以尾端分隔符為界，因此名稱有相同前綴的相鄰目錄不屬於根目錄；根目錄本身也不算根目錄之下的檔案。

`ResolveFileUnderRoots` 先驗證路徑，再驗證根目錄清單；正規化路徑後，先檢查包含關係，再檢查是否存在。既有檔案必須通過重新解析點與一般檔案檢查。若目標不存在，`mustExist` 會先拋出 `FileNotFoundException`，然後才可能檢查目錄目標或父目錄。新目標必須有既有父目錄，且祖先不可為連結。空根目錄清單會拋出 `InvalidOperationException`。

`ResolveExistingRelativeFileUnderRoot` 先驗證相對路徑與根目錄參數，再拒絕完整限定路徑、反斜線、冒號、NUL，以及空白段落、目前目錄段落或父目錄段落，然後解析既有根目錄。接著依序檢查包含關係、存在性、重新解析點及一般檔案狀態。`ResolveFileNameUnderRoot` 只接受單純檔名，不會建立或要求根目錄及目標存在。這些凍結的一般防護沒有 512 字元路徑上限；更嚴格的產品或套件路徑政策留在 NFC。

`RegularFileGuard.RequirePath` 保留 Windows 裝置、目錄與重新解析點屬性遮罩，以及 Unix `lstat` 的一般檔案遮罩。`RequireOpenHandle` 依序驗證控制代碼、顯示路徑、無效或已關閉狀態，再使用 Windows `GetFileType` 或 Unix `fstat`。`ReadUnixIdentity` 用於非 Windows 主機，透過 `stat` 跟隨連結，成功時回傳裝置及 inode，原生錯誤 2（`ENOENT`）與 20（`ENOTDIR`）回傳 null，其他原生錯誤維持例外。完整 `UnixFileStatus` 配置未變。

只改名下列防護訊息；其他訊息、例外型別、判斷式與順序保持凍結版本：

- `File paths must be relative and use forward slashes.`
- `File paths cannot contain empty, current, or parent segments.`
- `Relative file was not found.`
- `File '{displayPath}' has no valid open handle.`
- `File '{path}' must be a regular filesystem file.`
- `Could not inspect file '{path}' (native error {n}).`

`Could not inspect local file '{path}' (native error {n}).` 保持不變。相對路徑例外的參數名稱為 `relativePath`。

## 路徑讀取

呼叫端明確提供大於零且包含邊界的 `maximumBytes`。NFC 凍結的固定工作流程硬上限為 **100,000,000 位元組**，原測試以 100,000,001 位元組的稀疏檔案驗證拒絕；Core 不決定或放寬產品上限。固定機制界線仍為 64 KiB 緩衝區、一個尾端探測位元組，以及擷取儲存的 `Array.MaxLength`。`IdentityOnly` 不受陣列儲存上限限制。

`ReadFileAsync` 依序檢查正數上限、有效模式、取消要求、`Path.GetFullPath(path)` 及根目錄檔案解析。非 null 根目錄清單由呼叫端先以 `ResolveExistingRoot` 解析一次；讀取器直接使用，不會再次驗證每個根目錄的存在性。凍結的包含關係輔助函式仍會以 `Path.GetFullPath` 正規化根目錄字串。清單為 null 時，以檔案的既有父目錄作為唯一根目錄。

開啟設定為 `Open`、`Read`、`FileShare.Read`、`Asynchronous | SequentialScan` 及 64 KiB 緩衝區。讀取器以 `await using` 持有並釋放串流，量得長度後，超過呼叫端上限則拋出 `FileSizeLimitExceededException`，否則以 `ConfigureAwait(false)` 委派至未變的 `ReadAndHashAsync`。

這些是開啟前的根目錄檢查，不是持續持有的檔案系統 custody。它們無法消除路徑替換競爭，也無法偵測單次讀取期間所有長度不變的改寫。`ReadFileAsyncDetectsSameSizeMutation` 驗證兩次完整讀取之間完成的改寫。後續 Files Windows custody 工作提供更強的保證。

## 路徑測試對照與邊界

以下保留全部四個凍結路徑測試方法及八個 theory 案例。`Tests.cs` 代表 `tests/NvtFwCombiner.Infrastructure.Tests/Files/FileContentSnapshotInspectorTests.cs`，`BoundedIdentity.cs` 代表其 `.BoundedIdentity.cs` 同伴檔案。檔案測試位於 `BoundedFileReaderPathTests`。只移除產品提示欄位斷言，保留兩種模式、原始斷言與門檻。

| 凍結來源測試 | Core 測試 |
| --- | --- |
| `FileSystemPathGuardTests.ResolveExistingManifestFileUnderRootReturnsConfinedFile` | `RootedPathGuardTests.ResolveExistingRelativeFileUnderRootReturnsConfinedFile` |
| `FileSystemPathGuardTests.ResolveExistingManifestFileUnderRootRejectsPathSyntax` (all eight cases) | `RootedPathGuardTests.ResolveExistingRelativeFileUnderRootRejectsPathSyntax` |
| `FileSystemPathGuardTests.ResolveExistingManifestFileUnderRootRequiresFile` | `RootedPathGuardTests.ResolveExistingRelativeFileUnderRootRequiresFile` |
| `FileSystemPathGuardTests.ResolveExistingManifestFileUnderRootRequiresDirectoryRoot` | `RootedPathGuardTests.ResolveExistingRelativeFileUnderRootRequiresDirectoryRoot` |
| `Tests.cs:13 InspectAsyncReturnsContentAuthoritativeStampAndNonAuthoritativeHints` | `ReadFileAsyncReturnsLengthHashAndBytes` |
| `Tests.cs:34 InspectAsyncRejectsFileAboveResolvedMaximum` | `ReadFileAsyncRejectsFileAboveResolvedMaximum` |
| `Tests.cs:55 InspectAsyncRejectsHundredMegabyteOverflowBeforeMaterialization` | `ReadFileAsyncRejectsHundredMegabyteOverflowBeforeMaterialization` |
| `Tests.cs:123 InspectAsyncDetectsSameSizeMutation` | `ReadFileAsyncDetectsSameSizeMutation` |
| `Tests.cs:163 InspectAsyncRejectsPathOutsideAllowedRoot` | `ReadFileAsyncRejectsPathOutsideAllowedRoot` |
| `BoundedIdentity.cs:15 InspectAsyncHashesCompleteFileWithExplicitPayloadMode` | `ReadFileAsyncHashesCompleteFileWithExplicitPayloadMode` |
| `BoundedIdentity.cs:247 InspectionRejectsInvalidPayloadModeBeforeReading` (path half) | `ReadFileAsyncRejectsInvalidModeBeforeAccessingPath` |
| `BoundedIdentity.cs:271 InspectionPropagatesCancellationBeforeOpeningOrReading` (path half) | `ReadFileAsyncPropagatesCancellationBeforeAccessingPath` |

新增測試共 54 個公開方法、110 個案例：`RootedPathGuardTests` 為 20/42、`RegularFileGuardTests` 為 15/20、`BoundedFileReaderPathTests` 為 17/42、`BoundedFileReaderStorageBoundaryTests` 為 2/6。原有 24 方法、47 案例的串流套件未變。所有資料皆為合成資料；Files 自有 `TestWorkspace` 使用系統暫存目錄，保留原 Windows 有界刪除重試（總計 450 ms，每次等待最多 50 ms）。

涵蓋建立與缺少根目錄、外部及相同前綴相鄰路徑、第二根目錄、新檔及缺少父目錄、既有目錄目標、必要檔案不存在、空/null 根目錄及空白參數、單純檔名及所有原始相對路徑語法、檔案連結及連結父目錄/根目錄、Windows 大小寫折疊及其他平台大小寫敏感、一般/目錄/不存在路徑、有效/null/無效/已關閉控制代碼、真實 Windows/Unix pipe 控制代碼，以及 Unix identity 相等與不存在時回傳 null。順序驗證使用確定的無效參數、預先取消的 token 及依序完成的改寫。

| 邊界 | 新增覆蓋 |
| --- | --- |
| 正數呼叫端上限 | 0、-1、`long.MinValue` 在模式/取消/路徑前被拒絕；最小正數上限 1 接受長度 0、1，拒絕 2 |
| NFC 明確的 100,000,000 位元組上限 | 兩種模式皆測 99,999,999、100,000,000、100,000,001，另保留原稀疏擷取拒絕案例 |
| 固定 64 KiB 緩衝區 | 兩種模式完整讀取 65,535、65,536、65,537 位元組 |
| 固定 `Array.MaxLength` 擷取上限 | 少一、精確及多一；多一在讀取前拒絕，三種長度皆另測取消優先於配置 |
| 原完整檔案雜湊案例 | 長度 0、131,089；保留原 `long.MaxValue` 上限 |

接近 `Array.MaxLength` 的成功擷取測試每次配置約 2 GiB，預設明確略過。專用 64 位元主機需設定 `NVT_CORE_TEST_LARGE_CAPTURE=1`，且執行階段可用記憶體至少約 6 GiB，才會執行少一與精確邊界案例。結果需分開記錄，略過配置證據不算通過。作業系統拒絕建立符號連結時明確略過；平台專屬案例在另一平台明確略過。不可用案例不會提早返回而算通過。

## 所有權與採用

Files 擁有此根目錄機制、一般檔案防護，以及唯一的完整內容串流雜湊迴圈。沒有新增重複讀取迴圈或原生 custody owner。NFC 保留 `ProtectedPathGuard`、`LocalFileIdentity`、強化輸出寫入器、產品上限及套件路徑政策、stamp、選取檔案模型、顯示提示、schema、信任及發行權限。

NFC 以自己的獨立 PR 採用本模組。該 PR 使用 `vendor/nuget/` 中已驗證且具版本的 nupkg、精確 `[x]` 版本、鎖定相依、來源映射及 `SOURCE.md` 的來源/套件 SHA-256 收據。共用參照與鎖定檔由 NFC 內的負責者管理。只有呼叫端已使用 Core、保留 NFC 模式與例外映射、全部 40 路徑與 12 檔案案例通過，且必要產品輸出與像素證據通過後，才刪除搬移的 NFC 一般機制。Core 測試不能單獨證明 NFC 產品或像素相等。

之後的 Files 擴充會加入 held Windows 讀取 custody。本版本不提供此能力。

## 離線驗證

在方案根目錄設定 `AVALONIA_TELEMETRY_OPTOUT=1` 後執行：

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Files"
dotnet test Nvt.Core.sln --no-build
```
