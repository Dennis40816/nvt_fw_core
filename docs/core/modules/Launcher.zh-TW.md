[English](Launcher.md) | [中文](Launcher.zh-TW.md)

# Launcher 合約

`Nvt.Core.Launcher.Contracts` 提供套件驗證與受管理安裝共用的版本、內容身分、套件政策介面、執行檔租約介面及正規化結果值，目標為 `net8.0`，只依賴 BCL。NFC 必須保留 adapter，負責嚴格 schema、精確 product/runtime/registry 值、協定名稱、payload allowlist、來源及保留政策。這些 Core 值沒有建立新的 NFC 序列化 schema。

凍結的父版本基準：NFC（`nvt_fw_combiner`）、ref `origin/1.2.x`、完整 commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`。抽取的來源路徑：

- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedAppVersion.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedVersionRepository.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/VersionManagementPolicy.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/UpdateCatalogModels.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/LauncherBootstrapContracts.cs`
- `src/NvtFwCombiner.VersionManagement.Application/VersionManagement/ManagedLauncherEntry.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedInstallationLayout.cs`
- `src/NvtFwCombiner.VersionManagement.Infrastructure/VersionManagement/ManagedSetupTransactionDocuments.cs`

NFC Contracts 與 `docs/contracts/**` 仍是 R3 authority，只供唯讀行為檢查，不匯入 Core。對應的 catalog、release-manifest 與 launcher-bootstrap 合約維持不變。啟用階段、狀態機、根目錄解析、交易文件、process 程式碼、原生 custody、ZIP plan 及 repository inventory/delete 介面由各自的任務負責。

[`ProductDescriptor`](../../../src/Nvt.Core/Launcher/Contracts/ProductDescriptor.cs) 要求明確提供名稱與 callback。執行檔路徑必須是安全的斜線分隔相對路徑；Bootstrap 與每次 callback 回傳的 archive root 必須是單一安全名稱。空白、絕對路徑、目錄跳脫、alternate stream、反斜線、C0 控制字元、不合法標點及 Windows device name 都會被拒絕。路徑最多 512 個字元，與 NFC 相同。每個要求的版本都重新驗證 callback 結果。Descriptor 是執行期設定，不是內容信任證據。

[`ManagedAppVersion`](../../../src/Nvt.Core/Launcher/Contracts/ManagedAppVersion.cs) 保留 canonical stable 三段版本解析、數值比較及 invariant 格式。[`UpdateCatalogVersionSnapshot.Create`](../../../src/Nvt.Core/Launcher/Contracts/UpdateCatalogVersionSnapshot.cs) 讓原本 internal 的建構可在產品 admission 後使用。Factory 要求產品提供的套件位元組上限與 UTF-8 版本說明位元組上限（皆為正數）、UTC、結構安全且最多 512 字元的相對路徑、不超過套件上限的正長度、小寫 SHA-256、存在且不超過上限的 notes 及已定義的通知政策。NFC 提供凍結的上限（套件 134,217,728 位元組、版本說明 65,536 位元組），並仍負責 ZIP 路徑文法及嚴格時間戳記 wire 文法。Immutable properties 與 identity 組成維持：

```text
version|relative-package-path|invariant-package-size|package-sha256|release-manifest-sha256
```

設定的來源根目錄、發布時間、notes 與通知政策不納入 identity。`VerifiedUpdateCandidate` 保留版本、admission identity 與 release notes；`ManagedVersionAdmission` 另保留精確 manifest digest。不得由路徑推導 admission。

[`IProductPackagePolicy`](../../../src/Nvt.Core/Launcher/Contracts/PackageContracts.cs) 接收精確 manifest bytes。`archivePaths: null` 保留已安裝版本驗證模式。`PackageManifest` 攜帶通用的 product/runtime/version、完整正規化 file inventory，以及可為 null 的 `PackageLauncher`。Launcher 是尚未綁定 owner 的解析宣告。NFC adapter 必須先完成嚴格 manifest 與封閉 payload 驗證；之後 Core verifier 才綁定實際 owner admission 與精確 manifest hash。

[`ManagedLauncherIdentity.Create`](../../../src/Nvt.Core/Launcher/Contracts/ManagedLauncherIdentity.cs) 要求 descriptor、明確且不超過 200,000,000 bytes 的正執行檔上限，以及原有的 owner/version/hash/protocol/path/size 欄位。協定仍精確為 `1`，路徑以 ordinal 比較 descriptor，owner admission 上限仍為 2,048 字元，兩個 digest 都必須是小寫 SHA-256。`MatchesOwner` 精確比較應用程式版本、admission identity 與 release-manifest hash。`ManagedImmutableBootstrapIdentity.Create` 綁定 descriptor 的精確根目錄檔名，保留 200,000,000-byte 上限。沒有產品特定的 identity 預設值。

[`ManagedPackageResults`](../../../src/Nvt.Core/Launcher/Contracts/ManagedPackageResults.cs) 保留 install、verification、executable-lease 及 installed-launcher issue 名稱、數值與成功判斷，並保留 `HasSupportedManagedLauncher`。`IManagedExecutableLaunchLease` 是可 Dispose 的 custody，提供精確 executable/working-directory 與啟動前的最後同步驗證；實作與 process 行為留給各自的 owner。NFC wire DTO 與精確 `manual-only`/`notify` 映射仍由 adapter 負責。

移植前已完整閱讀三份凍結來源測試與其內部 helper。通用測試保留 `NonCanonicalOrNonStableVersionFailsClosed`、`PackageIdentityDoesNotIncludeConfiguredSourcePath`、`LauncherIdentityMatchesOnlyItsExactOwnerAdmission`、`ImmutableBootstrapIdentityAdmitsCanonicalMaximum`，並保留通用 round-trip、排序及 Launcher 驗證情境。產品 schema、真實套件及啟用狀態情境留在 NFC。新增合成測試涵蓋 descriptor 失敗路徑、明確上限、Bootstrap 檔名綁定、來源移動及不同 culture 下的 identity bytes、metadata 排除、factory 失敗、nullable policy 模式、全部結果成功條件及受限 public API。不需要 internal test access 或共用 project 修改。

Core 的例外訊息指出失敗的參數，文字可能與 NFC 不同。NFC 的 adapter 在呼叫端依賴時保留自己的訊息。

驗證：host 在本分支（基於 Core `main` `d47dfe3`）以 locked mode restore、建置 0 警告並執行完整 solution。DEL 與 C1 字元仍與 NFC 相同，會被接受。實際結果與兩次審查記在 pull request。
