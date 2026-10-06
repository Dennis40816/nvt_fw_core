[English](Launcher.md)

# Launcher

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedAppVersion.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedVersionRepository.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementPolicy.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateCatalogModels.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherBootstrapContracts.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedLauncherEntry.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionActivationPolicy.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherMutationFence.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagerStateStore.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateSourceRegistry.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedInstallationLayout.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedSetupTransactionDocuments.cs`

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Launcher.Contracts` 與 `Nvt.Core.Launcher.Activation` 提供受管理應用程式及 Launcher 啟用所需的值與介面，目標為 `net8.0`，只依賴 BCL。本模組不存取檔案系統或啟動 process；純路徑正規化遵循目前平台的規則。

抽取範圍包含版本與內容身分、descriptor、套件政策介面、正規化套件結果、不可變應用程式與 Launcher 狀態、轉移函式、durable snapshot 比較、通用 inventory、刪除 owner 保護及 repository/state-reader 介面。`UpdateSourceRegistry.cs` 僅抽取 `VersionSourceRegistryState`。`VersionManagementPolicy.cs` 僅抽取 inventory 與通用刪除判斷；保留門檻、自動刪除政策與探索通知留在 NFC。`LauncherMutationFence.cs` 抽取保護值與介面，experience partial 留在 NFC。`VersionManagerStateStore.cs` 抽取讀寫結果值與 `IVersionManagerStateReader`；writer acquisition 與 live custody 屬於獨立 persistence 合約。原生結構、execution token、ZIP plan 及嚴格 wire DTO/codec 不在本模組內。

## Contracts

[ProductDescriptor](../../../src/Nvt.Core/Launcher/Contracts/ProductDescriptor.cs) 要求明確提供 product、runtime、registry、執行檔及 protocol 名稱、Bootstrap 檔名與 archive-root callback。執行檔路徑必須是安全的斜線分隔相對路徑；Bootstrap 與每次 callback 的 archive root 必須是單一安全名稱。空白、絕對路徑、目錄跳脫、alternate stream、反斜線、C0 控制字元、不合法標點及 Windows device name 都會被拒絕。路徑最多 512 字元；DEL 與 C1 保留來源的接受行為。每次 callback 都重新驗證，設定不代表內容信任。

[ManagedAppVersion](../../../src/Nvt.Core/Launcher/Contracts/ManagedAppVersion.cs) 保留 canonical stable 三段版本解析、數值排序及 invariant 格式。[UpdateCatalogVersionSnapshot.Create](../../../src/Nvt.Core/Launcher/Contracts/UpdateCatalogVersionSnapshot.cs) 要求明確且為正數的套件與 UTF-8 notes 上限、UTC metadata、最多 512 字元的安全相對套件路徑、上限內的正套件長度、小寫 SHA-256、存在且符合上限的 notes 及已定義的通知政策。NFC 凍結上限為 134,217,728 套件 bytes 與 65,536 notes bytes；嚴格 ZIP 與 timestamp wire 文法仍由 NFC 驗證。

Catalog admission identity 保留 `version|relative-package-path|invariant-package-size|package-sha256|release-manifest-sha256`。來源根目錄、發布時間、notes 與通知政策不納入 identity。`VerifiedUpdateCandidate` 保留版本、admission identity 與 notes；`ManagedVersionAdmission` 另保留精確 manifest digest。不得由路徑推導 admission。

[IProductPackagePolicy](../../../src/Nvt.Core/Launcher/Contracts/PackageContracts.cs) 接收精確 manifest bytes，回傳產品已驗證的正規化事實。`archivePaths: null` 保留已安裝驗證模式。必要 NFC adapter 驗證嚴格 schema、精確 product/runtime 與封閉 payload，才回傳 `PackageManifest`。`PackageLauncher` 是尚未綁定 owner 的解析宣告；沒有預設接受的 policy 或 fallback adapter。

[ManagedLauncherIdentity.Create](../../../src/Nvt.Core/Launcher/Contracts/ManagedLauncherIdentity.cs) 要求 descriptor、明確且不超過 200,000,000 bytes 的正執行檔上限，以及所有精確 owner/version/hash/protocol/path/size 欄位。NFC 提供 200,000,000 bytes。Protocol 精確為 `1`；執行檔路徑以 ordinal 比較 descriptor；非空白 owner admission 最多 2,048 字元；兩個 digest 都必須是小寫 SHA-256。`MatchesOwner` 以 ordinal 比較應用版本、admission 字串與 manifest digest。`ManagedImmutableBootstrapIdentity.Create` 保留精確 descriptor root 檔名與 200,000,000-byte 上限。

[ManagedPackageResults](../../../src/Nvt.Core/Launcher/Contracts/ManagedPackageResults.cs) 保留 install、verification、executable-lease 與 installed-launcher issue 值及成功條件，包括 `HasSupportedManagedLauncher`。`IManagedExecutableLaunchLease` 提供可 Dispose custody、精確 executable/working-directory 及啟動前最終同步驗證；本模組不實作 process starter 或 custody。

## 啟用與 inventory API

[VersionManagerState.Create](../../../src/Nvt.Core/Launcher/Activation/VersionActivationPolicy.cs) 先驗證唯一 admission、非空白 identity 與小寫 manifest digest，再檢查所有參照的版本。拒絕同時存在啟用與檔案系統 journal、未定義 kind/phase、不符合完整 committed admission 的 delete，以及安裝已存在版本。一般 active-launch journal 必須綁定目前 active 及精確 prior active/fallback 版本；其他應用 phase 保留來源的 prior-version membership 判斷。應用 admission 字串不額外套用 Launcher owner 的長度上限。

`VersionSourceRegistryState` 保留 accepted revision、精確 digest 與 manual pin。Revision 零要求 absent digest 與 manual pin；正 revision 要求小寫 SHA-256；負值被拒絕，沒有 revision 上限。Registry source 必須為絕對且已正規化的路徑；尚未綁定的 seed state 可綁定 root 一次。Root ownership 使用平台 path comparer。Durable token 比較涵蓋每個應用 durable 欄位，包括 revision、digest 與 pin；token 中保存的 root/source 字串使用 ordinal 比較。

[VersionActivationPolicy](../../../src/Nvt.Core/Launcher/Activation/VersionActivationPolicy.cs) 提供 `BeginActivation`、`RecordCandidateLaunch`、`CancelRequestedActivation`、`CommitReady`、`FailActivation`、`RecordRollbackLaunch`、`CommitRollback`、`RecordActiveLaunch` 與 `ClearActiveLaunch`。Phase 保留 `Requested = 0`、`CandidateLaunchRecorded = 1`、`RollbackLaunchRecorded = 2`、`ActiveLaunchRecorded = 3`。Ready 只能提交已記錄的精確 candidate launch；取消僅關閉未啟動請求。失敗時，若記錄的 admitted LKG 不等於失敗 candidate，就恢復該 LKG，否則恢復 prior active。Rollback launch 選擇只記錄一次；重複選擇不再提供 target。Journal 不保證 crash 後 process 只啟動一次。明確選擇健康且 admitted 的舊版本仍合法；NFC 的 newer-only 自動通知政策獨立處理。

[LauncherBootstrapState.Create](../../../src/Nvt.Core/Launcher/Activation/LauncherBootstrapState.cs) 先正規化 root，再拒絕 split active/fallback presence、不同精確 prior identities 的 journal，以及指向其他 Launcher 的一般 active guard。Candidate、rollback、ready 與 failure 轉移保留來源 predicate 及 message；Launcher rollback recording 要求已記錄 candidate launch。所有 Launcher durable 欄位納入 token 比較，internal transition/observer helper 不授予 IO authority。

[ManagedVersionInventory.Create](../../../src/Nvt.Core/Launcher/Activation/VersionManagementPolicy.cs) 複製輸入、拒絕重複版本、最多允許一個 active row，並依來源順序驗證 admission/integrity。Rows 由新到舊排序；healthy/damaged 僅計算 admitted rows，其餘算 unadmitted。`Find` 以精確版本查詢。

`VersionManagementPolicy.DecideDelete` 與 [LauncherMutationProtection](../../../src/Nvt.Core/Launcher/Activation/LauncherMutationFence.cs) 保留 journal fencing 優先於完整 Launcher owner 保護，其後處理 absent/uncommitted inventory 及 active version。Owner equality 包含版本、admission identity 與 manifest digest。Fallback-only authority 提供失去 rollback 的資訊；此判斷不代表刪除同意。確認、保留門檻與通知仍由 NFC 負責。

[IManagedVersionRepository](../../../src/Nvt.Core/Launcher/Activation/ManagedVersionRepository.cs) 保留 `AcquireApplicationLaunchLeaseAsync`、`VerifyPackageAsync`、`InstallAsync`、`InventoryAsync`、`DeleteAsync` 的參數順序與 typed result。預設 lease 方法先檢查 cancellation，回傳 unavailable custody。整體 inventory unavailable 不帶部分資料。[IInstalledLauncherRepository](../../../src/Nvt.Core/Launcher/Activation/IInstalledLauncherRepository.cs) 保留 owner-bound verification 與 lease acquisition。[IVersionManagerStateReader](../../../src/Nvt.Core/Launcher/Activation/VersionManagerStateReader.cs) 載入完整已驗證 snapshot，不推測身分。`ILauncherBootstrapStateStore` 在呼叫端的應用 lease 下提供 load 與 typed save，不另建 writer lease。轉移不依賴 `IO.AtomicOutput` 或 `Lifecycle.UndoService`。

## 凍結測試對應

來源路徑位於凍結 parent 的 `tests/NvtFwCombiner.Application.Tests/VersionManagement/`；Core 對應位於 `tests/Nvt.Core.Tests/Launcher/Activation/`。

| 凍結來源 case | Core case |
| --- | --- |
| `VersionActivationPolicyTests:14 ReadyCommitsPendingCandidate` | `VersionActivationPolicyTests.ReadyCommitsPendingCandidate` |
| `VersionActivationPolicyTests:34 FailureRestoresPriorVersionAndCannotOscillate` | `VersionActivationPolicyTests.FailureRestoresPriorVersionAndCannotOscillate` |
| `VersionActivationPolicyTests:72 RequestedActivationCannotCommitReady` | `VersionActivationPolicyTests.RequestedActivationCannotCommitReady` |
| `VersionActivationPolicyTests:85 ConcurrentDurableTransactionsAreRejected` | `VersionActivationPolicyTests.ConcurrentDurableTransactionsAreRejected` |
| `VersionActivationPolicyTests:107 DeleteMutationAdmissionMustMatchCommittedAdmission` | `VersionActivationPolicyTests.DeleteMutationAdmissionMustMatchCommittedAdmission` |
| `VersionActivationPolicyTests:129 RegistryAuthorityDriftInvalidatesDurableSnapshotToken` | `VersionActivationPolicyTests.RegistryAuthorityDriftInvalidatesDurableSnapshotToken` |
| `LauncherBootstrapContractTests:125 LauncherStateRejectsSplitActivePairAndMismatchedPendingSnapshot` | `LauncherBootstrapContractTests.LauncherStateRejectsSplitActivePairAndMismatchedPendingSnapshot` |
| `LauncherBootstrapContractTests` 身分、phase、active guard cases | `LauncherBootstrapContractTests` 同名通用 cases |
| `VersionManagementPolicyTests.DeleteDecisionProtectsActiveAndWarnsForLastKnownGood` | `InventoryAndPortTests.DeleteDecisionProtectsActiveAndWarnsForLastKnownGood` |

`ActivationTransitionTests` 涵蓋完整 application/Launcher phase matrix、精確 message、錯誤身分、重複 recovery、舊版明確選擇及 mutation guards。`ActivationStateTests` 涵蓋所有 durable 欄位、prior identity、root binding、未定義 journal enum、digest 長度 63/64/65、owner admission 長度 2,047/2,048/2,049、路徑長度 511/512/513、200,000,000 bytes 周邊執行檔邊界，以及零/負數的明確上限。`InventoryAndPortTests` 涵蓋 active row 零/一/二的邊界、來源檢查順序、健康計數、精確 delete owners、fail-closed results 與 cancellation。Contracts tests 保留 canonical version、catalog package/notes 上限、identity 組成及 descriptor 文法。所有資料為合成資料；純合約不涉及原生執行。

## NFC 邊界與採用

NFC 保留嚴格 state/manifest/catalog DTO 與 codec、canonical wire schema、產品文字、精確 protocol 名稱、套件信任及 release authority、registry locator/replica、retention 與 notification policy、刪除同意、firmware 行為與 UI composition。

NFC 在建置時透過 `core-packages.json` 下載版本化套件，使用精確 `[x]` pin、lock files 及 locked restore。source mapping 將 Core packages 限制於下載資料夾。清單記錄每個套件的 Release 標籤與 SHA-256。Core 與 NFC 獨立 release。只有相應 NFC adapter 已使用 Core 並保持完整 values、event traces 與 output bytes，才刪除重複 executable bodies。影響 UI 的採用要求相同環境下 decoded pixels 零差異。八個 legacy font 值保持不變；Bootstrap package wiring 需要獨立的 Launcher 採用授權。

## 有界封存讀取

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/BoundedArchiveReader.cs` — 完整的實際展開位元組計量、雜湊、有界複製及檔案讀取包裝。

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Launcher.Verification.BoundedArchiveReader` 與 `ExpandedByteBudget` 均為 internal。讀取器對實際展開位元組計算雜湊，可將接受的區塊複製至目的串流，並讓多個項目共用明確指定的正值位元組預算。`ReadAndHashAsync` 與 `CopyAndHashAsync` 要求實際長度等於宣告長度；`ReadAtMostAndHashAsync` 允許較短內容。`ReadFileAndHashAsync` 自行開啟並釋放唯讀檔案串流。以串流為輸入的方法借用來源與目的串流，並保持兩者開啟，無須 seek 或長度中繼資料。壓縮套件的雜湊由 `Nvt.Core.Files.BoundedFileReader.ReadAndHashAsync` 負責。

固定緩衝區維持 65,536 位元組。每次讀取最多比項目剩餘限制與總預算剩餘值的較小者多一個位元組。實際位元組（含溢位哨兵）先計入總量，再檢查項目長度；同時超限時，總量超限優先。溢位區塊不會寫入目的串流，也不會傳給進度回呼。要求精確長度時，短讀會保留已接受的前綴，並回傳項目長度不符。失敗的有界結果不含雜湊；成功結果使用標準的 64 字元小寫 SHA-256。取消、讀取故障、寫入故障及回呼例外均向外傳遞。目的串流寫入先於進度通知。NFC 明確提供其凍結的展開上限 536,870,912 位元組（512 MiB）。

來源至 Core 的讀取器測試對照如下：

| 凍結來源案例 | Core 案例 |
| --- | --- |
| `FileSystemManagedVersionRepositoryTests.Security.cs`：`ActualEntryBytesCannotExceedDeclaredLength` | `BoundedArchiveReaderTests.ActualEntryBytesCannotExceedDeclaredLength` 保留宣告長度 8、預算 2,048 及 9 位元組哨兵。 |
| `FileSystemManagedVersionRepositoryTests.Security.cs`：`ActualExpandedBytesShareOneAggregateBudget` | `BoundedArchiveReaderTests.ActualExpandedBytesShareOneAggregateBudget` 保留第一項目 6 位元組、共用預算 10 位元組、已消耗總量 11 位元組及第二次讀取 5 位元組。 |

新增的合成測試涵蓋 512 MiB 少一位元組、剛好 512 MiB 及多一位元組；精確及最多讀取模式的相鄰項目長度；零長度項目；部分讀取跨越 65,536 位元組緩衝區的相鄰值；零與負值預算參數；long 計數器；參數及例外順序；溢位寫入邊界；確定性取消；有界故障；借用串流的生命週期；以及自有檔案串流的釋放。執行期檔案測試使用各自唯一的暫存目錄。

NFC 保留嚴格的 manifest 與 admission schema、產品 payload 允許清單、發行信任、韌體中繼資料及安裝政策。NFC 在獨立 pull request 採用移轉機制。NFC 在建置時透過 `core-packages.json` 下載版本化套件，使用精確 `[x]` 套件版本、locked restore 及來源對應。清單記錄每個套件的 Release 標籤與 SHA-256。只有呼叫端已使用 Core，且原有套件、安裝與產品相容性斷言以不變的值及輸出位元組通過後，NFC 才刪除其通用讀取器。Core 讀取器測試不能證明 NFC 的產品或像素一致性。

## 有界套件驗證

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Extracted source paths:

- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedPackageVerifier.cs` — 封閉封存清單、宣告與實際展開檢查、checksum 解析、內容驗證及 internal 解壓；嚴格 JSON 解析與產品中繼資料仍由 NFC 保留。
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/FileSystemManagedVersionRepository.cs` — 壓縮套件長度／雜湊 admission 與公開驗證結果對應；檔案系統解析、原生 custody、promotion 與 inventory 留給各自的消費端。
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/BoundedArchiveReader.cs` — 實際展開位元組計量，詳見前節。
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateCatalogModels.cs` — 透過既有正規化合約傳遞精確 candidate identity 與 release notes。
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedVersionRepository.cs` — 既有驗證結果、issue 數值與 launcher 存在旗標。

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

[`ManagedPackageVerifier`](../../../src/Nvt.Core/Launcher/Verification/ManagedPackageVerifier.cs) 是 `Nvt.Core.Launcher.Verification` 唯一的公開型別。建構式必須提供 `ProductDescriptor`、`IProductPackagePolicy` 與 `PackageVerificationLimits`。`VerifyAsync(Stream package, UpdateCatalogVersionSnapshot candidate, CancellationToken cancellationToken)` 回傳既有的 `ManagedPackageVerificationResult`。成功結果保留 candidate 的精確版本、admission identity 與 release notes；launcher 旗標要求符合 descriptor 的 launcher identity，且 owner version、admission identity 與 manifest hash 全部取自同一 candidate。

呼叫端借出可讀、可 seek 的套件串流，在驗證與 internal plan 使用期間持續保有穩定讀取 custody；verifier 不關閉套件。不支援的 capability 不會透過無界 buffering 補救。完整壓縮內容只交由 `Files.BoundedFileReader.ReadAndHashAsync` 的 identity-only 模式計算雜湊，包含精確長度、單一 EOF 哨兵及最後長度／位置檢查。長度失敗先於雜湊不符，雜湊不符先於開啟 ZIP。凍結的公開對應為：I/O、存取或 malformed ZIP 回傳 `PackageUnavailable`；相同長度但不同 digest 回傳 `PackageMismatch`；不安全的封存形狀或總展開超限回傳 `UnsafeArchive`；manifest、checksum 或成員內容無效回傳 `InvalidPayload`。取消與 adapter 程式錯誤向外傳遞；adapter I/O 失敗仍使用凍結的 I/O 對應。

封存 admission 拒絕空封存、過多成員、不符合 ordinal root prefix 的路徑、反斜線、link 與 reparse 屬性。呼叫必要的產品路徑政策前，結構檢查保留 512 字元限制、Windows device name 與不安全 segment 規則。檔案路徑以忽略大小寫方式保持唯一。明確目錄項目必須沒有內容且路徑安全，並占用封存成員數；與來源相同，只有檔案路徑的唯一隱含父目錄納入 installed-directory 計數，重複的空目錄項目仍可接受。宣告長度先相減再相加；之後單一 `ExpandedByteBudget` 計算 manifest、checksum 與 payload 的實際位元組，總量超限優先於成員超限，並保留一位元組哨兵。

精確 manifest hash 必須先符合 catalog pin，必要 adapter 才會收到原始 bytes 與完整檔案清單。NFC 必須在回傳正規化 facts 前完成嚴格 JSON 與 schema 驗證。Core 獨立重查 ordinal product/runtime、candidate version、正值且有界的檔案大小、64 字元小寫 SHA-256、保留成員排除、重複路徑與封閉清單。應用程式檔案受展開位元組上限限制；執行檔上限只適用於已宣告的 launcher。Core 在非同步內容驗證前複製檔案清單。Launcher 宣告必須符合 descriptor 的精確路徑、支援的 protocol 及宣告成員長度／hash，再以 candidate 的精確 owner admission 與 manifest digest 呼叫 `ManagedLauncherIdentity.Create`。成功 adapter 不能略過這些檢查。

Checksum 使用嚴格 UTF-8、精確的 64 字元小寫 hash、兩個 ASCII 空白與安全相對路徑。路徑使用 ordinal 比較，清單精確包含每個宣告 payload 及 manifest digest。Invalid UTF-8、BOM、重複、缺少、額外項目、hash 變更及無效分隔符都會失敗。來源允許 LF 或 CRLF、空白行、項目重新排序及沒有最後換行的 EOF；這些可接受的 byte 形狀維持不變。

所有產品上限都必須明確提供正值，保留原有參數順序，`MaximumInstalledDirectories` 接在 `MaximumExecutableBytes` 後；verifier 也重查 record copy。以下凍結值由 NFC 提供，Core 沒有產品預設值：

| 限制 | NFC 值 | Core 合約 |
| --- | ---: | --- |
| 封存成員 | 4,096 | `MaximumArchiveEntries` |
| 完整壓縮位元組 | 134,217,728 | `MaximumPackageBytes` |
| 實際展開位元組 | 536,870,912 | `MaximumExpandedBytes` |
| Manifest 與 checksum 各自的位元組 | 1,048,576 | 共用 `MaximumManifestBytes` |
| Admission 文件位元組 | 4,096 | `MaximumAdmissionBytes`，供解壓消費端使用的正值保留 |
| 已宣告的 launcher 位元組 | 200,000,000 | `MaximumExecutableBytes`，亦受既有 identity 合約限制 |
| Installed files | 4,097 | 封存上限加上一個 admission 保留，使用 long 計數器 |
| Installed directories | 4,096 | `MaximumInstalledDirectories` |
| 相對路徑字元 | 512 | 既有 contract validation |
| UTF-8 release-note 位元組 | 65,536 | 既有 catalog snapshot factory，依位元組而非字元計數 |

來源 verifier 的 200 字元單一 asset-name 限制適用於 manifest 的 SBOM／provenance 欄位。這些欄位沒有納入 `PackageManifest`，因此 NFC 的嚴格 adapter 隨 schema 保留該 predicate。合成 adapter 測試涵蓋 199、200 與 201 字元；Core 不取得 release metadata authority。

`ManagedPackagePlan` 與 `ManagedPackagePlanResult` 均為 internal。成功 plan 保留存活的 ZIP reader、封閉且唯讀的檔案清單、精確文件 bytes、檔案／目錄數、展開 facts 與 owner-bound launcher。Plan 擁有 ZIP reader；Dispose 不關閉呼叫端持有的套件。Internal 解壓使用新的共用實際位元組預算，重查所有精確長度／hash，擁有並 Dispose destination streams；內容變更以來源訊息 `Archive content changed after admission.` 拒絕。已 Dispose 的 plan 不能解壓。Admission 與 installed-file 保留提供給 internal 安裝消費端；verifier 不建立安裝根目錄、staging tree、admission JSON、activation transaction 或 promotion。

來源至 Core 的套件測試使用 `ManagedPackageVerifierTests`：

| 凍結來源案例 | Core 證據 |
| --- | --- |
| `Security.cs: ChangedPackageNeverReachesZipAdmission` | 同名；保留長度／digest issue 順序，且 policy／destination admission 次數為零。 |
| `Security.cs: DuplicateAndLinkArchiveMembersNeverVerify` | 同名；保留忽略大小寫的重複路徑與 UNIX link，另涵蓋 Windows reparse 屬性。 |
| `ExpandedBytes.cs: UnderreportedZipEntryFailsVerifyAndInstallWithoutMaterialization` | 同名；保留 README 宣告為一位元組，且在 internal 解壓建立 destination 前拒絕。 |
| `ExpandedBytes.cs: Zip64DeclaredSizeOverflowFailsVerifyAndInstallWithoutResidue` | 同名；保留第一項正長度與第二項 `long.MaxValue`，且沒有 plan 或 destination residue。 |
| `Security.cs: NonCanonicalChecksumDocumentNeverVerifies` | 同名；保留 changed-hash 與 extra-line mutations。 |
| `Security.cs: InvalidManifestOrClosedPayloadNeverVerifies` | 同名；保留通用 product/version/hash/size/missing/extra 斷言；合成 role 與 unknown-field 在嚴格 policy projection 前拒絕。 |
| `ExpandedBytes.cs: ExactExpandedByteBudgetInstallsWhileOneByteLessFailsBeforeMaterialization` | 同名；保留精確成員總量與少一位元組拒絕，並比較 internal 解壓輸出 bytes。 |

表內來源檔名是 `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/` 下的 `FileSystemManagedVersionRepositoryTests.Security.cs` 與 `FileSystemManagedVersionRepositoryTests.ExpandedBytes.cs`。名稱包含 installation 的 Core 案例測試封閉解壓 plan；repository staging、atomic promotion 與 installed inventory 斷言仍由 NFC adoption 提供。

`PackageCeilingTests` 涵蓋凍結的成員、installed-file、目錄、文件、壓縮內容、已宣告的 launcher 與相對路徑上限及相鄰值，也檢查超過執行檔上限的應用程式仍可接受、不依賴中繼資料的實際展開、文件正值與 underreported documents。`PackageVerificationLimitsTests` 涵蓋零／負值、copied limits、原有參數順序、明確 NFC 值及既有 executable identity ceiling。`PackageIdentityAndChecksumTests` 涵蓋 forged normalized identity、launcher owner binding、嚴格 policy 拒絕與 checksum byte grammar。`PackageStreamAndPlanTests` 涵蓋公開介面、借用 custody、確定性取消、有界壓縮讀取故障、Files probe 順序、不可變 plan facts、Dispose、admission 後竄改與 destination write failure。前節 reader 案例直接涵蓋凍結的 512 MiB 實際預算與 overflow sentinel。

NFC 保留嚴格 schema、產品 payload role 與 allowlist、wire grammar、release metadata、韌體資料、信任與發行權限。採用時在建置時透過 `core-packages.json` 下載獨立版本的套件，使用精確 `[x]` 版本、locked restore 與 source mapping。清單記錄每個套件的 Release 標籤與 SHA-256。原有 NFC schema、套件、安裝、完整值、trace 與輸出位元組斷言通過後，才能刪除已移轉的通用 verifier 與 reader。影響 UI 的採用也必須在同一 recorded environment 維持 decoded pixels。Core 合成測試不能證明 NFC 產品或像素一致性；此 API 也不授權 Bootstrap 套件接線。

### 與凍結原始碼的刻意差異

Core 與凍結的 NFC verifier 有三處不同。每一處都是拒絕原始碼以其他方式處理的輸入，沒有任何一處會接受原始碼拒絕的輸入。

- `VerifyAsync` 在讀取前先檢查套件 stream 可讀取且可 seek，並檢查候選套件大小不超過 `MaximumPackageBytes`。任一項不符時回傳 `PackageUnavailable`。
- 讀取途中套件內容改變時，結果是 `PackageUnavailable`。原始碼回傳 `PackageMismatch`。
- manifest 的檔案項目若名為 `RELEASE-MANIFEST.json` 或 `SHA256SUMS.txt`，會以 `InvalidPayload` 拒絕。
