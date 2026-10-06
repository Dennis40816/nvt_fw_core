// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

/// <summary>Managed-child startup handoff plus its durable version snapshot.</summary>
public sealed record ManagedApplicationStartupResult(
    ApplicationReadySignalOutcome ReadySignalOutcome,
    ManagedMutationSnapshot Snapshot);

/// <summary>Application-owned startup seam for READY-qualified state reload.</summary>
public interface IManagedApplicationStartupCoordinator
{
    /// <summary>Reports READY once and loads the exact durable version state.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="isReadOnly">Preserves READY signaling but forbids initialization writes and recovery.</param>
    ValueTask<ManagedApplicationStartupResult> CompleteStartupAsync(
        CancellationToken cancellationToken, bool isReadOnly = false);
}

/// <summary>Coordinates the managed child READY handoff with the launcher's exact state-path lease.</summary>
public sealed class ManagedApplicationStartupCoordinator : IManagedApplicationStartupCoordinator
{
    private readonly ManagedAppVersion _applicationVersion;
    private readonly IApplicationReadySignal _readySignal;
    private readonly IManagedApplicationInitialization _versionManagement;

    /// <summary>Creates one managed-child startup coordinator.</summary>
    public ManagedApplicationStartupCoordinator(
        ManagedAppVersion applicationVersion,
        IApplicationReadySignal readySignal,
        IManagedApplicationInitialization versionManagement)
    {
        _applicationVersion = applicationVersion;
        _readySignal = readySignal ?? throw new ArgumentNullException(nameof(readySignal));
        _versionManagement = versionManagement ?? throw new ArgumentNullException(nameof(versionManagement));
    }

    /// <inheritdoc />
    public async ValueTask<ManagedApplicationStartupResult> CompleteStartupAsync(
        CancellationToken cancellationToken, bool isReadOnly = false)
    {
        ApplicationReadySignalOutcome ready = await _readySignal.ReportReadyAsync(
            _applicationVersion,
            cancellationToken).ConfigureAwait(false);
        ManagedMutationSnapshot snapshot = isReadOnly
            ? await _versionManagement.InitializeAsync(cancellationToken, isReadOnly: true).ConfigureAwait(false)
            : ready == ApplicationReadySignalOutcome.Reported
            ? await _versionManagement.InitializeAfterManagedReadyAsync(
                cancellationToken).ConfigureAwait(false)
            : await _versionManagement.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return new(ready, snapshot);
    }
}
