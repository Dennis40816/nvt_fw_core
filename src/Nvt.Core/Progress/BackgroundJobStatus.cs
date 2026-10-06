// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Progress;

/// <summary>Describes the state of the single background job.</summary>
public enum BackgroundJobStatus
{
    /// <summary>No job has started.</summary>
    Idle,
    /// <summary>The operation is running.</summary>
    Running,
    /// <summary>Cancellation was requested, and the operation has not finished.</summary>
    Cancelling,
    /// <summary>The operation returned a result without cancellation.</summary>
    Succeeded,
    /// <summary>The operation finished after cancellation was requested.</summary>
    Cancelled,
    /// <summary>The operation failed without cancellation.</summary>
    Failed,
}