[English](LogConsole.md) | [繁體中文](LogConsole.zh-TW.md)

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

# LogConsole

`Nvt.Core.LogConsole` 提供 NFC、NFH、NFU 共用的非 UI console 模型。
目標框架是 `net8.0`，沒有新增套件。
控制器、標題、工具列與空狀態位於 `Nvt.Core.Avalonia.LogConsole`。Avalonia 列表見[列表檢視](#列表檢視)；宿主 commands 負責投影與狀態。

## 設計與來源

這是 Core PR #82 於 2026-10-07 核准設計的新實作，不是產品 console 的直接移植。
行為參考是先前的 NFH console 與已核准的 console redesign proposal。
NFH 量測包含產品投影與 UI 工作；不同 workload 不能作為直接的 UI 效能比較。
此模組的效能測試只量測純投影，不能直接證明 UI 改善。
非 UI 資料層沒有 UI 或產品邏輯。

## 狀態結構

事件篩選輸入集中在 `ConsoleFilter` record，包含等級、來源、字面搜尋、只看符合、去重與時間模式。
空來源集合代表所有來源，空等級集合代表不顯示任何等級，空搜尋代表沒有搜尋條件。
來源 ID 使用 ordinal identity，搜尋使用 ordinal 不分大小寫比較。

`LogStore` 是事件唯一 owner。
`LogSnapshot` 是不可變、帶 content lease 的版本快照。
Store 建立的每個 `LogEntry` 都維持 `EntryId` 等於 `Sequence`；selection 與 projection membership 使用此不變量。
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
宿主接受背景投影前，須確認快照、篩選與 presentation 輸入仍相同。
Avalonia 控制器統一管理標題與工具列的 command 路由。
非 UI 層提供不可變輸入與投影機制；列表虛擬化、列互動與鍵盤路由由 K2 的列表切片提供。

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
發布是非同步的；`CaptureSnapshot()` 立即回傳最後已發布版本，不等待 writer。
已發布版本可能落後於已接受的寫入與 Clear。
`CaptureLatestAsync(cancellationToken)` 非同步涵蓋呼叫前已接受的全部 Add 與 Clear。
Admission fence 在 `_gate` 內由 next sequence 加 generation 推導；拒絕與空 batch 不推進此 fence。
取消 token 或 Dispose store 會取消尚未完成的 barrier，不阻塞執行緒。
Writer 失敗會以原始例外結束等待中的 barrier，將未套用的 ownership 排回 queue，並透過 diagnostic trace 回報。
Callback 若 Dispose store，writer 會在停止前完成剩餘清理。內容 Dispose 拋出例外時仍會繼續清理其他內容，每個內容只呼叫一次。
Dispose 後再呼叫 CaptureLatestAsync 會拋出 ObjectDisposedException。
在 writer 執行緒，包括 Changed 與 content Dispose callback，兩種 Capture 都立即回傳目前 publication。
Writer 不受通知 ready 狀態限制；`GetChangesSince(version)` 與 `IsCurrent` 也不等待，直接讀取已發布狀態。

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
Writer 只在 ready 且有 subscribers 要接收通知時，於發布、比較與清理完成後讀取 notification clock。
Clock 例外只保留尚未送出的 Changed，等下一次明確喚醒（如 Add、Clear 或 SetReady(true)）再嘗試。
尚未發布工作的喚醒，以及失敗的嘗試期間或之後收到的喚醒，會合併為下一個 writer turn 的通知請求。
Clock 例外本身不會觸發重試或重新入列資料；publication 與 pending capacity 釋放仍會完成。
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

## 呈現契約

遵循 Console redesign v3 (#82) 的「v3 更新」、「由 app 注入／SourceRegistry」、
「增量連結解析」與「去重與篩選的投影」。沒有 UI、產品型別、resource lookup、
thread culture 讀取、本地時區查詢或檔案系統存取。Store admission、ownership、rejection、
dedupe 與 scanner grammar 維持不變。

### App 注入契約

App 以 `ConsoleProjectionOptions.SourceRegistry` 注入 immutable `ConsoleSource` 陣列。
每筆包含穩定 ordinal `SourceId`、`DisplayName` 與遞增 `DisplayOrder`。
ID 必須唯一；null entry、ID、name 或未初始化陣列皆無效。空 ID 與既有 store 相容。
相同 order 保留 registry 輸入順序。`ConsoleProjection.Sources` 是顯示順序的唯一輸出：
先列所有宣告來源，再依保留事件首次出現的 Sequence 列未知來源，未知來源以 ID 為名稱，display order 為 `int.MaxValue`。
`SourceCounts` 為每個宣告來源先填零，仍只套 level filter 並計 raw events。
篩選與搜尋繼續使用 ID，不將 count 存入來源 metadata，也不在每列重複存 name。
即使 snapshot version 相同，替換 registry 也會改變下一次 projection。
宿主的 stale-result check 必須納入 presentation／registry 輸入替換。

新增 `Project(snapshot, filter, viewState, options)`，原有三參數入口仍使用 invariant culture、
核准的 `Console.Timestamp.Ago` 預設 template `"{0} s ago"` 與 UTC。
App 傳入已解析的 template 與明確 `CultureInfo`，projection 期間不得修改 culture。
純函式 `ConsoleTimeFormatter.Format(timestamp, timeBase, mode, relativeTimeTemplate, culture, absoluteTimeZone)`
是唯一時間格式計算路徑。Placeholder 0 接收依 culture 以 `"0.0"` 格式化的秒數，例如 `2.3` 或 `2,3`。範本只在 Relative 模式格式化：格式錯誤的範本在該模式下由投影拋出 `FormatException`，Absolute 與 Hidden 模式則忽略。
暫停仍以 `PausedAt` 為基準，後續 capture 或 presentation 變更不會推進相對時間。
Hidden 維持空字串；absolute 維持 invariant `HH:mm:ss.fff`，以明確 `TimeZoneInfo` 轉換。
App 可傳自己選擇的本地時區或 UTC；Core 不自行取得本地時區。
Copy 與 stream export 維持 store 的 UTC occurrence timestamp，不受畫面選項影響。

### 投影列連結

新增 `ConsoleLinkCache.GetLinks(snapshot, row)`，snapshot 是產生該列的版本。
Caller 直接傳 `ConsoleRow`，不需尋找 raw entry。掃描使用 projection 既有 lease 的 segmented text 與 spans。
去重列代表內容來自最新保留成員，原始成員被 ring 淘汰後仍可使用；沒有新 owner 或全文複本。
Entry／group ID 在 cache key 分開，兩個 overload 共用 scanner、index、lock、Synchronize 與三個 retention budgets。
Search 或來源改名不會失效。每次接受 snapshot 都要呼叫 `Synchronize`，包含 Clear。
舊列仍可透過 lease 掃描自己的內容，但 stale snapshot 或 representative 不可填入目前 cache。
Representative 改變時，即使 text version 相同也失效，因為 supplied spans 不在 dedupe key 中。
超出 cache 額度仍完整回傳結果，但不快取。

### 首行 metadata

`ConsoleRow.GetFirstLine(maxCharacters = 1024)` 呼叫純函式 `ConsoleFirstLine.Read(content, maxCharacters)`。
按需回傳 `Text` 與 `HasMoreContent`，不在列上儲存 newline 或 truncation flag。
CR、LF、CRLF 都結束首行且不進入 preview。Cap 使用 UTF-16 字元，允許 0 至 4,096，
不切開合法 surrogate pair。最多讀 cap + 1 字元，單次最多 1,024，只有有界 prefix 建立字串。
`HasMoreContent` 表示 preview 省略任何原始內容，包含結尾換行。
因畫面寬度造成的截斷仍由 UI 計算。

### 假設

設計未指定的細節採以下假設：宣告 order 相同時依輸入順序；未知來源的「首次出現」限於
retained snapshot，不在 store 新存歷史 registry；preview hard cap 為 4,096；被省略的結尾換行
也算 more content。群組維持最新保留成員為 representative，更換時失效以尊重不同 structured spans。
Row link access 伴隨對應 snapshot，使用既有 live-cache generation／version fence。

### 新公開成員與原因

| 型別 | 新公開成員與原因 |
| --- | --- |
| `ConsoleSource` | Constructor `(SourceId, DisplayName, DisplayOrder = 0)`、`SourceId`、`DisplayName`、`DisplayOrder`、positional `Deconstruct`：app 來源 metadata，不依賴產品型別。 |
| `ConsoleProjectionOptions` | 無參數 constructor、`SourceRegistry`、`RelativeTimeTemplate`、`Culture`、`AbsoluteTimeZone`：明確的 app presentation 輸入。 |
| `ConsoleProjection` | `Sources`：來源顯示名稱與順序的單一輸出。 |
| `ConsoleProjector` | `Project(snapshot, filter, viewState, options)` overload：注入 registry 與時間呈現。 |
| `ConsoleLinkCache` | `GetLinks(snapshot, row)` overload：由投影列直接取 bounded cached links。 |
| `ConsoleTimeFormatter` | `Format(timestamp, timeBase, mode, relativeTimeTemplate, culture, absoluteTimeZone = null)`：唯一純時間格式計算路徑。 |
| `ConsoleFirstLine` | Constructor `(Text, HasMoreContent)`、`Text`、`HasMoreContent`、positional `Deconstruct`、`Read(content, maxCharacters = 1024)`：有界 derived preview。 |
| `ConsoleRow` | `GetFirstLine(maxCharacters = 1024)`：不儲存旗標的按需首行入口。 |

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
已知限制：含 separator 的成對引號敘述視為單一路徑，包括其中其他類型的巢狀引號。
`"Read/write error"` 與 `"could not open 'C:\My Docs\a.txt'"` 各產生涵蓋整段引號文字的檔案連結，而不是內層路徑。
`"unpaired load/save then 開啟"資料/a.txt"` 依 CJK 結尾引號規則產生引號內敘述路徑，以及後方未加引號的 `資料/a.txt` 路徑。
需要精確 target 的 app 應提供 structured spans。
結尾 separator 明確代表資料夾，即使帶有行欄 suffix 也一樣；沒有副檔名仍可能是檔案。
其他模糊的資料夾由 structured spans 指定。
沒有副檔名白名單，沒有存在性查詢，也不掃 repository。

引號 target 的起始引號須位於文字開頭、空白之後或非 name、CJK scalar 之後，右側須有可開始路徑的內容。
引號內容也可從 `~`、`%` 或 `$` 開始。前導 dots 後須接 name 字元或 separator；空白、單獨的 prose dot、只有 location 的標點或文字結尾都不會開始引號內容。
在候選內，前方為 CJK scalar 或 path separator、後方為 CJK 敘述的配對引號會關閉 target，包含最後一個 component 為 CJK 或以 separator 結尾的資料夾路徑。
其他情況下，遇到後方有效的同類起始引號時，先放棄前一候選，再考慮結尾條件。
其他情況下，配對引號在空白、CJK 敘述、標點（包含 location suffix 起點）或文字結尾之前關閉候選。
放棄或未配對的候選會從原始起始引號的下一個字元恢復掃描。兩種入口共用這一份引號規則；未配對引號與 prose apostrophe 不會遮蔽後方 target。
沒有 separator 的 location target 必須有非首字元的點，且點後接字母。
Candidate 超過 4,096 個 UTF-16 字元（含 location suffix、不含外層引號）時，在建立字串前略過。
略過的引號候選會排除包含引號的完整範圍，內部尾段不會成為其他連結；後方獨立 target 仍可辨識。
引號候選先檢查長度上限，再檢查 overlap；URL 掃描已接受的內嵌 URL 仍保留為連結。
App 提供的 `LinkSpans` 在 Add preparation 驗證；AddBatch 在 admission 前驗證整個 batch。
Default array、null span 或 target、無效 range 與 overlap 會拋出 ArgumentException，不轉移 ownership。
Structured span 驗證在起點已排序時以線性時間比較相鄰區間；未排序時排序 index 副本，時間為 O(n log n)。

App spans 完全取代推導，明確的空 spans 也同樣優先。
`ConsoleLinkIndex` 驗證 ranges 並提供 binary hit test。
`ConsoleLinkCache` key 包含可區分的 EntryId／GroupId 與 text revision，不含搜尋。
群組結果另外綁定最新保留的 representative，避免同文字版本、不同 app spans 的成員誤用舊結果。
`Synchronize` 是唯一語意失效點，每次接受快照都要呼叫，包含 Clear。
舊快照不能重新填入 live cache。
掃描在 cache 鎖外執行，發布前再次驗證 snapshot generation、version、live membership 與 text revision。
Cache 使用固定 1,024 字元的讀取 buffer 直接掃描 segmented content，候選 offset 可跨越讀取邊界。
只將確認的 link targets 建立成字串，不將完整一般訊息 materialize。
Entry 數、span 數與 target 字元都有獨立 retention limit。
過大的結果仍完整回傳，但不快取。

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
匯出與複製前，即使 console 已收合，宿主仍須 await `CaptureLatestAsync(cancellationToken)`，再投影該固定版本。
Formatter 接受既有 projection，不自行擷取或更新 store 狀態。

## Avalonia 控制器與工具列

`Nvt.Core.Avalonia.LogConsole.ConsoleController` 是不可變篩選條件與檢視狀態的唯一寫入者。
在 Core 已註冊的 `UiThread` 建立，傳入 app 擁有的 `LogStore`、不可變的
`ConsoleSource` 目錄及選用的 `ConsoleProjectionOptions`。目錄放在
`Options.SourceRegistry`；時間模式仍由 `Filter.TimeMode` 保存。時間模板、文化設定與
絕對時間的時區都明確注入，使用期間不可修改傳入的文化設定。

控制器先訂閱再擷取啟動快照，並啟用 store 通知。任意執行緒的通知合併成一個待執行的
UI 工作；每次更新擷取有 lease 的快照，只呼叫一次 `ConsoleProjector.Project`。
重映射原始事件選取、展開身分及去重切換時的暫停順序，再透過 `INotifyPropertyChanged`
發布 `Projection`。所有統計直接取自投影。下一個 Background 優先序的 dispatcher 回合，
在 binding 與排版採用新投影後釋放舊投影；持有內容以目前投影與一個待釋放投影為上限。
發布前以 `LogStore.IsCurrent` 驗證快照 generation，因此 Clear 的 admission fence 也會拒絕
writer reset 尚未發布時已排隊的 UI 更新。Dispose 立即停止目前的通知序列，等執行中的
callback 返回後才釋放 lease；callback 內重入 dispatcher 也不會提早釋放借用的投影。

列表借用 `Projection`，透過 `RequestViewState(state)` 回報不可變的意圖，不直接修改
控制器輸入，也不 Dispose 借用的投影。`Pause(rowId, textOffset, pixelOffset)` 保存閱讀
錨點，並透過新的非阻塞快照讀取 store 注入的時鐘，明確保存 Pause 當下的時間；
`Resume()` 就是跳至最新的意圖。收合只改檢視狀態，保留跟隨模式。宿主離開頁面時
呼叫 `Dispose()`，解除訂閱、取消排隊工作並釋放兩份投影；重複呼叫安全。Store 仍由 app
擁有。控制器在建構時對整個 store 呼叫 `SetReady(true)`；`Dispose()` 不會還原先前
的就緒狀態，其他訂閱者也會受到影響。宿主須納入這個持續生效的 store 通知狀態。

工具列使用 `ToggleLevelCommand`（`LogLevel` 參數）、`ToggleOnlyMatchesCommand`、
`ToggleDedupeCommand`、`SetTimeModeCommand`（`ConsoleTimeMode` 參數）、`ClearCommand`
及 `ResetFiltersCommand`。`SetSelectedSources(ids)` 以 ordinal ID 選取，空集合代表所有
來源。來源選單的 All sources 會勾選每個來源，也包含未來新增的來源。點擊已勾選來源，
會從目前目錄排除該來源；後續點擊切換明確選取集合的成員。最後一個勾選來源不可取消，
以保留空集合代表所有來源的既有契約；All sources 恢復不限來源的狀態。Detach 或替換
Controller 時關閉並清空來源選單，釋放 binding，讓保留的舊項目 command 失效。
來源選單開啟期間，投影目錄變更會透過同一個 builder 原地重建項目，保持 popup 開啟。
View 直接以項目和投影比較來源成員，不另存來源目錄。不存在的來源 ID 顯示未勾選與零筆，
名稱以 ID 作為 fallback。
`SetSearchText(text)` 將純空白視為沒有搜尋條件。重設恢復預設篩選與去重，保留時間
呈現及閱讀狀態。所有意圖與 Dispose 都必須在 UI 執行緒執行。匯出旗標由 `ExportOptions`
保存，使用 `ToggleExportTimeCommand` 與 `ToggleExportLevelCommand` 切換。

`ConsoleHeader`、`ConsoleToolbar`、`ConsoleEmptyState` 透過 `Controller` styled property
取得控制器。比照其他 Core 樣式入口載入 `LogConsole/LogConsoleStyles.axaml`；控制項本身
會區域載入 `LogConsole/ConsoleControlStyles.axaml`。宿主提供 Core theme tokens 與 Fonts roles，寬度至少維持 640 DIP。核准的 B
配置在寬版把搜尋放在標題旁，等級與來源放在篩選列。960 DIP 以下含邊界時，搜尋與
Only matches 移到篩選區第二行；Display 收納時間與去重，More 收納匯出與清除。標題高
48 DIP；篩選區寬版高 48 DIP、窄版高 88 DIP。休止動作沒有底色或外框，等級按鈕使用
語意圖示，Core tooltip 顯示完整原始筆數。共用圖示校正依渲染尺度更新，detach 時解除
訂閱。只有 `Projection.IsEmpty` 時顯示空狀態。若 `Projection.EventCount == 0`，使用尚無事件
的文案並隱藏 Reset filters；篩選後沒有符合項目時，顯示本地化篩選摘要與 Reset filters。
Console 動作按鈕與選單項目以區域 DynamicResource 樣式，直接將字型 family、size、weight
綁定 Core Body role，包含中文來源名稱；其他控制項保留既有字型角色。

標題的 `Title` 預設為 Console。App 綁定 `CopySelectedCommand`、`CopyVisibleCommand`、
`SaveLogCommand`；選單提供獨立的 Include time 與 Include level。標題選單的 binding
直接跟隨標題的目前 Controller，避免 popup 關閉時中斷正在執行的 command。
Clipboard、檔案 adapter、
列導覽與列表渲染由宿主的互動與列表層負責。

| Avalonia 型別 | 公開成員 |
| --- | --- |
| `ConsoleController` | 建構子 `(store, sources, options = null)`、`PropertyChanged`、`Filter`、`ViewState`、`Options`、`Projection`、`ExportOptions`、`RefreshError`、`ToggleLevelCommand`、`ToggleOnlyMatchesCommand`、`ToggleDedupeCommand`、`SetTimeModeCommand`、`ClearCommand`、`ResetFiltersCommand`、`ToggleExportTimeCommand`、`ToggleExportLevelCommand`、`SetSelectedSources`、`SetSearchText`、`RequestViewState`、`Pause`、`Resume`、`Dispose`。 |
| `ConsoleHeader` | 建構子、自動產生的 `InitializeComponent(loadXaml = true)`、`Controller` / `ControllerProperty`、`Title` / `TitleProperty`、`CopySelectedCommand` / `CopySelectedCommandProperty`、`CopyVisibleCommand` / `CopyVisibleCommandProperty`、`SaveLogCommand` / `SaveLogCommandProperty`。 |
| `ConsoleToolbar` | 建構子、自動產生的 `InitializeComponent(loadXaml = true)`、`Controller` / `ControllerProperty`。 |
| `ConsoleEmptyState` | 建構子、自動產生的 `InitializeComponent(loadXaml = true)`、`Controller` / `ControllerProperty`。 |


建構子會驗證 `RelativeTimeTemplate`，格式錯誤時以指向 `options` 的 `ArgumentException`
拒絕輸入，並保留原本的 `FormatException` 作為原因。更新失敗時保留先前投影，透過唯讀
`RefreshError` 發布例外；下一次成功接受的更新會清除此值。失敗的投影會釋放新取得的
lease。輸入通知之前就會排定更新，即使 observer 丟出例外仍會更新。
控制器 Dispose 後，搜尋輸入與清除搜尋動作不再呼叫控制器。
由 `SetFilter`（包含搜尋、來源與 command 輸入）及 `RequestViewState` 發出的通知，
會將訂閱者例外同步傳回呼叫者。
成功接受的更新先完成所有狀態變更，再依序通知 `ViewState`、`Projection`，以及有變更的
`RefreshError`。更新通知會收集每個訂閱者的例外並完成整個通知序列，再將保留堆疊的重新
拋出動作排入 UI dispatcher，進入 dispatcher 的未處理例外路徑。單一例外直接重新拋出；
多個例外合併成一個 `AggregateException`。只有嘗試替換通知後才會釋放舊投影。
失敗通知的 observer 丟出例外時，先前投影仍保留，後續更新也仍可執行。

### 資源 keys

`LogConsole/ConsoleResources.axaml` 定義英文預設值與結構尺寸，合併於
`LogConsoleStyles.axaml`。控制項在區域載入不含預設資源的控制樣式；動態資源 binding
僅在宿主未提供 key 時，才使用同一份字典的預設值。宿主可在 application、window 或
console 祖先的 resources 覆寫 `Nvt.Console.*`，也能在執行期間替換。
宿主範本發生 `FormatException` 時，會改用內建英文範本。
`MinimumWidth` 與 `NarrowBreakpoint` 是所有 console 區塊共用的唯一定義；
組合高度會跟隨既有控制高度與 spacing 的動態 tokens。

時間模式與等級標籤透過 keys 對應。控制器只提供篩選資料，`FilterSummary` 不再是公開 API；
檢視從資源模板推導摘要。`Count` 接受標籤與原始筆數，`Sources.Selected` 與 `Dedupe.Count`
接受筆數，`Empty.NoMatches` 接受篩選摘要。`Filter.Search` 接受搜尋原文與搜尋模式標籤，
`Filter.Summary` 接受等級、來源、搜尋後綴與去重後綴；請保留有效的 composite placeholders。
`Title` 是預設標題，宿主明確指定的標題優先。獨立注入的
`ConsoleProjectionOptions.RelativeTimeTemplate` 仍負責格式化事件時間。

| Key | 英文預設值／DIP 尺寸 |
| --- | --- |
| `Nvt.Console.MinimumWidth` | `640` |
| `Nvt.Console.NarrowBreakpoint` | `960` |
| `Nvt.Console.Title` | `Console` |
| `Nvt.Console.Search.Placeholder` | `Search messages, sources or paths` |
| `Nvt.Console.Search.Name` | `Search console` |
| `Nvt.Console.Search.Clear` | `Clear search` |
| `Nvt.Console.OnlyMatches` | `Only matches` |
| `Nvt.Console.Export` | `Export` |
| `Nvt.Console.Export.Name` | `Export console` |
| `Nvt.Console.Display` | `Display` |
| `Nvt.Console.Display.Name` | `Display console` |
| `Nvt.Console.More` | `More` |
| `Nvt.Console.More.Name` | `More console actions` |
| `Nvt.Console.Time.Name` | `Time display` |
| `Nvt.Console.Time.Absolute` | `Absolute time` |
| `Nvt.Console.Time.Relative` | `Relative time` |
| `Nvt.Console.Time.Hidden` | `Hidden time` |
| `Nvt.Console.CopySelected` | `Copy selected` |
| `Nvt.Console.CopyVisible` | `Copy visible rows` |
| `Nvt.Console.SaveLog` | `Save as .log` |
| `Nvt.Console.IncludeTime` | `Include time` |
| `Nvt.Console.IncludeLevel` | `Include level` |
| `Nvt.Console.Clear` | `Clear console` |
| `Nvt.Console.Sources.Name` | `Select sources` |
| `Nvt.Console.Sources.All` | `All sources` |
| `Nvt.Console.Sources.Selected` | `Sources ({0})` |
| `Nvt.Console.Dedupe` | `Dedupe` |
| `Nvt.Console.Dedupe.Count` | `Dedupe ×{0}` |
| `Nvt.Console.Count` | `{0} · {1}` |
| `Nvt.Console.Empty.NoEvents` | `No events yet` |
| `Nvt.Console.Empty.NoMatches` | `No matching events · {0}` |
| `Nvt.Console.ResetFilters` | `Reset filters` |
| `Nvt.Console.Filter.NoLevels` | `no levels` |
| `Nvt.Console.Filter.Search` | `; search ‘{0}’ ({1})` |
| `Nvt.Console.Filter.MatchesOnly` | `matches only` |
| `Nvt.Console.Filter.Highlight` | `highlight` |
| `Nvt.Console.Filter.Dedupe` | `; dedupe` |
| `Nvt.Console.Filter.Separator` | `, ` |
| `Nvt.Console.Filter.Summary` | `{0}; {1}{2}{3}` |
| `Nvt.Console.Level.Trace` | `Trace` |
| `Nvt.Console.Level.Debug` | `Debug` |
| `Nvt.Console.Level.Info` | `Info` |
| `Nvt.Console.Level.Warn` | `Warn` |
| `Nvt.Console.Level.Error` | `Error` |
| `Nvt.Console.Level.Fatal` | `Fatal` |

## 公開 API

完整 type 與 member 表見 [英文 Public API](LogConsole.md#public-api)。
主要入口是 `LogStore`、`ConsoleProjector`、`ConsoleViewState`、`ConsoleLinkScanner`、`ConsoleLinkCache` 與 `ConsoleExportFormatter`。
資料與輸出型別包含 `LogEntry`、`LogSnapshot`、`LogChangeSet`、`ConsoleFilter`、`ConsoleProjection`、`ConsoleRow` 與穩定 IDs。
App content contract 是 `ILogTextContent`。
`ConsoleRow.TextContent` 取代完整 Message 字串；`ConsoleProjection` 實作 IDisposable。
`ConsoleViewState.Selection` 與 `FormatSelection` 使用 `ImmutableHashSet<long>` 原始 EntryIds。
`ConsoleFollow` 改為 private base constructor 的 immutable class 階層，巢狀兩種狀態皆為 sealed。

## 同步保護與重用機制

`LogStore._gate` 保護 admission queues、尚未發布的 writer ownership、cleanup queues、RejectedCount、next sequence、generation、ready、Dispose 狀態、subscriptions 與 published reference。
Ring、group index、history、version、EvictedCount 與 notification cursor 只有 writer 可修改。
Writer 排程使用一個 interlocked flag，published state 的 reference count 也使用 Interlocked。
`ContentOwner._contentGate` 只保護 reference lifetime，active read 先 pin 再於鎖外呼叫 app。
快照以 Interlocked 確保只釋放一次，投影的每個 lease 也只釋放一次。
`ConsoleLinkCache._cacheGate` 保護 cache，以及包含 live entry／group revisions 和 revision stamp 的單一 immutable state；同步時整體替換。其他 model state 不可變。
Avalonia 控制器在 Core 已註冊的 UI 執行緒替換篩選與 view state，並管理 commands。
宿主與列表透過 `RequestViewState` 提交不可變意圖，不直接替換控制器狀態。

Clock 使用 BCL `TimeProvider`，測試重用 Core 的 `Time.DelegateTimeProvider`。
實作 baseline 的 Lifecycle 只有 CoalescedRefresh 與 UndoService，沒有可重用的 generation helper。
CoalescedRefresh 不能將 ready 與 pending work 原子整合進 store 的同一鎖。
MessageCenter 與 Persistence 的 generation 各自綁定 modal 與 save coordinator，不能代替 store generation。
本模組只有一個 internal `ConsoleGeneration`，統一遞增與 stale-work checks，由 store lock 保護。

## 驗證與採用

回歸涵蓋 admission 上限、整批接受或拒絕、reentrant callback、generation reset、snapshot leases、
writer failure recovery、dispatcher 上不等待的讀取、可取消的 async barrier、隔離的 callback 例外、notification clock recovery、跨讀取邊界的 Unicode 與引號規則、
target 長度上限、structured span 驗證，以及 async-only stream 匯出。搜尋每次 projection 共用一個 chunk buffer。
Deterministic scanner corpus 對每個 split 比對獨立預期 spans。
生成的 quote oracle 由文字組件（包含相鄰 CJK 敘述與後方引號）建立預期 links，在每個 storage 與 scanner read split 驗證兩種入口。
效能測試僅在 `NVT_CORE_PERF=1` 執行，不斷言時間上限。

```powershell
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build --no-restore
```

Avalonia 標題、工具列與空狀態的 accessibility、視覺證據與列表檢視已實作。
連結與選取的 pointer 互動、console keyboard commands，以及整個 console 的宿主 accessibility 與視覺驗證，仍由後續 UI 工作完成；
clipboard 與檔案 adapter 仍由 app 擁有。
宿主採用是另外的變更，路徑政策、開啟、clipboard 與 spill-store 整合仍屬於 app。

## 列表檢視

`Nvt.Core.Avalonia.LogConsole.ConsoleListView` 是可選用的樣板化列表控制項。
載入 `avares://Nvt.Core.Avalonia/LogConsole/ConsoleListStyles.axaml`、Core theme tokens 與字型角色，
並在宿主設定 `WithNvtCoreFonts()`。列表樣式組合既有按鈕、捲軸與圖示樣式，
不依賴 panel controller 或 app 型別。
列表載入 `ConsoleListGeometry.axaml` 提供結構尺寸，並沿用 Core 共用圖示尺寸、
間距與控制項高度 token。所有等級字形皆來自 `NvtIcons`。

| 成員 | 呼叫端契約 |
|---|---|
| `Projection` / `ProjectionProperty` | 借用 `ConsoleProjection`；呼叫端在替換前維持 leases 有效並負責 Dispose。 |
| `ViewState` / `ViewStateProperty` | 呼叫端持有的 immutable `ConsoleViewState`；列表不自行寫入展開或跟隨狀態。 |
| `TimeOptions` / `TimeOptionsProperty` | 明確的 `ConsoleProjectionOptions` culture、相對時間樣板與絕對時間時區。列表忽略 `SourceRegistry`；來源欄顯示 source ID。 |
| `TimeMode` / `TimeModeProperty` | `ConsoleTimeMode`，預設 Absolute；Hidden 移除整個時間欄。 |
| `ViewStateRequested` | 以 `EventHandler<ConsoleViewState>` 傳回要求替換的新狀態。 |
| `JumpToLatest()` | 要求 Resume；宿主的 Ctrl+End command 可呼叫此方法。 |

輸入設定與要求接受都在 UI thread 執行。呼叫端決定是否接受要求，
替換自己的狀態後再設定 `ViewState`。列表不呼叫 `ConsoleProjector`、
不訂閱 store，也不 Dispose 投影。外層 console 收合時，狀態仍由呼叫端保留。
設定投影時會同步將保留的 presenter 重新繫結；設定返回後，呼叫端即可立即 Dispose
先前投影，即使尚未 layout 或接著再次替換也一樣。

收合列使用 `ConsoleRow.GetFirstLine`，列高固定 20 DIP。
欄位為時間 104、等級 84、來源 120、訊息剩餘空間、重複次數 48、箭頭 24 DIP，
內容左右各留 16 DIP。等級同時顯示共用 Material Symbols 圖示與名稱。
長來源使用字元省略號，tooltip 保留完整 SourceId。
內容被省略或實際超出寬度才顯示箭頭；重複列才顯示次數。
展開列保留換行並折行，metadata 對齊首行。
訊息使用 Body，時間與來源使用 MonoCaption，次數使用 Numbers，圖示使用 Icon，
每個角色都使用 Family、Size、Weight。時間由 `ConsoleTimeFormatter` 依
`Projection.TimeBase` 格式化，不使用 timer 或隱含的本機時鐘。

使用者可見文字透過宿主資源解析，英文預設值放在 `ConsoleListGeometry.axaml`。
可在列表或資源祖先覆寫下列鍵以在地化。跳至最新與保留範圍通知使用 `TimeOptions.Culture`
格式化；`{0}` 是投影的新訊息數或淘汰數。新訊息或淘汰數恰為一則時使用各自的單數鍵，
零或其他數量使用複數鍵。資源變更會更新顯示文字。
每個列出的文字鍵在缺漏、型別不是字串、空白或複合格式無效（含不存在的參數）時，
都回退至內建英文預設值；文字覆寫不會從 measure 或資源事件處理常式拋出例外。
未知等級鍵回退至鍵的最後一段。等級文字不接受格式參數；字面大括號須寫成 `{{` 與 `}}`。

| 資源鍵 | 英文預設值 | 繁體中文覆寫範例 |
|---|---|---|
| `Nvt.Console.List.JumpToLatestOne` | `Jump to latest ({0} new message)` | `跳至最新（{0} 則新訊息）` |
| `Nvt.Console.List.JumpToLatestMany` | `Jump to latest ({0} new messages)` | `跳至最新（{0} 則新訊息）` |
| `Nvt.Console.List.RetentionOne` | `Retention changed · {0} message evicted` | `保留範圍已變更 · 已淘汰 {0} 則訊息` |
| `Nvt.Console.List.RetentionFormat` | `Retention changed · {0} messages evicted` | `保留範圍已變更 · 已淘汰 {0} 則訊息` |
| `Nvt.Console.List.Level.Trace` | `Trace` | `追蹤` |
| `Nvt.Console.List.Level.Debug` | `Debug` | `偵錯` |
| `Nvt.Console.List.Level.Info` | `Info` | `資訊` |
| `Nvt.Console.List.Level.Warn` | `Warn` | `警告` |
| `Nvt.Console.List.Level.Error` | `Error` | `錯誤` |
| `Nvt.Console.List.Level.Fatal` | `Fatal` | `嚴重` |

來源欄與 tooltip 顯示 `ConsoleRow.SourceId`。列表忽略 `TimeOptions.SourceRegistry`；
registry 的顯示名稱仍由投影或宿主處理。

固定的 item source 借用目前投影。以像素捲動的可回收 host 依 `ConsoleRowId`
保留已實現容器；套用投影時更新身分，不重設 source。
只實現 viewport 與少量 overscan。展開高度先估計，再由 measure 更新。
超大訊息以有界讀取分段，段落邊界使用實際折行位置。
只保存精簡 offset／height 索引，文字 layout 僅保留可見段落；
不逐行建立 visual，也不把完整訊息轉成單一字串。
列寬跟隨 logical viewport，水平捲動關閉。

Error／Fatal 使用既有 danger surface。訊息與來源搜尋命中使用既有 warning surface
與 strong warning text。繪製順序是搜尋底色、文字、命中前景；
後續底線層保留給連結互動。Theme 或 resource 改變會使顯示快取失效。
暫停閱讀時，重建量測高度會保留首個可見列及其 DIP 內位移。

使用者離開末尾時，以首個可見列、sequence、文字 offset 與 DIP offset 要求 Pause。
展開以同一個新狀態合併展開與暫停；pointer 移動超過 4 DIP 即取消啟用。
訊息欄與箭頭的命中區皆接受 pointer 啟用。
按下時擷取 pointer，因此移出列後再回到起點仍會取消啟用。
失去擷取時清除手勢；放開、取消與 detach 都釋放手勢的擷取。
暫停期間，使用者捲動造成 offset 改變且未抵達末尾時，才要求更新閱讀座標，
不改變暫停時間、generation、序號邊界與凍結的列順序。同一 offset 的重複通知、
viewport 改變、資源失效與列表自身的高度修正都不要求狀態。
只有捲動來源的要求會在 measure 期間延後，並合併為待處理的使用者意圖；量測結束後，
才依目前畫面位置與呼叫端狀態擷取要求。待送期間的投影更新、高度修正與寬度重排會保留閱讀位置；
viewport 夾限 offset 不會改變使用者的暫停／恢復意圖。送出前先完成 layout 與恢復，再取消尚未執行的恢復工作，
因此呼叫端延後接受時，先前排隊的恢復也不會推翻已發布的閱讀位置。
後續的展開切換或 `JumpToLatest()` 會取消該待送捲動要求，
並立即送出自身要求。高度修正與寬度重排會保留量測期間收到的使用者捲動，不恢復量測開始時的位置。
無關狀態變更若沿用同一個 Follow instance，會保留即時閱讀位置，即使呼叫端仍持有舊錨點也一樣。
已送出的錨點只保留有界的近期歷史。接受仍在歷史內的錨點時，會保留即時座標，
並只移除該錨點及之前的項目，因此後續排隊要求可依序接受，而不重播舊位置。
以歷史外的不同 Follow 替換時，會清除歷史、取消待送捲動意圖並恢復呼叫端明確指定的位置。
重新 attach 使用已接受的錨點。
暫停時先保存目前錨點，套用投影後於 layout 恢復；合併的投影替換與寬度改變
沿用待恢復錨點，直到恢復完成或使用者捲動替換它。
程式捲動使用可巢狀的事件抑制；有排程工作時，仍接受使用者離開末尾的 Pause 要求。
寬度改變會在重新折行後映射文字 offset。已淘汰錨點使用 `ResolvedAnchorId`
並要求更新為後繼列。待送捲動期間，Following 投影未提供後繼列時，即時錨點保留 sequence 與列順序，
以第一個仍存在的後繼列恢復，依序退回 sequence 與最後保留列；保留範圍通知顯示 `EvictedCount`。
右下角跳至最新按鈕顯示 `NewSincePauseCount`。
抵達末尾、按下按鈕或呼叫 `JumpToLatest()` 都要求 Resume。
Attach 建立 handlers 與可取消、合併的 dispatcher 工作；
detach 釋放 handlers、借用內容及顯示快取。
Attachment 生命週期可重複 Dispose；所有 host 狀態只在 UI thread 存取。

標頭、工具列、空狀態、連結、選取、複製／匯出、右鍵選單與 console 快捷鍵
由宿主及其他控制項組合。
