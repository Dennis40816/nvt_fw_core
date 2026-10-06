// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;

namespace Nvt.Core.Launcher.Persistence;

/// <summary>Stable outcome from acquiring the one cross-process version-manager writer.</summary>
public enum VersionManagerWriteLeaseIssue
{
    /// <summary>The caller exclusively owns the exact canonical state file.</summary>
    None,
    /// <summary>Another application or launcher process currently owns the state file.</summary>
    Busy,
    /// <summary>The platform could not create or inspect the writer lease.</summary>
    Unavailable,
}

/// <summary>Exclusive ownership held across one complete state/filesystem/process transaction.</summary>
public sealed class VersionManagerWriteLeaseResult : IDisposable
{
    private readonly IDisposable? _lease;
    private int _disposed;

    /// <summary>Creates a typed result; an arbitrary disposable does not confer production custody.</summary>
    /// <param name="issue">The acquisition outcome.</param>
    /// <param name="lease">Exactly one owned disposable for a successful outcome; null otherwise.</param>
    public VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue issue, IDisposable? lease = null)
    {
        bool succeeded = issue == VersionManagerWriteLeaseIssue.None;
        if (succeeded != (lease is not null))
        {
            throw new ArgumentException("A successful writer lease must own exactly one handle.", nameof(lease));
        }
        Issue = issue;
        _lease = lease;
    }

    /// <summary>Gets the acquisition result.</summary>
    public VersionManagerWriteLeaseIssue Issue { get; }

    /// <summary>Gets whether acquisition succeeded; use <see cref="HoldsStatePath"/> for live authority.</summary>
    public bool IsAcquired => Issue == VersionManagerWriteLeaseIssue.None;

    /// <summary>Verifies live, non-serializable production custody for one exact canonical state path.</summary>
    /// <param name="statePath">The exact application-state path whose writer is required.</param>
    /// <returns>Whether the undisposed result owns a valid open handle for that path.</returns>
    public bool HoldsStatePath(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        return Volatile.Read(ref _disposed) == 0 &&
            _lease is IVersionManagerWriteLeaseCustody custody &&
            custody.HoldsStatePath(statePath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lease?.Dispose();
        }
    }
}

internal interface IVersionManagerWriteLeaseCustody : IDisposable
{
    bool HoldsStatePath(string statePath);
}

/// <summary>Atomic persistence port implemented by the application's strict state adapter.</summary>
public interface IVersionManagerStateStore : IVersionManagerStateReader
{
    /// <summary>Tries to own the store's canonical state path across one complete transaction.</summary>
    /// <param name="waitTimeout">Maximum bounded contention wait; zero makes an immediate attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An exclusive lease or a typed busy/unavailable outcome.</returns>
    ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
        TimeSpan waitTimeout, CancellationToken cancellationToken);

    /// <summary>Atomically saves one validated state snapshot under caller-held writer custody.</summary>
    /// <param name="state">Validated immutable state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completion token.</returns>
    ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken);

    /// <summary>Atomically saves state and converts adapter failure into a stable result.</summary>
    /// <param name="state">Validated immutable state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stable persistence outcome.</returns>
    async ValueTask<VersionManagerStateSaveResult> TrySaveAsync(
        VersionManagerState state, CancellationToken cancellationToken)
    {
        try
        {
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return new(VersionManagerStateSaveIssue.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(VersionManagerStateSaveIssue.Unavailable);
        }
    }
}
