# Nvt.Core.TestSupport

[English](README.md) | 繁體中文

這是供確定性測試使用的 net10.0 可封裝程式庫。版本繼承 Directory.Build.props，
只為了 `ChildProcessFixture` 使用的 Core 行程啟動 seam `ProcessLaunchGate` 而相依於 Nvt.Core，
不相依於 xUnit 或 Avalonia。**只有測試專案可以參考此專案或套件**。
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

## SignalWait

SignalWait 是供測試設定與等待的一次性訊號，取代以 Task.Delay 與 Thread.Sleep
等待結果的做法。Set 第一次呼叫回傳 true，之後回傳 false；等待者在其他執行緒
繼續，不會在 Set 內執行。WaitAsync 等待訊號、取消權杖或看門狗。

看門狗只用來避免卡死，必須是有限的正數（預設 30 秒，DefaultWatchdog），絕不
決定測試結果。到期時 WaitAsync 擲出 TimeoutException，訊息會寫出訊號名稱；
權杖取消仍是 OperationCanceledException。看門狗時鐘預設為 TimeProvider.System，
測試可改傳其他時鐘，例如 ManualTimeProvider，不必實際等待就能驗證到期。
靜態的 WaitAsync(Task, name, ...) 為任何工作加上同樣的看門狗，工作本身的失敗
原樣傳出。

## ChildProcessFixture

ChildProcessFixture.Start 不經 shell 啟動可執行檔。標準輸出與標準錯誤依到達
順序合併擷取，最多到字元上限（預設 64 KiB，OutputTruncated 表示輸出被截斷）。
標準輸入會立即關閉。子行程未自行結束時，看門狗（預設 60 秒）會結束整個行程
樹；之後 WaitForExitAsync 擲出 TimeoutException，WatchdogExpired 為 true。
WaitForOutputAsync 等待輸出包含指定文字，輸出結束仍無該文字則失敗。KillTree
依要求結束行程樹。Dispose 與 DisposeAsync 會結束整個行程樹，有限度等待根行程
結束並釋放控制代碼，可重複呼叫。子行程若一直存活，會占住檔案並讓下一個測試
隨機失敗，所以每個子行程都應經由此 fixture 啟動。行程樹結束只能涵蓋父行程仍
存活的子孫；比父行程活得更久的子孫無法觸及。

## RelativePerf

RelativePerf 讓效能門檻跟著機器走。「少於一秒」這類絕對時間在慢的 CI 機器上會誤判，
在快的機器上又掩蓋退化。請用三種形式之一。

- **校準單位。** CalibrationUnit 執行固定的參考工作量並回傳其中位數時間。InUnits 把量到的時間
  換成該單位的倍數。請在同一個行程、貼近量測前取得單位。
- **規模比例。** ScaleRatio 把較大輸入的中位數時間除以較小輸入的中位數時間。線性演算法加倍約為 2，
  平方演算法約為 4。這個比例與機器無關。
- **計數。** MedianAllocatedBytes 計算工作在呼叫執行緒上配置的位元組。計數與機器速度無關，
  能用計數時優先使用。

每次量測都會暖身、取至少七個樣本（MinimumSamples）並回報中位數。門檻失敗不重試；不穩定的測試
請開 Issue。門檻要寫在測試旁邊：參考機器上量到的中位數，以及乘上的餘裕。測試請標
[Trait("Category", "Performance")]，CI 才能用獨立步驟執行。預設時鐘是 TimeProvider.System；
測試此 helper 本身時改傳 ManualTimeProvider。

## TaskBlock

TaskBlock.UntilComplete 會阻塞呼叫執行緒直到工作完成。只能用在無法 await 的測試掛鉤，例如
dispatcher 回呼或 fixture 建構函式。測試中禁用 Task.Wait、Task.Result 與 GetAwaiter().GetResult()。
UntilComplete 以事件阻塞、重新擲回原始例外（不是 AggregateException）、已取消的工作會擲出
OperationCanceledException，並且不使用呼叫端的同步內容。工作必須在其他執行緒執行；需要被阻塞
執行緒才能繼續的工作永遠不會完成。

## TestFiles

File.ReadAll* 與 File.WriteAll* 這類捷徑在所有專案都被禁用。測試需要單純的 fixture 檔案時，
使用 TestFiles.WriteAllBytes 與 TestFiles.ReadLinesAsync，兩者都經由串流。
只能用在 TestWorkspace 內的路徑。

## 基準與刻意差異

這些工具取代儲存庫 Dennis40816/nvt_fw_core（分支 `main`，PR #147 的時鐘）中的
`tests/Nvt.Core.Tests/Processes/ManualTimeProvider.cs` 與
`tests/Nvt.Core.Tests/Processes/TestWorkspace.cs`。這兩個檔案在提交
d3f0a1ddb467b0abb1cb832b81b0bf6da69ef559 與共用支援變更的父提交
f90900bbb04f84e590aa77dc47b6e04b7a77d9c6 完全相同。可用
`git show <commit>:<path>` 比對。`SignalWait` 與 `ChildProcessFixture` 是新增的，不取代任何既有程式，所以沒有要比對的來源行為。以下行為是刻意改變，已遷移的 Core 測試不依賴舊行為：

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
