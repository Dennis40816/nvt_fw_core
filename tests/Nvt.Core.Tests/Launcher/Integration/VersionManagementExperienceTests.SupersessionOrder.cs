// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;

namespace Nvt.Core.Tests.Launcher.Integration;

public sealed partial class VersionManagementExperienceTests
{
    /// <summary>
    /// Superseding a running check restores the prior source status before it cancels the check, so a
    /// check whose cancellation completes inline never returns the transient Checking snapshot.
    /// </summary>
    [Fact]
    public async Task SupersessionRestoresSourceStatusBeforeCancellingTheRunningCheck()
    {
        VersionManagerState initial = State(
            [Admission("0.10.5")],
            active: "0.10.5",
            lastKnownGood: "0.10.5");
        var source = new InlineCancellingCatalogSource(new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None));
        using var stateStore = new MemoryStateStore(initial);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"),
            "managed-root",
            stateStore,
            source,
            new HealthyRepository());
        VersionManagementSnapshot completed = await experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken);
        Assert.Equal(VersionSourceStatus.Connected, completed.SourceStatus);
        Task<VersionManagementSnapshot> interruptedCheck = experience.CheckAsync(
            isAutomatic: false,
            TestContext.Current.CancellationToken).AsTask();
        _ = await source.SecondCheckStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        VersionManagementSnapshot restored = await experience.AcknowledgeRetentionReviewAsync(
            TestContext.Current.CancellationToken);
        VersionManagementSnapshot interruptedResult = await interruptedCheck;

        Assert.True(source.CancellationCompletedTheCheckInline);
        Assert.Equal(VersionSourceStatus.Connected, restored.SourceStatus);
        Assert.Equal(VersionSourceStatus.Connected, interruptedResult.SourceStatus);
    }

    /// <summary>Completes the second load inside the cancellation callback, so the whole interrupted check runs inline.</summary>
    private sealed class InlineCancellingCatalogSource(UpdateCatalogLoadResult firstResult) : IRootCatalogSourceTestDouble
    {
        // Guard: Interlocked counts loads; Volatile reads and writes publish the two flags across threads.
        private int _loadCount;
        private int _interruptedCheckObserved;
        private int _completedInline;

        internal TaskCompletionSource<bool> SecondCheckStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool CancellationCompletedTheCheckInline => Volatile.Read(ref _completedInline) != 0;

        public async ValueTask<UpdateCatalogLoadResult> LoadAsync(
            string sourceRoot,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _loadCount) == 1) { return firstResult; }

            // The default options let the continuation run on the thread that cancels.
            var pending = new TaskCompletionSource<UpdateCatalogLoadResult>();
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                _ = pending.TrySetCanceled(cancellationToken);
                Volatile.Write(ref _completedInline, Volatile.Read(ref _interruptedCheckObserved));
            });
            _ = SecondCheckStarted.TrySetResult(true);
            try
            {
                return await pending.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Volatile.Write(ref _interruptedCheckObserved, 1);
                throw;
            }
        }
    }
}
