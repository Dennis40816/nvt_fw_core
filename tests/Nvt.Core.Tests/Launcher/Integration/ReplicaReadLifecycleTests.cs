// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.IntegrationTestData;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Frozen shared-read, abandonment and cancellation assertions driven by the shared clock.</summary>
public sealed class ReplicaReadLifecycleTests
{
    /// <summary>A blocked synchronous primary cannot hide a valid backup beyond its deadline.</summary>
    [Fact]
    public async Task TimedOutPrimaryAllowsValidBackup()
    {
        var primary = new GatedRegistry(synchronous: true);
        UpdateSourceRegistrySnapshot backup = Snapshot(4, 'c');
        var clock = Clock();
        var registry = WithManualPrimary(primary, Stub(Success(backup)), clock);
        try
        {
            Task<UpdateSourceRegistryLoadResult> pending = registry.LoadAsync(TestContext.Current.CancellationToken).AsTask();
            await primary.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await clock.WhenPendingAsync(1, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            UpdateSourceRegistryLoadResult outcome = await pending;
            Assert.Same(backup, outcome.Snapshot);
            Assert.Equal(UpdateSourceRegistryLoadIssue.None, outcome.Issue);
            Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryTimedOut, outcome.Replicas![0].Issue);
            Assert.True(outcome.Replicas[1].IsSelected);
        }
        finally
        {
            _ = primary.Completion.TrySetResult(new(null, UpdateSourceRegistryLoadIssue.RegistryUnavailable));
        }
    }

    /// <summary>Three checks retain one stalled physical read and read the healthy replica three times.</summary>
    [Fact]
    public async Task RepeatedTimeoutsKeepOnePhysicalReadPerReplica()
    {
        var primary = new GatedRegistry(synchronous: true);
        var available = new GatedRegistry();
        UpdateSourceRegistrySnapshot backup = Snapshot(4, 'c');
        available.Completion.SetResult(Success(backup));
        var clock = Clock();
        var registry = WithManualPrimary(primary, available, clock);
        try
        {
            Task<UpdateSourceRegistryLoadResult> pending = registry.LoadAsync(TestContext.Current.CancellationToken).AsTask();
            await primary.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await clock.WhenPendingAsync(1, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            UpdateSourceRegistryLoadResult first = await pending;
            UpdateSourceRegistryLoadResult second = await registry.LoadAsync(TestContext.Current.CancellationToken);
            UpdateSourceRegistryLoadResult third = await registry.LoadAsync(TestContext.Current.CancellationToken);
            Assert.All(new[] { first, second, third }, outcome =>
            {
                Assert.Same(backup, outcome.Snapshot);
                Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryTimedOut, outcome.Replicas![0].Issue);
            });
            Assert.Equal(1, primary.LoadCount);
            Assert.Equal(3, available.LoadCount);
        }
        finally
        {
            _ = primary.Completion.TrySetResult(new(null, UpdateSourceRegistryLoadIssue.RegistryUnavailable));
        }
    }

    /// <summary>A single explicit locator receives the same bounded single-flight protection.</summary>
    [Fact]
    public async Task SingleReplicaRepeatedTimeoutsKeepOnePhysicalRead()
    {
        var primary = new GatedRegistry();
        var clock = Clock();
        var registry = new ReplicatedUpdateSourceRegistry([primary], TimeSpan.FromMilliseconds(100), clock);
        try
        {
            Task<UpdateSourceRegistryLoadResult> pending = registry.LoadAsync(TestContext.Current.CancellationToken).AsTask();
            await primary.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await clock.WhenPendingAsync(1, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            UpdateSourceRegistryLoadResult first = await pending;
            UpdateSourceRegistryLoadResult second = await registry.LoadAsync(TestContext.Current.CancellationToken);
            UpdateSourceRegistryLoadResult third = await registry.LoadAsync(TestContext.Current.CancellationToken);
            Assert.All(new[] { first, second, third }, static outcome =>
            {
                Assert.Null(outcome.Snapshot);
                Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryTimedOut, outcome.Issue);
                Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryTimedOut, outcome.Replicas![0].Issue);
            });
            Assert.Equal(1, primary.LoadCount);
        }
        finally
        {
            _ = primary.Completion.TrySetResult(Success(Snapshot()));
        }
    }

    /// <summary>Eight concurrent callers share each physical read.</summary>
    [Fact]
    public async Task ConcurrentTimeoutsKeepOnePhysicalReadPerReplica()
    {
        var primary = new GatedRegistry(synchronous: true);
        var available = new GatedRegistry();
        UpdateSourceRegistrySnapshot backup = Snapshot(4, 'c');
        var clock = Clock();
        var registry = WithManualPrimary(primary, available, clock);
        try
        {
            Task<UpdateSourceRegistryLoadResult>[] pending = [.. Enumerable.Range(0, 8)
                .Select(_ => registry.LoadAsync(TestContext.Current.CancellationToken).AsTask())];
            await primary.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await available.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await clock.WhenPendingAsync(1, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            available.Completion.SetResult(Success(backup));
            clock.Advance(TimeSpan.FromMilliseconds(100));
            UpdateSourceRegistryLoadResult[] outcomes = await Task.WhenAll(pending);
            Assert.All(outcomes, outcome =>
            {
                Assert.Same(backup, outcome.Snapshot);
                Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryTimedOut, outcome.Replicas![0].Issue);
                Assert.True(outcome.Replicas[1].IsSelected);
            });
            Assert.Equal(1, primary.LoadCount);
            Assert.Equal(1, available.LoadCount);
        }
        finally
        {
            _ = available.Completion.TrySetResult(Success(backup));
            _ = primary.Completion.TrySetResult(new(null, UpdateSourceRegistryLoadIssue.RegistryUnavailable));
        }
    }

    /// <summary>A completed abandoned read is retired and one new physical read can recover.</summary>
    [Fact]
    public async Task CompletedAbandonedReadAllowsOneFreshPhysicalRead()
    {
        var primary = new GatedRegistry();
        var clock = Clock();
        UpdateSourceRegistrySnapshot backup = Snapshot(4, 'c');
        var registry = WithManualPrimary(primary, Stub(Success(backup)), clock);
        Task<UpdateSourceRegistryLoadResult> pending = registry.LoadAsync(TestContext.Current.CancellationToken).AsTask();
        await primary.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await clock.WhenPendingAsync(1, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        UpdateSourceRegistryLoadResult timedOut = await pending;
        primary.Completion.SetResult(new(null, UpdateSourceRegistryLoadIssue.RegistryUnavailable));
        UpdateSourceRegistryLoadResult recovered = await LoadUntilRecoveredAsync(registry)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryTimedOut, timedOut.Replicas![0].Issue);
        Assert.Same(backup, recovered.Snapshot);
        Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryUnavailable, recovered.Replicas![0].Issue);
        Assert.Equal(2, primary.LoadCount);
    }

    /// <summary>A late abandoned fault is retired and does not poison the next physical read.</summary>
    [Fact]
    public async Task LateAbandonedFailureIsRetired()
    {
        var primary = new GatedRegistry();
        var clock = Clock();
        var registry = new ReplicatedUpdateSourceRegistry([primary], TimeSpan.FromMilliseconds(100), clock);
        Task<UpdateSourceRegistryLoadResult> pending = registry.LoadAsync(TestContext.Current.CancellationToken).AsTask();
        await primary.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await clock.WhenPendingAsync(1, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryTimedOut, (await pending).Issue);
        primary.Completion.SetException(new IOException("Synthetic late read failure."));
        UpdateSourceRegistryLoadResult recovered = await LoadUntilRecoveredAsync(registry)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryUnavailable, recovered.Issue);
        Assert.Equal(2, primary.LoadCount);
    }

    /// <summary>Caller cancellation propagates while a synchronous adapter remains blocked.</summary>
    [Fact]
    public async Task CallerCancellationPropagatesWhileReplicaIsSynchronouslyBlocked()
    {
        var primary = new GatedRegistry(synchronous: true);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var registry = new ReplicatedUpdateSourceRegistry([primary]);
        try
        {
            Task<OperationCanceledException> canceled = Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => registry.LoadAsync(caller.Token).AsTask());
            await primary.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await caller.CancelAsync();
            _ = await canceled;
            Assert.False(primary.ReceivedToken.CanBeCanceled);
            Assert.Equal(1, primary.LoadCount);
        }
        finally
        {
            _ = primary.Completion.TrySetResult(Success(Snapshot()));
        }
    }

    /// <summary>Canceling one waiter leaves another waiter on the same uncanceled physical read.</summary>
    [Fact]
    public async Task CallerCancellationDoesNotCancelSharedRead()
    {
        var primary = new GatedRegistry();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var registry = new ReplicatedUpdateSourceRegistry([primary]);
        Task<OperationCanceledException> canceled = Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => registry.LoadAsync(caller.Token).AsTask());
        Task<UpdateSourceRegistryLoadResult> remaining = registry.LoadAsync(TestContext.Current.CancellationToken).AsTask();
        await primary.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await caller.CancelAsync();
        _ = await canceled;
        UpdateSourceRegistrySnapshot publication = Snapshot();
        primary.Completion.SetResult(Success(publication));
        Assert.Same(publication, (await remaining).Snapshot);
        Assert.False(primary.ReceivedToken.CanBeCanceled);
        Assert.Equal(1, primary.LoadCount);
    }

    /// <summary>A pre-canceled caller does not contact the port.</summary>
    [Fact]
    public async Task PreCanceledCallerStartsNoRead()
    {
        var primary = new GatedRegistry();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        var registry = new ReplicatedUpdateSourceRegistry([primary]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.LoadAsync(caller.Token).AsTask());
        Assert.Equal(0, primary.LoadCount);
    }

    /// <summary>Synchronous throws, asynchronous faults and adapter cancellation all become unavailable.</summary>
    [Theory]
    [InlineData("throw")]
    [InlineData("fault")]
    [InlineData("cancel")]
    public async Task AdapterFailuresAreUnavailable(string failure)
    {
        var port = new DelegateRegistry(_ => failure switch
        {
            "throw" => throw new IOException("Synthetic synchronous read failure."),
            "fault" => new(Task.FromException<UpdateSourceRegistryLoadResult>(new IOException("Synthetic fault."))),
            _ => new(Task.FromCanceled<UpdateSourceRegistryLoadResult>(new CancellationToken(canceled: true))),
        });
        var registry = new ReplicatedUpdateSourceRegistry([port]);
        Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryUnavailable,
            (await registry.LoadAsync(TestContext.Current.CancellationToken)).Issue);
    }

    /// <summary>The minimum positive timeout and its adjacent tick remain legal and bounded.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MinimumPositiveTimeoutIsAdmitted(long ticks)
    {
        var primary = new GatedRegistry();
        var clock = Clock();
        TimeSpan timeout = TimeSpan.FromTicks(ticks);
        var registry = new ReplicatedUpdateSourceRegistry([primary], timeout, clock);
        try
        {
            Task<UpdateSourceRegistryLoadResult> pending = registry.LoadAsync(TestContext.Current.CancellationToken).AsTask();
            // WaitAsync may round sub-millisecond deadlines to immediate expiration.
            // Advance past either representation without requiring a timer to exist.
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(UpdateSourceRegistryLoadIssue.RegistryTimedOut, (await pending).Issue);
        }
        finally
        {
            _ = primary.Completion.TrySetResult(Success(Snapshot()));
        }
    }
}
