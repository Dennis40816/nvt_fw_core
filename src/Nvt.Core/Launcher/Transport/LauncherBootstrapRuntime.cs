// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Processes;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Classification of one consumed outer launcher READY inheritance context.</summary>
public enum LauncherReadyInheritanceOutcome
{
    /// <summary>The Launcher was started without immutable-Bootstrap supervision.</summary>
    NotInherited,
    /// <summary>Both inherited values are syntactically valid and may be used once.</summary>
    Inherited,
    /// <summary>The inherited pair was partial, blank, or malformed.</summary>
    InvalidInheritedContext,
}

/// <summary>One consumed outer READY inheritance context with no public handshake material.</summary>
public sealed class LauncherReadyInheritance : IDisposable
{
    private readonly ReadyIdentity _identity;
    // Guard: Interlocked.Exchange claims or disposes the captured handle once.
    private SafePipeHandle? _handle;

    private LauncherReadyInheritance(
        LauncherReadyInheritanceOutcome outcome,
        SafePipeHandle? handle,
        string? expected)
    {
        _identity = new(outcome, expected);
        _handle = handle;
    }

    /// <summary>Gets the complete inherited-context classification.</summary>
    public LauncherReadyInheritanceOutcome Outcome => _identity.Outcome;

    internal string? Expected => _identity.Expected;

    internal static LauncherReadyInheritance NotInherited { get; } =
        new(LauncherReadyInheritanceOutcome.NotInherited, null, null);

    internal static LauncherReadyInheritance Invalid { get; } =
        new(LauncherReadyInheritanceOutcome.InvalidInheritedContext, null, null);

    internal static LauncherReadyInheritance CreateInherited(SafePipeHandle handle, string expected)
    {
        return new(LauncherReadyInheritanceOutcome.Inherited, handle, expected);
    }

    internal SafePipeHandle? TakeHandle()
    {
        return Interlocked.Exchange(ref _handle, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
    }
    private sealed record ReadyIdentity(LauncherReadyInheritanceOutcome Outcome, string? Expected);
}

/// <summary>Public host seam used only by the immutable Bootstrap executable.</summary>
public sealed class LauncherBootstrapRuntime
{
    private readonly RuntimeProtocol _protocol;
    private readonly Func<string, string, LauncherBootstrapRuntimeServices> _compose;
    private readonly LauncherBootstrapRuntimeHooks _hooks;

    /// <summary>Creates runtime composition over mandatory strict state and repository adapters.</summary>
    /// <param name="descriptor">Product-supplied executable and protocol names.</param>
    /// <param name="maximumReadyLineCharacters">Positive launcher READY character ceiling.</param>
    /// <param name="maximumBootstrapIdentityCharacters">Positive serialized Bootstrap identity ceiling.</param>
    /// <param name="lifetimeJobNamePrefix">Exact named-Job prefix.</param>
    /// <param name="compose">Mandatory adapter that binds strict state and repository ports to the exact normalized root and state path.</param>
    public LauncherBootstrapRuntime(
        ProductDescriptor descriptor,
        int maximumReadyLineCharacters,
        int maximumBootstrapIdentityCharacters,
        string lifetimeJobNamePrefix,
        Func<string, string, LauncherBootstrapRuntimeServices> compose)
        : this(descriptor, maximumReadyLineCharacters, maximumBootstrapIdentityCharacters,
            lifetimeJobNamePrefix, compose, new LauncherBootstrapRuntimeHooks())
    {
    }

    internal LauncherBootstrapRuntime(
        ProductDescriptor descriptor,
        int maximumReadyLineCharacters,
        int maximumBootstrapIdentityCharacters,
        string lifetimeJobNamePrefix,
        Func<string, string, LauncherBootstrapRuntimeServices> compose,
        LauncherBootstrapRuntimeHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReadyLineCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBootstrapIdentityCharacters);
        var lifetime = new ManagedLifetimeProtocol(descriptor.ProtocolNames, lifetimeJobNamePrefix);
        _protocol = new(descriptor, maximumReadyLineCharacters,
            new InheritedManagedBootstrapIdentityContext(descriptor, maximumBootstrapIdentityCharacters), lifetime);
        _compose = compose ?? throw new ArgumentNullException(nameof(compose));
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
    }

    /// <summary>Launcher exit code preserving an unconfirmed nested-process cleanup journal.</summary>
    public const int UnconfirmedTerminationExitCode = 17;

    /// <summary>Captures and authorizes Bootstrap custody before any managed-state access.</summary>
    public async ValueTask<int> RunEntryAsync(
        string managedRoot,
        string statePath,
        CancellationToken cancellationToken)
    {
        using LauncherBootstrapStartupContext startup = CaptureStartup(statePath);
        return startup.Outcome == LauncherBootstrapStartupOutcome.InvalidInheritedContext
            ? ImmutableBootstrapExitCodeCodec.EncodeFailure(
                ImmutableBootstrapExitIssue.InvalidInheritedContext)
            : !await startup.WaitForStartAsync(cancellationToken).ConfigureAwait(false)
                ? ImmutableBootstrapExitCodeCodec.EncodeFailure(
                    ImmutableBootstrapExitIssue.StartNotAuthorized)
                : await RunCoreAsync(managedRoot, statePath, startup, cancellationToken)
                    .ConfigureAwait(false);
    }

    internal LauncherBootstrapStartupContext CaptureStartup(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        bool lifetimeAdvertised = new InheritedManagedProcessLifetime(_protocol.Descriptor.ProtocolNames, _protocol.Lifetime.JobNamePrefix).IsContextAdvertised();
        IInheritedManagedProcessLifetimeCapture lifetime = _hooks.CaptureLifetime is { } capture
            ? capture(statePath, ManagedProcessLifetimeKind.Bootstrap, lifetimeAdvertised)
            : ManagedProcessLifetimeLease.CaptureInherited(_protocol.Lifetime, statePath,
                ManagedProcessLifetimeKind.Bootstrap, lifetimeAdvertised);
        var gate = BootstrapStartGate.Capture(_protocol.Descriptor.ProtocolNames);
        var admission = BootstrapAdmissionSignal.Capture(_protocol.Descriptor.ProtocolNames);
        ManagedImmutableBootstrapIdentity? identity =
            _protocol.BootstrapIdentity.CaptureAndClear();
        bool legacy = lifetime.Outcome == InheritedManagedProcessLifetimeOutcome.NotInherited &&
            gate.Outcome == BootstrapStartGateInheritanceOutcome.NotInherited &&
            admission.Outcome == BootstrapAdmissionInheritanceOutcome.NotInherited;
        bool inherited = lifetime.Outcome == InheritedManagedProcessLifetimeOutcome.Captured &&
            gate.Outcome == BootstrapStartGateInheritanceOutcome.Inherited &&
            admission.Outcome == BootstrapAdmissionInheritanceOutcome.Inherited;
        return new(
            legacy
                ? LauncherBootstrapStartupOutcome.LegacyDirect
                : inherited
                    ? LauncherBootstrapStartupOutcome.Inherited
                    : LauncherBootstrapStartupOutcome.InvalidInheritedContext,
            lifetime,
            gate,
            admission,
            inherited ? identity : null);
    }

    private async ValueTask<int> RunCoreAsync(
        string managedRoot,
        string statePath,
        LauncherBootstrapStartupContext startup,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        string root = Path.GetFullPath(managedRoot);
        string state = Path.GetFullPath(statePath);
        LauncherBootstrapRuntimeServices services = _compose(root, state);
        IVersionManagerStateStore stateStore = services.ApplicationStateStore;
        ManagedVersionSeedOutcome seedOutcome = await new ManagedVersionSeedBootstrapper(
                root,
                stateStore,
                services.SeedStateStore,
                services.VersionRepository)
            .EnsureInitializedAsync(
                LauncherBootstrapCoordinator.StartupWriterLeaseTimeout,
                cancellationToken).ConfigureAwait(false);
        if (seedOutcome is not ManagedVersionSeedOutcome.ExistingState and
            not ManagedVersionSeedOutcome.Seeded)
        {
            return seedOutcome is ManagedVersionSeedOutcome.Busy
                ? ImmutableBootstrapExitCodeCodec.EncodeFailure(ImmutableBootstrapExitIssue.Busy)
                : seedOutcome is ManagedVersionSeedOutcome.StateUnavailable
                    ? ImmutableBootstrapExitCodeCodec.EncodeFailure(
                        ImmutableBootstrapExitIssue.StateUnavailable)
                    : seedOutcome is ManagedVersionSeedOutcome.ManagedRootMismatch
                        ? ImmutableBootstrapExitCodeCodec.EncodeFailure(
                            ImmutableBootstrapExitIssue.ManagedRootMismatch)
                        : ImmutableBootstrapExitCodeCodec.EncodeFailure(
                            ImmutableBootstrapExitIssue.InvalidState);
        }
        using var process = new AnonymousPipeManagedLauncherProcess(
            _protocol.Descriptor, _protocol.MaximumReadyLineCharacters,
            _protocol.BootstrapIdentity.MaximumSerializedCharacters, _protocol.Lifetime.JobNamePrefix,
            ManagedProcessTermination.Instance, startup.Admission, inheritedBootstrapIdentity: startup.Identity);
        var coordinator = new LauncherBootstrapCoordinator(
            root,
            state,
            stateStore,
            services.LauncherStateStore,
            services.LauncherRepository,
            process);
        LauncherBootstrapResult result = await coordinator.RunAsync(cancellationToken).ConfigureAwait(false);
        return EncodeCoordinatorOutcome(result.Outcome);
    }

    internal static int EncodeCoordinatorOutcome(LauncherBootstrapOutcome outcome)
    {
        return outcome switch
        {
            LauncherBootstrapOutcome.Ready => ImmutableBootstrapExitCodeCodec.Ready,
            LauncherBootstrapOutcome.RolledBack => ImmutableBootstrapExitCodeCodec.RolledBack,
            LauncherBootstrapOutcome.Busy => Encode(ImmutableBootstrapExitIssue.Busy),
            LauncherBootstrapOutcome.InvalidState => Encode(ImmutableBootstrapExitIssue.InvalidState),
            LauncherBootstrapOutcome.ManagedRootMismatch =>
                Encode(ImmutableBootstrapExitIssue.ManagedRootMismatch),
            LauncherBootstrapOutcome.AppMutationPending =>
                Encode(ImmutableBootstrapExitIssue.MutationPending),
            LauncherBootstrapOutcome.DamagedLauncher =>
                Encode(ImmutableBootstrapExitIssue.DamagedLauncher),
            LauncherBootstrapOutcome.ProtocolMismatch =>
                Encode(ImmutableBootstrapExitIssue.ProtocolMismatch),
            LauncherBootstrapOutcome.StartFailed => Encode(ImmutableBootstrapExitIssue.StartFailed),
            LauncherBootstrapOutcome.RollbackUnavailable =>
                Encode(ImmutableBootstrapExitIssue.RollbackUnavailable),
            LauncherBootstrapOutcome.StateChanged => Encode(ImmutableBootstrapExitIssue.StateChanged),
            LauncherBootstrapOutcome.StateUnavailable =>
                Encode(ImmutableBootstrapExitIssue.StateUnavailable),
            LauncherBootstrapOutcome.TerminationUnconfirmed =>
                Encode(ImmutableBootstrapExitIssue.TerminationUnconfirmed),
            _ => Encode(ImmutableBootstrapExitIssue.UndefinedFailure),
        };

        static int Encode(ImmutableBootstrapExitIssue issue)
        {
            return ImmutableBootstrapExitCodeCodec.EncodeFailure(issue);
        }
    }

    /// <summary>Consumes and classifies the outer READY environment before a nested process can inherit it.</summary>
    public LauncherReadyInheritance CaptureNestedReadyContext()
    {
        string? handle = Environment.GetEnvironmentVariable(
            _protocol.Descriptor.ProtocolNames.LauncherReadyHandle);
        string? expected = Environment.GetEnvironmentVariable(
            _protocol.Descriptor.ProtocolNames.ExpectedLauncherReady);
        Environment.SetEnvironmentVariable(_protocol.Descriptor.ProtocolNames.LauncherReadyHandle, null);
        Environment.SetEnvironmentVariable(_protocol.Descriptor.ProtocolNames.ExpectedLauncherReady, null);
        if (handle is null && expected is null)
        {
            return LauncherReadyInheritance.NotInherited;
        }
        if (string.IsNullOrWhiteSpace(handle) ||
            !long.TryParse(handle, NumberStyles.None, CultureInfo.InvariantCulture, out long pipeHandle) ||
            pipeHandle <= 0)
        {
            return LauncherReadyInheritance.Invalid;
        }
#pragma warning disable CA2000 // Ownership transfers to the returned typed context.
        var ownedHandle = new SafePipeHandle(new IntPtr(pipeHandle), ownsHandle: true);
#pragma warning restore CA2000
        if (!ProcessLaunchGate.TryClearInheritance(ownedHandle.DangerousGetHandle()))
        {
            ownedHandle.Dispose();
            return LauncherReadyInheritance.Invalid;
        }
        if (!LauncherReadyProtocol.IsExpectedPrefix(expected))
        {
            ownedHandle.Dispose();
            return LauncherReadyInheritance.Invalid;
        }
        return LauncherReadyInheritance.CreateInherited(ownedHandle, expected!);
    }

    /// <summary>Reports nested app readiness only after reloading exact app and launcher identities.</summary>
    public async ValueTask<bool> ReportNestedReadyAsync(
        LauncherReadyInheritance context,
        string managedRoot,
        string statePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Outcome != LauncherReadyInheritanceOutcome.Inherited)
        {
            return false;
        }
        string expected = context.Expected!;

        try
        {
            LauncherBootstrapRuntimeServices services = _compose(managedRoot, statePath);
            IVersionManagerStateStore appStateStore = services.ApplicationStateStore;
            using VersionManagerWriteLeaseResult lease = await appStateStore.TryAcquireWriteLeaseAsync(
                LauncherBootstrapCoordinator.StartupWriterLeaseTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                return false;
            }

            VersionManagerStateLoadResult app = await appStateStore.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            LauncherBootstrapStateLoadResult launcher = await services.LauncherStateStore
                .LoadAsync(cancellationToken).ConfigureAwait(false);
            ManagedLauncherIdentity? running = new[]
            {
                launcher.State?.Pending?.Candidate,
                launcher.State?.Pending?.PreviousLastKnownGood,
                launcher.State?.Active,
            }.FirstOrDefault(identity =>
                identity is not null &&
                string.Equals(expected, LauncherReadyProtocol.CreateExpectedPrefix(identity), StringComparison.Ordinal));
            ManagedVersionAdmission? admission = app.State?.Admissions.SingleOrDefault(
                value => value.Version == app.State.ActiveVersion);
            if (!app.IsSuccess || !launcher.IsSuccess ||
                !app.State!.IsBoundToManagedRoot(managedRoot) ||
                !launcher.State!.IsBoundToManagedRoot(managedRoot) ||
                app.State!.PendingActivation is not null || app.State.PendingMutation is not null ||
                running is null || admission is null)
            {
                return false;
            }

            SafePipeHandle? handle = context.TakeHandle();
            if (handle is null)
            {
                return false;
            }
            await using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle);
            byte[] message = Encoding.UTF8.GetBytes(LauncherReadyProtocol.Create(running, admission) + "\n");
            await pipe.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
        {
            return false;
        }
    }

    internal enum LauncherBootstrapStartupOutcome
    {
        LegacyDirect,
        Inherited,
        InvalidInheritedContext,
    }

    internal sealed class LauncherBootstrapStartupContext : IDisposable
    {
        private readonly StartupCapture _capture;

        internal LauncherBootstrapStartupContext(
            LauncherBootstrapStartupOutcome outcome,
            IInheritedManagedProcessLifetimeCapture lifetime,
            BootstrapStartGate gate,
            BootstrapAdmissionSignal admission,
            ManagedImmutableBootstrapIdentity? identity)
        {
            _capture = new(outcome, lifetime, gate, admission, identity);
        }

        internal LauncherBootstrapStartupOutcome Outcome => _capture.Outcome;
        internal BootstrapAdmissionSignal Admission => _capture.Admission;
        internal ManagedImmutableBootstrapIdentity? Identity => _capture.Identity;

        internal ValueTask<bool> WaitForStartAsync(CancellationToken cancellationToken)
        {
            return Outcome switch
            {
                LauncherBootstrapStartupOutcome.LegacyDirect => ValueTask.FromResult(true),
                LauncherBootstrapStartupOutcome.Inherited => _capture.Gate.WaitForStartAsync(cancellationToken),
                LauncherBootstrapStartupOutcome.InvalidInheritedContext => ValueTask.FromResult(false),
                _ => throw new InvalidOperationException("Bootstrap startup outcome is undefined."),
            };
        }

        public void Dispose()
        {
            Admission.Dispose();
            _capture.Gate.Dispose();
            _capture.Lifetime.Dispose();
        }

        private sealed record StartupCapture(
            LauncherBootstrapStartupOutcome Outcome, IInheritedManagedProcessLifetimeCapture Lifetime,
            BootstrapStartGate Gate, BootstrapAdmissionSignal Admission, ManagedImmutableBootstrapIdentity? Identity);
    }
    private sealed record RuntimeProtocol(
        ProductDescriptor Descriptor, int MaximumReadyLineCharacters,
        InheritedManagedBootstrapIdentityContext BootstrapIdentity, ManagedLifetimeProtocol Lifetime);
}

internal sealed record LauncherBootstrapRuntimeHooks(
    Func<string, ManagedProcessLifetimeKind, bool, IInheritedManagedProcessLifetimeCapture>? CaptureLifetime = null);
