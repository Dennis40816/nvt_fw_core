[English](SourceFileNavigation.md) | [中文](SourceFileNavigation.zh-TW.md)

# SourceFileNavigation

只依賴 BCL 的程序啟動與預設應用程式開啟機制已抽取至 `Nvt.Core.SourceFileNavigation`，目標為 `net8.0`。本次遵循擁有者於 2026-10-05 決定的期限：可共用模組須於 2026-10-18 前移入 Core。工具採用仍是獨立任務，須經審查及擁有者核准。

## 凍結來源

NFU（`Dennis40816/nvt-event-buffer-replay`）、`origin/0.2.0`、commit `915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b`。

- `src/Nvt.Replay.Avalonia/SourceFileNavigator.cs`：`SourceFileOpenResult`（第 6–10 行）、`TryStart`（第 134–153 行）及 `Open` 的預設開啟分支（第 45–57 行）。
- `tests/Nvt.Replay.Avalonia.Tests/SourceFileNavigatorTests.cs`：已檢查既有覆蓋範圍；四個副檔名路由案例測試的是主機政策，因此留在 NFU。來源沒有可直接移植的 BCL 機制測試。

Core 調整可見性與命名空間、加入版權標頭與 XML 文件，並將兩個直接啟動程序的呼叫改為接受單次呼叫啟動委派的 internal 多載。公開方法傳入 `Process.Start`。沒有共用的可變啟動委派，也沒有公開測試 API。

## 公開 API

```csharp
public sealed record SourceFileOpenResult(
    bool Opened, bool ExactLine, string Application, string? Error = null);

public static class SourceFileNavigator
{
    public static bool TryStart(
        string executable, IReadOnlyList<string> arguments, out string? error);

    public static SourceFileOpenResult OpenDefault(string path);
}
```

[`TryStart`](../../../src/Nvt.Core/SourceFileNavigation/SourceFileNavigator.cs) 只設定 `ProcessStartInfo.FileName` 與 `UseShellExecute = false`，再依原順序將每個參數原樣加入 `ArgumentList`。`Process.Start` 正常返回即回傳 `true` 且 error 為 null，包含返回 `null` 的情況。遇到 `InvalidOperationException` 或 `Win32Exception` 時，回傳 `false` 及原樣的 `exception.Message`。

`OpenDefault` 只設定 `FileName = path` 與 `UseShellExecute = true`。正常返回（含 `null`）時結果為 `(true, false, "default application", null)`；相同兩種例外的失敗結果為 `(false, false, "default application", exception.Message)`。`ExactLine` 永遠為 false。兩個方法皆讓其他例外傳遞至呼叫端，不等待啟動的應用程式，也不驗證它是否已顯示檔案。

路徑驗證、正規化與存在檢查、Excel COM 自動化及清理、編輯器探索、路由順序、編輯器專用參數、行號下限處理、來源查找與 UI 文字仍留在 NFU。本輪不新增 Windows 專案，也不將編輯器探索或路由政策移入 Core。

## 驗證與採用

[`SourceFileNavigatorTests`](../../../tests/Nvt.Core.Tests/SourceFileNavigation/SourceFileNavigatorTests.cs) 使用合成路徑與單次呼叫委派，驗證凍結機制的行為。測試檢查執行檔／路徑值、參數順序（含空白、引號、空字串及 Unicode）、空參數清單、shell 旗標與未修改的程序設定；涵蓋返回 null 或尚未啟動的 Process 物件、兩種已處理例外的完整多行及空錯誤訊息、其他例外的傳遞、預設開啟的全部結果欄位，以及巢狀呼叫的隔離。所有測試皆不啟動真實程序。

既有套件還原完成後執行，不變更骨架或 lock files：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

NFU 後續採用時，以相同合成路徑、參數及假的程序啟動結果，比對凍結 BCL 分支與 Core。逐項比對 `ProcessStartInfo` 設定、參數順序、結果欄位及錯誤字串，保留例外傳遞與返回 null 仍視為成功的行為。NFU 路由測試及 inspector／raw 來源連結測試保留於原測試套件，僅替換兩個機制前後皆須執行。真實預設應用程式與編輯器 smoke 檢查須使用相同 OS 及應用程式清單，本次抽取尚未驗證。NFU 採用是另一個任務，改用有版本號的 Core 套件。
