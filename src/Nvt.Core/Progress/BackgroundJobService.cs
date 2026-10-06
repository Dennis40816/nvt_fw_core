// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Progress;

/// <summary>
/// Owns one observable background operation. Work runs on the thread pool.
/// Cancellation suppresses results, and job identity checks reject progress from completed jobs.
/// </summary>
/// <typeparam name="TProgress">The caller's progress reference type.</typeparam>
/// <typeparam name="TResult">The caller's result reference type.</typeparam>
public sealed class BackgroundJobService<TProgress, TResult>
    where TProgress : class
    where TResult : class
{
    private readonly object gate = new();
    private ActiveJob? active;
    private long nextJobId;
    private BackgroundJobSnapshot<TProgress, TResult> snapshot = new(0, BackgroundJobStatus.Idle);

    /// <summary>Gets the current snapshot under the service lock.</summary>
    public BackgroundJobSnapshot<TProgress, TResult> Snapshot
    {
        get
        {
            lock (gate) return snapshot;
        }
    }

    /// <summary>Gets whether the current job is running or cancelling.</summary>
    public bool IsActive => Snapshot.IsActive;

    /// <summary>Starts one operation and reports its initial snapshot before scheduling the work.</summary>
    /// <param name="operation">The operation that receives the job token and inline progress reporter.</param>
    /// <param name="initialProgress">The progress stored in the initial running snapshot.</param>
    /// <param name="observer">The snapshot observer. Its exceptions are ignored.</param>
    /// <returns>The admitted job's identity and completion task.</returns>
    /// <exception cref="ArgumentNullException">The operation is null.</exception>
    /// <exception cref="InvalidOperationException">A job is already active.</exception>
    public BackgroundJobHandle<TProgress, TResult> Start(
        Func<CancellationToken, IProgress<TProgress>, Task<TResult>> operation,
        TProgress? initialProgress = null,
        IProgress<BackgroundJobSnapshot<TProgress, TResult>>? observer = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ActiveJob job;
        BackgroundJobSnapshot<TProgress, TResult> started;
        lock (gate)
        {
            if (active is not null)
                throw new InvalidOperationException("A background job is already active.");
            nextJobId = checked(nextJobId + 1);
            job = new ActiveJob(nextJobId, new CancellationTokenSource(), observer);
            active = job;
            started = new BackgroundJobSnapshot<TProgress, TResult>(
                job.JobId,
                BackgroundJobStatus.Running,
                initialProgress);
            snapshot = started;
        }
        SafeReport(observer, started);
        var completion = Task.Run(() => RunAsync(job, operation, observer));
        return new BackgroundJobHandle<TProgress, TResult>(job.JobId, completion);
    }

    /// <summary>Requests cancellation once. The job remains active until its operation finishes.</summary>
    /// <returns>True for the first request; false when idle or already cancelling.</returns>
    /// <exception cref="AggregateException">A cancellation callback throws. The request remains in effect.</exception>
    public bool Cancel()
    {
        BackgroundJobSnapshot<TProgress, TResult> cancelling;
        IProgress<BackgroundJobSnapshot<TProgress, TResult>>? observer;
        ActiveJob job;
        lock (gate)
        {
            if (active is null || snapshot.Status == BackgroundJobStatus.Cancelling)
                return false;
            snapshot = snapshot with { Status = BackgroundJobStatus.Cancelling };
            cancelling = snapshot;
            observer = active.Observer;
            job = active;
            job.CancelRequested = true;
        }
        SafeReport(observer, cancelling);
        try
        {
            job.Cancellation.Cancel();
        }
        finally
        {
            var dispose = false;
            lock (gate)
            {
                job.CancelCallFinished = true;
                dispose = job.CompletionFinished;
            }
            if (dispose) job.Cancellation.Dispose();
        }
        return true;
    }

    private async Task<BackgroundJobSnapshot<TProgress, TResult>> RunAsync(
        ActiveJob job,
        Func<CancellationToken, IProgress<TProgress>, Task<TResult>> operation,
        IProgress<BackgroundJobSnapshot<TProgress, TResult>>? observer)
    {
        var progress = new InlineProgress<TProgress>(item => ReportProgress(job.JobId, item, observer));
        TResult? result = null;
        Exception? error = null;
        try
        {
            result = await operation(job.Cancellation.Token, progress).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            error = exception;
        }

        var publish = false;
        var dispose = false;
        BackgroundJobSnapshot<TProgress, TResult> completed;
        lock (gate)
        {
            completed = job.CancelRequested || job.Cancellation.IsCancellationRequested
                ? new BackgroundJobSnapshot<TProgress, TResult>(job.JobId, BackgroundJobStatus.Cancelled, snapshot.Progress)
                : error is not null
                    ? new BackgroundJobSnapshot<TProgress, TResult>(job.JobId, BackgroundJobStatus.Failed, snapshot.Progress, Error: error)
                    : new BackgroundJobSnapshot<TProgress, TResult>(job.JobId, BackgroundJobStatus.Succeeded, snapshot.Progress, result);
            if (active?.JobId == job.JobId)
            {
                snapshot = completed;
                active = null;
                publish = true;
            }
            job.CompletionFinished = true;
            dispose = !job.CancelRequested || job.CancelCallFinished;
        }
        if (dispose) job.Cancellation.Dispose();
        if (publish) SafeReport(observer, completed);
        return completed;
    }

    private void ReportProgress(
        long jobId,
        TProgress progress,
        IProgress<BackgroundJobSnapshot<TProgress, TResult>>? observer)
    {
        BackgroundJobSnapshot<TProgress, TResult>? update = null;
        lock (gate)
        {
            if (active?.JobId != jobId || !snapshot.IsActive) return;
            snapshot = snapshot with { Progress = progress };
            update = snapshot;
        }
        SafeReport(observer, update);
    }

    private static void SafeReport(
        IProgress<BackgroundJobSnapshot<TProgress, TResult>>? observer,
        BackgroundJobSnapshot<TProgress, TResult>? update)
    {
        if (observer is null || update is null) return;
        try
        {
            observer.Report(update);
        }
        catch
        {
            // Progress presentation must never fail or cancel the underlying job.
        }
    }

    private sealed class ActiveJob(
        long jobId,
        CancellationTokenSource cancellation,
        IProgress<BackgroundJobSnapshot<TProgress, TResult>>? observer)
    {
        public long JobId { get; } = jobId;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public IProgress<BackgroundJobSnapshot<TProgress, TResult>>? Observer { get; } = observer;
        public bool CancelRequested { get; set; }
        public bool CancelCallFinished { get; set; }
        public bool CompletionFinished { get; set; }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}