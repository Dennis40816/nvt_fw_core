// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>Tool-owned inputs for the six generic runtime commands.</summary>
/// <param name="ToolName">The tool name returned by ping.</param>
/// <param name="ToolVersion">The tool version returned by ping.</param>
/// <param name="GetCommands">Gets the completed command list in registration order when help runs.</param>
/// <param name="GetMainWindow">Gets the current main window when focus or default capture runs.</param>
/// <param name="Navigation">The tool's page navigation.</param>
/// <param name="DecideExit">
/// Synchronously decides whether closing can proceed when DecideExitRequest is unset.
/// Ignores request arguments and must not open a dialog.
/// </param>
/// <param name="Close">
/// Starts the normal close on the UI thread after Core returns the Closing response.
/// State can change between the decision and this call, for example when the user selects a file.
/// The tool should skip its own confirmation for an approved close.
/// </param>
public sealed record RuntimeQueryGenericCommandOptions(
    string ToolName,
    string ToolVersion,
    Func<IReadOnlyList<RuntimeQueryCommand>> GetCommands,
    Func<Window?> GetMainWindow,
    IRuntimeQueryNavigation Navigation,
    Func<RuntimeQueryExitResult> DecideExit,
    Action Close)
{
    /// <summary>Optional help text returned unchanged instead of the registered command list.</summary>
    public string? HelpText { get; init; }

    /// <summary>Optionally decides whether closing can proceed using the request's confirmation.</summary>
    /// <remarks>
    /// When set, this delegate replaces DecideExit. Core validates confirm before calling it and ignores other arguments.
    /// The delegate runs synchronously on the UI thread and must not open a dialog.
    /// Return NeedsConfirmation when approval is missing, Closing to approve closing, or Rejected when closing cannot proceed.
    /// </remarks>
    public Func<RuntimeQueryExitRequest, RuntimeQueryExitResult>? DecideExitRequest { get; init; }

    /// <summary>Optional capture replacement, called after Core validates the path and checks file existence.</summary>
    /// <remarks>
    /// The delegate owns frame waits, layout, capture and a write that never replaces an existing file.
    /// Core forwards the command's cancellation token and does not lay out the window before calling this delegate.
    /// Observe cancellation cooperatively and finish cleanup after committing a file.
    /// Failure codes and messages pass through unchanged. Exceptions escape the handler.
    /// </remarks>
    public Func<string, CancellationToken, Task<RuntimeQueryScreenshotResult>>? CaptureScreenshot { get; init; }
}
