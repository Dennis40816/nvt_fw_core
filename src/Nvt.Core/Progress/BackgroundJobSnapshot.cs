// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Progress;

/// <summary>Stores the job identity, state, latest progress, result, and failure.</summary>
/// <typeparam name="TProgress">The caller's progress reference type.</typeparam>
/// <typeparam name="TResult">The caller's result reference type.</typeparam>
/// <param name="JobId">The job identity, or zero before the first job.</param>
/// <param name="Status">The job state.</param>
/// <param name="Progress">The latest progress, or null when absent.</param>
/// <param name="Result">The successful result, or null when absent.</param>
/// <param name="Error">The operation's failure, or null when absent.</param>
public sealed record BackgroundJobSnapshot<TProgress, TResult>(
    long JobId,
    BackgroundJobStatus Status,
    TProgress? Progress = null,
    TResult? Result = null,
    Exception? Error = null)
    where TProgress : class
    where TResult : class
{
    /// <summary>Gets whether the job is running or cancelling.</summary>
    public bool IsActive => Status is BackgroundJobStatus.Running or BackgroundJobStatus.Cancelling;
}