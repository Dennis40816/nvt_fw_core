// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.MessageCenter;

/// <summary>Supplies passive counts and admitted metadata without transferring history ownership.</summary>
public interface IMessageCenterProvider
{
    /// <summary>Gets the active diagnostic count without projecting activity rows.</summary>
    int ActiveDiagnosticCount { get; }

    /// <summary>Gets the activity count before display filtering without projecting rows.</summary>
    int ActivityCount { get; }

    /// <summary>Captures ordered, admitted metadata over immutable host entries without projecting rows.</summary>
    /// <returns>A snapshot in host order, unaffected by subsequent changes to the host's history.</returns>
    /// <remarks>Each projection must return a row with the same severity as its captured metadata.</remarks>
    IReadOnlyList<MessageCenterActivity> CaptureActivity();
}
