[English](Startup.md) | [中文](Startup.zh-TW.md)

# Startup

`Nvt.Core.Startup.StartupTrace` 記錄啟動階段、經過時間與記憶體配置總量。
主應用程式決定輸出環境變數、結構版本名稱、階段名稱與額外 JSON 區段。
此模組只使用 .NET 10 基礎程式庫，不需要額外套件。

凍結的父版本基準：NFC（`nvt_fw_combiner`）、ref `origin/1.2.x`、完整 commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。抽取的來源路徑：

- `src/NvtFwCombiner.Presentation.Avalonia/StartupTraceSession.cs`
- `src/NvtFwCombiner.Presentation.Avalonia/StartupTraceFileSink.cs`

記錄器、provider 的呼叫順序與 JSON 屬性順序都和這兩個檔案相同。環境變數、結構版本名稱與額外區段改由主應用程式提供。產品專屬的標記與預載階段留在 NFC。`tests/NvtFwCombiner.UiSmoke.Tests/StartupTraceSessionTests.cs` 的四個測試已改用合成資料移植。

## 公開 API

```csharp
public sealed class StartupTrace
{
    public static StartupTrace Disabled { get; }
    public bool IsEnabled { get; }
    public TimeSpan? ElapsedSinceManagedEntry { get; }
    public static StartupTrace StartFromEnvironment(string outputPathEnvironmentVariable);
    public static StartupTrace Create(
        string? outputPath,
        TimeProvider? timeProvider = null,
        Func<long>? allocatedBytesProvider = null,
        bool measureWithoutOutput = false);
    public void Mark(string stage);
    public bool Complete(
        string finalStage,
        string schemaVersion,
        Action<System.Text.Json.Utf8JsonWriter>? writeHostSections = null);
}
```

## 主應用程式用法

```csharp
using Nvt.Core.Startup;

StartupTrace trace = StartupTrace.StartFromEnvironment("APP_STARTUP_TRACE_PATH");
trace.Mark("options.ready");
bool written = trace.Complete("window.opened", "app-startup-trace-v1", writer =>
{
    writer.WriteString("hostStatus", "ready");
});
TimeSpan? elapsed = trace.ElapsedSinceManagedEntry;
```

`Create` 會去除路徑前後的空白，並將空白路徑視為未指定路徑。
沒有路徑且 `measureWithoutOutput: false` 時，直接回傳 `Disabled`，不讀取任何提供者。
設定 `measureWithoutOutput: true` 時仍會計時，但 `IsEnabled` 為 false，
忽略階段標記，且完成操作回傳 false。`StartFromEnvironment` 一律採用這種計時行為，
也是唯一會讀取環境變數的 API。

預設提供者為 `TimeProvider.System` 與
`GC.GetTotalAllocatedBytes(precise: false)`。建立時依序讀取時間戳記、
配置位元組數與 UTC 時間。每個記錄的階段先讀取時間戳記，再讀取配置位元組數。
完成操作先標記最後階段並設定完成狀態，再讀取 UTC 時間並寫入檔案。
只要計時啟用，每次讀取 `ElapsedSinceManagedEntry` 都會取得新的時間戳記，
完成後也相同；此屬性不會固定為完成當下的時間長度。

記錄從 `managed-entry` 開始，其測量值皆為零。階段名稱不得為空值、空字串或只有空白，
即使記錄器停用或已完成也會驗證。經過毫秒數使用時間提供者的時間戳記頻率計算。
配置總量以建立時的數值為基準，負值限制為零；配置差值以上一個記錄的總量為基準，
同樣將負值限制為零。請依序呼叫同一個執行個體；此模組不會同步並行呼叫。

## 檔案契約

完成操作使用 `FileMode.CreateNew`、`FileAccess.Write` 與 `FileShare.Read`。
它不會覆寫檔案，也不會建立上層目錄。主應用程式必須準備目的目錄，並選擇尚未使用的檔名。
完成操作只嘗試一次；之後的標記會被忽略，之後的完成呼叫回傳 false，寫入失敗後也相同。

JSON 使用縮排，根物件屬性依下列順序寫入：

1. `schemaVersion`：主應用程式傳入的名稱。
2. `processId`：目前處理程序識別碼。
3. `runtime`：執行階段的框架說明。
4. `osArchitecture`：作業系統架構。
5. `processArchitecture`：處理程序架構。
6. `startedUtc`：建立時讀取的 UTC 時間。
7. `completedUtc`：標記最後階段後讀取的 UTC 時間。
8. `stages`：依序記錄的階段物件。

每個階段依序寫入 `name`、`elapsedMilliseconds`、`deltaMilliseconds`、
`allocatedBytesSinceManagedEntry` 與 `allocationDeltaBytes`。
`deltaMilliseconds` 是目前經過時間減去上一個階段的經過時間，初始基準為零。

`stages` 陣列關閉後，選用的回呼會在尚未關閉的根物件中寫入額外屬性。
主應用程式必須輸出有效的屬性，並保持根物件開啟。
檔案寫入會攔截 `IOException`、`UnauthorizedAccessException`、
`NotSupportedException` 與 `ArgumentException`，以例外訊息記錄
`Startup trace was not written: {0}` 警告，並回傳 false。
回呼拋出的其他例外類型會向外傳遞。寫入或回呼失敗可能留下不完整的檔案，
完成狀態仍然維持不變。

## 驗證

`StartupTraceTests` 使用佇列中的時間戳記與 UTC 時間、合成的配置數值、
唯一的環境變數名稱，以及可釋放的暫存工作目錄。
測試涵蓋 JSON 順序、主應用程式區段、提供者讀取順序、無輸出時計時、
配置負值限制、階段驗證、完成狀態、回呼例外、既有檔案與缺少目錄的情況。

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
dotnet test Nvt.Core.sln --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Startup"
```
