// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>Pins the blocking helper: result, original fault, cancellation, and a task that finishes on another thread.</summary>
public sealed class TaskBlockTests
{
    /// <summary>A completed task returns at once with its result.</summary>
    [Fact]
    public void CompletedTaskReturnsItsResult()
    {
        Assert.Equal(42, TaskBlock.UntilComplete(Task.FromResult(42)));
        TaskBlock.UntilComplete(Task.CompletedTask);
    }

    /// <summary>A task that another thread completes later releases the caller.</summary>
    [Fact]
    public void TaskCompletedOnAnotherThreadReleasesTheCaller()
    {
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => source.SetResult(7), TestContext.Current.CancellationToken);

        Assert.Equal(7, TaskBlock.UntilComplete(source.Task));
    }

    /// <summary>A fault is rethrown as the original exception, not as an aggregate.</summary>
    [Fact]
    public void FaultIsRethrownAsTheOriginalException()
    {
        var failure = new InvalidOperationException("synthetic");
        Task faulted = Task.FromException(failure);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => TaskBlock.UntilComplete(faulted));

        Assert.Same(failure, thrown);
    }

    /// <summary>A canceled task throws OperationCanceledException.</summary>
    [Fact]
    public void CanceledTaskThrowsOperationCanceled()
    {
        Task canceled = Task.FromCanceled(new CancellationToken(canceled: true));

        _ = Assert.Throws<OperationCanceledException>(() => TaskBlock.UntilComplete(canceled));
    }

    /// <summary>A missing task is rejected.</summary>
    [Fact]
    public void MissingTaskIsRejected()
    {
        _ = Assert.Throws<ArgumentNullException>(() => TaskBlock.UntilComplete(null!));
    }
}
