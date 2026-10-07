// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>The tool's synchronous close decision. The decision must not open a dialog.</summary>
public enum RuntimeQueryExitResult
{
    /// <summary>The tool can close now and commits to its normal close action.</summary>
    Closing,
    /// <summary>Confirmation is needed. Do not close or open a dialog.</summary>
    NeedsConfirmation,
    /// <summary>The tool rejected closing. Do not close.</summary>
    Rejected
}
