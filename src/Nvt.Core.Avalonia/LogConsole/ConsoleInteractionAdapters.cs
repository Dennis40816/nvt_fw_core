// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

/// <summary>App-owned link capabilities and opening. Implementations must return promptly on the UI thread.</summary>
/// <remarks>The app resolves paths asynchronously and exposes cached availability here. Core performs no IO.</remarks>
public interface IConsoleLinkOpener
{
    /// <summary>Returns null when opening is available, otherwise a user-facing reason (including pending resolution).</summary>
    string? GetUnavailableReason(LinkTarget target);
    /// <summary>Queues opening the full target, including its line and column, through the app's adapter.</summary>
    void Open(LinkTarget target);
}

/// <summary>App-owned clipboard dispatch. Core supplies at most 65,536 UTF-16 characters.</summary>
public interface IConsoleClipboard
{
    /// <summary>Queues a clipboard write on the appropriate app TopLevel; implementations must return promptly.</summary>
    void SetText(string text);
}
