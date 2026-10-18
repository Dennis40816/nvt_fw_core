// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Observes existing managed lifetime authority without creating a lease or job.</summary>
public sealed class FileSystemManagedProcessLifetimeProbe : IManagedProcessLifetimeProbe
{
    private readonly ManagedLifetimeProtocol _protocol;

    /// <summary>Creates a read-only lifetime probe with the product's explicit protocol namespace.</summary>
    public FileSystemManagedProcessLifetimeProbe(LauncherProtocolNames protocolNames, string jobNamePrefix)
    {
        _protocol = new(protocolNames, jobNamePrefix);
    }

    /// <inheritdoc />
    public ValueTask<ManagedProcessLifetimeStatus> ObserveAsync(
        string statePath,
        ManagedProcessLifetimeKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ManagedProcessLifetimeLease.GetStatus(_protocol, statePath, kind));
    }
}
