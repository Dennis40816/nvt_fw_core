[English](LogConsole.md) | [繁體中文](LogConsole.zh-TW.md)

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

# LogConsole

`Nvt.Core.LogConsole` 提供 NFC、NFH、NFU 共用的非 UI console 模型。
目標框架是 `net8.0`，沒有新增套件。
Avalonia 控制項與共用 commands 會在 K2 實作。

## 設計與來源

這是 Core PR #82 於 2026-10-07 核准設計的新實作，不是產品 console 的直接移植。
行為參考是實作前的 NFH console baseline。
該 baseline 的 10,000 筆同步突發量測為 9714.908 ms，包含產品投影與 UI 工作。
此模組的效能測試只量測純投影，不能直接證明 UI 改善。
本次沒有 UI 或產品邏輯。

## 狀態結構

輸入集中在 `ConsoleFilter` record，包含等級、來源、字面搜尋、只看符合、去重與時間模式。
空來源集合代表所有來源，空等級集合代表不顯示任何等級，空搜尋代表沒有搜尋條件。
來源 ID 使用 ordinal identity，搜尋使用 ordinal 不分大小寫比較。

`LogStore` 是事件唯一 owner。
`LogSnapshot` 是不可變、帶 content lease 的版本快照。
它包含 version、generation、保留範圍、事件數、淘汰總數與 entries。
排序只看 Sequence，不看時間。
Clear 後 EntryId 與 GroupId 不會重用。

View 集中在 `ConsoleViewState` record。
`ConsoleFollow` 是封閉階層，只有 `Following` 與 `Paused(anchor, pausedAt)`。
Selection 是原始 EntryId 的 immutable set，去重列是否選取由成員推導。
ExpandedIds 使用穩定 row ID。
收合不會恢復跟隨。
`Pause` 保存當下列順序，`Resume` 才明確恢復依最新序號排序。
`Remap` 在去重切換時保留同一批原始選取事件，並透過原始事件成員映射展開狀態、閱讀 anchor 與凍結順序。
暫停時間、watermark、文字位置與像素差維持不變。
被篩選隱藏的選取仍保留，被淘汰的選取會移除。
複製只包含目前可見的選取列。

`ConsoleProjector.Project(snapshot, filter, viewState)` 是純函式。
它不讀時鐘，也不直接存取檔案系統。
時間基準只來自快照時間或暫停時間。
完整內容 handles、搜尋 ranges、counts 與 memberships 都在 `ConsoleProjection` 中。
投影不將完整訊息複製成列字串。
Counts 與空狀態不另外存成 state。
宿主接受背景投影前，須確認快照與篩選輸入仍相同。
K2 會讓選單與快捷鍵走相同 commands。
K1 提供機制與不可變值，不另建一套 controller。

## 保留預算與生命週期

預設上限分別是 10,000 entries、4 Mi 保留 UTF-16 字元、256 Ki pending UTF-16 字元。
三項獨立生效。
字元預算計入 `ILogTextContent.ResidentCharacterCount`。
預設記憶體內容計入完整長度。
App 可以注入不可變、可分段讀取的大訊息內容。
Spill 位置與清理政策由 app 決定，Core 不提供碰磁碟的內容實作。

成功入列時，內容的獨占 ownership 轉交 store。
Stale batch 不會開始 enumeration。
入列前被拒絕的輸入仍屬 caller。
`Add` 接受時回傳正數 stable ID；容量不足時回傳無效 ID `0`。
`AddBatch` 因容量或 generation 被拒絕時回傳 `false`。
Batch 在鎖外準備，於一次 lock hold 內整批接受；拒絕時完全不轉移 ownership。
準備遇到第一筆超出 pending 字元或 handle 上限的輸入便停止，最多檢查 `maxEntries + 1` 筆。
已準備內容與尚未列舉的 suffix 都仍屬 caller；enumeration 或 preparation 例外也不轉移任何內容。
拒絕不消耗 ID、不發出 `Changed`、不拋例外，store 絕不 Dispose 被拒絕內容。
無效參數、preparation 失敗與 store 已 Dispose 仍維持既有例外行為。
唯讀 `RejectedCount` 累計 store 生命週期中的拒絕呼叫次數。
整批拒絕計一次，stale batch 也計入；Clear 不重設此計數。
同一個內容 instance 不得用於多筆輸入。
Length、resident charge 與 Version 必須固定，Read 必須填滿要求區間。
內容實作須支援並行讀取。

`Add` 與 `AddBatch` 在鎖外驗證 metadata、讀取 timestamp 與計算 fingerprint。
短時間 admission lock 先檢查總容量，再分配 ID 並按序入列。
Producer 不等待容量、writer 或 callback。
發布是非同步的；`CaptureSnapshot` 在所有鎖外等待先前已接受的 Add 與 Clear 發布。
Admission fence 在 `_gate` 內由 next sequence 加 generation 推導。
被拒絕的寫入與空 batch 不推進此 fence。
匯出先取得這份最新快照，再投影固定版本的資料。
在 writer 執行緒呼叫 Capture 時，包括 Changed 與 content Dispose callback，只回傳目前已發布狀態。
它不會等待自己 callback 才入列的操作。
Writer 不受通知 ready 狀態限制。
`GetChangesSince(version)` 與 `IsCurrent` 不等待，直接讀取已發布狀態。
Store Dispose 以 `ObjectDisposedException` 解除等待中的 Capture。

只有 writer 修改 ring、groups、version、history 與 EvictedCount。
Writer 取出 queued work、比較全文、套用新增與淘汰，最後以一次 reference swap 發布不可變狀態。
Reader 在固定時間鎖內 pin 該狀態，再於鎖外建立 leased snapshot。
Reader 只看見前一個完整版本或本次完整提交。
不再有 preparation reservation、容量等待、提早淘汰或 optimistic retry。
Ring 使用 `Queue<T>` 的循環陣列，不搬移頭端，也不逐筆重建完整 console 文字。
Delta history 的 marker 數與 removal ID 數都受 entry limit 限制。
歷史不足時明確要求 reset，使用最新完整快照恢復。

Pending budget 獨立計入所有尚未發布且由 store 擁有的內容。
範圍包含 queued writes、discarded writes、writer 處理中的內容與尚未發布內容的延後清理。
Clear 留下的內容持續計入額度，直到 writer 的實際 Dispose 返回。
超出字元或 handle 上限時，admission 拒絕新輸入；不因 overflow 丟棄已接受的 queued writes。
所有尚未發布內容的 handle 數合計受 `maxEntries` 限制，包含零字元 charge 的內容。
此外最多保留一個 reset marker。
只有 ring eviction 增加 `EvictedCount`；拒絕呼叫增加 `RejectedCount`。
Writer callback 忙碌時，只要還有額度，producer 仍可入列。
Pending 用量在 admission lock 內由 ownership collections 推導，不儲存獨立的字元總數。
Caller 擁有的 preparation 不計入 store ownership 上限。
Retained budget 在同一個發布版本套用新增及其造成的最舊事件淘汰。
超過 pending 容量的輸入被拒絕；已接受但超過 retained 容量的輸入會在 ring 淘汰自己。

`Clear` 立即遞增 generation fence 並入列 reset。
它取代尚未執行的 resets，並將過時 queued writes 移至 writer cleanup。
Writer 在套用 reset 前捨棄舊 generation 的 queued writes。
Reset 清除 retained content、groups、history 與 EvictedCount。
Reset 與其後新增使用不同版本。
`AddBatch` 在 enumeration 前、入列時及 writer 中驗證 generation。
Writer 比較期間 generation 改變時捨棄該步，不 retry。
`IsCurrent` 同時檢查立即 fence 與已發布狀態。
`Dispose` 立即關閉 admission 並喚醒 writer 釋放 queued 與 retained leases。
既有快照與投影在自身 Dispose 前仍可讀取。
已發布內容使用 ring 容量，不計入 pending 容量。
已淘汰但仍由 snapshot 或 projection lease 持有的內容，等到 lease 結束才釋放。
Lease holder 負責 Dispose 自己的快照與投影。
已發布內容的最後 lease 釋放及其 writer cleanup 不計入 pending 上限。

App content Dispose 由 writer 在發布後、所有鎖外執行。
最後一個 lease 與 active read 釋放後才會清理內容。
最後 lease 的釋放會入列 writer cleanup，store Dispose 後也適用。
資料 writer 清理後停止，較晚釋放的 leases 只再次排程剩餘清理。
快照或投影的 Dispose 不會在 caller 執行 app Dispose。
去重比較也只在 writer 的鎖外執行。
投影、scanner、export、metadata、clock 與 fingerprint 仍保留各自 caller-thread 的讀取契約。

一個 interlocked flag 涵蓋 queued 與 active writer，包括所有 app callbacks。
既有 `schedule` delegate 啟動 writer。
Thread-pool dispatch 在 producer 執行緒外呼叫 delegate，因此 inline scheduler 不會阻塞 producer。
Scheduler 丟例外時不得已經排入 callback；失敗時在 dispatch 執行緒繼續 writer。
Subscriber、比較與 Dispose 的例外各自隔離，不能中斷後續 logging 與清理。
比較失敗時保留已接受輸入並使用獨立群組，不將比較失敗算成 ring eviction。

新 store 尚未 ready 通知。
宿主先訂閱、擷取 startup snapshot 與保存 version。
`SetReady(true)` 只控制 `Changed`，不控制寫入或 pending capacity 釋放。
Subscribers 與 scoped lease 釋放完成前，不會重疊另一個通知。
Reentrant Add 入列供 writer 下一步處理。
`Changed` 資料只在 callback 內有效，需要保留時另取快照。
Consumer 必須驗證 generation。

## 去重與投影

去重鍵是 SourceId、Level 與未改變的完整 message。
時間與 text version 不參與 key。
Hash 碰撞以分段讀取比較全文。
原始換行不同就不合併。
只要成員還存在，GroupId 就維持穩定。
投影包含保留 member sequences、首末 occurrence time、count 與 last sequence。
淘汰後由保留成員重新計算。
Following 依最後序號排序。
Paused 固定原有列位置，新列排在後面。
新列使用穩定 EntryId 或 GroupId 排序；重複抵達與群組最初成員被淘汰都不改變插入順序。
新增 raw events 的計數包含只增加去重次數的事件。
此計數涵蓋 pause watermark 之後仍保留的 matching events，已淘汰事件另列 EvictedCount。
閱讀 anchor 被淘汰時，依暫停時凍結的列順序選第一個仍存在的後繼列。
暫停時 relative time 使用固定 PausedAt。

Level counts 只套來源條件，source counts 只套等級條件。
兩者都數保留 raw events，不受搜尋或去重影響。
搜尋涵蓋完整訊息與來源，hit ranges 使用 UTF-16 offset 與 exclusive end。
每次訊息讀取最多 1,024 個 UTF-16 字元；只保留與搜尋字面長度成比例的 overlap，以支援跨段命中。
OnlyMatches 關閉時所有符合來源與等級的列仍保留高亮。
EventCount 是快照的未篩選保留事件數，MatchingEventCount 是可見列的成員總數。
RowCount 是投影列數，多行訊息不增加事件數。

## 連結與 app 注入

`ConsoleLinkScanner` 是純函式，優先找 URL，再找引號與未加引號的路徑。
支援 drive、UNC、relative paths、Unicode、引號內空白與兩種行欄 suffix。
結束引號必須配對起始引號；雙引號路徑內的單引號不會截斷完整路徑或行欄。
兩種入口共用一個 Unicode scalar 名稱規則與一個路徑起點規則。
未加引號的 component 名稱接受所有 Unicode letters、三種 marks、decimal digits，以及 `_`、`-`、`.`。
Separator 與行欄 suffix 是獨立語法 token。
`.config` 與 `.github` 等 dot-prefixed component 保留起始的 dot。
Decomposed 名稱保留 combining marks 與完整的第一個 component。
Target 保留輸入的 Unicode 形式，不做 normalization。
已辨識的 drive 與 UNC prefix 可以緊鄰前面的 CJK 敘述。
URL 保留成對括號，外側中文標點不是 target。
外側標點包含全形冒號；只有檔名的相對路徑若帶行欄 suffix 也會接受。
這包含引號內帶空白的檔名。
沒有 separator 或行欄 suffix 的引號名稱不會成為連結。
結尾 separator 明確代表資料夾，沒有副檔名仍可能是檔案。
其他模糊的資料夾由 structured spans 或 app resolver 指定。
沒有副檔名白名單，沒有存在性查詢，也不掃 repository。

App spans 完全取代推導，明確的空 spans 也同樣優先。
`ConsoleLinkIndex` 驗證 ranges 並提供 binary hit test。
`ConsoleLinkCache` key 包含 EntryId、text revision 與 resolver policy revision，不含搜尋。
`Synchronize` 是唯一語意失效點，每次接受快照都要呼叫，包含 Clear。
舊快照不能重新填入 live cache。
掃描在 cache 鎖外執行，發布前再次驗證 snapshot 與 policy revision。
Cache 使用固定 1,024 字元的讀取 buffer 直接掃描 segmented content，候選 offset 可跨越讀取邊界。
只將確認的 link targets 建立成字串，不將完整一般訊息 materialize。
Entry 數、span 數與 target 字元都有獨立 retention limit。
過大的結果仍完整回傳，但不快取。

`IConsolePathResolver` 非同步回傳 candidates，不替使用者選模糊結果。
App 必須遵守 cancellation、MaxCandidates 與 MaxProbes。
PolicyVersion 用來使 cache 失效，Core 不提供 resolver 實作。
`IConsoleLinkOpener` 接收完整 `LinkTarget`，回報開啟、父資料夾、行與欄能力。
開啟結果沿用 `SourceFileNavigation.SourceFileOpenResult`，本模組不提供 opener 實作。
來源名稱、logging adapter、路徑政策、開啟、clipboard、儲存目的地與大訊息 storage 都由 app 提供。

## 匯出

三種操作都以同一份 frozen projection version 使用 `ConsoleExportFormatter`。
`FormatSelection` 接受原始 EntryId 並推導可見去重列的選取，`FormatVisible` 包含 viewport 外的所有篩選列。
`WriteLogAsync` 寫入相同文字，目的 stream 由 app 提供且保持開啟。
使用無 BOM 的 UTF-8。
Stream 匯出以有界 chunks 讀寫，不建立完整匯出字串或 byte array。
回傳字串的複製方法只為呼叫端要求的最後輸出配置記憶體。
IncludeTime 與 IncludeLevel 預設開啟，彼此獨立。
匯出時間使用 UTC clock time，不受畫面時間模式影響。
來源與完整訊息一定保留，原始換行不變，列間以 LF 分隔。
重複列保留 `×N`，不讀取截斷畫面或舊格式化字串。
稍後 Add 發布後，宿主即使收合也須擷取最新快照供匯出。

## 公開 API

完整 type 與 member 表見 [英文 Public API](LogConsole.md#public-api)。
主要入口是 `LogStore`、`ConsoleProjector`、`ConsoleViewState`、`ConsoleLinkScanner`、`ConsoleLinkCache` 與 `ConsoleExportFormatter`。
資料與輸出型別包含 `LogEntry`、`LogSnapshot`、`LogChangeSet`、`ConsoleFilter`、`ConsoleProjection`、`ConsoleRow` 與穩定 IDs。
App contracts 是 `ILogTextContent`、`IConsolePathResolver` 與 `IConsoleLinkOpener`。
`ConsoleRow.TextContent` 取代完整 Message 字串；`ConsoleProjection` 實作 IDisposable。
`ConsoleViewState.Selection` 與 `FormatSelection` 使用 `ImmutableHashSet<long>` 原始 EntryIds。
`ConsoleFollow` 改為 private base constructor 的 immutable class 階層，巢狀兩種狀態皆為 sealed。

## 同步保護與重用機制

`LogStore._gate` 保護 admission queues、尚未發布的 writer ownership、cleanup queues、RejectedCount、next sequence、generation、ready、Dispose 狀態、subscriptions 與 published reference。
Ring、group index、history、version、EvictedCount 與 notification cursor 只有 writer 可修改。
Writer 排程使用一個 interlocked flag，published state 的 reference count 也使用 Interlocked。
`ContentOwner._contentGate` 只保護 reference lifetime，active read 先 pin 再於鎖外呼叫 app。
快照以 Interlocked 確保只釋放一次，投影的每個 lease 也只釋放一次。
`ConsoleLinkCache._cacheGate` 保護 cache 與 revision stamp，其他 model state 不可變。
宿主在單一執行緒替換 view state 並處理 commands。

Clock 使用 BCL `TimeProvider`，測試重用 Core 的 `Time.DelegateTimeProvider`。
開啟結果重用 `SourceFileNavigation.SourceFileOpenResult`。
實作 baseline 的 Lifecycle 只有 CoalescedRefresh 與 UndoService，沒有可重用的 generation helper。
CoalescedRefresh 不能將 ready 與 pending work 原子整合進 store 的同一鎖。
MessageCenter 與 Persistence 的 generation 各自綁定 modal 與 save coordinator，不能代替 store generation。
本模組只有一個 internal `ConsoleGeneration`，統一遞增與 stale-work checks，由 store lock 保護。

## 驗證與採用

測試涵蓋 startup delta、Clear、所有可達 drain 交錯、三項 budget、content leases、hash 碰撞、去重、counts、search、pause、selection、scanner 語法與 IL、cache 失效、區間索引與匯出 bytes。
回歸涵蓋 reentrant app callbacks、串行通知、凍結順序的 anchor successor、原始事件選取與封閉 Follow constructors。
也涵蓋收合後最新快照匯出、絕對與隱藏時間、文件 hygiene。
生成的 16 Mi 字元訊息只計入 64 個 resident 字元；測試要求投影配置少於 256 KiB、每次讀取最多 1,024 個字元、stream 寫入最多 4,096 bytes。
測試驗證跨段搜尋與 UTF-8 surrogate pair，沒有保留整份匯出內容。
同一份大內容的連結掃描也要求配置少於 256 KiB，且每次讀取最多 1,024 個字元。
案例包含一般文字，以及很長的引號候選內含一個短 URL，確保優先順序在建立 target 字串前判斷。
過大的 lazy batch 在第一筆超額輸入停止準備，不接受任何 prefix；無限零 charge batch 也驗證準備的 handle 上限。
Held-writer 測試將 queued、discarded 與 active ownership 的推導用量，對照已接受內容的可觀察生命週期。
涵蓋字元與 handle 峰值、重複 Clear、延後 Dispose、整批拒絕與 concurrent rejection 下的 ID 順序。
拒絕測試涵蓋 counters、無事件與例外、caller ownership、無 ID 消耗及無發布。
已發布 snapshot cleanup 不計入 pending 額度也有獨立回歸。
最新 Capture 與匯出測試先進入 publication barrier，再釋放 writer。
Writer callback 中的 Capture，以及 Dispose 解除 reader 等待，都有 timeout 回歸。
通知 clock 例外會重新排程尚未送出的 Changed，並有 throw-once 回歸。
兩種 scanner 共用 Unicode scalar 規則，span 仍使用 UTF-16 offset。
回歸涵蓋跨讀取邊界的 supplementary 路徑字元，以及緊鄰 CJK 敘述的 URL。
具名 dot-prefix 與 mark 回歸涵蓋兩種入口及 name、suffix 內的每個讀取邊界。
Deterministic differential corpus 有 115 行不同內容，包含第四至第八輪 review 的每個 scanner 範例。
它測試每個 UTF-16 split，對不超過 32 字元的短行也測試每對 interior split。
實體 content 分段涵蓋 surrogate pair 與三種 Unicode mark categories。
Padding 將每個 split 移到 scanner 真正的 1,024 字元讀取邊界。
13,181 次比對包含 10,971 個三段切割。
除了兩種入口 parity，獨立預期 spans 也驗證完整 component 起點與不變的相對 target。
URL 與 absolute-prefix 的 word boundary 也共用所有 mark categories。
回歸驗證 word 內 spacing 與 enclosing marks 後面的 drive 或 URL prefix 不會被誤認為起點。
必要回歸涵蓋會 logging 的 callbacks、八個 concurrent producers、pending overflow、並行 Clear 與 Dispose、回傳 IDs 的遞增順序。
另測試空與失敗的空 batches、nested preparation callback，以及阻塞的 inline scheduler。
Reservation 與 queued-drop 測試移除或改寫為 admission ownership 與拒絕容量。
暫停排序回歸涵蓋重複事件、群組最初成員淘汰與切換去重時 anchor remapping。
連結測試涵蓋配對引號，以及跨段 prefixes、paths、quotes 與行欄 suffix。
含空白與 Unicode 的引號檔名只要帶行欄 suffix，就不需要 separator。
兩種 scanner 入口都測試引號檔名與 suffix 內每個讀取邊界。
沒有 separator 或 suffix 的引號名稱仍為一般文字。
混合 Add、被拒絕的 Add、AddBatch 與 Clear，每次操作後都有最新 Capture 回歸。
Admission fence 不另存獨立計數器。
第六輪緊鄰 CJK 敘述的 drive 與 UNC prefix 回歸保留不變且通過。
Deterministic writer explorer 涵蓋其有限情境中所有可達 enqueue、ready、post 與 writer 交錯。
效能測試只有 `NVT_CORE_PERF=1` 才執行。
它先 warm up，再投影 10,000 筆 retained events，啟用去重與搜尋，只印時間不斷言時間上限。

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
$env:NVT_CORE_PERF = '1'
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~ConsolePerformanceTests" --logger "console;verbosity=detailed"
```

2026-10-08 第九輪修正後的 Debug 驗證結果：solution build 為零警告、零錯誤。
Solution tests 為 3,706 通過、16 跳過、零失敗。

| 測試專案 | 通過 | 跳過 | 失敗 |
| --- | ---: | ---: | ---: |
| Nvt.Core.Tests | 3,077 | 16 | 0 |
| Nvt.Core.Avalonia.Tests | 604 | 0 | 0 |
| Nvt.Core.Fonts.Tests | 25 | 0 | 0 |

LogConsole 為 181 通過、1 個 opt-in 效能案例跳過。
第五輪 32 個、第六輪 9 個、第七輪 10 個與第八輪 11 個回歸案例全部通過。
第九輪 18 個回歸案例全部通過，包含 differential corpus 與 word 內 prefix 前方的 mark 案例。
第六輪 review 記錄的六個 store 失敗目前全部通過。
另一次單獨效能測試為 1 通過、零跳過、零失敗：10,000 筆投影耗時 25.604 ms，產生 1,000 列。
這是一次 warm-up 後的單次投影量測，不是時間上限。
Restricted sandbox 內將 TEMP 與 TMP 指向 worktree 中產生的目錄，讓既有 Windows custody tests 使用允許的暫存根目錄。

UI virtualization、pointer coordinates、keyboard commands、accessibility 與視覺證據由 K2 驗證。
宿主採用是另外的變更，真實 resolver、opener、clipboard 與 spill-store 整合仍屬於 app。


