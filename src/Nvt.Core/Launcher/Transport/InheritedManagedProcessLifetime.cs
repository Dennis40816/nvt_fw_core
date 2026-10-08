// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Classification of inherited managed-tree lifetime context.</summary>
public enum InheritedManagedProcessLifetimeOutcome
{
    /// <summary>No managed lifetime context was advertised.</summary>
    NotInherited,
    /// <summary>The exact inherited start lease and tree authority were captured.</summary>
    Captured,
    /// <summary>Managed context was advertised but incomplete, malformed, or unusable.</summary>
    InvalidInheritedContext,
}

/// <summary>Typed inherited lifetime capture held until the managed process exits.</summary>
public interface IInheritedManagedProcessLifetimeCapture : IDisposable
{
    /// <summary>Gets the exact inherited-context classification.</summary>
    InheritedManagedProcessLifetimeOutcome Outcome { get; }
}

/// <summary>Consumes inherited managed-tree lifetime context at process entry.</summary>
public sealed class InheritedManagedProcessLifetime
{
    private readonly ManagedLifetimeProtocol _protocol;

    /// <summary>Creates a capture seam with explicit product protocol names and the exact named-Job prefix.</summary>
    public InheritedManagedProcessLifetime(LauncherProtocolNames protocolNames, string lifetimeJobNamePrefix)
    {
        _protocol = new(protocolNames, lifetimeJobNamePrefix);
    }

    /// <summary>Reports whether any lifetime field advertises a managed parent context.</summary>
    public bool IsContextAdvertised()
    {
        return Environment.GetEnvironmentVariable(_protocol.Names.LifetimeContext) is not null ||
               Environment.GetEnvironmentVariable(_protocol.Names.LifetimeHandle) is not null ||
               Environment.GetEnvironmentVariable(_protocol.Names.LifetimeJob) is not null ||
               Environment.GetEnvironmentVariable(_protocol.Names.LifetimeStatePath) is not null ||
               Environment.GetEnvironmentVariable(_protocol.Names.LifetimeKind) is not null;
    }

    /// <summary>Reports whether the application READY channel advertises managed startup.</summary>
    public bool IsApplicationReadyContextAdvertised()
    {
        return Environment.GetEnvironmentVariable(_protocol.Names.ApplicationReadyHandle) is not null ||
               Environment.GetEnvironmentVariable(_protocol.Names.ExpectedApplicationVersion) is not null;
    }

    /// <summary>Consumes all lifetime fields, clears handle inheritance, and validates the exact state path and role before joining the Job.</summary>
    public IInheritedManagedProcessLifetimeCapture Capture(
        string? statePath,
        ManagedProcessLifetimeKind kind,
        bool managedContextAdvertised)
    {
        return ManagedProcessLifetimeLease.CaptureInherited(_protocol, statePath, kind, managedContextAdvertised);
    }
}

internal sealed class InheritedManagedProcessLifetimeCapture : IInheritedManagedProcessLifetimeCapture
{
    private readonly CaptureState _state;

    private InheritedManagedProcessLifetimeCapture(
        InheritedManagedProcessLifetimeOutcome outcome, IDisposable? lease, IDisposable? job)
    {
        _state = new(outcome, lease, job);
    }

    public InheritedManagedProcessLifetimeOutcome Outcome => _state.Outcome;

    internal static InheritedManagedProcessLifetimeCapture NotInherited { get; } =
        new(InheritedManagedProcessLifetimeOutcome.NotInherited, null, null);
    internal static InheritedManagedProcessLifetimeCapture Invalid { get; } =
        new(InheritedManagedProcessLifetimeOutcome.InvalidInheritedContext, null, null);

    internal static InheritedManagedProcessLifetimeCapture Create(IDisposable lease, IDisposable job) =>
        new(InheritedManagedProcessLifetimeOutcome.Captured, lease, job);

    public void Dispose()
    {
        _state.Lease?.Dispose();
        _state.Job?.Dispose();
    }

    private sealed record CaptureState(
        InheritedManagedProcessLifetimeOutcome Outcome, IDisposable? Lease, IDisposable? Job);
}
