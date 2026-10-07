// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.MessageCenter;

/// <summary>Rejects stale picker results and export status publications using a shared modal session.</summary>
/// <remarks>
/// The host supplies view identity, export I/O, and status callbacks. Await continuations retain the
/// caller's context; this workflow adds no dispatcher, synchronization, or cancellation policy.
/// </remarks>
public sealed class MessageCenterExportWorkflow
{
    private readonly MessageCenterSession _session;
    private readonly Func<string, CancellationToken, Task> _export;
    private readonly Action _succeeded;
    private readonly Action _failed;

    /// <summary>Creates a workflow over the host's session, exporter, and status callbacks.</summary>
    /// <param name="session">The session whose generation identifies each export context.</param>
    /// <param name="export">The sole I/O operation, invoked with the unchanged destination and token.</param>
    /// <param name="succeeded">Publishes success when the captured generation still matches.</param>
    /// <param name="failed">Publishes an expected export or current picker failure.</param>
    /// <exception cref="ArgumentNullException">A supplied session or delegate is null.</exception>
    public MessageCenterExportWorkflow(
        MessageCenterSession session,
        Func<string, CancellationToken, Task> export,
        Action succeeded,
        Action failed)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _export = export ?? throw new ArgumentNullException(nameof(export));
        _succeeded = succeeded ?? throw new ArgumentNullException(nameof(succeeded));
        _failed = failed ?? throw new ArgumentNullException(nameof(failed));
    }

    /// <summary>Runs the host exporter and publishes status only for the captured generation.</summary>
    /// <param name="destinationPath">The destination passed unchanged to the host exporter.</param>
    /// <param name="cancellationToken">The token passed unchanged to the host exporter.</param>
    /// <remarks>
    /// Completion checks generation only, including exports started while closed or on another pane.
    /// IOException, UnauthorizedAccessException, and ArgumentException from the exporter or success
    /// callback invoke the failure callback only for the same generation. Failure callback faults,
    /// unexpected faults, and cancellation propagate. Closing the session does not cancel a write.
    /// </remarks>
    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken)
    {
        long contextGeneration = _session.ExportContextGeneration;
        try
        {
            await _export(destinationPath, cancellationToken);
            if (contextGeneration != _session.ExportContextGeneration) { return; }
            _succeeded();
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException)
        {
            if (contextGeneration != _session.ExportContextGeneration) { return; }
            _failed();
        }
    }

    /// <summary>Accepts a selected path only for the captured view identity and open activity generation.</summary>
    /// <param name="pickPathAsync">The host picker; a null or blank result is a silent no-op.</param>
    /// <param name="isViewContextCurrent">Checks identity against the host view captured before this call.</param>
    /// <remarks>
    /// A picker starts only while the session is open. Picker cancellation is silent; other picker
    /// faults publish failure only after identity and session checks. Accepted exports use
    /// CancellationToken.None. Identity and status callback faults propagate outside the picker catch.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A supplied picker or identity delegate is null.</exception>
    public async Task ExportWithPickerAsync(
        Func<Task<string?>> pickPathAsync,
        Func<bool> isViewContextCurrent)
    {
        ArgumentNullException.ThrowIfNull(pickPathAsync);
        ArgumentNullException.ThrowIfNull(isViewContextCurrent);
        if (!_session.IsOpen)
        {
            return;
        }

        long contextGeneration = _session.ExportContextGeneration;
        string? path;
        try
        {
            path = await pickPathAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            if (isViewContextCurrent() && _session.IsExportContextCurrent(contextGeneration))
            {
                _failed();
            }
            return;
        }
        if (!string.IsNullOrWhiteSpace(path) &&
            isViewContextCurrent() &&
            _session.IsExportContextCurrent(contextGeneration))
        {
            await ExportAsync(path, CancellationToken.None);
        }
    }
}
