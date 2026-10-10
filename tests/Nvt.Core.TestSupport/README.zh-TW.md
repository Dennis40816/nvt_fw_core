# Nvt.Core.TestSupport

[English](README.md) | 繁體中文

這是供確定性測試使用的 net10.0 可封裝程式庫。版本繼承 Directory.Build.props，
不相依於 Nvt.Core、xUnit 或 Avalonia。**只有測試專案可以參考此專案或套件**。
不得新增時鐘或測試工作目錄的副本；需要新行為時，擴充共用實作及其測試。

## ManualTimeProvider

建構時明確傳入 DateTimeOffset 起始時間，轉為 UTC；LocalTimeZone 為 UTC，
初始時間戳記為零。TimestampFrequency 固定為 TimeSpan.TicksPerSecond，
繼承的 GetElapsedTime 與 GetUtcNow 提供一致的經過時間。

Advance 拒絕負值及 UTC 範圍溢位。計時器在推進時間的執行緒同步執行，
回呼觀察到各自的到期時間；一次推進會執行每個已經過的週期。
依到期時間排序，相同時間依排程順序。無限到期時間停用計時器，
零或無限週期表示單次執行；其他負值無效。Change 以目前手動時間為基準，
釋放後回傳 false。釋放等待中的計時器會取消後續回呼。

回呼在內部鎖之外執行，可以讀取時間、建立、變更或釋放計時器。
遞迴或同時呼叫 Advance 會擲出 InvalidOperationException。
回呼例外直接傳回，時鐘停在該回呼的到期時間；之後可以再推進。
計時器的 DisposeAsync 會等待已取得執行權的回呼完成；
回呼不可同步等待自身的非同步釋放。

Task.Delay(delay, provider, token) 及 CancellationTokenSource(delay, provider)
使用這些計時器。測試結果不依賴真實時間休眠；TimeProvider.System
只用於訊號等待的有界看門狗，不得用來決定受測行為。

## TestWorkspace

Create 建立唯一、短 nvt- 名稱的系統暫存目錄。RootPath 為絕對路徑。
GetPath 解析測試檔案路徑，不建立檔案；兩種斜線皆視為分隔符號。
拒絕空路徑、絕對／磁碟機／UNC 路徑、冒號（包含 Windows 替代資料串流）、
所有父目錄片段，以及僅由點與空白組成的模糊片段。
單一點表示根目錄，..fixture 等一般名稱允許使用。
驗證是字面路徑檢查；測試不得建立指向根目錄外資料的連結或 junction。

TestWorkspace 實作 IDisposable 與 IAsyncDisposable；請使用 using／await using。
Dispose 與 DisposeAsync 清除合成測試資料，可重複呼叫；
目錄已被移除也視為成功，清除失敗後則重複回報相同例外。IOException 或 UnauthorizedAccessException
最多重試十次，單調時間總預算為 500 ms，每次等待最多 50 ms。
預算無法中斷已在執行的作業系統檔案呼叫。
失敗會擲出含保留路徑及原始例外的 IOException；之後釋放不會重新取得重試預算，
而是回報同一例外。解除占用或權限問題後，保留路徑可供手動清理。
DisposeAsync 執行相同的同步有界清除。內部刪除、等待與經過時間掛鉤
讓測試可在不休眠的情況下驗證重試。
正式清除本身以真實時間等待，每次嘗試之間最多 50 ms。

這些工具不初始化 UI，也不包含產品資料。Core Processes 測試已採用此專案；
Files、Startup、Progress，以及 StartupTraceTests、ThrottledProgressTests
內部的時鐘副本留待發布後遷移。
Launcher 傳輸測試內部的 LinkedProbeWorkspace 是另一份工作區副本，同樣留待遷移。

## 基準與刻意差異

這些工具取代儲存庫 Dennis40816/nvt_fw_core（分支 `main`，PR #147 的時鐘）中的
`tests/Nvt.Core.Tests/Processes/ManualTimeProvider.cs` 與
`tests/Nvt.Core.Tests/Processes/TestWorkspace.cs`。這兩個檔案在提交
d3f0a1ddb467b0abb1cb832b81b0bf6da69ef559 與本變更的父提交
f90900bbb04f84e590aa77dc47b6e04b7a77d9c6 完全相同。可用
`git show <commit>:<path>` 比對。以下行為是刻意改變，已遷移的 Core 測試不依賴舊行為：

時鐘：

- LocalTimeZone 是 UTC。舊時鐘沿用機器時區。
- 建構子必須明確提供起始時間。舊時鐘使用固定起點。
- 釋放後呼叫 Change 回傳 false。舊時鐘會重新啟動計時器。
- `Change(Infinite, period)` 會取代週期。舊時鐘保留舊週期。
- 計時器在時鐘選取後才被釋放，仍會執行已取得的回呼。舊時鐘只會略過單次計時器，週期計時器仍會執行。沒有測試固定這一項，因為需要在選取與呼叫之間製造競爭。
- 計時器的 DisposeAsync 會等待執行中的回呼。舊版立即回傳。
- 遞迴或並行的 Advance 擲出 InvalidOperationException。舊時鐘允許。
- 無效的計時器時間與超出 UTC 範圍的 Advance 會擲出例外。週期大到溢位時只觸發一次就停止；舊時鐘會溢位回繞並不斷重複觸發。
- 可同時有多個等待者（內部的 WhenPendingAsync）。舊時鐘只保留最後一個，第一個永遠不會完成。
- 週期計時器重新排程時會通知等待者；已取消的等待者會被移除。

與舊時鐘相同：回呼例外會讓時間停在該回呼的到期時間；被前一個回呼釋放的計時器不會觸發。

工作區：

- 清除在所有作業系統執行，500 ms 內最多十次，失敗時擲出含路徑的 IOException。
- 位於系統暫存目錄之外的根目錄，或暫存目錄本身，在建構時失敗；前綴為 nvt-。
- GetPath 以 Path.GetFullPath 正規化（a/./b 變成 a/b，單一點為根目錄），並拒絕空白路徑、冒號、父目錄片段，以及僅由點或空白組成的片段。反斜線在所有作業系統都是分隔符號。

此套件隨 Core 專案一起建置，但 `scripts/pack.ps1` 以名稱只封裝 `Nvt.Core` 與
`Nvt.Core.Avalonia`，所以 `core-v*` 發布不會發布本套件。發布本套件屬於後續工作。
