# Core console 設計提案

2026-10-06。適用 NFC、NFH、NFU 的 Core 1.0.0 共用 console。

## 結論

採用緊湊的事件列表，讓訊息成為畫面的主體。
時間、等級與來源固定對齊，長訊息只在需要時展開。
篩選、搜尋與去重放在同一個工作面，閱讀舊訊息時不強迫跳到最新。
沿用 Core 的配色、32 px 按鈕、捲軸、字型與 Focus B，本案不新增顏色 token。
本次交付是可審閱的視覺提案；21 項問題的修復契約是下一階段 Core 實作的必要條件。

先看 [v2 與 v3 比較](https://raw.githubusercontent.com/Dennis40816/nvt_fw_core/f5f69b3277d89f7dcc8a85024ff6bb7bcc98550a/pr-assets/console-design/v3/compare-v3-zh.png)。再看 [Light 主畫面](https://raw.githubusercontent.com/Dennis40816/nvt_fw_core/f5f69b3277d89f7dcc8a85024ff6bb7bcc98550a/pr-assets/console-design/v3/proposal-light-v3-zh.png)、[Dark 主畫面](https://raw.githubusercontent.com/Dennis40816/nvt_fw_core/f5f69b3277d89f7dcc8a85024ff6bb7bcc98550a/pr-assets/console-design/v3/proposal-dark-v3-zh.png)、[Light 狀態圖](https://raw.githubusercontent.com/Dennis40816/nvt_fw_core/f5f69b3277d89f7dcc8a85024ff6bb7bcc98550a/pr-assets/console-design/v3/states-light-v3-zh.png) 與 [Dark 狀態圖](https://raw.githubusercontent.com/Dennis40816/nvt_fw_core/f5f69b3277d89f7dcc8a85024ff6bb7bcc98550a/pr-assets/console-design/v3/states-dark-v3-zh.png)。灰階檢查看 [灰階圖](https://raw.githubusercontent.com/Dennis40816/nvt_fw_core/f5f69b3277d89f7dcc8a85024ff6bb7bcc98550a/pr-assets/console-design/v3/gray-light-v3-en.png)。
英文版把檔名的 `-zh` 換成 `-en`，灰階圖只有英文版。
下面第一節「v3 更新」是目前的依據。本文以下各節的 header、等級篩選與去重外觀，若與它不同，以它為準。
舊版 v2 圖保留供對照：[新舊比較](https://raw.githubusercontent.com/Dennis40816/nvt_fw_core/650491b6271c5e2e991ef7f239baa4b6be706757/pr-assets/console-design/compare-v2.png)、[Dark 主畫面](https://raw.githubusercontent.com/Dennis40816/nvt_fw_core/650491b6271c5e2e991ef7f239baa4b6be706757/pr-assets/console-design/proposal-dark-v2.png)、[Light 主畫面](https://raw.githubusercontent.com/Dennis40816/nvt_fw_core/650491b6271c5e2e991ef7f239baa4b6be706757/pr-assets/console-design/proposal-light-v2.png)。

## v3 更新（2026-10-08，以本節為準）

結論：v3 把外觀定案了。header 改成 B「搜尋優先」，等級篩選改成純圖示，「只看符合」與「去重」改成淡色底切換。版面、欄位與列表內容和 v2 相同。

### 擁有者決定（原話）

- 2026-10-08 08:5x：header 選 **B 搜尋優先**。
- 2026-10-08 08:5x：「我覺得等級用 icon 點就好目前文字按鈕太重」。
- 2026-10-08 09:3x：「按鈕好像有些太大了 太笨重」。
- 2026-10-08 09:4x：「我是覺得 icon 直接不要外框」。
- 2026-10-08 10:1x：「怎麼感覺 warning 沒有對齊?」，並選「開啟實心、關閉空心 (Recommended)」。
- 2026-10-08 11:1x：看完對照圖後選「可以，做 v3 (Recommended)」。

### v2 → v3

| v2 | v3 |
|---|---|
| 等級在篩選列，搜尋在等級後面 | 第一列放標題與搜尋，第二列放等級、來源與去重 |
| 六個文字膠囊，旁邊顯示筆數 | 六個純圖示，沒有底色也沒有外框，選取時也一樣 |
| 選取用底色表示 | 開啟：實心圖示加等級色。關閉：空心圖示加灰色 |
| 筆數顯示在按鈕旁 | 筆數只在提示，例如 `Error · 1`；零筆仍可切換 |
| 等級按鈕約 32 px 高的膠囊 | 點擊範圍 32 × 32 DIP，圖示 18 DIP，六個圖示間距 4 DIP，成一組 |
| 滑過、按下用同一種底色 | 滑過顯示極淡底，按下稍深；鍵盤焦點才出現焦點環 |
| 「只看符合」與「去重」選取時是外框膠囊 | 淡色底加藍字；關閉時透明，滑過才有淡底 |
| 時間格式、欄位各有按鈕 | 收進「顯示」選單 |
| 匯出是獨立動作 | 匯出選單顯示複製、儲存與包含欄位 |

### 功能位置

| 功能 | v3 位置 |
|---|---|
| 搜尋與「只看符合」 | header 第一列 |
| 等級篩選 | header 第二列，名稱與筆數在提示 |
| 來源選單與「去重 ×N」 | header 第二列，等級右側 |
| 絕對、相對、隱藏時間 | 顯示 → 時間格式 |
| 時間、等級、來源、訊息欄位的可見性 | 顯示 → 欄位 |
| 複製選取、複製可見列、儲存為 .log、包含時間與等級 | 匯出選單 |
| 跟隨最新 | 狀態列 |
| 收合 | header 第一列最右側 |

### 等級圖示規則

- 每個等級的圖形不同，灰階下仍分得出來。Trace、Debug、Info、Warn、Error、Fatal 的圖示不變。
- 開啟與關閉使用同一個字形的實心（FILL 1）與空心（FILL 0）版本，可見範圍相同，切換時圖示不位移。
- 六個圖示依可見筆畫置中，垂直中心差：Light 0 px、Dark 0.5 px，測試門檻 0.5 px。Warn 三角形底邊對齊整數像素。
- 灰階下，六個等級開與關的亮度差都大於 0.025，測試鎖定這個門檻。
- 圖示對背景至少 3:1，實測最低約 5.1:1。「只看符合」與「去重」的淡色底切換，文字對比至少 4.5:1。
- 主畫面範例是 Debug 關閉、其餘開啟；去重開啟、「只看符合」關閉。

### 這次新增或變動的樣式

- 不新增顏色 token。等級色沿用 `NfcInfoTextBrush`、`NfcWarningTextStrongBrush`、`NfcDangerTextStrongBrush`、`NfcTextMutedBrush` 等既有資源。
- Error 與 Fatal 的 ToggleButton 危險色沿用已合併的 FluentPill 修正（Core #120）。
- 淡色底切換（暫名 `toggleSoft`）先放在測試專案的樣式，之後另開一張小 PR 搬進 Core，全部使用 token，沒有寫死的顏色或圓角。
- 實作時，Console 的外觀要和這幾張 v3 圖一致。實作 PR 附「示意圖與實作並排」截圖，差異逐項列出，不同的地方要擁有者同意。

### v3 沒有重畫的部分

- 960 DIP 以下的窄版配置沒有重畫。header 改成兩列後，窄版要怎麼收，留到實作時依 v3 重新檢討。
- 本文以下各節講到 header、等級膠囊、等級筆數與選取外觀的地方，已被本節取代。

## 擁有者決定（2026-10-07 23:5x）

擁有者接受本提案的版面與功能，外觀之後另定。
擁有者先選了「先定版面，外觀跟 ToggleButton (Recommended)」，接著說「接受這個提案」。

定案的部分如下，Core 實作照這些做：
- 欄位：時間到毫秒、等級圖示、來源、訊息。
- 等級篩選與各等級筆數。
- 搜尋與「只看符合」。
- 重複訊息合併成 ×N。
- 長訊息展開。
- 跟隨最新。
- 狀態列。
- 等級圖示置中。
- 下文「21 項問題如何在新設計中解決」的修復做法。

當時尚未定案的部分如下，2026-10-08 已由上面的「v3 更新」定案：
- 工具列按鈕、等級篩選膠囊、顏色與圓角。
- 實作時，這些樣式集中在 token 與樣式檔，之後可以整組更換。

## 版面與欄位（欄寬規則、列高、字型、截斷與展開）

畫面依序是 48 px 標題列、48 px 篩選列、24 px 欄位標題、事件列表與 20 px 狀態列。
1200 × 420 的主畫面可完整顯示 14 列。
內容左右各保留 16 px，捲軸使用 Core 的 14 px 通道與 6 px thumb。
列表不使用斑馬紋或逐列邊框。
只有錯誤、選取、搜尋命中與鍵盤焦點需要額外視覺訊號。

| 欄位 | 預設寬度 | 規則 |
|---|---:|---|
| 時間 | 104 DIP | 足夠容納 `HH:mm:ss.fff`；相對時間使用同一欄；選擇隱藏時整欄移除 |
| 等級 | 84 DIP | 16 px 圖示與文字名稱並列，不能只用顏色表達 |
| 來源 | 120 DIP | `runtime-query` 可完整顯示；更長名稱省略並提供完整提示 |
| 訊息 | 剩餘寬度 | 優先獲得額外空間；不為短訊息強制加寬 metadata |
| 重複次數 | 48 DIP | 只有重複時顯示 `×N`，位置固定在列末 |
| 展開提示 | 24 DIP | 有換行或實際被截斷時顯示箭頭 |

所有尺寸都是 DIP，不能再次乘上 DPI。
預設列高與訊息行高都是 20 px。
展開列依內容增加高度，時間、來源與等級仍對齊首行。
展開保留換行並折行，沒有水平捲動。
收合只顯示第一行，超出寬度使用字元省略號。
完整內容不因截斷或收合而改變。
複製與匯出一律使用完整內容。

| 用途 | Core 字型角色 |
|---|---|
| Console 標題 | Heading：Inter 16 / 600 |
| 訊息、搜尋與選單 | Body：Inter 13 / 400 |
| 已選工具列動作 | BodyStrong：Inter 13 / 600 |
| 時間與來源 | MonoCaption：Cascadia Mono 11 / 400 |
| 路徑、URL 與技術值 | Mono：Cascadia Mono 13 / 400 |
| 次數與統計數字 | Numbers：Cascadia Mono 13 / 400 |
| 欄位標題 | CaptionStrong：Inter 11 / 600 |
| 次要狀態 | Caption：Inter 11 / 400 |
| 圖示 | Icon：Material Symbols Outlined 16 / 400 |
| 比較圖的大標籤 | Title：Inter 24 / 600 |

每個角色同時套用 Family、Size 與 Weight 三個資源。
繁體中文缺字由 `WithNvtCoreFonts()` 指向 Noto Sans TC。
一般中文使用 Regular 400，強調角色依既定規則使用 Bold 700。
不新增 Windows 系統字型到 Core。

960 DIP 以下，標題列的時間與去重收進「顯示」，匯出與清除收進「更多」。
等級切換維持第一行，搜尋與「只看符合」移到第二行。
這時篩選區高 88 px，控制項仍高 32 px。
本案支援的最小 console 寬度是 640 DIP，宿主須維持這個最小配置寬度。

## 色彩與等級（每個等級的圖示、顏色 token、錯誤列底色；新增 token 的 Light／Dark 值與對比）

主畫面使用 `NfcSurfaceBrush`。
篩選列、欄位標題與狀態列使用 `NfcSurfaceSubtleBrush`。
訊息使用 `NfcTextBrush`，來源使用 `NfcTextSecondaryBrush`，時間使用 `NfcTextMutedBrush`。
Error 與 Fatal 列使用淡的 `NfcDangerSurfaceBrush`，訊息仍維持一般文字色。
選取使用 `NfcSelectionSurfaceBrush`，並保留等級圖示與名稱。
Focus B 使用 `Nvt.Focus.RingBrush` 與 `Nvt.Focus.RingThickness`，外框 2 px，外側間隔 2 px。
鍵盤焦點不改變背景、選取狀態或文字色。

下表對比以 sRGB 相對亮度計算。
數值取常態、次要、app 背景、選取、錯誤與兩種 accent 淡底色中最差的一個組合。
這些組合的最低值都出現在選取底色。

| 等級 | Material 圖示 | 顏色 token | Light | Dark | 最低對比 Light / Dark |
|---|---|---|---|---|---|
| Trace | `more_horiz` | `NfcTextMutedBrush` | `#526176` | `#A1AEC2` | 5.399 / 6.512 |
| Debug | `bug_report` | `NfcTextSecondaryBrush` | `#475569` | `#CBD5E1` | 6.488 / 9.853 |
| Info | `info` | `NfcInfoTextBrush` | `#245B91` | `#8AC5F2` | 6.033 / 7.906 |
| Warn | `warning` | `NfcWarningTextBrush` | `#875400` | `#F5CE8A` | 5.451 / 9.807 |
| Error | `error` | `NfcDangerTextBrush` | `#A82035` | `#FFADB7` | 6.151 / 8.299 |
| Fatal | `cancel` | `NfcDangerTextStrongBrush` | `#861B2C` | `#FFD0D7` | 8.147 / 10.627 |

錯誤列底色的 Light / Dark 值是 `#FFF1F3` / `#2B171E`。
搜尋命中底色沿用 `NfcWarningSurfaceBrush`：`#FFFBEB` / `#2D2313`。
命中文字沿用 `NfcWarningTextStrongBrush`：`#70440B` / `#FFE9C4`。
搜尋命中對比為 8.034 / 13.005。
連結 hover 使用 `NfcAccentBrush`，最低對比為 5.034 / 5.746。
輸入邊界使用 `NfcBorderBrush`，最低對比為 3.438 / 3.680。
Focus B 最低對比為 4.228 / 5.572。
捲軸 thumb 對列表底色的對比為 4.559 / 5.184。
一般文字最低對比為 12.525 / 11.866。
metadata 與 placeholder 最低對比為 5.399 / 6.512。

新增 token：無。
等級是語意色，不另建六套 console 專用顏色。
文字維持至少 4.5:1，焦點、輸入邊界與捲軸維持至少 3:1。
休止連結的細底線使用 `NfcBorderSoftBrush`，它是輔助提示，連結文字本身仍滿足文字對比。

圖片使用 Core 預設 NFC accent。
NFH 與 NFU 實作時依 Theme.md 注入各自的七個 `NfcAccent*` 值。
按鈕使用 `ButtonStyles.axaml` 的 `actionGhost`、`actionNeutral`、`chipAction`、`actionIconButton` 與 `actionPrimary`。
按鈕圖示繼承所組合角色的顏色。
捲軸直接使用 `ScrollStyles.axaml` 與 viewport-bound 配對樣式。

## 等級圖示置中

> 2026-10-08 更新：header 的六個等級篩選圖示改用「可見筆畫置中」（見上面的「v3 更新」）。下面的校正表只適用於事件列裡的等級欄圖示。

Material Symbols 的上下留白不一致，對齊文字盒仍會讓 Debug 與 Warn 偏高。
列模板改用共用的字形與渲染尺度校正表，只移動圖示，保留原本列高與欄位。
100% 的 Debug、Warn 下移 1 DIP；150% 的 Debug 下移 1 DIP、Warn 下移 0.5 DIP。
這是每種字形的共用校正，虛擬列回收時不另設每列位移。

Dark 與 Light 的修正後數值相同。單位為實際輸出像素，負值表示圖示偏高。

| 等級 | 100% | 150% |
|---|---:|---:|
| Trace | −0.5 | 0.0 |
| Debug | −0.5 | +0.5 |
| Info | 0.0 | 0.0 |
| Warn | 0.0 | +0.5 |
| Error | −0.5 | 0.0 |
| Fatal | −0.5 | 0.0 |

六個等級在兩個主題與兩個尺度都符合 ±0.5 px。
前後數字與墨跡量測方法見 qa/icon-centering.md（量測檔在 NFH 工作資料夾）。
狀態圖末尾增加六列 4 倍放大圖，細線分別標出圖示與等級文字中心。
Core 正式實作必須保留這項像素墨跡測試。字型、DPI 或列模板改變時重新量測，其他 DPI 也須驗證。

## 互動（篩選、搜尋、去重、連結、右鍵選單、匯出、自動跟隨、鍵盤操作與快捷鍵、選取與複製）

- 等級切換是可複選的純圖示 ToggleButton，筆數只在提示顯示（例如 `Error · 1`），保留事件的原始筆數。
- 等級筆數套用來源條件，但不套用等級、文字搜尋或去重，避免數字互相消失。
- 零筆等級仍可操作，新事件進來後可立即顯示。
- 來源選單提供「所有來源」與 app 注入的來源名稱，可複選並顯示筆數。
- 合成資料的來源筆數是 app 4、dxf 8、export 5、runtime-query 4。
- 搜尋預設使用不分大小寫的字面文字，搜尋完整訊息與來源。
- 輸入搜尋後只顯示符合列，命中片段使用上述高亮。
- 「只看符合」關閉時恢復目前等級與來源條件下的全部列，高亮仍保留。
- 搜尋空白時等同沒有搜尋條件。
- 去重鍵是 SourceId、Level 與完整 Message，忽略時間，不跨來源或等級合併。
- 相同訊息即使中間插入其他事件也會合併，換行內容不同就不合併。
- 去重列使用最後一次發生時間，以最後序號排序，列末顯示 `×N`。
- 去重計數只包含目前保留事件，尾窗淘汰後同步扣減。
- 點訊息或箭頭切換展開，拖曳文字時不觸發展開。
- 休止連結保留一般文字色與淡底線，hover 改為 app accent。
- 只有 Ctrl+左鍵開啟連結，按下與放開必須是同一個 target，移動超過 4 DIP 就視為拖曳。
- 開啟動作不以 caret 或上一次按下的位置作為 fallback。
- 檔案 target 保留路徑、行與欄，範例是 `C:\Demo\Core\ExportWriter.cs(142,18)`。
- 資料夾路徑不依賴副檔名，Ctrl+左鍵直接開啟該資料夾。
- 中文、空白、UNC 與相對路徑由 scanner 及 app resolver 支援。
- 路徑尚未解析時可以複製原始路徑，解析與存在性查詢不能阻塞 UI。
- 檔案右鍵選單依序是「開啟檔案」、「開啟所在資料夾」、「複製路徑」與「複製選取內容」。
- 資料夾 target 的資料夾動作開啟該資料夾本身，檔案動作不適用。
- URL 選單顯示「開啟連結」與「複製連結」，不提供無效的檔案動作。
- URL 括號成對保留，外側中文標點不屬於 target。
- 匯出選單提供「複製選取」、「複製可見列」與「儲存為 .log」。
- 匯出選單提供獨立的「包含時間」與「包含等級」選項，預設兩者都包含。
- 「可見列」指篩選後的全部結果，包含捲動區外的列。
- 匯出保留來源、完整訊息、原始換行與去重的 `×N`，使用 UTF-8。
- 匯出從同一份最新版本快照建立投影，收合 console 也不使用過期顯示字串。
- 向上捲動、選取舊列或拖曳文字會暫停跟隨，新增事件不移動閱讀位置。
- 右下角顯示「跳至最新（N 筆新訊息）」，N 是符合目前條件的新原始事件數，包含增加去重次數的事件。
- 點擊「跳至最新」或 Ctrl+End 才明確恢復跟隨，收合再展開保留原狀態。
- 暫停時固定正在閱讀的列順序，新的排序變更延後到恢復跟隨，資料快照與匯出仍更新。
- 空狀態只有一行淡文字、目前篩選條件與「重設篩選」，不放大型插圖。
- 時間選單提供絕對時間、相對時間與隱藏，相對時間例如 `−2.3 s`。
- 暫停閱讀時相對時間以暫停瞬間為基準，避免每秒改動正在看的文字。

| 操作範圍 | 鍵盤操作 |
|---|---|
| Console 內 | Ctrl+F 聚焦搜尋；Esc 清除搜尋，有開啟選單時先關閉選單 |
| 工具列 | Tab / Shift+Tab 依序移動；Space 切換等級、去重與只看符合 |
| 列表 | 上下鍵、Home、End、PageUp、PageDown 移動列焦點 |
| 選取 | Shift+上下鍵擴大列範圍；Ctrl+A 選取目前投影的全部列 |
| 展開 | Enter 切換目前列；左右鍵收合或展開 |
| 文字 | F2 進入展開內容的文字選取；Esc 回到列焦點 |
| 連結 | Ctrl+Enter 開啟列內唯一連結；多個連結時先顯示選擇選單 |
| 選單 | Shift+F10 或 Menu 鍵開啟目前列／連結選單；方向鍵與 Enter 執行 |
| 複製 | Ctrl+C 優先複製已選文字，沒有文字選取時複製已選列 |
| 跟隨 | Ctrl+End 執行跳至最新 |

搜尋框內的 Ctrl+A 與 Ctrl+C 維持文字編輯行為。
快捷鍵只由 console controller 處理，宿主不再建立第二套 handler。
多列選取保存穩定 EntryId／GroupId，不因新增事件重設。
去重切換將選取映射到同一批原始事件，篩選隱藏的選取不會被默默複製。
被容量淘汰的選取明確移除，不能複製到另一筆事件。
每個按鈕、列與連結都有 AutomationName、狀態與完整 target 提示。
列表使用一個 Tab 入口與列內移動，避免上萬筆事件進入 Tab 順序。

## 21 項問題如何在新設計中解決（逐項一行）

以下每一項都是 Core 實作與驗收條件，圖片不能證明競態或效能已修復。

| 編號 | 原問題 → 設計與實作契約 |
|---|---|
| 01 | 啟動快照與 Add 重複 → 快照含 Generation 與 LastSequence，後續只接收更大的序號；驗證 startup flush 每個 EntryId 只有一次。 |
| 02 | Clear 後排隊新增回來 → Clear 原子遞增 Generation 並清除 pending，舊 generation 的 callback 與解析結果全部作廢。 |
| 03 | UI-ready 漏通知 → 入列、ready 切換與排定 drain 使用同一鎖與單一排程旗標，驗證所有可達交錯都會 drain。 |
| 04 | 逐筆複製與滿載重建 → 循環 buffer 配有版本的 batch delta，UI 一批更新一次，禁止逐筆組合整份文字或頭端搬移。 |
| 05 | 收合仍解析隱藏 editor → 收合停止 visual 投影與解析工作，只更新計數；展開只取一次最新快照並恢復閱讀狀態。 |
| 06 | 完整 link parse／Exists 阻塞 UI → EntryId＋文字版本快取、增量解析與區間索引，存在性與 resolver 在背景執行，搜尋改變不重跑連結解析。 |
| 07 | entry 上限沒有記憶體上限 → 有界 pending、字元預算與有限快取，大訊息使用可分段讀取內容，Clear 釋放內容與解析快取。 |
| 08 | 全文替換重設選取 → 不使用全文 editor，列表以穩定 ID 增量更新，文字選取保存內容版本與 range。 |
| 09 | 捲動沒有 entry 閱讀錨點 → 保存首個可見 GroupId／EntryId、文字位置與像素差，投影後重映射；淘汰時選最近仍存在的後繼列。 |
| 10 | 收合再展開強制跳最新 → FollowState 與 Anchor 獨立於 IsExpanded，展開不呼叫 JumpToLatest。 |
| 11 | 無效的水平 offset 保留 → 訊息在展開後折行，列表停用水平捲動，移除對 AvaloniaEdit 空方法的依賴。 |
| 12 | 連結命中座標錯誤 → 由列內文字 layout／link segment 的本地座標命中，使用框架座標轉換，不計算整份文件偏移。 |
| 13 | 搜尋與 hover 快取不失效 → 搜尋版本與 hovered target 只使受影響列重繪，命中底色與前景最後套用，不能被 link hover 蓋掉。 |
| 14 | 路徑邊界與 Unicode 漏判 → scanner 支援 Unicode、引號、UNC 與獨立的資料夾 target，優先採 app 提供的結構化 link spans。 |
| 15 | URL 合法尾端被裁切 → URL 與路徑分開掃描，保留成對括號，測試合法 `export_(v2)` 及外側中文句號。 |
| 16 | 同步遞迴掃 repo／猜同名檔 → app 注入有範圍的非同步 resolver，同名候選由使用者明確選擇，Core 不掃 repo 或選最短路徑。 |
| 17 | 行列資訊未傳到 opener → LinkTarget 與 IConsoleLinkOpener 都保留 Path、Line、Column；提示依實際 app 開啟能力顯示。 |
| 18 | Shell-hosted 與 workspace 兩套行為 → 一個 ConsoleController、state 與 commands，所有宿主只提供位置、展開高度與 app adapter。 |
| 19 | Lines 計數不等於顯示內容 → 使用「列／事件」兩種數字，列數取最後投影，筆數取快照；多行展開不冒充新事件。 |
| 20 | 收合期間 Copy all 漏新 log → CopyVisible、CopySelection 與 SaveLog 都走最新有版本快照及共用投影，不讀 ConsoleText cache。 |
| 21 | 連結與圖示缺鍵盤／可及性 → 提供 Ctrl+Enter、Shift+F10、完整 target、AutomationName、選取與展開狀態，焦點使用 Core Focus B。 |

## 八個面向自檢（每個面向：做了什麼、還有什麼限制）

| 面向 | 做了什麼與修訂 | 還有什麼限制 |
|---|---|---|
| 版面 | 將訊息起點由現有固定 56 字元來源欄移到 324 DIP；狀態列改為 20 px，消除主畫面頂端半列裁切；展開 metadata 改成首行對齊。 | 截圖使用四個短來源；更長來源需要省略提示與來源選單。 |
| 留白 | 使用 16 px 外距、8 px 工具列間隔與單一間隔 owner；移除逐列框線；減少展開長訊息的空白高度。 | 20 px 是滑鼠與鍵盤的密集桌面規格，沒有另做觸控規格。 |
| 視覺層次 | 標題、篩選、表頭、訊息與 metadata 各有固定角色；只讓錯誤淡底、命中高亮與恢復跟隨動作突出。 | 六個等級全開時，Core 的 selected chip 外觀仍有較多 accent 邊界。 |
| 色彩 | 全部新介面顏色解析 Core tokens；Debug 與 Trace 分開使用 secondary／muted；placeholder 改用 Core muted 並取消 Fluent template 的再次淡化；逐一計算文字與狀態對比。 | 圖片示範 Core 預設 accent，NFH／NFU accent 仍須在實作套件整合時跑同一套對比檢查。 |
| 動態 | 沿用按鈕 120 ms transition；提議展開／收合 120 ms、選單淡入 80 ms；新訊息不逐列淡入；焦點立即顯示；reducedMotion 停用 transition。 | PNG 無法證明動畫順暢度；自訂展開與選單動態是實作契約，尚未以錄影或實機測量。 |
| 微互動 | 補齊長中文展開箭頭、rest／hover 底線、搜尋命中、行列選單、去重徽章、暫停提示與實際鍵盤 Focus B；選單內容改為左對齊。 | 選單、hover 與 paused 是明確設定的 mock 狀態，不能當作完整 pointer、clipboard 或外部 opener 驗證。 |
| 響應式 | 實際渲染 640／840／1200 DIP；窄版將搜尋移到第二行；以 RenderTargetBitmap 在 125／150／200% 重繪，沒有放大低解析度圖片。 | 640 DIP 以下由宿主最小寬度限制；實機跨螢幕 DPI 切換、拖動視窗與文字 raster 差異仍需驗收。 |
| 原創性 | 把共用 console 定義為可閱讀的事件列表，將來源、去重與暫停狀態整合在同一工作面，以閱讀節奏建立辨識度。 | 配色、字型與 Focus B 已固定，差異主要來自資訊配置與操作連續性；不宣稱取得獎項。 |

Traditional Chinese 訊息與含空白的中文路徑已在明暗主題檢查。
搜尋字為「樣品 A」，三個路徑的命中背景都實際存在於 PNG。
Focus B 使用真正的 Tab 類型焦點，擷取測試確認該列 IsFocused。
640／840 DIP 的列表 extent 等於 viewport，沒有水平溢位。
QA 圖與量測見 qa/verification.json（量測檔在 NFH 工作資料夾） 與 qa/layout-checks.txt（量測檔在 NFH 工作資料夾）。
等級圖示自檢：以非背景像素邊界比較圖示與等級文字中心，六個等級在 Dark／Light、100%／150% 均在 ±0.5 px。Core 必須保留回歸測試；狀態圖末尾的六列 4 倍標線與 前後量測表（量測檔在 NFH 工作資料夾） 可供核對。

## 給 Core 實作的結構建議（資料模型、去重與篩選的投影、增量連結解析、虛擬化列表、由 app 注入的部分：來源名稱、副檔名、路徑解析、開啟動作）

### 資料模型

- `LogEntry` 保存 EntryId、Generation、Sequence、UTC Timestamp、Level、SourceId、TextContent 與可選的結構化 LinkSpans。
- EntryId 與 Sequence 不能以時間代替，跨執行緒排序只看序號。
- `LogSnapshot` 保存版本、generation、尾窗範圍、事件數與不可變的 entry 視圖。
- `LogStore` 使用循環 buffer，來源資料是唯一真相，不保存第二份全 console 格式化字串。
- Clear 與 Add 的線性化點位於同一同步區域，所有待送批次都攜帶 generation。
- Avalonia adapter 維持一個 queued drain，每批才切回 UI，不讓每筆事件都 post callback。
- 建議初始預算為 10,000 entries、4 Mi UTF-16 字元及 256 Ki 字元 pending，三個上限分別生效。
- 超過 inline 預算的大訊息使用有生命週期的 `ILogTextContent` 分段讀取，展開仍能取得完整文字。
- 內容 spill store 的位置與生命週期由 app 注入，pending 只保留有界 handle，不先累積無上限字串。
- 淘汰、Clear 或 Dispose 釋放內容 handle、dedup 成員索引與解析結果，不能只清 UI 列表。
- 如有超過容量而捨棄的事件，必須顯示「因保留上限移除 N 筆」的明確計數。

### 去重與篩選的投影

- 先以 SourceId＋Level＋原始完整訊息建立 group，時間不參與 key。
- 雜湊碰撞必須比較原文，不能只比較 hash。
- Group 保存穩定 GroupId、成員序號、首末時間、目前次數與最後 Sequence。
- 再套用來源、等級與搜尋 predicate，最後建立可見列與命中 ranges。
- 搜尋比對完整訊息，不能只比對第一行或目前畫面上的文字。
- 尾窗淘汰增量移除成員，最後一次事件淘汰時重新計算該 group 的時間與位置。
- 搜尋版本與 snapshot 版本一起驗證，晚到的背景結果不能覆蓋新條件。
- Controller 分開保存 FollowState、ReadingAnchor、Selection、ExpandedIds 與呈現順序。
- Copy／Export 的格式化器和畫面使用同一投影規則，不能各自重新推導 filter 或 dedup。
- 匯出先凍結 snapshot version，寫檔在背景執行，避免匯出過程的新增事件造成半新半舊內容。

### 增量連結解析

- 有結構化 spans 時直接使用，沒有時才以 Unicode scanner 推導。
- scanner 先辨識 URL，再處理引號、Windows drive、UNC、相對路徑、行列與資料夾邊界。
- 語法解析不呼叫 File.Exists 或 Directory.Exists。
- 存在性、相對路徑與歧義解析交給 app 的非同步 resolver。
- Cache key 是 EntryId＋TextVersion＋ResolverPolicyVersion，搜尋版本不在 link parse key 中。
- 新增只解析新 entry，淘汰只移除對應 cache。
- 每列的 spans 以 start 排序並建立區間索引，命中查詢不逐一掃描全歷史連結。
- UI 只接受仍符合 generation 與內容版本的解析結果，收合或離開宿主時取消工作。
- URL／file／folder 是不同 LinkKind，folder target 不以 extension 白名單決定。

### 虛擬化列表

- 使用可回收容器的虛擬化 ItemsControl／ListBox，不能把所有列放進 StackPanel。
- 收合列固定 20 DIP，展開列使用可估計與可更新的高度。
- 超大展開內容也需分段虛擬化，不能因一筆 stack trace 建立數十萬個文字 visual。
- 相同 EntryId／GroupId 維持 container identity，新增不替換整份 ItemsSource。
- 列寬隨 viewport 更新，搭配 Core viewport-bound 樣式，禁止水平捲動。
- 每次投影更新先保存閱讀錨點，再更新、排版與恢復位置。
- 已淘汰錨點使用最近的後繼列，並告知保留範圍已改變。
- 列內文字 range 使用 TextLayout 的 hit-test，link pointer 座標由框架轉換。
- 搜尋背景先畫，文字、link underline 與命中前景依明確優先序組合。
- 訂閱與 cancellation 隨 attach／detach 生命週期建立與 Dispose，反覆切頁不保留舊 view。

### 由 app 注入

| 注入契約 | app 責任 |
|---|---|
| SourceRegistry | 穩定 SourceId、顯示名稱與來源排序，不依賴 NFH Shell 類型 |
| PathPolicy | 可辨識副檔名、相對路徑基準、UNC 與資料夾政策 |
| IConsolePathResolver | 有界、非同步的解析與同名候選，Core 不猜 repo root 或遞迴掃描 CWD |
| IConsoleLinkOpener | URL、file、folder 開啟動作，接收完整 path／line／column 與能力資訊 |
| ExportAdapter | 儲存對話框、目的地與 .log 寫入，Clipboard 使用目前 TopLevel |
| ContentStorePolicy | 大訊息內容 handle、spill 位置、容量與刪除生命週期 |
| Accent / logging adapter | 七個 app accent tokens、NLog 等資料來源 adapter 與來源命名 |
| Host layout | console 位置、可用高度、最小寬度與收合後的 row 高度 |

ConsolePanel 只接收 Core state 與 commands，不 cast ShellViewModel。
NLog 設定、全域 target 註冊與 app log 目錄不屬於 panel。

## 未決事項

- v3 版面與互動狀態仍待 owner 重新核准，核准後才開始 Core 正式實作。
- 各 app 的 opener 是否支援跳到行列，須由能力契約回報；不支援時提示必須明確寫「開啟檔案」。
- 記憶體預算的建議初始值須以 4,000／10,000 entries、長訊息與慢 resolver 測量後定案，不能取消有界設計。
- Avalonia 可變列高回收、真實 clipboard、screen reader、競態與跨螢幕 DPI 都須進入實作驗收。
- Core 1.0.0 與獨立 Fonts 套件的採用版本由整合階段指定。

Owner 已固定的十三項決定、Core palette、Focus B 與 Fonts 不在未決事項內。

### 本機擷取與交付紀錄

| 檔案 | 像素尺寸 | 內容 |
|---|---|---|
| `images/current-dark.png` | 1200 × 420 | NFH 現有 ConsolePanel、formatter、link colorizer 與原有樣式 |
| `images/current-light.png` | 1200 × 420 | 同上，Application 與 Window 都切換至 Light |
| `images/proposal-dark.png` | 1200 × 420 | Core 實際 Avalonia 控制項，去重開啟並跟隨最新 |
| `images/proposal-light.png` | 1200 × 420 | 同上，Light |
| `images/states-dark.png` | 1200 × 1656 | 八組有標籤的狀態，涵蓋所有指定互動 |
| `images/states-light.png` | 1200 × 1656 | 同上，Light |
| `images/compare.png` | 2464 × 1024 | 新舊左右並列，Dark 在上、Light 在下，標籤使用 Title 24 |

兩套畫面使用同一份合成記錄資料，共 21 筆事件、18 個去重 group。
NFH 保留原有 14 px Consolas 設定，臨時 headless host 載入本機 Consolas 與 Microsoft JhengHei，避免測試平台缺字。
這些 Windows 字型沒有加入 Core 或交付資產。
提案全部使用 `Nvt.Core.Fonts` 嵌入字型。
QA 圖包含 640／840 DIP 與 125／150／200% 直接重繪結果。
此 renderer 為視覺 mock，列表使用合成資料，外部開啟動作未執行。
正式虛擬化與 21 項修復必須依上面的實作契約完成，不能沿用 mock 的 StackPanel。
擷取 helper 在交付前刪除，所有修改的 tracked 檔案以原始位元組還原。
所有 build 使用 `--no-restore` 與 `AVALONIA_TELEMETRY_OPTOUT=1`。
未讀取 `example/`，未提交，未推送。

交付時 git status：Core 與 NFH 都是 clean，HEAD 分別仍是 `0330fd7d` 與 `6fe3c269`。
還原後兩個原始測試專案重新 build，均為零警告、零錯誤。



### 2026-10-07 等級圖示置中修訂

新版主畫面與比較圖維持原尺寸。新版狀態圖保留原有八節，末尾增加六等級置中證據，尺寸為 1200 × 2336。舊圖片保留。
