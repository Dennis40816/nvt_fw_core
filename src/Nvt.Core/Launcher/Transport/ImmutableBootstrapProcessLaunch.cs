// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Nvt.Core.Launcher.Transport;

internal sealed class ImmutableBootstrapProcessLaunch : IImmutableBootstrapLaunch
{
    private const int MaximumAdmissionLineCharacters = 32;
    internal static readonly TimeSpan MaximumCleanupObservationBudget = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan BackgroundCleanupBudget = TimeSpan.FromSeconds(5);
    private readonly LaunchAuthority _authority;
    private readonly object _cleanupSync = new();
    // Guard: _cleanupSync owns creation of the single background cleanup task.
    private Task<bool>? _cleanupTask;
    // Guard: Interlocked owns the one-time resource release transition.
    private int _resourcesDisposed;
    // Guard: Interlocked/Volatile own LaunchPhase transitions; callers serialize admission and completion waits.
    private int _state;

    internal ImmutableBootstrapProcessLaunch(
        Task<Process?> startTask, AnonymousPipeServerStream admissionPipe,
        BootstrapStartAuthorization startGate, ManagedProcessLifetimeLease lifetime,
        IManagedProcessTermination termination, TimeSpan maximumCleanupObservation)
    {
        ArgumentNullException.ThrowIfNull(startTask);
        ArgumentNullException.ThrowIfNull(admissionPipe);
        ArgumentNullException.ThrowIfNull(startGate);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(termination);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumCleanupObservation, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCleanupObservation, MaximumCleanupObservationBudget);
        _authority = new(startTask, admissionPipe, startGate, lifetime, termination, maximumCleanupObservation);
    }

    private Process? StartedProcess => _authority.StartTask.IsCompletedSuccessfully ? _authority.StartTask.Result : null;

    public void Dispose()
    {
        if (Volatile.Read(ref _state) != (int)LaunchPhase.Completed)
        {
            _ = StartCleanup();
            return;
        }
        DisposeResources();
    }

    public async ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        long budgetStarted = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(ref _state, (int)LaunchPhase.AwaitingAdmission, (int)LaunchPhase.Pending) != (int)LaunchPhase.Pending)
        {
            return new(ImmutableBootstrapAdmissionOutcome.HealthUnavailable);
        }
        using var operationDeadline = new CancellationTokenSource(budget.RemainingOperation);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            operationDeadline.Token);
        Process? process;
        try
        {
            try
            {
                process = await _authority.StartTask.WaitAsync(operation.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
            {
                return await FailBeforeAdmissionAsync(
                    ImmutableBootstrapAdmissionOutcome.LaunchFailed,
                    exitCode: null,
                    budgetStarted,
                    budget.RemainingTotal,
                    ImmutableBootstrapExitIssue.StartFailed).ConfigureAwait(false);
            }
            if (process is null)
            {
                return await FailBeforeAdmissionAsync(
                    ImmutableBootstrapAdmissionOutcome.LaunchFailed,
                    exitCode: null,
                    budgetStarted,
                    budget.RemainingTotal,
                    ImmutableBootstrapExitIssue.StartFailed).ConfigureAwait(false);
            }
            if (operation.IsCancellationRequested ||
                Stopwatch.GetElapsedTime(budgetStarted) >= budget.RemainingOperation ||
                !_authority.StartGate.TryAuthorize())
            {
                return await FailBeforeAdmissionAsync(
                    ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
                    exitCode: null,
                    budgetStarted,
                    budget.RemainingTotal).ConfigureAwait(false);
            }
            Task<string?> signalTask = BoundedUtf8LineReader.ReadAsync(
                _authority.AdmissionPipe,
                MaximumAdmissionLineCharacters,
                bufferSize: 64,
                operation.Token);
            Task exitTask = process.WaitForExitAsync(operation.Token);
            Task completed = await Task.WhenAny(signalTask, exitTask).ConfigureAwait(false);
            if (signalTask.IsCompletedSuccessfully && IsAdmitted(signalTask.Result) &&
                !operation.IsCancellationRequested &&
                IsWithinBudget(budgetStarted, budget.RemainingOperation))
            {
                return AcceptAdmission();
            }
            if (exitTask.IsCompletedSuccessfully)
            {
                string? signal = await signalTask.ConfigureAwait(false);
                if (IsAdmitted(signal) &&
                    !operation.IsCancellationRequested &&
                    IsWithinBudget(budgetStarted, budget.RemainingOperation))
                {
                    return AcceptAdmission();
                }
                int exitCode = process.ExitCode;
                ImmutableBootstrapAdmissionResult exited = MapExitBeforeAdmission(exitCode);
                return await FailBeforeAdmissionAsync(
                    exited.Outcome,
                    exited.ExitCode,
                    budgetStarted,
                    budget.RemainingTotal,
                    exited.ExitIssue).ConfigureAwait(false);
            }
            if (signalTask.IsCompletedSuccessfully)
            {
                string? signal = signalTask.Result;
                if (signal is { Length: 0 })
                {
                    await exitTask.ConfigureAwait(false);
                    int exitCode = process.ExitCode;
                    ImmutableBootstrapAdmissionResult exited = MapExitBeforeAdmission(exitCode);
                    return await FailBeforeAdmissionAsync(
                        exited.Outcome,
                        exited.ExitCode,
                        budgetStarted,
                        budget.RemainingTotal,
                        exited.ExitIssue).ConfigureAwait(false);
                }
                return await FailBeforeAdmissionAsync(
                    ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
                    exitCode: null,
                    budgetStarted,
                    budget.RemainingTotal).ConfigureAwait(false);
            }
            await completed.ConfigureAwait(false);
            return await FailBeforeAdmissionAsync(
                ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
                exitCode: null,
                budgetStarted,
                budget.RemainingTotal).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await FailBeforeAdmissionAsync(
                ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
                exitCode: null,
                budgetStarted,
                budget.RemainingTotal).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            DecoderFallbackException or IOException or InvalidOperationException or Win32Exception)
        {
            return await FailBeforeAdmissionAsync(
                ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
                exitCode: null,
                budgetStarted,
                budget.RemainingTotal).ConfigureAwait(false);
        }
    }

    public async ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(
        ImmutableBootstrapWaitBudget budget,
        CancellationToken cancellationToken)
    {
        long budgetStarted = Stopwatch.GetTimestamp();
        if (Volatile.Read(ref _state) != (int)LaunchPhase.Admitted)
        {
            return new(ImmutableBootstrapCompletionOutcome.Unavailable);
        }
        using var operationDeadline = new CancellationTokenSource(budget.RemainingOperation);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            operationDeadline.Token);
        try
        {
            Process process = StartedProcess ?? throw new InvalidOperationException(
                "An admitted Bootstrap has no process receipt.");
            long completionStarted = Stopwatch.GetTimestamp();
            await process.WaitForExitAsync(operation.Token).ConfigureAwait(false);
            int exitCode = process.ExitCode;
            if (operation.IsCancellationRequested ||
                Stopwatch.GetElapsedTime(completionStarted) >= budget.RemainingOperation ||
                !IsWithinBudget(budgetStarted, budget.RemainingOperation))
            {
                return await FailBeforeCompletionAsync(
                    budgetStarted,
                    budget.RemainingTotal).ConfigureAwait(false);
            }
            if (exitCode is
                ImmutableBootstrapExitCodeCodec.Ready or
                ImmutableBootstrapExitCodeCodec.RolledBack)
            {
                if (!_authority.Lifetime.TryReleaseAcceptedTree())
                {
                    return await FailBeforeCompletionAsync(
                        budgetStarted,
                        budget.RemainingTotal).ConfigureAwait(false);
                }
                _ = Interlocked.Exchange(ref _state, (int)LaunchPhase.Completed);
                DisposeResources();
                return new(
                    ImmutableBootstrapExitCodeCodec.ClassifyCompletion(exitCode),
                    exitCode,
                    ImmutableBootstrapExitIssue.None);
            }
            bool confirmed = await ObserveCleanupAsync(
                budgetStarted,
                budget.RemainingTotal).ConfigureAwait(false);
            return new(
                confirmed
                    ? ImmutableBootstrapExitCodeCodec.ClassifyCompletion(exitCode)
                    : ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed,
                confirmed ? exitCode : null,
                confirmed
                    ? ImmutableBootstrapExitCodeCodec.DecodeFailure(exitCode)
                    : ImmutableBootstrapExitIssue.TerminationUnconfirmed);
        }
        catch (OperationCanceledException)
        {
            return await FailBeforeCompletionAsync(
                budgetStarted,
                budget.RemainingTotal).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or Win32Exception)
        {
            return await FailBeforeCompletionAsync(
                budgetStarted,
                budget.RemainingTotal).ConfigureAwait(false);
        }
    }

    internal static ImmutableBootstrapAdmissionResult MapExitBeforeAdmission(int exitCode)
    {
        ImmutableBootstrapExitIssue issue = ImmutableBootstrapExitCodeCodec.DecodeFailure(exitCode);
        return new(
            ImmutableBootstrapExitCodeCodec.ClassifyAdmission(issue),
            exitCode,
            issue);
    }

    private ImmutableBootstrapAdmissionResult AcceptAdmission()
    {
        _authority.AdmissionPipe.Dispose();
        _authority.StartGate.Dispose();
        _ = Interlocked.Exchange(ref _state, (int)LaunchPhase.Admitted);
        return new(ImmutableBootstrapAdmissionOutcome.Admitted);
    }

    private static bool IsAdmitted(string? signal)
    {
        return string.Equals(signal, "ADMITTED", StringComparison.Ordinal);
    }

    private async ValueTask<ImmutableBootstrapAdmissionResult> FailBeforeAdmissionAsync(
        ImmutableBootstrapAdmissionOutcome confirmedOutcome,
        int? exitCode,
        long budgetStarted,
        TimeSpan totalBudget,
        ImmutableBootstrapExitIssue exitIssue = ImmutableBootstrapExitIssue.None)
    {
        bool confirmed = await ObserveCleanupAsync(budgetStarted, totalBudget).ConfigureAwait(false);
        return new(
            confirmed
                ? confirmedOutcome
                : ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed,
            confirmed ? exitCode : null,
            confirmed
                ? exitIssue
                : ImmutableBootstrapExitIssue.TerminationUnconfirmed);
    }

    private async ValueTask<ImmutableBootstrapCompletionResult> FailBeforeCompletionAsync(
        long budgetStarted,
        TimeSpan totalBudget)
    {
        bool confirmed = await ObserveCleanupAsync(budgetStarted, totalBudget).ConfigureAwait(false);
        return new(
            confirmed
                ? ImmutableBootstrapCompletionOutcome.Unavailable
                : ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed,
            ExitIssue: confirmed
                ? ImmutableBootstrapExitIssue.None
                : ImmutableBootstrapExitIssue.TerminationUnconfirmed);
    }

    private async ValueTask<bool> ObserveCleanupAsync(long budgetStarted, TimeSpan totalBudget)
    {
        Task<bool> cleanup = StartCleanup();
        TimeSpan remaining = Remaining(totalBudget, Stopwatch.GetElapsedTime(budgetStarted));
        TimeSpan observation = remaining < _authority.MaximumCleanupObservation
            ? remaining
            : _authority.MaximumCleanupObservation;
        if (observation <= TimeSpan.Zero)
        {
            return cleanup.IsCompletedSuccessfully && cleanup.Result;
        }
        try
        {
            return await cleanup.WaitAsync(observation).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static bool IsWithinBudget(long started, TimeSpan totalBudget)
    {
        return Stopwatch.GetElapsedTime(started) < totalBudget;
    }

    private static TimeSpan Remaining(TimeSpan total, TimeSpan elapsed)
    {
        return elapsed >= total ? TimeSpan.Zero : total - elapsed;
    }

    private Task<bool> StartCleanup()
    {
        _ = _authority.StartGate.TryAbort();
        lock (_cleanupSync)
        {
            return _cleanupTask ??= Task.Run(CleanupCore);
        }
    }

    private bool CleanupCore()
    {
        try
        {
            Process? process;
            try
            {
                process = _authority.StartTask.GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is
                AggregateException or IOException or UnauthorizedAccessException or
                InvalidOperationException or Win32Exception)
            {
                process = null;
            }
            bool treeExited = _authority.Lifetime.TerminateTreeAndConfirmEmpty(BackgroundCleanupBudget);
            bool processExited = process is null || _authority.Termination.ConfirmExited(process).IsExitConfirmed;
            return treeExited && processExited;
        }
        finally
        {
            _ = Interlocked.Exchange(ref _state, (int)LaunchPhase.Completed);
            DisposeResources();
        }
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, (int)ResourcePhase.Disposed) != (int)ResourcePhase.Held)
        {
            return;
        }
        _authority.AdmissionPipe.Dispose();
        _authority.StartGate.Dispose();
        StartedProcess?.Dispose();
        _authority.Lifetime.Dispose();
    }

    private enum LaunchPhase { Pending, AwaitingAdmission, Admitted, Completed }
    private enum ResourcePhase { Held, Disposed }
    private sealed record LaunchAuthority(
        Task<Process?> StartTask, AnonymousPipeServerStream AdmissionPipe,
        BootstrapStartAuthorization StartGate, ManagedProcessLifetimeLease Lifetime,
        IManagedProcessTermination Termination, TimeSpan MaximumCleanupObservation);
}
