// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Progress;

/// <summary>Provides the job identity and its terminal snapshot task.</summary>
/// <typeparam name="TProgress">The caller's progress reference type.</typeparam>
/// <typeparam name="TResult">The caller's result reference type.</typeparam>
/// <param name="JobId">The job identity.</param>
/// <param name="Completion">The task that completes after the terminal observer returns.</param>
public sealed record BackgroundJobHandle<TProgress, TResult>(
    long JobId,
    Task<BackgroundJobSnapshot<TProgress, TResult>> Completion)
    where TProgress : class
    where TResult : class;