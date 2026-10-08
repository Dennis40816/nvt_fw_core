[English](Persistence.md) | [中文](Persistence.zh-TW.md)

# Persistence：LocalJsonDocument 與 LatestSnapshotPersistenceCoordinator

## 0.9.0 前的不相容變更

公開的 `Options` 欄位已由 `LocalJsonDocument.CreateOptions()` 取代。
每次呼叫都回傳獨立且可變的副本。
Codec 使用私有且已凍結的預設選項。

- 將 `LocalJsonDocument.Options` 讀取改為 `LocalJsonDocument.CreateOptions()`。
- 在第一次序列化使用前調整回傳的副本。
- 由擁有組態的呼叫端保存該副本。

```csharp
JsonSerializerOptions options = LocalJsonDocument.CreateOptions();
options.WriteIndented = false;
byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, options);
```

修改副本不會影響 codec 反序列化或後續副本。
預設編碼、屬性大小寫、縮排、跳脫與 null 省略行為維持不變。
測試固定輸出位元組與選項副本的獨立性。

[`LocalJsonDocument`](../../../src/Nvt.Core/Persistence/LocalJsonDocument.cs) 提供既有本機狀態 JSON 選項、由 host 傳入的目錄／檔名組合，以及 UTF-8 與帶 BOM 的 UTF-16／UTF-32 串流反序列化。輸入 stream 必須可 seek，且讀取後保持開啟。檔案寫入、原子替換、大小上限、schema 與 fallback 政策仍由 host 負責。原始抽取變更 namespace、公開可見性、版權標頭與 API 文件。
副本工廠現在隔離可變的序列化組態。

[`LatestSnapshotPersistenceCoordinator<TSnapshot>`](../../../src/Nvt.Core/Persistence/LatestSnapshotPersistenceCoordinator.cs) 同步擷取 host 提供的 snapshot、序列化儲存、取消被新 snapshot 取代的工作，並以 request generation 回報終止結果。Retry 重用最新已擷取的值。`CompleteAsync` 封閉新工作准入，等待目前 save 與 observer 完成，不取消它們；`Reopen` 在關閉失敗後保留原有序列 tail。Save 失敗會被記錄，不會使 tail fault；後續成功也不會清除 `LastFailure`。Coordinator 沒有 dispose API；host 須等待完成再釋放 persistence 資源。除 namespace、公開可見性、版權標頭與 API 文件外，唯一實作變更是將 `private readonly Lock _gate` 改為 `private readonly object _gate`；所有既有 `lock` 與互斥邊界均維持原樣，以支援 net8.0。

模組凍結基準：NFC（`nvt_fw_combiner`）、`origin/1.2.x`、commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。來源均以該 commit 的 `git show` 讀取：

- 已抽取：`src/NvtFwCombiner.Presentation.Avalonia/LocalJsonDocument.cs`。
- 從 `tests/NvtFwCombiner.UiSmoke.Tests/ShellNavigationSystemTests.Preferences.cs` 移植 codec 案例：`ShellPreferenceFileStoreRoundTripsAndInvalidValuesFallBack` 的 codec 部分，以及 `ShellPreferenceFileStoreLoadsBomDocuments`。
- 從 `tests/NvtFwCombiner.UiSmoke.Tests/ReportHistoryPersistenceTests.cs` 移植 codec 案例：`LoadLargeHistoryAvoidsWholeFileTextAllocation`，保留 4 MiB payload，以及 UTF-8／舊 UTF-16 每字元三 bytes 的配置量上限。Core 使用合成的記憶體內文件；產品 store 與 UI assertions 留在 NFC。
- 已抽取：`src/NvtFwCombiner.Presentation.Avalonia/LatestSnapshotPersistenceCoordinator.cs`，使用與 codec 相同的凍結 commit，以及 net8.0 需要的 object lock 替換。
- 已移植 `tests/NvtFwCombiner.UiSmoke.Tests/ReportHistoryPersistenceTests.cs` 的全部四個 coordinator 直接測試：`CoordinatorKeepsLatestQueuedSnapshot`、`CoordinatorCompletesLatestSaveBeforeShutdown`、`CoordinatorReopenSerializesNewSnapshotAfterDelayedOldSave`、`CoordinatorRecoversAfterSaveFault`。以合成 snapshot 與記憶體內 save 取代產品 report 型別／file store，保留排程與關閉 assertions。
- 已移植 `tests/NvtFwCombiner.UiSmoke.Tests/LocalStateSaveNoticeTests.cs` 的全部四個 coordinator 直接測試：`CoordinatorReportsTerminalSavesButNotSupersededOnes`、`GenerationStaysCurrentOnlyUntilANewerSnapshotIsQueued`、`CoordinatorRetryRequeuesLatestCapturedSnapshot`、`CoordinatorObserverFailureDoesNotPoisonLaterSaves`。產品 UI 與 host store 測試仍留在 NFC。

[`LocalJsonDocumentTests`](../../../tests/Nvt.Core.Tests/Persistence/LocalJsonDocumentTests.cs) 另固定序列化 bytes、Unicode、短／null JSON、無效 JSON、屬性比對、取消、呼叫端 stream 所有權、絕對位置重設、不完整 prefix 讀取、不可 seek 的輸入與路徑參數行為。

[`LatestSnapshotPersistenceCoordinatorTests`](../../../tests/Nvt.Core.Tests/Persistence/LatestSnapshotPersistenceCoordinatorTests.cs) 另固定可變輸入擷取、被取代 save 的靜默取消、未要求的取消失敗、retry generation 與保留失敗、空 completion／reopen，以及 null 參數驗證。兩個確定性 close race 分別涵蓋新 request 仍在擷取時關閉，以及 save 的 cancellation source 已 dispose、terminal observer 仍在執行時關閉；固定拒絕晚到寫入、dispose 後安全 retry、序列順序與 completion 等待 observer 的行為。

使用既有已還原套件驗證 Core：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

日後 NFC 採用時的零差異驗證：先執行 Core persistence 測試與 NFC 凍結 commit 的原測試，再於 NFC 參照 Core 並移除重複 codec／coordinator 後重跑。各 NFC checkout 先以 `--no-restore` 建置，再執行：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build --filter "FullyQualifiedName~ShellNavigationSystemTests|FullyQualifiedName~ReportHistoryPersistenceTests|FullyQualifiedName~LocalStateSaveNoticeTests|FullyQualifiedName~WindowLifetimeTests"
```

使用相同合成文件與 serializer 呼叫，逐 byte 比較儲存的 UTF-8 輸出，並比對各編碼的載入值／例外、stream／取消行為及既有配置量上限。以相同閘控 save 排程，比較准入／儲存 snapshot 順序、取消、terminal observer 結果與 generation、保留失敗的例外物件、retry 擷取次數、completion 後拒絕、reopen 順序，以及 close 是否等待 save 與 observer。NFC 前後比較須使用相同 OS／runtime，保留 host 政策，包含 dispose 時解除 UI observer。NFC 的來源文字架構檢查日後需辨識共用 codec／coordinator 參照，同時保留 host 邊界檢查。本次不修改 NFC，也不更新基準。
