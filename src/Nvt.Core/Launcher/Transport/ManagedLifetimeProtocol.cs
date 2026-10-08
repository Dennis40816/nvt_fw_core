// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Transport;

/// <summary>One explicit protocol namespace shared by parent-side lifetime operations.</summary>
internal sealed record ManagedLifetimeProtocol
{
    internal ManagedLifetimeProtocol(LauncherProtocolNames names, string jobNamePrefix)
    {
        Names = names ?? throw new ArgumentNullException(nameof(names));
        ArgumentException.ThrowIfNullOrWhiteSpace(jobNamePrefix);
        JobNamePrefix = jobNamePrefix;
    }

    internal LauncherProtocolNames Names { get; }
    internal string JobNamePrefix { get; }
}

/// <summary>Immutable fault gates; production adapters supply no hooks.</summary>
internal sealed record ManagedProcessStartHooks(
    Action<ProcessStartInfo>? BeforeStartValidation = null,
    Action? AfterProcessCreation = null,
    CancellationToken DeadlineSignal = default);
