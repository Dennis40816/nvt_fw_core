// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Text;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Launcher.Windows;
using Nvt.Core.Processes;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Starts only the exact stable launcher located at the managed root.</summary>
public sealed class StableLauncherHandoff :
    IStableLauncherHandoff,
    IImmutableBootstrapLeaseHandoff
{
    private readonly HandoffProtocol _protocol;
    private readonly HandoffAuthority _authority;
    private readonly HandoffOperations _operations;

    /// <summary>Creates a handoff for one exact root and state path with explicit product bounds.</summary>
    /// <param name="descriptor">Product-supplied executable and protocol names.</param>
    /// <param name="maximumExecutableBytes">Positive Bootstrap executable ceiling, at most 200,000,000 bytes.</param>
    /// <param name="maximumBootstrapIdentityCharacters">Positive serialized identity ceiling.</param>
    /// <param name="lifetimeJobNamePrefix">Exact named-Job prefix.</param>
    /// <param name="managedRoot">Absolute root owned by this handoff.</param>
    /// <param name="statePath">Absolute custom state path propagated to Bootstrap.</param>
    /// <param name="maximumCleanupObservation">Positive caller-visible cleanup observation cap, at most 500 milliseconds.</param>
    /// <param name="expectedIdentity">Inherited exact Bootstrap authority, or null to disable legacy restart.</param>
    public StableLauncherHandoff(
        ProductDescriptor descriptor,
        long maximumExecutableBytes,
        int maximumBootstrapIdentityCharacters,
        string lifetimeJobNamePrefix,
        string managedRoot,
        string statePath,
        TimeSpan maximumCleanupObservation,
        ManagedImmutableBootstrapIdentity? expectedIdentity = null)
        : this(descriptor, maximumExecutableBytes, maximumBootstrapIdentityCharacters,
            lifetimeJobNamePrefix, managedRoot, statePath, maximumCleanupObservation,
            ManagedProcessTermination.Instance, expectedIdentity: expectedIdentity)
    {
    }

    internal StableLauncherHandoff(
        ProductDescriptor descriptor,
        long maximumExecutableBytes,
        int maximumBootstrapIdentityCharacters,
        string lifetimeJobNamePrefix,
        string managedRoot,
        string statePath,
        TimeSpan maximumCleanupObservation,
        IManagedProcessTermination termination,
        Action<string>? beforeProcessStart = null,
        Action? afterExecutableAcquired = null,
        ManagedImmutableBootstrapIdentity? expectedIdentity = null,
        Func<IManagedExecutableLaunchLease, bool>? validateLauncherForStart = null,
        Func<Process, bool>? hasExited = null,
        Func<Process, int>? getExitCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        if (!Path.IsPathFullyQualified(managedRoot))
        {
            throw new ArgumentException("Managed root must be an absolute path.", nameof(managedRoot));
        }
        if (statePath is not null && !Path.IsPathFullyQualified(statePath))
        {
            throw new ArgumentException("State path must be an absolute path.", nameof(statePath));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExecutableBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumExecutableBytes, ManagedImmutableBootstrapIdentity.MaximumExecutableBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBootstrapIdentityCharacters);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumCleanupObservation, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCleanupObservation, ImmutableBootstrapProcessLaunch.MaximumCleanupObservationBudget);
        _authority = new(Path.GetFullPath(managedRoot), Path.GetFullPath(statePath), expectedIdentity);
        _protocol = new(descriptor, new ManagedLifetimeProtocol(descriptor.ProtocolNames, lifetimeJobNamePrefix),
            new InheritedManagedBootstrapIdentityContext(descriptor, maximumBootstrapIdentityCharacters), maximumExecutableBytes);
        _operations = new(termination ?? throw new ArgumentNullException(nameof(termination)),
            beforeProcessStart, afterExecutableAcquired,
            validateLauncherForStart ?? (static lease => lease.TryValidateForStart()),
            hasExited ?? (static process => process.HasExited),
            getExitCode ?? (static process => process.ExitCode), maximumCleanupObservation);
    }

    /// <inheritdoc />
    public async ValueTask<StableLauncherStartResult> TryStartLauncherAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process? process;
        try
        {
            if (_authority.ExpectedIdentity is null ||
                !string.Equals(
                    _authority.ExpectedIdentity.FileName,
                    _protocol.Descriptor.BootstrapExecutableFileName,
                    StringComparison.Ordinal))
            {
                return new(StableLauncherStartOutcome.HandoffFailed);
            }
            string launcher = Path.Combine(_authority.ManagedRoot, _protocol.Descriptor.BootstrapExecutableFileName);
            if (!RepositoryPathSafety.IsSafeExistingDirectory(_authority.ManagedRoot))
            {
                return new(StableLauncherStartOutcome.HandoffFailed);
            }
            ManagedExecutableLaunchLeaseResult acquired =
                await StableManagedExecutableLaunchLease.TryAcquireAsync(
                    launcher,
                    _authority.ExpectedIdentity.Length,
                    _authority.ExpectedIdentity.Sha256,
                    _protocol.MaximumExecutableBytes,
                    cancellationToken).ConfigureAwait(false);
            if (!acquired.IsAcquired)
            {
                return new(StableLauncherStartOutcome.HandoffFailed);
            }
            using IManagedExecutableLaunchLease lease = acquired.Lease!;
            cancellationToken.ThrowIfCancellationRequested();
            // Cancellation and start compete once; custody I/O cannot reserve admission.
            // After admission wins, native creation owns the start through ResumeThread.
            int admission = (int)StartAdmissionPhase.Pending;
            bool protocolRejected = false;
            using CancellationTokenRegistration revocation = cancellationToken.Register(
                () => Interlocked.CompareExchange(ref admission, (int)StartAdmissionPhase.Cancelled, (int)StartAdmissionPhase.Pending));
            process = ProcessLaunchGate.StartContained(CreateBootstrapStartInfo(
                lease.ExecutablePath,
                _authority.ManagedRoot,
                admissionPipeHandle: null,
                startGate: null,
                lifetime: null,
                identity: _authority.ExpectedIdentity), [], () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _operations.BeforeProcessStart?.Invoke(lease.ExecutablePath);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_operations.ValidateLauncherForStart(lease))
                    {
                        protocolRejected = true;
                        return false;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Interlocked.CompareExchange(ref admission, (int)StartAdmissionPhase.Admitted, (int)StartAdmissionPhase.Pending) != (int)StartAdmissionPhase.Pending)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return false;
                    }
                    return true;
                });
            if (process is null)
            {
                return new(protocolRejected
                    ? StableLauncherStartOutcome.HandoffFailed
                    : StableLauncherStartOutcome.ProcessCreationFailed);
            }
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            return new(exception is Win32Exception
                ? StableLauncherStartOutcome.ProcessCreationFailed
                : StableLauncherStartOutcome.HandoffFailed);
        }
        using (process)
        {
            try
            {
                return _operations.HasExited(process)
                    ? new(StableLauncherStartOutcome.ExitedImmediately, _operations.GetExitCode(process))
                    : new(StableLauncherStartOutcome.Started);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
            {
                return new(StableLauncherStartOutcome.HandoffFailed);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<ImmutableBootstrapStartResult> StartAsync(
        string managedRoot,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease ownedLease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownedLease);
        IManagedExecutableLaunchLease? pendingLease = ownedLease;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryValidateOwnedLease(
                    managedRoot,
                    expectedIdentity,
                    pendingLease,
                    out string root))
            {
                return ValueTask.FromResult(StartFailure(ImmutableBootstrapStartIssue.Damaged));
            }
            IManagedExecutableLaunchLease transferredLease = pendingLease;
            pendingLease = null;
            return ValueTask.FromResult(StartWithOwnedLease(
                root,
                expectedIdentity,
                transferredLease,
                cancellationToken));
        }
        finally
        {
            pendingLease?.Dispose();
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
        "The owned launch lease transfers to the process-start task and is disposed on every exit.")]
    private ImmutableBootstrapStartResult StartWithOwnedLease(
        string root,
        ManagedImmutableBootstrapIdentity expectedIdentity,
        IManagedExecutableLaunchLease lease,
        CancellationToken cancellationToken)
    {
        try
        {
            _operations.AfterExecutableAcquired?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            lease.Dispose();
            throw;
        }
        AnonymousPipeServerStream? admissionPipe = null;
        BootstrapStartAuthorization? startGate = null;
        ManagedProcessLifetimeLease? lifetime = null;
        try
        {
            ManagedProcessLifetimeLeaseAcquisitionOutcome lifetimeAcquisition =
                ManagedProcessLifetimeLease.Acquire(
                _protocol.Lifetime,
                _authority.StatePath,
                ManagedProcessLifetimeKind.Bootstrap,
                out lifetime);
            if (lifetimeAcquisition != ManagedProcessLifetimeLeaseAcquisitionOutcome.Acquired)
            {
                lease.Dispose();
                return StartFailure(lifetimeAcquisition switch
                {
                    ManagedProcessLifetimeLeaseAcquisitionOutcome.Busy =>
                        ImmutableBootstrapStartIssue.Busy,
                    ManagedProcessLifetimeLeaseAcquisitionOutcome.Unavailable =>
                        ImmutableBootstrapStartIssue.Unavailable,
                    ManagedProcessLifetimeLeaseAcquisitionOutcome.Acquired =>
                        throw new InvalidOperationException(
                            "Acquired Bootstrap lifetime did not return custody."),
                    _ => throw new InvalidOperationException(
                        "Bootstrap lifetime acquisition returned an undefined outcome."),
                });
            }
            lifetime = lifetime ?? throw new InvalidOperationException(
                "Acquired Bootstrap lifetime did not return custody.");
            admissionPipe = new AnonymousPipeServerStream(
                PipeDirection.In,
                HandleInheritability.None);
            startGate = new BootstrapStartAuthorization(_protocol.Descriptor.ProtocolNames);
            ProcessStartInfo startInfo = CreateBootstrapStartInfo(
                    lease.ExecutablePath,
                    root,
                    admissionPipe.GetClientHandleAsString(),
                    startGate,
                    lifetime,
                    expectedIdentity);
            Task<Process?> startTask = Task.Run<Process?>(() =>
            {
                try
                {
                    Process? process = ProcessLaunchGate.StartContained(
                        startInfo,
                        [
                            ProcessInheritedHandle.Parse(
                                _protocol.Descriptor.ProtocolNames.BootstrapAdmissionHandle,
                                admissionPipe.GetClientHandleAsString()),
                            startGate.InheritedHandle,
                            new ProcessInheritedHandle(
                                _protocol.Descriptor.ProtocolNames.LifetimeHandle,
                                lifetime.InheritedHandleValue),
                        ],
                        () =>
                        {
                            _operations.BeforeProcessStart?.Invoke(lease.ExecutablePath);
                            return lease.TryValidateForStart();
                        });
                    return process ?? throw new InvalidDataException(
                        "Bootstrap tree changed after executable verification.");
                }
                finally
                {
                    DisposeLocalClientHandle(admissionPipe);
                    startGate.DisposeLocalClientHandle();
                    lease.Dispose();
                }
            });
            return new(
                new ImmutableBootstrapProcessLaunch(
                    startTask,
                    admissionPipe,
                    startGate,
                    lifetime,
                    _operations.Termination,
                    _operations.MaximumCleanupObservation),
                ImmutableBootstrapStartIssue.None);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            lease.Dispose();
            admissionPipe?.Dispose();
            startGate?.Dispose();
            lifetime?.Dispose();
            return StartFailure(ImmutableBootstrapStartIssue.StartFailed);
        }
        catch (ArgumentException)
        {
            lease.Dispose();
            admissionPipe?.Dispose();
            startGate?.Dispose();
            lifetime?.Dispose();
            throw;
        }
    }

    private bool TryValidateOwnedLease(
        string managedRoot,
        ManagedImmutableBootstrapIdentity? expectedIdentity,
        IManagedExecutableLaunchLease lease,
        out string root)
    {
        root = string.Empty;
        if (string.IsNullOrWhiteSpace(managedRoot) || expectedIdentity is null ||
            expectedIdentity.Length > _protocol.MaximumExecutableBytes ||
            !Path.IsPathFullyQualified(managedRoot) ||
            !string.Equals(
                expectedIdentity.FileName,
                _protocol.Descriptor.BootstrapExecutableFileName,
                StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(managedRoot));
            string configuredRoot = Path.TrimEndingDirectorySeparator(_authority.ManagedRoot);
            string workingDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(lease.WorkingDirectory));
            string executable = Path.GetFullPath(lease.ExecutablePath);
            string expectedExecutable = Path.Combine(root, expectedIdentity.FileName);
            return LifetimeStatePath.PathComparer.Equals(root, configuredRoot) &&
                LifetimeStatePath.PathComparer.Equals(workingDirectory, root) &&
                LifetimeStatePath.PathComparer.Equals(executable, expectedExecutable);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or NotSupportedException)
        {
            root = string.Empty;
            return false;
        }
    }

    private ProcessStartInfo CreateBootstrapStartInfo(
        string executable,
        string managedRoot,
        string? admissionPipeHandle,
        BootstrapStartAuthorization? startGate,
        ManagedProcessLifetimeLease? lifetime,
        ManagedImmutableBootstrapIdentity identity)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = managedRoot,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--managed-root");
        startInfo.ArgumentList.Add(managedRoot);
        startInfo.ArgumentList.Add("--state-path");
        startInfo.ArgumentList.Add(_authority.StatePath);
        if (admissionPipeHandle is not null)
        {
            startInfo.Environment[
                _protocol.Descriptor.ProtocolNames.BootstrapAdmissionHandle] =
                admissionPipeHandle;
        }
        startGate?.ApplyInheritedContext(startInfo);
        lifetime?.ApplyInheritedContext(startInfo);
        _protocol.BootstrapIdentity.Apply(startInfo, identity);
        return startInfo;
    }

    private static ImmutableBootstrapStartResult StartFailure(ImmutableBootstrapStartIssue issue)
    {
        return new(null, issue);
    }

    private static void DisposeLocalClientHandle(AnonymousPipeServerStream pipe)
    {
        try
        {
            pipe.DisposeLocalCopyOfClientHandle();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
        }
    }

    private enum StartAdmissionPhase { Pending, Admitted, Cancelled }

    private sealed record HandoffProtocol(
        ProductDescriptor Descriptor, ManagedLifetimeProtocol Lifetime,
        InheritedManagedBootstrapIdentityContext BootstrapIdentity, long MaximumExecutableBytes);
    private sealed record HandoffAuthority(
        string ManagedRoot, string StatePath, ManagedImmutableBootstrapIdentity? ExpectedIdentity);
    private sealed record HandoffOperations(
        IManagedProcessTermination Termination, Action<string>? BeforeProcessStart,
        Action? AfterExecutableAcquired, Func<IManagedExecutableLaunchLease, bool> ValidateLauncherForStart,
        Func<Process, bool> HasExited, Func<Process, int> GetExitCode, TimeSpan MaximumCleanupObservation);
}
