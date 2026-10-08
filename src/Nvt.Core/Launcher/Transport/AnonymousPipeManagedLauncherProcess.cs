// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Processes;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Starts one exact version-scoped launcher and accepts one identity-bound READY line.</summary>
public sealed class AnonymousPipeManagedLauncherProcess : IManagedLauncherProcess, IDisposable
{
    private readonly LauncherProtocol _protocol;
    private readonly BootstrapAdmissionSignal _admission;
    private readonly ManagedImmutableBootstrapIdentity? _inheritedBootstrapIdentity;
    private readonly IManagedProcessTermination _termination;
    private readonly ManagedProcessStartHooks _hooks;

    /// <summary>Captures outer ADMITTED authority and creates one exact READY supervisor.</summary>
    /// <param name="descriptor">Product-supplied executable and protocol names.</param>
    /// <param name="maximumReadyLineCharacters">Positive decoded-character ceiling supplied by the retained adapter.</param>
    /// <param name="maximumBootstrapIdentityCharacters">Positive serialized Bootstrap identity ceiling supplied by the retained adapter.</param>
    /// <param name="lifetimeJobNamePrefix">Exact named-Job prefix supplied by the retained adapter.</param>
    /// <param name="inheritedBootstrapIdentity">Explicitly admitted Bootstrap identity; null strips ambient identity.</param>
    public AnonymousPipeManagedLauncherProcess(
        ProductDescriptor descriptor,
        int maximumReadyLineCharacters,
        int maximumBootstrapIdentityCharacters,
        string lifetimeJobNamePrefix,
        ManagedImmutableBootstrapIdentity? inheritedBootstrapIdentity = null)
    {
        _protocol = CreateProtocol(descriptor, maximumReadyLineCharacters,
            maximumBootstrapIdentityCharacters, lifetimeJobNamePrefix);
        _termination = ManagedProcessTermination.Instance;
        _admission = BootstrapAdmissionSignal.Capture(descriptor.ProtocolNames);
        _hooks = new();
        _inheritedBootstrapIdentity = inheritedBootstrapIdentity;
    }

    internal AnonymousPipeManagedLauncherProcess(
        ProductDescriptor descriptor,
        int maximumReadyLineCharacters,
        int maximumBootstrapIdentityCharacters,
        string lifetimeJobNamePrefix,
        IManagedProcessTermination termination,
        BootstrapAdmissionSignal admission,
        ManagedProcessStartHooks? hooks = null,
        ManagedImmutableBootstrapIdentity? inheritedBootstrapIdentity = null)
    {
        _protocol = CreateProtocol(descriptor, maximumReadyLineCharacters,
            maximumBootstrapIdentityCharacters, lifetimeJobNamePrefix);
        _termination = termination ?? throw new ArgumentNullException(nameof(termination));
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _hooks = hooks ?? new();
        _inheritedBootstrapIdentity = inheritedBootstrapIdentity;
    }

    private static LauncherProtocol CreateProtocol(
        ProductDescriptor descriptor,
        int maximumReadyLineCharacters,
        int maximumBootstrapIdentityCharacters,
        string lifetimeJobNamePrefix)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReadyLineCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBootstrapIdentityCharacters);
        return new(new ManagedLifetimeProtocol(descriptor.ProtocolNames, lifetimeJobNamePrefix),
            new InheritedManagedBootstrapIdentityContext(descriptor, maximumBootstrapIdentityCharacters),
            maximumReadyLineCharacters);
    }

    /// <inheritdoc />
    public ValueTask<ManagedProcessLifetimeStatus> GetLifetimeStatusAsync(
        string statePath,
        ManagedProcessLifetimeKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ManagedProcessLifetimeLease.GetStatus(
            _protocol.Lifetime,
            statePath,
            kind));
    }

    /// <inheritdoc />
    public async ValueTask<LauncherProcessStartResult> StartUntilReadyAsync(
        string managedRoot,
        string statePath,
        ManagedLauncherIdentity launcher,
        IManagedExecutableLaunchLease executableLease,
        TimeSpan readyDeadline,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(executableLease);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(readyDeadline, TimeSpan.Zero);
        var deadline = new ManagedStartDeadline(readyDeadline, cancellationToken, _hooks.DeadlineSignal);
        return await deadline.RunAsync(
            () => StartCoreAsync(
                managedRoot, statePath, launcher, executableLease, deadline, cancellationToken),
            static () => new(LauncherProcessStartOutcome.TerminationUnconfirmed, null),
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<LauncherProcessStartResult> StartCoreAsync(
        string managedRoot,
        string statePath,
        ManagedLauncherIdentity launcher,
        IManagedExecutableLaunchLease executableLease,
        ManagedStartDeadline deadline,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        ManagedProcessLifetimeLease? lifetime = null;
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (_admission.Outcome == BootstrapAdmissionInheritanceOutcome.Invalid)
            {
                return Failure(LauncherProcessStartOutcome.StartFailed);
            }
            lifetime = ManagedProcessLifetimeLease.TryAcquire(
                _protocol.Lifetime,
                statePath,
                ManagedProcessLifetimeKind.Launcher);
            deadline.Token.ThrowIfCancellationRequested();
            if (lifetime is null)
            {
                return Failure(LauncherProcessStartOutcome.TerminationUnconfirmed);
            }
            using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
            var startInfo = new ProcessStartInfo
            {
                FileName = executableLease.ExecutablePath,
                WorkingDirectory = executableLease.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            startInfo.ArgumentList.Add("--managed-root");
            startInfo.ArgumentList.Add(Path.GetFullPath(managedRoot));
            startInfo.ArgumentList.Add("--state-path");
            startInfo.ArgumentList.Add(Path.GetFullPath(statePath));
            string expected = LauncherReadyProtocol.CreateExpectedPrefix(launcher);
            startInfo.Environment[_protocol.Lifetime.Names.LauncherReadyHandle] = pipe.GetClientHandleAsString();
            startInfo.Environment[_protocol.Lifetime.Names.ExpectedLauncherReady] = expected;
            lifetime.ApplyInheritedContext(startInfo);
            _ = startInfo.Environment.Remove(
                _protocol.Lifetime.Names.BootstrapIdentity);
            if (_inheritedBootstrapIdentity is not null)
            {
                _protocol.BootstrapIdentity.Apply(
                    startInfo,
                    _inheritedBootstrapIdentity);
            }
            try
            {
                process = ProcessLaunchGate.StartContained(
                    startInfo,
                    [
                        ProcessInheritedHandle.Parse(
                            _protocol.Lifetime.Names.LauncherReadyHandle,
                            pipe.GetClientHandleAsString()),
                        new ProcessInheritedHandle(
                            _protocol.Lifetime.Names.LifetimeHandle,
                            lifetime.InheritedHandleValue),
                    ],
                    () =>
                    {
                        if (deadline.Token.IsCancellationRequested)
                        {
                            return false;
                        }
                        _hooks.BeforeStartValidation?.Invoke(startInfo);
                        return !deadline.Token.IsCancellationRequested &&
                            executableLease.TryValidateForStart() &&
                            deadline.TryBeginCreation();
                    });
            }
            finally
            {
                DisposeLocalClientHandle(pipe);
            }
            if (process is not null)
            {
                _hooks.AfterProcessCreation?.Invoke();
            }
            deadline.Token.ThrowIfCancellationRequested();
            if (process is null)
            {
                return Failure(LauncherProcessStartOutcome.StartFailed);
            }
            if (!await _admission.ReportAdmittedAsync(deadline.Token).ConfigureAwait(false))
            {
                return Terminate(process, lifetime, LauncherProcessStartOutcome.StartFailed);
            }
            deadline.Token.ThrowIfCancellationRequested();

            Task<string?> readyTask = BoundedUtf8LineReader.ReadAsync(
                pipe,
                _protocol.MaximumReadyLineCharacters,
                bufferSize: 512,
                deadline.Token);
            Task exitTask = process.WaitForExitAsync(deadline.Token);
            Task completed = await Task.WhenAny(readyTask, exitTask).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (readyTask.IsCompletedSuccessfully)
            {
                if (LauncherReadyProtocol.TryParse(
                        readyTask.Result,
                        expected,
                        out ManagedVersionAdmission? readyAdmission))
                {
                    return lifetime.TryReleaseAcceptedTree()
                        ? new(LauncherProcessStartOutcome.Ready, null, readyAdmission)
                        : Terminate(process, lifetime, LauncherProcessStartOutcome.StartFailed);
                }
                if (readyTask.Result is { Length: 0 })
                {
                    await exitTask.ConfigureAwait(false);
                    return Exited(process, lifetime);
                }
                return Terminate(process, lifetime, LauncherProcessStartOutcome.InvalidReadySignal);
            }
            if (completed == exitTask && exitTask.IsCompletedSuccessfully)
            {
                return Exited(process, lifetime);
            }
            await completed.ConfigureAwait(false);
            return Terminate(process, lifetime, LauncherProcessStartOutcome.ReadyTimeout);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return process is null
                ? new(LauncherProcessStartOutcome.ReadyTimeout, null)
                : Terminate(process, lifetime!, LauncherProcessStartOutcome.ReadyTimeout);
        }
        catch (OperationCanceledException)
        {
            if (process is not null)
            {
                _ = _termination.ConfirmExited(process);
                _ = lifetime?.TerminateTreeAndConfirmEmpty(ManagedProcessTermination.DefaultWaitTimeout);
            }
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidOperationException or
            DecoderFallbackException or Win32Exception)
        {
            return process is null
                ? new(LauncherProcessStartOutcome.StartFailed, null)
                : Terminate(process, lifetime!, LauncherProcessStartOutcome.StartFailed);
        }
        finally
        {
            DisposeProcess(process);
            lifetime?.Dispose();
        }
    }

    private static LauncherProcessStartResult Failure(LauncherProcessStartOutcome outcome)
    {
        return new(outcome, null);
    }

    private LauncherProcessStartResult Exited(
        Process process,
        ManagedProcessLifetimeLease lifetime)
    {
        int exitCode = process.ExitCode;
        LauncherProcessStartResult result = Terminate(
            process,
            lifetime,
            LauncherProcessStartOutcome.ExitedBeforeReady);
        return exitCode == LauncherBootstrapRuntime.UnconfirmedTerminationExitCode
            ? new(LauncherProcessStartOutcome.TerminationUnconfirmed, exitCode)
            : result;
    }

    private LauncherProcessStartResult Terminate(
        Process process,
        ManagedProcessLifetimeLease lifetime,
        LauncherProcessStartOutcome confirmedOutcome)
    {
        ManagedProcessTerminationResult termination = _termination.ConfirmExited(process);
        bool treeExited = lifetime.TerminateTreeAndConfirmEmpty(ManagedProcessTermination.DefaultWaitTimeout);
        return termination.IsExitConfirmed && treeExited
            ? new(confirmedOutcome, termination.ExitCode)
            : new(LauncherProcessStartOutcome.TerminationUnconfirmed, null);
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

    private static void DisposeProcess(Process? process)
    {
        try
        {
            process?.Dispose();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }
    /// <inheritdoc />
    public void Dispose()
    {
        _admission.Dispose();
    }

    private sealed record LauncherProtocol(
        ManagedLifetimeProtocol Lifetime,
        InheritedManagedBootstrapIdentityContext BootstrapIdentity,
        int MaximumReadyLineCharacters);

}
