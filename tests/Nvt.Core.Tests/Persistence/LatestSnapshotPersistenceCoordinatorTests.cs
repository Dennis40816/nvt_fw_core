// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Persistence;
using Xunit;

namespace Nvt.Core.Tests.Persistence;

/// <summary>Ports the frozen coordinator tests using synthetic snapshots and characterizes shutdown races.</summary>
public sealed class LatestSnapshotPersistenceCoordinatorTests
{
    // Ported coordinator scenarios from ReportHistoryPersistenceTests.
    /// <summary>Queued persistence serializes writes and drops a superseded snapshot before it starts.</summary>
    [Fact]
    public async Task CoordinatorKeepsLatestQueuedSnapshot()
    {
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> savedSources = [];
        LatestSnapshotPersistenceCoordinator<IReadOnlyList<Snapshot>> coordinator =
            CreateCoordinator(async (snapshots, _) =>
        {
            string source = Assert.Single(snapshots).SourceName;
            lock (savedSources)
            {
                savedSources.Add(source);
            }

            if (string.Equals(source, "first.json", StringComparison.Ordinal))
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
            }
        });

        coordinator.Queue([CreateSnapshot("first.json")]);
        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Queue([CreateSnapshot("superseded.json")]);
        coordinator.Queue([CreateSnapshot("latest.json")]);
        releaseFirst.SetResult();

        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["first.json", "latest.json"], savedSources);
    }

    /// <summary>Shutdown waits for the latest save and seals the coordinator against later writes.</summary>
    [Fact]
    public async Task CoordinatorCompletesLatestSaveBeforeShutdown()
    {
        IReadOnlyList<Snapshot> persisted = [];
        TaskCompletionSource saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        LatestSnapshotPersistenceCoordinator<IReadOnlyList<Snapshot>> coordinator =
            CreateCoordinator(async (snapshots, cancellationToken) =>
        {
            saveStarted.SetResult();
            await releaseSave.Task;
            cancellationToken.ThrowIfCancellationRequested();
            persisted = snapshots;
        });
        coordinator.Queue([CreateSnapshot("latest-before-close.json")]);
        await saveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        Task completion = coordinator.CompleteAsync();

        Assert.False(completion.IsCompleted);
        releaseSave.SetResult();
        await completion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            "latest-before-close.json",
            Assert.Single(persisted).SourceName);
        _ = Assert.Throws<InvalidOperationException>(
            () => coordinator.Queue([CreateSnapshot("after-close.json")]));
    }

    /// <summary>A resumed save keeps the original serial tail, so an older delayed write cannot win.</summary>
    [Fact]
    public async Task CoordinatorReopenSerializesNewSnapshotAfterDelayedOldSave()
    {
        TaskCompletionSource oldStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseOld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> persisted = [];
        LatestSnapshotPersistenceCoordinator<IReadOnlyList<Snapshot>> coordinator =
            CreateCoordinator(async (snapshots, _) =>
            {
                string name = Assert.Single(snapshots).SourceName;
                if (name == "old.json")
                {
                    oldStarted.SetResult();
                    await releaseOld.Task;
                }
                lock (persisted)
                {
                    persisted.Add(name);
                }
            });

        coordinator.Queue([CreateSnapshot("old.json")]);
        await oldStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task sealedTail = coordinator.CompleteAsync();
        coordinator.Reopen();
        coordinator.Queue([CreateSnapshot("new.json")]);
        releaseOld.SetResult();
        await Task.WhenAll(sealedTail, coordinator.CompleteAsync())
            .WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["old.json", "new.json"], persisted);
    }

    /// <summary>An unexpected best-effort save fault is observed without poisoning later persistence.</summary>
    [Fact]
    public async Task CoordinatorRecoversAfterSaveFault()
    {
        List<string> attemptedSources = [];
        LatestSnapshotPersistenceCoordinator<IReadOnlyList<Snapshot>> coordinator =
            CreateCoordinator((snapshots, _) =>
        {
            string source = Assert.Single(snapshots).SourceName;
            attemptedSources.Add(source);
            return string.Equals(source, "faulted.json", StringComparison.Ordinal)
                ? Task.FromException(new InvalidOperationException("synthetic persistence failure"))
                : Task.CompletedTask;
        });

        coordinator.Queue([CreateSnapshot("faulted.json")]);
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Queue([CreateSnapshot("recovered.json")]);

        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["faulted.json", "recovered.json"], attemptedSources);
        _ = Assert.IsType<InvalidOperationException>(coordinator.LastFailure);
    }

    // Ported coordinator scenarios from LocalStateSaveNoticeTests.
    /// <summary>
    /// The coordinator reports finished and failed saves in queue order and never a superseded one, including an
    /// in-flight write that ignores its cancellation and finishes after a newer snapshot was queued.
    /// </summary>
    [Fact]
    public async Task CoordinatorReportsTerminalSavesButNotSupersededOnes()
    {
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> saved = [];
        List<string> outcomes = [];
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            async (snapshot, _) =>
            {
                lock (saved)
                {
                    saved.Add(snapshot);
                }

                if (snapshot == "first")
                {
                    firstStarted.SetResult();
                    // The write ignores its cancellation and finishes after it was superseded.
                    await releaseFirst.Task;
                }
                else if (snapshot == "failed")
                {
                    throw new IOException("synthetic failure");
                }
            },
            static snapshot => snapshot,
            (failure, _) =>
            {
                lock (outcomes)
                {
                    outcomes.Add(failure?.Message ?? "saved");
                }
            });

        coordinator.Queue("first");
        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Queue("superseded");
        coordinator.Queue("failed");
        releaseFirst.SetResult();
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["first", "failed"], saved);
        Assert.Equal(["synthetic failure"], outcomes);
        _ = Assert.IsType<IOException>(coordinator.LastFailure);

        coordinator.Queue("latest");
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["first", "failed", "latest"], saved);
        Assert.Equal(["synthetic failure", "saved"], outcomes);
    }

    /// <summary>A reported generation becomes stale when a newer snapshot is queued before deferred apply.</summary>
    [Fact]
    public async Task GenerationStaysCurrentOnlyUntilANewerSnapshotIsQueued()
    {
        TaskCompletionSource bStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseB = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<(Exception? Failure, long Generation)> reported = [];
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            async (snapshot, _) =>
            {
                if (snapshot == "B")
                {
                    bStarted.SetResult();
                    await releaseB.Task;
                }
            },
            static snapshot => snapshot,
            (failure, generation) =>
            {
                lock (reported)
                {
                    reported.Add((failure, generation));
                }
            });

        coordinator.Queue("A");
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
        (Exception? aFailure, long aGeneration) = Assert.Single(reported);
        Assert.Null(aFailure);
        // Nothing newer is queued yet: applying A's outcome right now would still be correct.
        Assert.True(coordinator.IsCurrentGeneration(aGeneration));

        // B is queued only after A's own completion already reported — the gap a UI dispatcher post leaves open.
        coordinator.Queue("B");
        await bStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Applying A's outcome now must recognize it as stale, even though the coordinator already reported it as
        // a terminal, non-superseded save; only B's own future report may still clear a notice for this state.
        Assert.False(coordinator.IsCurrentGeneration(aGeneration));

        releaseB.SetResult();
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, reported.Count);
        (Exception? bFailure, long bGeneration) = reported[1];
        Assert.Null(bFailure);
        Assert.True(coordinator.IsCurrentGeneration(bGeneration));
    }

    /// <summary>Retry re-queues the latest captured snapshot through the same serialized coordinator.</summary>
    [Fact]
    public async Task CoordinatorRetryRequeuesLatestCapturedSnapshot()
    {
        int captures = 0;
        List<string> saved = [];
        List<Exception?> outcomes = [];
        bool fail = true;
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            (snapshot, _) =>
            {
                saved.Add(snapshot);
                return fail ? Task.FromException(new IOException("synthetic failure")) : Task.CompletedTask;
            },
            snapshot =>
            {
                captures++;
                return snapshot + "#captured";
            },
            (failure, _) => outcomes.Add(failure));

        Assert.False(coordinator.TryRetry());
        coordinator.Queue("latest");
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(coordinator.TryRetry());
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
        fail = false;
        Assert.True(coordinator.TryRetry());
        await coordinator.CompleteAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["latest#captured", "latest#captured", "latest#captured"], saved);
        Assert.Equal(1, captures);
        Assert.Equal(3, outcomes.Count);
        _ = Assert.IsType<IOException>(outcomes[0]);
        _ = Assert.IsType<IOException>(outcomes[1]);
        Assert.Null(outcomes[2]);
        Assert.False(coordinator.TryRetry());
    }

    /// <summary>A failing outcome observer cannot stop later saves from running and being observed.</summary>
    [Fact]
    public async Task CoordinatorObserverFailureDoesNotPoisonLaterSaves()
    {
        List<string> saved = [];
        int observed = 0;
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            (snapshot, _) =>
            {
                saved.Add(snapshot);
                return Task.CompletedTask;
            },
            static snapshot => snapshot,
            (_, _) =>
            {
                observed++;
                throw new InvalidOperationException("synthetic observer failure");
            });

        coordinator.Queue("first");
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Queue("second");
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second"], saved);
        Assert.Equal(2, observed);
        Assert.Null(coordinator.LastFailure);
    }

    /// <summary>Shutdown seals admission while a concurrent Queue call is still capturing its input.</summary>
    [Fact]
    public async Task ShutdownRejectsSnapshotStillBeingCaptured()
    {
        TaskCompletionSource saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource captureStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCapture = new ManualResetEventSlim();
        List<string> saved = [];
        List<(Exception? Failure, long Generation)> outcomes = [];
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            async (snapshot, cancellationToken) =>
            {
                saveStarted.SetResult();
                await releaseSave.Task;
                cancellationToken.ThrowIfCancellationRequested();
                saved.Add(snapshot);
            },
            snapshot =>
            {
                if (snapshot == "racing")
                {
                    captureStarted.SetResult();
                    releaseCapture.Wait(TestContext.Current.CancellationToken);
                }

                return snapshot;
            },
            (failure, generation) => outcomes.Add((failure, generation)));

        coordinator.Queue("admitted");
        await saveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task<InvalidOperationException> racingQueue = Task.Run(() =>
            Assert.Throws<InvalidOperationException>(() => coordinator.Queue("racing")),
            TestContext.Current.CancellationToken);
        try
        {
            await captureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Task completion = coordinator.CompleteAsync();
            Assert.False(completion.IsCompleted);
            Assert.False(coordinator.TryRetry());
            releaseCapture.Set();
            InvalidOperationException rejected = await racingQueue.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Local-state persistence is already completing.", rejected.Message);

            releaseSave.SetResult();
            await completion.WaitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["admitted"], saved);
            (Exception? failure, long generation) = Assert.Single(outcomes);
            Assert.Null(failure);
            Assert.Equal(1, generation);
            Assert.True(coordinator.IsCurrentGeneration(generation));
            Assert.Null(coordinator.LastFailure);
        }
        finally
        {
            releaseCapture.Set();
            _ = releaseSave.TrySetResult();
            await racingQueue.WaitAsync(TestContext.Current.CancellationToken);
            await coordinator.CompleteAsync().WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Closing after save cleanup disposed its cancellation source still waits for its terminal observer.</summary>
    [Fact]
    public async Task ShutdownDuringSaveCleanupWaitsForObserverAndRejectsNewWrites()
    {
        TaskCompletionSource observerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseObserver = new ManualResetEventSlim();
        List<string> saved = [];
        List<(Exception? Failure, long Generation)> outcomes = [];
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            (snapshot, _) =>
            {
                saved.Add(snapshot);
                return Task.CompletedTask;
            },
            static snapshot => snapshot,
            (failure, generation) =>
            {
                _ = observerStarted.TrySetResult();
                releaseObserver.Wait(TestContext.Current.CancellationToken);
                outcomes.Add((failure, generation));
            });

        coordinator.Queue("before-close");
        try
        {
            await observerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            // The save's CancellationTokenSource is disposed before this observer is invoked.
            // A retry here must queue safely behind the still-running observer.
            Assert.True(coordinator.TryRetry());
            Task completion = coordinator.CompleteAsync();
            Assert.False(completion.IsCompleted);
            Assert.Same(completion, coordinator.CompleteAsync());
            Assert.False(coordinator.TryRetry());
            _ = Assert.Throws<InvalidOperationException>(() => coordinator.Queue("after-close"));
            Assert.Equal(["before-close"], saved);

            releaseObserver.Set();
            await completion.WaitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["before-close", "before-close"], saved);
            Assert.Equal([(null, 1L), (null, 2L)], outcomes);
            Assert.Null(coordinator.LastFailure);
        }
        finally
        {
            releaseObserver.Set();
            await coordinator.CompleteAsync().WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Mutable input is copied at Queue time rather than when its serialized save finally starts.</summary>
    [Fact]
    public async Task QueueCapturesMutableInputBeforeBackgroundSave()
    {
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> saved = [];
        LatestSnapshotPersistenceCoordinator<IReadOnlyList<Snapshot>> coordinator =
            CreateCoordinator(async (snapshots, _) =>
            {
                string name = Assert.Single(snapshots).SourceName;
                saved.Add(name);
                if (name == "first")
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task;
                }
            });

        coordinator.Queue([CreateSnapshot("first")]);
        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        List<Snapshot> mutable = [CreateSnapshot("captured")];
        coordinator.Queue(mutable);
        mutable[0] = CreateSnapshot("mutated");
        releaseFirst.SetResult();
        await coordinator.CompleteAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["first", "captured"], saved);
    }

    /// <summary>A superseded in-flight save observes cancellation without recording a failure or outcome.</summary>
    [Fact]
    public async Task SupersededSaveCancellationIsSilent()
    {
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource holdFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> saved = [];
        List<(Exception? Failure, long Generation)> outcomes = [];
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            async (snapshot, cancellationToken) =>
            {
                if (snapshot == "first")
                {
                    firstStarted.SetResult();
                    await holdFirst.Task.WaitAsync(cancellationToken);
                }

                saved.Add(snapshot);
            },
            static snapshot => snapshot,
            (failure, generation) => outcomes.Add((failure, generation)));

        coordinator.Queue("first");
        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Queue("latest");
        await coordinator.CompleteAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["latest"], saved);
        Assert.Equal([(null, 2L)], outcomes);
        Assert.Null(coordinator.LastFailure);
    }

    /// <summary>Cancellation not requested by supersession remains a save failure with its original exception.</summary>
    [Fact]
    public async Task UnrequestedCancellationIsReportedAsFailure()
    {
        var failure = new OperationCanceledException("synthetic unrelated cancellation");
        List<(Exception? Failure, long Generation)> outcomes = [];
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            (_, _) => Task.FromException(failure),
            static snapshot => snapshot,
            (exception, generation) => outcomes.Add((exception, generation)));

        coordinator.Queue("snapshot");
        await coordinator.CompleteAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Same(failure, coordinator.LastFailure);
        (Exception? reported, long generation) = Assert.Single(outcomes);
        Assert.Same(failure, reported);
        Assert.Equal(1, generation);
    }

    /// <summary>Retry advances the generation, reuses the captured value, and keeps the last recorded failure.</summary>
    [Fact]
    public async Task SuccessfulRetryAdvancesGenerationAndRetainsLastFailure()
    {
        var failure = new IOException("synthetic failure");
        List<(Exception? Failure, long Generation)> outcomes = [];
        int attempts = 0;
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            (_, _) => ++attempts == 1 ? Task.FromException(failure) : Task.CompletedTask,
            static snapshot => snapshot,
            (exception, generation) => outcomes.Add((exception, generation)));

        coordinator.Queue("snapshot");
        await coordinator.WaitForIdleAsync().WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(coordinator.TryRetry());
        await coordinator.CompleteAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal([(failure, 1L), (null, 2L)], outcomes);
        Assert.False(coordinator.IsCurrentGeneration(1));
        Assert.True(coordinator.IsCurrentGeneration(2));
        Assert.Same(failure, coordinator.LastFailure);
    }

    /// <summary>Empty completion is idempotent, and reopening admits a save without resetting the generation.</summary>
    [Fact]
    public async Task EmptyCompletionCanReopenWithoutResettingGeneration()
    {
        List<string> saved = [];
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            (snapshot, _) =>
            {
                saved.Add(snapshot);
                return Task.CompletedTask;
            },
            static snapshot => snapshot);

        Assert.True(coordinator.IsCurrentGeneration(0));
        Task emptyTail = coordinator.WaitForIdleAsync();
        Assert.True(emptyTail.IsCompletedSuccessfully);
        Assert.Same(emptyTail, coordinator.CompleteAsync());
        Assert.False(coordinator.TryRetry());
        coordinator.Reopen();
        coordinator.Queue("first");
        await coordinator.CompleteAsync().WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Reopen();
        Assert.True(coordinator.TryRetry());
        await coordinator.CompleteAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["first", "first"], saved);
        Assert.True(coordinator.IsCurrentGeneration(2));
    }

    /// <summary>Invalid delegates or snapshots fail synchronously before a save is admitted.</summary>
    [Fact]
    public void RejectsNullDelegatesAndSnapshots()
    {
        ArgumentNullException save = Assert.Throws<ArgumentNullException>(() =>
            new LatestSnapshotPersistenceCoordinator<string>(null!, static snapshot => snapshot));
        ArgumentNullException capture = Assert.Throws<ArgumentNullException>(() =>
            new LatestSnapshotPersistenceCoordinator<string>(static (_, _) => Task.CompletedTask, null!));
        var coordinator = new LatestSnapshotPersistenceCoordinator<string>(
            static (_, _) => throw new InvalidOperationException("Unexpected save."),
            static _ => null!);
        ArgumentNullException snapshot = Assert.Throws<ArgumentNullException>(() => coordinator.Queue(null!));
        ArgumentNullException captured = Assert.Throws<ArgumentNullException>(() => coordinator.Queue("input"));

        Assert.Equal("saveAsync", save.ParamName);
        Assert.Equal("capture", capture.ParamName);
        Assert.Equal("snapshot", snapshot.ParamName);
        Assert.Equal("capturedSnapshot", captured.ParamName);
        Assert.True(coordinator.WaitForIdleAsync().IsCompletedSuccessfully);
        Assert.False(coordinator.TryRetry());
        Assert.Null(coordinator.LastFailure);
    }

    private static Snapshot CreateSnapshot(string sourceName) => new(sourceName);

    private static LatestSnapshotPersistenceCoordinator<IReadOnlyList<Snapshot>> CreateCoordinator(
        Func<IReadOnlyList<Snapshot>, CancellationToken, Task> saveAsync)
    {
        return new LatestSnapshotPersistenceCoordinator<IReadOnlyList<Snapshot>>(
            saveAsync,
            snapshots => [.. snapshots]);
    }

    private sealed record Snapshot(string SourceName);
}
