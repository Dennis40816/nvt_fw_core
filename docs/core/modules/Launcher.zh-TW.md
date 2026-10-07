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
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagerStateStore.cs` — 讀寫結果與介面，以及 writer result、精確存活 custody 合約與 state-store 介面。
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateSourceRegistry.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedInstallationLayout.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedSetupTransactionDocuments.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/FileSystemVersionManagerWriteLease.cs` — exclusive writer acquisition、lock identity 與存活 custody。
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedPathSafety.cs` — `ReadBoundedFileAsync` 的路徑 admission、開啟串流及長度檢查；完整內容讀取交由 Files。
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/JsonVersionManagerStateStore.cs` — 明確 raw path、有界位元組讀寫與 writer 委派；產品預設值與嚴格 codec 留在 NFC。
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/JsonLauncherBootstrapStateStore.cs` — injective 路徑推導、有界 raw byte access 與 typed write failure；suffix 設定與嚴格 codec 留在 NFC。

- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/StableManagedExecutableLaunchLease.cs` (complete lease adapter, PE checks, held measurement and copying; complete-content hashing delegates to Files)
- `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/FileSystemManagedVersionRepositoryTests.cs` (`AcquiredApplicationLeaseDeniesExecutableSwapUntilReleased` generic custody assertions only)
- `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/FileSystemInstalledLauncherRepositoryTests.cs` (generic held lease, ancestor replacement, content and late-child assertions; product schema and repository policy remain in NFC)
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedActivationCoordinator.cs` — 應用 process／READY 介面、結果與監督；穩定桌面 handoff 宣告由 process adapter 保留.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherBootstrapCoordinator.cs` — 完整 Launcher supervisor.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherBootstrapCoordinator.ActiveAttemptRecovery.cs` — 完整 active-attempt recovery.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedProcessLifetimeContracts.cs` — 僅 ManagedProcessLifetimeKind.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedVersionSeedBootstrapper.cs` — canonical seed policy 與 bootstrapper.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedApplicationStartupCoordinator.cs` — READY dispatch；桌面 snapshot 經 initialization 介面投影.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementInitialization.cs` — 唯讀與 writer-qualified initialization dispatch.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.cs` — initialization、guarded delete 與 operation outcome；source／check／session／UI composition 留在 NFC.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.Install.cs` — prepared install transaction；catalog selection 為必要 caller 介面.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.Activation.cs` — activation preparation／cancellation 與 retention acknowledgement.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.Recovery.cs` — prepared mutation convergence 與 commit helper；retention advice 為必要 caller 介面.
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementExperience.State.cs` — durable root／load／recovery 與 inventory projection；source／session snapshot 留在 NFC.

<!-- Copyright (c) 2026 Dennis Liu. All rights reserved. -->

`Nvt.Core.Launcher.Contracts` 與 `Nvt.Core.Launcher.Activation` 提供受管理應用程式及 Launcher 啟用所需的值與介面，目標為 `net8.0`，只依賴 BCL。`Nvt.Core.Launcher.Persistence` 提供有界 raw state access 與精確 app-state writer。verification 與 internal Windows lease adapter 如下所述。純路徑正規化遵循目前平台的規則。`Nvt.Core.Launcher.Coordination` 透過注入的 process 介面執行 READY supervision；process creation 與原生 custody 由對應機制負責。

抽取範圍包含版本與內容身分、descriptor、套件政策介面、正規化套件結果、不可變應用程式與 Launcher 狀態、轉移函式、durable snapshot 比較、通用 inventory、刪除 owner 保護、repository/state 介面、raw state access 與 writer custody。`UpdateSourceRegistry.cs` 僅抽取 `VersionSourceRegistryState`。`VersionManagementPolicy.cs` 僅抽取 inventory 與通用刪除判斷；保留門檻、自動刪除政策與探索通知留在 NFC。`LauncherMutationFence.cs` 抽取保護值、介面及 writer-scoped experience guard；嚴格 JSON projection adapter 留在 NFC。原生 custody 結構屬於 Files。嚴格 wire DTO/codec 由產品 owner 保留；execution token、ZIP plan 與測試 hook 為 internal。

## Contracts

[ProductDescriptor](../../../src/Nvt.Core/Launcher/Contracts/ProductDescriptor.cs) 要求明確提供 product、runtime、registry、執行檔及 protocol 名稱、Bootstrap 檔名與 archive-root callback。執行檔路徑必須是安全的斜線分隔相對路徑；Bootstrap 與每次 callback 的 archive root 必須是單一安全名稱。空白、絕對路徑、目錄跳脫、alternate stream、反斜線、C0 控制字元、不合法標點及 Windows device name 都會被拒絕。路徑最多 512 字元；DEL 與 C1 保留來源的接受行為。每次 callback 都重新驗證，設定不代表內容信任。

[ManagedAppVersion](../../../src/Nvt.Core/Launcher/Contracts/ManagedAppVersion.cs) 保留 canonical stable 三段版本解析、數值排序及 invariant 格式。[UpdateCatalogVersionSnapshot.Create](../../../src/Nvt.Core/Launcher/Contracts/UpdateCatalogVersionSnapshot.cs) 要求明確且為正數的套件與 UTF-8 notes 上限、UTC metadata、最多 512 字元的安全相對套件路徑、上限內的正套件長度、小寫 SHA-256、存在且符合上限的 notes 及已定義的通知政策。NFC 凍結上限為 134,217,728 套件 bytes 與 65,536 notes bytes；嚴格 ZIP 與 timestamp wire 文法仍由 NFC 驗證。

Catalog admission identity 保留 `version|relative-package-path|invariant-package-size|package-sha256|release-manifest-sha256`。來源根目錄、發布時間、notes 與通知政策不納入 identity。`VerifiedUpdateCandidate` 保留版本、admission identity 與 notes；`ManagedVersionAdmission` 另保留精確 manifest digest。不得由路徑推導 admission。

[IProductPackagePolicy](../../../src/Nvt.Core/Launcher/Contracts/PackageContracts.cs) 接收精確 manifest bytes，回傳產品已驗證的正規化事實。`archivePaths: null` 保留已安裝驗證模式。必要 NFC adapter 驗證嚴格 schema、精確 product/runtime 與封閉 payload，才回傳 `PackageManifest`。`PackageLauncher` 是尚未綁定 owner 的解析宣告；沒有預設接受的 policy 或 fallback adapter。

[ManagedLauncherIdentity.Create](../../../src/Nvt.Core/Launcher/Contracts/ManagedLauncherIdentity.cs) 要求 descriptor、明確且不超過 200,000,000 bytes 的正執行檔上限，以及所有精確 owner/version/hash/protocol/path/size 欄位。NFC 提供 200,000,000 bytes。Protocol 精確為 `1`；執行檔路徑以 ordinal 比較 descriptor；非空白 owner admission 最多 2,048 字元；兩個 digest 都必須是小寫 SHA-256。`MatchesOwner` 以 ordinal 比較應用版本、admission 字串與 manifest digest。`ManagedImmutableBootstrapIdentity.Create` 保留精確 descriptor root 檔名與 200,000,000-byte 上限。

[ManagedPackageResults](../../../src/Nvt.Core/Launcher/Contracts/ManagedPackageResults.cs) 保留 install、verification、executable-lease 與 installed-launcher issue 值及成功條件，包括 `HasSupportedManagedLauncher`。`IManagedExecutableLaunchLease` 提供可 Dispose custody、精確 executable/working-directory 及啟動前最終同步驗證；此 port 未宣告 process starter；其 internal Windows adapter 將 custody 委派給 Files。

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

NFC 使用建置時透過 `core-packages.json` 下載的已驗證版本化 Core 套件，並使用精確 `[x]` 版本、lock 檔、locked restore 與限定至下載資料夾的來源對應。清單記錄每個套件的 Release 標籤與 SHA-256。Core 與 NFC 獨立 release。只有相應 NFC adapter 已使用 Core 並保持完整 values、event traces 與 output bytes，才刪除重複 executable bodies。影響 UI 的採用要求相同環境下 decoded pixels 零差異。八個 legacy font 值保持不變；Bootstrap package wiring 需要獨立的 Launcher 採用授權。

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

NFC 保留嚴格的 manifest 與 admission schema、產品 payload 允許清單、發行信任、韌體中繼資料及安裝政策。NFC 在獨立 pull request 採用移轉機制，使用建置時透過 `core-packages.json` 下載的已驗證版本化 Core 套件，並使用精確 `[x]` 版本、lock 檔、locked restore 與限定至下載資料夾的來源對應。清單記錄每個套件的 Release 標籤與 SHA-256。只有呼叫端已使用 Core，且原有套件、安裝與產品相容性斷言以不變的值及輸出位元組通過後，NFC 才刪除其通用讀取器。Core 讀取器測試不能證明 NFC 的產品或像素一致性。

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

NFC 保留嚴格 schema、產品 payload role 與 allowlist、wire grammar、release metadata、韌體資料、信任與發行權限。採用時使用建置時透過 `core-packages.json` 下載的已驗證版本化 Core 套件，並使用精確 `[x]` 版本、lock 檔、locked restore 與限定至下載資料夾的來源對應。清單記錄每個套件的 Release 標籤與 SHA-256。原有 NFC schema、套件、安裝、完整值、trace 與輸出位元組斷言通過後，才能刪除已移轉的通用 verifier 與 reader。影響 UI 的採用也必須在同一 recorded environment 維持 decoded pixels。Core 合成測試不能證明 NFC 產品或像素一致性；此 API 也不授權 Bootstrap 套件接線。

### 與凍結原始碼的刻意差異

Core 與凍結的 NFC verifier 有三處不同。每一處都是拒絕原始碼以其他方式處理的輸入，沒有任何一處會接受原始碼拒絕的輸入。

- `VerifyAsync` 在讀取前先檢查套件 stream 可讀取且可 seek，並檢查候選套件大小不超過 `MaximumPackageBytes`。任一項不符時回傳 `PackageUnavailable`。
- 讀取途中套件內容改變時，結果是 `PackageUnavailable`。原始碼回傳 `PackageMismatch`。
- manifest 的檔案項目若名為 `RELEASE-MANIFEST.json` 或 `SHA256SUMS.txt`，會以 `InvalidPayload` 拒絕。

## Raw state access 與精確 writer custody

公開 API 位於 `Nvt.Core.Launcher.Persistence`。既有 read/load/save 類別與 `IVersionManagerStateReader` 仍位於 `Nvt.Core.Launcher.Activation`；`ILauncherBootstrapStateStore` 保留原有簽章，沒有 writer acquisition 成員。

```csharp
FileSystemVersionManagerWriteLease.TryAcquireAsync(
    string statePath, TimeSpan waitTimeout, CancellationToken cancellationToken)
    // ValueTask<VersionManagerWriteLeaseResult>
VersionManagerStateFile(string path, int maximumBytes)
LauncherBootstrapStateFile(string versionManagerStatePath, string pathSuffix, int maximumBytes)
LauncherBootstrapStateFile.DerivePath(string versionManagerStatePath, string pathSuffix)
    // string
```

兩個 raw file collaborator 都提供 `StatePathIdentity` 及 `ReadAsync(token) -> ValueTask<byte[]?>`。`VersionManagerStateFile` 另提供 `TryAcquireWriteLeaseAsync(waitTimeout, token)` 與 `WriteAsync(bytes, token) -> ValueTask`。`LauncherBootstrapStateFile` 提供 `TryWriteAsync(bytes, token) -> ValueTask<LauncherBootstrapStateSaveResult>`。NFC 明確提供凍結的 app-state 上限 **1,048,576 bytes**、launcher-state 上限 **65,536 bytes** 及 suffix `.launcher-bootstrap.v1.json`。Core 要求明確正值上限及非空白固定 suffix，不提供產品路徑或上限預設值。固定 suffix 會附加在完整正規化 app-state 路徑後，不同 canonical app-state path 保持 injective mapping。

`IVersionManagerStateStore` 繼承原有 reader 介面，保留 `TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken)`、`SaveAsync(VersionManagerState state, CancellationToken cancellationToken)` 與預設 `TrySaveAsync`。預設 save mapping 傳遞取消，將 `IOException`、`UnauthorizedAccessException`、`InvalidOperationException` 對應至 `VersionManagerStateSaveIssue.Unavailable`，其他錯誤仍向外傳遞。

`VersionManagerWriteLeaseIssue` 保留 `None`、`Busy`、`Unavailable`。`VersionManagerWriteLeaseResult(issue, IDisposable? lease = null)` 保留成功結果必須擁有一個 disposable 的 invariant 與訊息 `A successful writer lease must own exactly one handle.` `Issue` 與 `IsAcquired` 描述取得結果；**authority 必須透過 `HoldsStatePath(statePath)` 檢查**。該方法驗證非空白參數，要求結果未 Dispose、custody 是 internal production 實作、handle 開啟且有效，並以 ordinal 比較正規化精確路徑。任意 disposable、其他路徑、已 Dispose 或 closed custody 都不授予 authority。`Dispose()` 只釋放一次；歷史 `IsAcquired` 在 Dispose 後仍為 true，與來源一致。

Acquisition 先驗證非空白路徑，再拒絕負等待值；零仍立即嘗試。Identity 是去除尾端 separator 的完整路徑，Windows 再以 invariant 大寫正規化。Sibling key 為 `.{original-state-filename}.{first-24-lowercase-SHA256-characters}.writer.lock`，對 UTF-8 identity bytes 計算雜湊。Lock 使用 `OpenOrCreate`、`ReadWrite`、`FileShare.None`、buffer size 1 與 `WriteThrough`。Retry 保留 50 ms，每次 delay 不超過剩餘等待值。只有 native sharing code 32、33 在 elapsed 達到上限時回傳 `Busy`。原有路徑／目錄、存取及其他 I/O 錯誤保持 `Unavailable`，取消向外傳遞。最後一次 retry 先嘗試開啟，失敗時才判斷 deadline。Dispose 後可以留下 lock file；檔案存在本身不授予 authority。

Raw read 保留來源的存在及檔案層級 reparse 檢查，再以 `Open`、`Read`、`FileShare.Read`、64 KiB buffer、`Asynchronous | SequentialScan` 開啟。長度小於 1 或超過明確上限時，在 capture 前拒絕。缺少、linked、空、過大或長度改變的檔案回傳 null；提前 EOF 保持 `EndOfStreamException`。完整 held stream 讀取只使用 Files 的 `BoundedFileReader.ReadAndHashAsync` capture 模式，包含單一 trailing-byte probe 及最後長度／位置檢查。讀取已 admitted 內容時會傳遞取消。NFC 的嚴格 adapter 在 raw read 前區分 Missing，並將 null 對應為 Invalid；raw bytes 本身沒有 canonical-state authority。這些 raw 檢查不提供獨立的 Files Windows stable-path custody。

App write 超過上限時使用原有訊息 `Version-manager state exceeds its bounded size.` Launcher write 對 overflow 與原有 I/O／access／invalid-operation 類別回傳 `Unavailable`；取消向外傳遞。兩者皆使用 `IO.AtomicOutput.WriteBytesAsync(path, bytes, token)`，Launcher 沒有另一套 temp write、flush、move 或 cleanup engine。呼叫端從嚴格 decode、journal decision、encode、publication 到交易剩餘步驟，都持有同一 app-state writer；launcher collaborator 不取得第二個 writer。AtomicOutput 擁有 publication mechanics，啟用與復原 journal semantics 仍由 Launcher 擁有。

NFC 保留嚴格 app-state／launcher-state codec、schema predicate、JSON depth 上限 **32** 與 **16**、root binding、預設 LOCALAPPDATA 路徑及產品檔名。Adapter 在 raw publication 前保留精確 parent-path 訊息 `Version-manager state has no parent directory.` 與 `Launcher state has no parent directory.`，並先完成 state validation 與 encoding，保留產品 load/result mapping。沒有使用 permissive `LocalJsonDocument` 或預設接受的 authority adapter。

### Persistence 測試對照

凍結測試位於 `tests/NvtFwCombiner.Infrastructure.Tests/VersionManagement/`，Core 測試位於 `tests/Nvt.Core.Tests/Launcher/Persistence/`。

| 凍結來源案例 | Core 案例與保留邊界 |
| --- | --- |
| `JsonVersionManagerStateStoreTests.SaveAndLoadRoundTrip` | `StateFileTests.SaveAndLoadRoundTrip`：精確合成文件 bytes、adapter 解碼值、沒有 temp residue 與存活 caller custody；canonical JSON 斷言留在 NFC。 |
| `JsonVersionManagerStateStoreTests.CancelledSavePreservesPriorStateAndCleansTemporaryFile` | `StateFileTests` 同名方法涵蓋 app 與 launcher raw access，保留 prior complete bytes、合成值及 temp cleanup。 |
| `JsonLauncherBootstrapStateStoreTests.StatePathMappingIsInjective` | `StateFileTests` 同名方法使用明確合成 suffix，保留 full-path append，並涵蓋相鄰 canonical paths。 |
| `FileSystemVersionManagerWriteLeaseTests.RecoveryCapabilityIsLiveExactAndNotForgeable` | 同名 class/method，涵蓋等價路徑、其他路徑、任意 disposable 及 disposed capability。 |
| `FileSystemVersionManagerWriteLeaseTests.WindowsAbandonedProcessReleasesWriterForRestartConvergence` | 同名 class/method 使用 `hold-lock`，保留 60 秒啟動預算、10 秒 readiness bound、`LOCK_HELD` 與 ready marker、hard stop、2 秒 reacquisition bound。測試自行寫入 abandoned residue，child 不寫 temp file；residue 不授予 authority，後續完整 publication 也不刪除它。 |
| `JsonLauncherBootstrapStateStoreTests.NonCanonicalStateIsRejected` | `StateFileTests` 同名方法將精確 raw bytes 交給封閉的合成 codec 拒絕 malformed shape；原有 JSON schema 與 wire 案例留在 NFC。 |

`StateFileTests` 涵蓋兩個產品上限的少一／精確／多一、最小正上限 1 配合長度 0/1/2、零與負參數、空白 path/suffix、empty/missing 順序、file link 與實際 Windows sharing denial。`FileSystemVersionManagerWriteLeaseTests` 涵蓋實際獨立 writer、Windows canonical case folding、精確 hash key、Dispose、closed handle、physical exclusivity、取消及原有 result invariant。每次呼叫的 internal timing operations 以確定性方式檢查零與一 tick 等待、49/50/51 ms 邊界、精確 deadline 成功及 native code 31/32/33/34，不改變 global state。

`StatePublicationTests` 使用 IO 既有 per-call physical stream seam，實際寫入 prefix、使 async／disk flush 失敗，或在實際 disk flush 後、move 前取消，檢查 prior complete bytes、cleanup 與存活 writer custody。另涵蓋實際 native move denial、成功替換後的後續失敗、byte-identical replacement 的 native file identity 改變，以及 publication gate 前後的 writer contention。`StateStorePortTests` 只檢查 default-port exception mapping，不提供 physical write evidence。Native Windows 案例在其他系統明確 skip；無法建立 symbolic link 時也明確 skip。所有合成 fixture 使用唯一的系統 temp folder。

### Persistence 採用規則

NFC 使用建置時透過 `core-packages.json` 下載的已驗證版本化 Core 套件，並使用精確 `[x]` 版本、lock 檔、locked restore 與限定至下載資料夾的來源對應。清單記錄每個套件的 Release 標籤與 SHA-256。採用時使用套件，不以 ProjectReference 指向 Core checkout。嚴格 codec 與 state path 保持 narrow adapter；所有 launcher recovery／setup consumer 都共用這個存活 writer capability，檢查精確 app-state path，不建立第二個 lock owner。Caller 使用 Core owner，且原有產品 schema、完整值、trace、輸出 bytes 與 recovery evidence 通過後，NFC 才刪除已移轉的 writer／raw-read／atomic-publication 本體。影響 UI 的採用仍須在相同環境維持 decoded pixels 與既有 legacy font 值；這些 API 不授予 Bootstrap package wiring 或 release authority。

## Held executable lease

`Nvt.Core.Launcher.Windows.StableManagedExecutableLaunchLease` 是既有公開 `IManagedExecutableLaunchLease` 的 internal adapter，不新增公開 factory 或原生結構。Files 擁有所有原生路徑、樹、promotion 與相對寫入 custody。lease 提供精確 held `ExecutablePath`、其 parent `WorkingDirectory` 及 `TryValidateForStart()`。呼叫端須讓 lease 保持存活直到最後驗證與 process 建立；process 建立由 Processes 負責。

internal acquisition 與 measurement 必須提供正值 `maximumExecutableBytes`。NFC 透過符合 descriptor 的 `ManagedLauncherIdentity.Create` 合約提供凍結的 200,000,000 位元組 launcher 上限。application consumer 保留自己的 admitted package 上限，不會把 launcher 上限默默套用到無關 application 檔案。預期大小無效或 digest 為空，保留在路徑取得前回傳 `Tampered` 的順序。unsafe、reparse 或 changed custody 對應 `UnsafePath`；無法存取、contended 或 unavailable custody 對應 `Unavailable`。

`TryCreateAsync(ownedCustody, executableRelativePath, expectedSize, expectedSha256, maximumExecutableBytes, token)` 在每個結果都接收 custody 所有權。依序檢查取消、正值上限、精確長度、凍結 PE 條件，使用 `Files.BoundedFileReader.ReadAndHashAsync` 對完整 held bytes 計算雜湊、ordinal 比對 digest，再驗證封閉 held tree。不新增私有雜湊迴圈。成功時所有權轉入 lease；失敗或取消釋放串流與 custody。完整內容 EOF 與最後長度／位置檢查仍由 Files 提供。

`TryCreateFromVerifiedTreeAsync` 是獨立的 internal 路徑，要求呼叫端已在相同 held tree 下驗證完整 package 內容；保留精確長度、PE 與 topology 檢查，不重複雜湊已證明內容。outer-launcher 路徑雜湊自己的執行檔，同時持有周圍 declared tree 的封閉 topology；其他 payload 位元組驗證屬於完整 application activation。兩者均不驗證產品 JSON，也不取代必要 NFC admission adapter。僅捕捉 topology 不授予 release content 權限。

PE 檢查保留最少 64 位元組 DOS header、精確 `MZ`、`0x3C` 的 signed little-endian offset、offset 至少 64 且不超過長度減四，以及精確四位元組 `PE\0\0` signature。`CopyToAsync` 使用 create-new 輸出、凍結的 65,536 位元組 buffer、非同步 flush 與 flush-to-disk，原內容 custody 持續存活。measurement 使用相容 net8.0 的小寫 SHA-256。

來源至 Core lease 測試對照：

| 凍結來源案例 | `StableManagedExecutableLaunchLeaseTests` Core 案例 |
| --- | --- |
| `FileSystemManagedVersionRepositoryTests:65 AcquiredApplicationLeaseDeniesExecutableSwapUntilReleased` | 同名方法；合成 descriptor-relative 執行檔在 Dispose 前不可替換 |
| `FileSystemInstalledLauncherRepositoryTests:196 AddedChildAfterManifestProofFailsClosedAndReleasesCustody` | 同名方法；通用實體 proof 與 release 斷言 |
| `AcquiredLauncherLeaseClosesAncestorAdmissionRace` | 同名方法，保留一／兩層祖先 |
| `SameLengthLauncherBytesChangedReturnsTamperedBeforeLeaseAdmission` | `SameLengthExecutableSwapFailsContentAdmission` |
| `DeclaredNonLauncherMemberChangedIsRejectedByApplicationActivationAfterLeaseAdmission` | `VerifiedTreeDoesNotRehashContentButOuterLauncherDoes` 描述通用分工；完整產品 activation 斷言仍留在 NFC |
| `RepositoryLeaseRejectsLateChildBeforeLauncherProcessStart` | `SharedProbeLateChildFailsFinalStartValidation` |

其他案例涵蓋 PE 大小／offset 精確及相鄰邊界、全部 signature 位元組、199,999,999／200,000,000／200,000,001 位元組 sparse executable、每個 acquisition 與 transferred-custody 入口的無效上限、digest／大小順序、取消及 no-replace copy。實際 Windows child 證據使用共用 test probe：複製整個 framework-dependent 輸出目錄，只把 apphost 改為 descriptor 路徑。案例在 custody 持續存活時啟動該執行檔；確定性的 late-child gate 在產生 child 或 marker 前拒絕 start。

NFC 保留嚴格 manifest／admission schema、產品名稱與安全路徑政策、release-coupled identity 權限、firmware、信任、發行核准與產品 process orchestration。採用使用獨立版本 nupkg、精確 `[x]` 版本、locked restore、限制來源映射及記錄的 source／package SHA-256。只有原呼叫端透過此 port 在 start 前後保留相同 held identity、原 schema／產品／值／trace／output 斷言通過，且必要 UI 像素比較相同後，NFC 才刪除舊 lease adapter。Core 測試不授予 Bootstrap 套件 wiring 權限。

## 啟用與 mutation 協調

僅依賴 BCL 的 `Nvt.Core.Launcher.Coordination` API 包含：

- [ManagedActivationCoordinator](../../../src/Nvt.Core/Launcher/Coordination/ManagedActivationCoordinator.cs)：`(managedRoot, stateStore, repository, process, readyDeadline = null)` 與 `RunAsync(token) -> ValueTask<ManagedLauncherResult>`。
- [LauncherBootstrapCoordinator](../../../src/Nvt.Core/Launcher/Coordination/LauncherBootstrapCoordinator.cs)：`(managedRoot, statePath, appStateStore, launcherStateStore, repository, process, readyDeadline = null)` 與 `RunAsync(token) -> ValueTask<LauncherBootstrapResult>`。
- [ManagedMutationCoordinator](../../../src/Nvt.Core/Launcher/Coordination/ManagedMutationCoordinator.cs)：明確 managed root 與精確 state path、state／repository／fence 介面，以及必要的 `IManagedPackageSelection`、`IManagedRetentionPolicy`。提供 initialization、READY-qualified initialization、prepared install／delete、activation preparation／cancellation 與 retention acknowledgement。`ManagedMutationSnapshot` 包含 durable state、完整 inventory，以及獨立 state／inventory issue。
- [ManagedVersionSeedBootstrapper](../../../src/Nvt.Core/Launcher/Coordination/ManagedVersionSeedBootstrapper.cs) 與 `ManagedVersionSeedPolicy`：明確 destination／packaged-seed 介面、canonical single-admission seed policy，以及 `EnsureInitializedAsync(writerLeaseTimeout, token)`。
- [ManagedApplicationStartupCoordinator](../../../src/Nvt.Core/Launcher/Coordination/ManagedApplicationStartupCoordinator.cs)：執行中版本、必要 READY writer 與 `IManagedApplicationInitialization`；`CompleteStartupAsync(token, isReadOnly = false)` 回傳 READY outcome 與 durable snapshot。
- [InstalledApplicationCoordinator](../../../src/Nvt.Core/Launcher/Coordination/InstalledApplicationCoordinator.cs)：明確 `ProductDescriptor`、install root、state／repository／process 介面與必要 `IInstalledApplicationPresentation`。`StartAsync(token)` 使用相同 application supervisor。`ReadInstalledApplicationAsync(token)` 讀取 active installation；版本 overload 讀取精確 admitted installed version。

`InstalledApplicationInfo` 提供 product identity、installed version、verified executable path、display name、stable launch entry point 與 icon path。執行檔來自 repository-held custody；presentation 與 shortcut 路徑由 caller 的必要 adapter 原樣提供。Metadata read 驗證完整 admission、healthy inventory 與 executable custody，不做 initialization write 或 recovery。上層建立或移除捷徑；Core 不提供 shortcut writer 或 application registry。同一 app 的所有介面須綁定相同 product identity。Caller 提供 install root 與 update source；source 經已驗證 durable state 與目前 package selection 傳入，Core 不從產品名稱推導。

`IManagedApplicationProcess` 與 `IManagedLauncherProcess` 保留精確 executable lease、READY result／admission 與 authoritative lifetime 合約。Contained creation、inherited handle、Job、protocol decoding 與 cleanup 委派給 process implementation。協調層不宣告 native structure 或 execution token。Process 介面從 start entry 起執行 READY budget；凍結的 cleanup-confirmation extension 最多仍為十秒。

### Writer scope 與 durable 順序

Application supervision 在 load、root validation、完整 inventory、executable custody、launch-journal save、READY 與 commit／rollback 全程持有 state store 的精確 writer receipt。State-store adapter 必須為其精確 canonical app-state path 取得 production writer。預設 READY deadline 為 **20 秒**，application writer wait 為 **五秒**。正值 READY override 原樣傳遞；零與負值依凍結 constructor 順序拒絕。

Launcher startup 使用同一 app-state writer，wait 為 **250 ms**。Process creation 前保存 requested 與 recorded launcher phase；nested READY 期間釋放 writer，之後重新取得同一 writer 並重載兩份 durable state。Commit 要求重載後的 admission、root、active app 與精確 recorded launcher identity 一致。Contention、authority 改變及 save failure 保留 typed outcome；launcher-state store 不提供第二個 writer。Recorded fallback 不以 directory scanning 或 app candidate 取代。

一般 active-attempt guard 只有在 authoritative `Exited` 後才能清除。`Active` 與 `Unavailable` 保留 guard 並阻止第二次 launch。App／launcher journal 保留凍結 cross-journal exclusion predicate，包括 active launcher guard 與 app candidate／rollback recovery 的有限 overlap。Admission 後的一般 failure 與 candidate rollback 分開處理。

`ManagedMutationCoordinator` 在載入或改變 mutation authority 前，另檢查 `VersionManagerWriteLeaseResult.HoldsStatePath(statePath)`。任意 disposable、foreign-path capability 與已 Dispose receipt 回傳 unavailable state。單一 instance 的 mutation semaphore 排序呼叫；production app-state writer 提供跨 process exclusivity。不以私有 lock-file owner、latest-snapshot save coordinator 或 undo service 取代 durable journal。

Install 保留 prepare-save、repository promotion、完整 inventory、retention advice、commit-save 順序；commit 失敗或中斷保留 prepared journal。Recovery 只 admission 與 journal 精確相符且健康的 observed payload；不存在的 install target 可清除 journal，identity 不符仍為 unadmitted。Delete 保留 policy、明確 rollback-loss consent、launcher fallback-owner retirement、prepare-save、guarded filesystem delete、inventory、commit 順序。精確 target 已不存在仍可收斂為 committed；recovery failure 或 unavailable inventory 保留 journal。完整 durable source／registry authority 在兩種轉移後保持不變。

`IManagedPackageSelection` 必須從 caller 目前已驗證 catalog 選出指定版本；source、active-version 或 catalog authority 改變時回傳 null。Core 重新檢查回傳版本，並保留 package factory 的機械 identity／limit 檢查。Source check、discovery supersession 與 source／session／UI snapshot 留在上層，進入 mutation 前由上層 supersede discovery。`IManagedRetentionPolicy` 提供 NFC 凍結 advice：成功 update 且超過 **三個 healthy version** 才提醒，降至 **三個或以下** 可清除提醒。Core 儲存 reminder，不提供 retention threshold、自動刪除或 consent policy。

一般 initialization 以 zero wait 嘗試 writer；READY-qualified initialization 使用五秒 app budget，writer 可用後重載。唯讀 initialization 不取得 writer，也不恢復 prepared mutation。Startup 在每種模式都先回報 READY；只有 `Reported` 使用有界 writer wait，唯讀 startup 始終走唯讀路徑。Seed import 在 preflight 前檢查正值 writer override，不覆蓋 invalid 或不同 root 的 durable state，取得 lease 後重載，只接受一個 canonical admission 與一個 healthy inventory row，不以 directory scanning 尋找 seed。

### 協調測試對應

凍結來源位於 `tests/NvtFwCombiner.Application.Tests/VersionManagement/`；Core 位於 `tests/Nvt.Core.Tests/Launcher/Coordination/`。

| 凍結來源 case | Core case |
| --- | --- |
| `ManagedActivationCoordinatorTests.cs:11 ReadyCommitsCandidate` | `ManagedActivationCoordinatorTests.ReadyCommitsCandidate` |
| `ManagedActivationCoordinatorTests.cs:33 FailureRollsBackExactlyOnce` | `ManagedActivationCoordinatorTests.FailureRollsBackExactlyOnce` |
| `ManagedActivationCoordinatorTests.cs:200 CandidateLaunchJournalFailureStartsNoProcess` | `ManagedActivationCoordinatorTests.CandidateLaunchJournalFailureStartsNoProcess` |
| `ManagedActivationCoordinatorTests.cs:277 ReadyCommitFailureRestartsDirectlyIntoRecordedRollback` | `ManagedActivationCoordinatorTests.ReadyCommitFailureRestartsDirectlyIntoRecordedRollback` |
| `ManagedActivationCoordinatorTests.cs:310 RollbackCommitFailureRestartsOnlyRecordedFallback` | `ManagedActivationCoordinatorTests.RollbackCommitFailureRestartsOnlyRecordedFallback` |
| `ManagedActivationCoordinatorTests.Concurrency.cs:10 ConcurrentLaunchersStartRequestedCandidateOnlyOnce` | `ManagedActivationCoordinatorTests.ConcurrentLaunchersStartRequestedCandidateOnlyOnce` |
| `ManagedActivationCoordinatorTests.ActiveLaunchAttemptRecovery.cs:9 ActiveTerminationUnconfirmedBlocksSecondRunThenRecoversAfterExit` | `ManagedActivationCoordinatorTests.ActiveTerminationUnconfirmedBlocksSecondRunThenRecoversAfterExit` |
| `LauncherBootstrapCoordinatorTests.cs:16 FirstVerifiedLauncherBecomesActiveOnlyAfterReadyAndDurableReload` | `LauncherBootstrapCoordinatorTests.FirstVerifiedLauncherBecomesActiveOnlyAfterReadyAndDurableReload` |
| `LauncherBootstrapCoordinatorTests.cs:364 EveryCandidateStateSaveFailureFailsClosed` | `LauncherBootstrapCoordinatorTests.EveryCandidateStateSaveFailureFailsClosed` |
| `VersionManagementExperienceTests.Transaction.cs:9 InstallCommitSaveFailureConvergesFromDurableJournalAfterRestart` | `ManagedMutationCoordinatorTests.InstallCommitSaveFailureConvergesFromDurableJournalAfterRestart` |
| `VersionManagementExperienceTests.DeleteRecoveryTransaction.cs:84 DeleteRecoveryCommitSaveFailureRemainsJournaledUntilNextRestart` | `ManagedMutationCoordinatorTests.DeleteRecoveryCommitSaveFailureRemainsJournaledUntilNextRestart` |
| `ManagedActivationCoordinatorTests` 與 concurrency／fail-closed／launch-lease／active-recovery partial 的其餘通用 cases | `ManagedActivationCoordinatorTests` 與 `LauncherBootstrapCoordinatorTests` 的同名方法 |
| `LauncherBootstrapCoordinatorTests` 與 `ActiveRecoveryMatrix` cases | `LauncherBootstrapCoordinatorTests` 同名方法；builder／store／process double 保留來源 assertions |
| `VersionManagementExperienceTests.Transaction`、`DeleteRecoveryTransaction` 的通用 cases | `ManagedMutationCoordinatorTests` 同名方法；source／catalog policy 改用合成 selection 介面 |
| `ManagedVersionSeedBootstrapperTests` | `ManagedVersionSeedBootstrapperTests` 同名方法 |
| `ManagedApplicationStartupCoordinatorTests` READY／唯讀 dispatch | `ManagedApplicationStartupCoordinatorTests` 同名方法 |
| `ManagedApplicationStartupCoordinatorTests.ManagedReadyInitializationWaitsThenReloadsDurableState` 與一般 zero-wait contention | `ManagedMutationCoordinatorTests.ManagedReadyInitializationWaitsThenReloadsDurableState`，使用 production writer |

`CoordinationCrashMatrixTests` 交叉七種 app state、五種 launcher state 與三種 lifetime result，共 **105 個完整 state／trace cases**；另涵蓋 READY 後 commit 前 crash、兩種 rollback READY commit failure、guard-clear save failure，以及 inventory unavailable 時兩份 serialized journal 的保存。封閉合成 codec 將每個 durable 欄位序列化至實際 temporary state file；process trace 證明 app READY 全程持有 writer，而 launcher READY 釋放後重新取得。`ManagedMutationCoordinatorTests` 另涵蓋真實 writer capability rejection、唯讀 recovery exclusion、兩份 journal fencing、consent／retirement 順序、cancellation 與 callback identity 檢查。

`CoordinationBoundaryTests` 涵蓋預設 20 秒 READY、五秒 app writer、250 ms launcher writer、各預設值前後一 tick、最小正值 override、零／負值、最大 `TimeSpan` 與 canonical seed 零／一／二的數量邊界；這些 cases 驗證協調層 forwarding 與 check order。Native elapsed-time／handle／Job 行為由 contained-process implementation 負責。`InstalledApplicationCoordinatorTests` 組合兩個合成 app，檢查完整 installed／presentation facts、source 保存、共用 READY path、唯讀行為、adapter failure 與 custody disposal。Runtime fixture 使用 system temp 下的唯一資料夾；合成協調測試不證明產品 wire schema、firmware 或 UI parity。

### 協調採用規則

NFC 保留嚴格 state／manifest／catalog codec、product identity／payload policy、source／registry policy、discovery／session／UI composition、retention advice、consent、firmware、信任與發行權限。Adapter 提供 validated state／selected package、精確 state-store writer、product-bound process／repository 介面及 presentation path。完整 engine behavior 必須將 native process、physical repository implementation 與這些 owner 組合。

NFC 在建置時透過 `core-packages.json` 下載已驗證的版本套件，並以精確 `[x]` 套件版本、locked restore 與限定至下載資料夾的來源對應使用 Core。清單記錄每個套件的 Release 標籤與 SHA-256。只有 adapter 使用 Core，且原有完整值、process／writer trace、durable bytes 與 recovery assertion 不變時，NFC 才刪除已移轉的 supervisor 及 journal／mutation 本體。影響 UI 的採用另須相同環境下 decoded pixels 不變，並保持八個 legacy font 值；此抽取不授予套件發布或 Bootstrap package-wiring 權限。
