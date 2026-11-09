[English](Hosting.md) | [中文](Hosting.zh-TW.md)

# Hosting: KnownFolders and HostEnvironment

`Nvt.Core.Hosting` holds the two static readers that a host uses to ask the operating system where it runs. Each reader is the one place in Core for its banned call. A host calls the reader and keeps `Environment.GetFolderPath` and `Environment.GetEnvironmentVariable` out of its own code. Both readers are listed as permanent seams in `eng/code-health/seam-owners.json`.

## Public API

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

- `KnownFolders.GetLocalApplicationData` returns the per-user folder for application data that does not roam. On Windows this is `%LOCALAPPDATA%`. The result is empty only when the operating system has no such folder for the user.
- `HostEnvironment.GetVariable` returns the value of a process environment variable, or null when it is not set. A null or empty name throws `ArgumentException`. Windows compares names without regard to case.

## Provenance

New work for the NFH rewrite (owner 2026-11-09 17:34: 「core 部分可以再 pc 做，優先改 nfh 要導入的地方」). NFH replaces `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` in `AppGeneralSettingsStore`, and `Environment.GetEnvironmentVariable` in `AppGeneralSettingsStore` and `StartupPerfTracker`. The readers do no policy. A host keeps its own rules for names and folders.

## Verification

[`HostingTests`](../../../tests/Nvt.Core.Tests/Hosting/HostingTests.cs) covers an absolute folder path, a set variable, an unset variable and a null or empty name.
