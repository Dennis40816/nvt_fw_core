// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Processes;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Windows process adapter with an inherited one-use anonymous ready pipe.</summary>
public sealed class AnonymousPipeManagedApplicationProcess : IManagedApplicationProcess
{
    private readonly ApplicationProtocol _protocol;
    private readonly IManagedProcessTermination _termination;
    private readonly ManagedProcessStartHooks _hooks;

    /// <summary>Creates an adapter with explicit product protocol names, READY ceiling, state path, and Job namespace.</summary>
    /// <param name="descriptor">Product-supplied executable and protocol names.</param>
    /// <param name="maximumReadyLineCharacters">Positive decoded-character ceiling; the retained adapter supplies its frozen value.</param>
    /// <param name="statePath">Exact version-state path supplied by the retained adapter.</param>
    /// <param name="lifetimeJobNamePrefix">Exact named-Job prefix supplied by the retained adapter.</param>
    public AnonymousPipeManagedApplicationProcess(
        ProductDescriptor descriptor,
        int maximumReadyLineCharacters,
        string statePath,
        string lifetimeJobNamePrefix)
        : this(descriptor, maximumReadyLineCharacters, statePath, lifetimeJobNamePrefix,
            ManagedProcessTermination.Instance)
    {
    }

    internal AnonymousPipeManagedApplicationProcess(
        ProductDescriptor descriptor,
        int maximumReadyLineCharacters,
        string statePath,
        string lifetimeJobNamePrefix,
        IManagedProcessTermination termination,
        ManagedProcessStartHooks? hooks = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReadyLineCharacters);
        _protocol = new(new ManagedLifetimeProtocol(descriptor.ProtocolNames, lifetimeJobNamePrefix),
            Path.GetFullPath(statePath), maximumReadyLineCharacters);
        _termination = termination ?? throw new ArgumentNullException(nameof(termination));
        _hooks = hooks ?? new();
    }

    /// <inheritdoc />
    public ValueTask<ManagedProcessLifetimeStatus> GetLifetimeStatusAsync(
        string managedRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ManagedProcessLifetimeLease.GetStatus(
            _protocol.Lifetime,
            _protocol.StatePath,
            ManagedProcessLifetimeKind.Application));
    }

    /// <inheritdoc />
    public async ValueTask<ManagedProcessStartResult> StartUntilReadyAsync(
        string managedRoot,
        ManagedAppVersion version,
        IManagedExecutableLaunchLease executableLease,
        TimeSpan readyDeadline,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentNullException.ThrowIfNull(executableLease);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(readyDeadline, TimeSpan.Zero);
        var deadline = new ManagedStartDeadline(readyDeadline, cancellationToken, _hooks.DeadlineSignal);
        return await deadline.RunAsync(
            () => StartCoreAsync(managedRoot, version, executableLease, deadline, cancellationToken),
            static () => new(ManagedProcessStartOutcome.TerminationUnconfirmed, null),
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ManagedProcessStartResult> StartCoreAsync(
        string managedRoot,
        ManagedAppVersion version,
        IManagedExecutableLaunchLease executableLease,
        ManagedStartDeadline deadline,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        ManagedProcessLifetimeLease? lifetime = null;
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            lifetime = ManagedProcessLifetimeLease.TryAcquire(
                _protocol.Lifetime,
                _protocol.StatePath,
                ManagedProcessLifetimeKind.Application);
            deadline.Token.ThrowIfCancellationRequested();
            if (lifetime is null)
            {
                return new(ManagedProcessStartOutcome.TerminationUnconfirmed, null);
            }
            using var pipe = new AnonymousPipeServerStream(
                PipeDirection.In,
                HandleInheritability.None);
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
            startInfo.ArgumentList.Add(_protocol.StatePath);
            startInfo.Environment[_protocol.Lifetime.Names.ApplicationReadyHandle] = pipe.GetClientHandleAsString();
            startInfo.Environment[_protocol.Lifetime.Names.ExpectedApplicationVersion] = version.ToString();
            lifetime.ApplyInheritedContext(startInfo);
            try
            {
                process = ProcessLaunchGate.StartContained(
                    startInfo,
                    [
                        ProcessInheritedHandle.Parse(
                            _protocol.Lifetime.Names.ApplicationReadyHandle,
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
                return new(ManagedProcessStartOutcome.StartFailed, null);
            }

            Task<string?> readyTask = BoundedUtf8LineReader.ReadAsync(
                pipe,
                _protocol.MaximumReadyLineCharacters,
                bufferSize: 256,
                deadline.Token);
            Task exitTask = process.WaitForExitAsync(deadline.Token);
            Task completed = await Task.WhenAny(readyTask, exitTask).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (readyTask.IsCompletedSuccessfully)
            {
                string expected = $"READY:{version}";
                if (string.Equals(readyTask.Result, expected, StringComparison.Ordinal))
                {
                    return lifetime.TryReleaseAcceptedTree()
                        ? new(ManagedProcessStartOutcome.Ready, null)
                        : Terminate(process, lifetime, ManagedProcessStartOutcome.StartFailed);
                }
                if (readyTask.Result is { Length: 0 })
                {
                    await exitTask.ConfigureAwait(false);
                    return Terminate(
                        process,
                        lifetime,
                        ManagedProcessStartOutcome.ExitedBeforeReady);
                }
                return Terminate(process, lifetime, ManagedProcessStartOutcome.InvalidReadySignal);
            }
            if (completed == exitTask && exitTask.IsCompletedSuccessfully)
            {
                return Terminate(process, lifetime, ManagedProcessStartOutcome.ExitedBeforeReady);
            }

            await completed.ConfigureAwait(false);
            return Terminate(process, lifetime, ManagedProcessStartOutcome.ReadyTimeout);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return process is null
                ? new(ManagedProcessStartOutcome.ReadyTimeout, null)
                : Terminate(process, lifetime!, ManagedProcessStartOutcome.ReadyTimeout);
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
        catch (DecoderFallbackException)
        {
            return process is not null
                ? Terminate(process, lifetime!, ManagedProcessStartOutcome.InvalidReadySignal)
                : new(ManagedProcessStartOutcome.InvalidReadySignal, null);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            return process is not null
                ? Terminate(process, lifetime!, ManagedProcessStartOutcome.StartFailed)
                : new(ManagedProcessStartOutcome.StartFailed, null);
        }
        finally
        {
            DisposeProcess(process);
            lifetime?.Dispose();
        }
    }

    private ManagedProcessStartResult Terminate(
        Process process,
        ManagedProcessLifetimeLease lifetime,
        ManagedProcessStartOutcome confirmedOutcome)
    {
        ManagedProcessTerminationResult termination = _termination.ConfirmExited(process);
        bool treeExited = lifetime.TerminateTreeAndConfirmEmpty(ManagedProcessTermination.DefaultWaitTimeout);
        return termination.IsExitConfirmed && treeExited
            ? new(confirmedOutcome, termination.ExitCode)
            : new(ManagedProcessStartOutcome.TerminationUnconfirmed, null);
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
    private sealed record ApplicationProtocol(
        ManagedLifetimeProtocol Lifetime,
        string StatePath,
        int MaximumReadyLineCharacters);

}
