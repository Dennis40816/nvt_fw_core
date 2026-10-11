# Core 套件下載

工具只提交小型 manifest，並在每次還原前下載 Core 套件，不提交套件檔案。
腳本需要 Python 3.10 或更新版本，且只使用標準函式庫。
Core 的 GitHub 儲存庫是公開的，下載不需要權杖。
Core Release 發布後不可變更。

## 採用腳本

1. 將 `fetch_core_packages.py` 原樣複製到工具的 `scripts/fetch_core_packages.py`。
   在工具的 pull request 記錄提供此腳本的完整 Core commit。
2. 將 [core-packages.example.json](core-packages.example.json) 複製為工具儲存庫根目錄的 `core-packages.json`。
   只保留工具有參考的套件。
3. 提交腳本與 manifest。
   在工具的 `.gitignore` 加入 `artifacts/core-packages/`。
4. 依照下方來源對應設定，在 `NuGet.config` 加入本機資料夾。
5. 將每個套件參考固定為確切版本，並更新工具的鎖定檔。
   鎖定檔仍固定每個套件的版本與內容雜湊。
6. 在工具的建置腳本、驗證腳本與 CI 中，每次還原前都執行下載腳本。
   若工作流程直接呼叫 `dotnet restore`，就在該命令前新增一個下載步驟。
   工作流程變更由 owner 推送。
7. 執行工具的鎖定還原、既有行為測試與發行冒煙測試。

工具儲存庫根目錄使用以下完整的 `NuGet.config`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="core-packages" value="artifacts/core-packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="core-packages">
      <package pattern="Nvt.Core" />
      <package pattern="Nvt.Core.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Core 的確切名稱與前綴優先於 `*`。
NuGet 只從本機資料夾解析 `Nvt.Core` 與 `Nvt.Core.*`。
其他套件使用 `nuget.org`。
詳見 [NuGet 來源對應規則](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#package-pattern-precedence)。
下載腳本不會退回其他套件來源。

在工具儲存庫根目錄執行：

```powershell
python -B scripts/fetch_core_packages.py
dotnet restore --locked-mode
```

腳本傳回非零結束碼時，CI 必須停止。
建置與驗證腳本也必須在下載或驗證失敗時停止，不得繼續還原。

## Manifest 與命令

每個套件項目包含 Release 標籤、資產名稱，以及該 Release 的 `SHA256SUMS` 所列的 SHA-256。
Manifest 使用 schema `1` 與 repository `Dennis40816/nvt_fw_core`。
未知欄位與重複資產名稱都是錯誤。
Release 標籤與資產名稱只允許英文字母、數字、`.`、`_` 與 `-`，且不得包含 `..`。
資產名稱必須以 `.nupkg` 結尾。
SHA-256 必須是 64 個小寫十六進位字元。

每個套件各自固定 Release，不要求所有套件使用相同版本。
`core-v<version>` 包含 `Nvt.Core` 與 `Nvt.Core.Avalonia`。
`Nvt.Core.Fonts` 使用獨立版本 `0.1.0` 與 `core-fonts-v0.1.0`。
[範例 manifest](core-packages.example.json) 已包含 `Nvt.Core.Fonts.0.1.0.nupkg`。
範例字型項目的全零 SHA-256 是佔位值，必須換成該 Release 的 `SHA256SUMS` 所列雜湊。

```xml
<ItemGroup>
  <PackageReference Include="Nvt.Core" Version="0.9.0" />
  <PackageReference Include="Nvt.Core.Avalonia" Version="0.9.0" />
  <PackageReference Include="Nvt.Core.Fonts" Version="0.1.0" />
</ItemGroup>
```

```text
python fetch_core_packages.py [--manifest PATH] [--dest PATH] [--offline] [--base-url URL]
```

`--manifest` 預設為目前目錄的 `core-packages.json`。
`--dest` 預設為 manifest 所在資料夾下的 `artifacts/core-packages`。
所有相對目的路徑都以 manifest 所在資料夾為基準。
`--offline` 只驗證現有檔案，不下載任何檔案。
`--base-url` 用於本機 HTTP 測試，取代 GitHub 的基底網址。
一般下載只允許 HTTPS。

腳本發送要求前會先驗證快取套件。
若快取雜湊錯誤，腳本會先發出警告，再下載替代檔案。
腳本驗證每次下載後，才以不可分割的替換動作更新目的檔案。
網路失敗最多嘗試三次，等待時間會短暫增加。
SHA-256 不符時不重試。
每次要求的逾時為 60 秒，回應大小上限為 256 MiB。

每個套件在標準輸出有一行以 `core-packages: ` 開頭的訊息。
成功時最後一行是 `core-packages: ` 加上目的資料夾的絕對路徑。
警告與錯誤寫入標準錯誤。
結束碼 `0` 代表成功，`1` 代表下載或驗證失敗，`2` 代表用法或 manifest 錯誤。

## 升級、回復與授權

升級時，將 manifest 改為新 Release 的資產名稱與 `SHA256SUMS` 中的 SHA-256。
更新確切套件參考與鎖定檔，再執行工具的採用驗證。
回復時，將較早 Release 的值放回 manifest，並還原其套件參考與鎖定檔。
字型套件使用同一份 manifest，並在 `release` 固定獨立的 `core-fonts-v<version>` 標籤。

工具發行時，從已驗證的 Core 下載套件取出 `LICENSE`，放入 `licenses/Nvt.Core/LICENSE`。
此授權涵蓋 Core 套件程式碼，包括 Fonts。
使用 Fonts 的工具也須隨附套件中的 `licenses/`，並保留 Inter 相依套件的授權與聲明。
詳見 [Fonts 授權義務](../../docs/core/modules/Fonts.zh-TW.md#授權義務與更新)。
Core 只能依其專有 `LICENSE` 散布。
工具本身的授權不會重新授權 Core。

## 沒有網路的沙箱

先在主機執行腳本。
讓沙箱可存取下載資料夾，並保持相同的 manifest 相對目的路徑。
再於沙箱內先驗證套件，然後還原：

```powershell
python -B scripts/fetch_core_packages.py --offline
dotnet restore --locked-mode
```
