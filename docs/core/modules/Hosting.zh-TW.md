[English](Hosting.md) | [中文](Hosting.zh-TW.md)

# Hosting：KnownFolders 與 HostEnvironment

`Nvt.Core.Hosting` 有兩個靜態讀取器，讓應用程式問作業系統自己在哪裡執行。每個讀取器是 Core 內該項禁用呼叫的唯一位置。應用程式呼叫讀取器，自己的程式碼不再直接呼叫 `Environment.GetFolderPath` 與 `Environment.GetEnvironmentVariable`。兩個讀取器都列在 `eng/code-health/seam-owners.json` 的永久 seam。

## 公開 API

```csharp
public static class KnownFolders
{
    public static string GetLocalApplicationData();
}

public static class HostEnvironment
{
    public static string? GetVariable(string name);
}
```

- `KnownFolders.GetLocalApplicationData` 回傳目前使用者不漫遊的應用程式資料資料夾。Windows 上是 `%LOCALAPPDATA%`。只有作業系統沒有這個資料夾時才回傳空字串。
- `HostEnvironment.GetVariable` 回傳行程環境變數的值，未設定時回傳 null。名稱為 null 或空字串時擲出 `ArgumentException`。Windows 比對名稱時不分大小寫。

## 來源

為 NFH 重寫新增的工作（owner 2026-11-09 17:34：「core 部分可以再 pc 做，優先改 nfh 要導入的地方」）。NFH 用它取代 `AppGeneralSettingsStore` 內的 `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)`，以及 `AppGeneralSettingsStore` 與 `StartupPerfTracker` 內的 `Environment.GetEnvironmentVariable`。讀取器不含政策，名稱與資料夾的規則仍由應用程式決定。

## 驗證

[`HostingTests`](../../../tests/Nvt.Core.Tests/Hosting/HostingTests.cs) 涵蓋絕對路徑的資料夾、已設定的變數、未設定的變數，以及 null 或空字串名稱。
