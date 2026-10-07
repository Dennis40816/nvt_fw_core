// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Avalonia.MessageCenter;

/// <summary>Supplies application wording and count formatting for the presentation.</summary>
public interface IMessageCenterText
{
    /// <summary>Gets the action label for disclosing debug activity.</summary>
    string ShowDebugActivityLabel { get; }

    /// <summary>Gets the action label for hiding debug activity.</summary>
    string HideDebugActivityLabel { get; }

    /// <summary>Gets the idle refresh action label.</summary>
    string RefreshDiagnosticsLabel { get; }

    /// <summary>Gets the refresh progress label and announcement.</summary>
    string RefreshingDiagnosticsLabel { get; }

    /// <summary>Gets the successful export status.</summary>
    string DiagnosticsExportedLabel { get; }

    /// <summary>Gets the failed export status.</summary>
    string DiagnosticsExportFailedLabel { get; }

    /// <summary>Formats the unfiltered session activity count.</summary>
    /// <param name="count">The unchanged provider count.</param>
    string FormatSessionActivitySummary(int count);

    /// <summary>Formats the accessible name using the active diagnostic count.</summary>
    /// <param name="count">The unchanged provider count.</param>
    string FormatMessageCenterAccessibleName(int count);

    /// <summary>Formats the idle system announcement using the active diagnostic count.</summary>
    /// <param name="count">The unchanged provider count.</param>
    string FormatSystemDiagnosticAnnouncement(int count);
}
