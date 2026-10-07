// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Avalonia.MessageCenter;

/// <summary>Identifies an interaction for application-owned activity recording.</summary>
public enum MessageCenterInteraction
{
    /// <summary>The modal was requested to open, including repeated opens.</summary>
    Opened,

    /// <summary>An explicit refresh was requested.</summary>
    RefreshRequested,

    /// <summary>An export completed in the same generation.</summary>
    ExportSucceeded,

    /// <summary>An expected export or current picker failure occurred.</summary>
    ExportFailed,
}
