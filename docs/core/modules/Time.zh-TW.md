[English](Time.md) | [中文](Time.zh-TW.md)

# Time: DelegateTimeProvider

[`DelegateTimeProvider`](../../../src/Nvt.Core/Time/DelegateTimeProvider.cs) 在 `Nvt.Core.Time` 中將 delegate 轉接為測試與舊有時間來源可用的介面，目標為 `net8.0`，只依賴 BCL。正式環境程式碼注入 BCL 的 `TimeProvider`。NFC 的 `ISystemClock` 不會移植，因為 `TimeProvider.GetUtcNow()` 可直接取代它。

```csharp
using Nvt.Core.Time;

var fixedValue = DateTimeOffset.UnixEpoch;
TimeProvider fixedClock = new DelegateTimeProvider(() => fixedValue);
var queue = new Queue<DateTimeOffset>([fixedValue, fixedValue.AddSeconds(1)]);
TimeProvider queuedClock = new DelegateTimeProvider(queue.Dequeue);
```

保留行為：每次 UTC 讀取恰好呼叫 delegate 一次，原樣回傳數值與時區位移，不快取，也不讀取真實 UTC 時鐘。delegate 的例外直接傳遞，包含空佇列的 `InvalidOperationException`。第一個建構函式使用 `Stopwatch.GetTimestamp()` 與 `Stopwatch.Frequency`；第二個使用 timestamp delegate 與正數頻率。兩個建構函式都拒絕 null delegate，第二個也拒絕零或負數頻率。此類別支援繼承。`GetElapsedTime` 與 `GetLocalNow` 維持基底類別實作；計時器與本地時區遵循基底類別，因此使用系統行為。使用繼承的 `GetLocalNow()` 時，請依 `TimeProvider` 的預期提供時區位移為零的 UTC 值。此轉接器不提供可控制的計時器。

凍結的父版本基準：NFC（`nvt_fw_combiner`）、ref `origin/1.2.x`、完整 commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。以下輔助類別僅供行為參考，皆不移植：

- `tests/NvtFwCombiner.TestSupport/FakeClock.cs`：依序取出的時間戳記。
- `tests/NvtFwCombiner.Bootstrap.Tests/CompositionRunExecutionMetricsTests.cs`，第 415–428 行：`CountingClock`。
- `tests/NvtFwCombiner.Application.Tests/Diagnostics/SystemInformationServiceTests.cs`，第 418–423 行：`StubClock`。
- `tests/NvtFwCombiner.Application.Tests/VersionManagement/ManagedFirstInstallationBootstrapOutcomeTests.cs`，第 685–697 行：timestamp 與頻率覆寫。

[`DelegateTimeProviderTests`](../../../tests/Nvt.Core.Tests/Time/DelegateTimeProviderTests.cs) 使用合成資料驗證參數檢查、數值與時區位移完全一致、呼叫次數、佇列與計數器 delegate、經過時間、stopwatch 時間戳記、繼承的本地時間，以及衍生輔助類別中的可變狀態。NFC 沒有可移植的時鐘輔助類別直接測試。

既有套件還原完成後，於不使用網路的情況下執行 Core 驗證：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Time"
```

NFC 採用仍待後續完成（T12 與 T13）。
