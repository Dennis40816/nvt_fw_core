// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

public sealed partial class ManagedActivationCoordinatorTests
{
    /// <summary>Two launchers cannot start the same requested candidate concurrently.</summary>
    [Fact]
    public async Task ConcurrentLaunchersStartRequestedCandidateOnlyOnce()
    {
        VersionManagerState pending = VersionActivationPolicy.BeginActivation(
            State(),
            ManagedAppVersion.Parse("0.10.6"));
        var store = new ExclusiveStateStore(pending);
        var process = new BlockingProcess();
        var first = new ManagedActivationCoordinator("managed", store, new HealthyRepository(), process);
        var second = new ManagedActivationCoordinator("managed", store, new HealthyRepository(), process);

        Task<ManagedLauncherResult> owner = first.RunAsync(TestContext.Current.CancellationToken).AsTask();
        await process.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        ManagedLauncherResult contended = await second.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.Busy, contended.Outcome);
        Assert.Equal([ManagedAppVersion.Parse("0.10.6")], process.StartedVersions);
        _ = process.Release.TrySetResult(ManagedProcessStartOutcome.Ready);
        Assert.Equal(ManagedLauncherOutcome.Ready, (await owner).Outcome);
        Assert.Null(store.State.PendingActivation);
    }

    /// <summary>Two launchers cannot start the recorded fallback concurrently.</summary>
    [Fact]
    public async Task ConcurrentLaunchersStartRecordedFallbackOnlyOnce()
    {
        VersionManagerState pending = VersionActivationPolicy.RecordCandidateLaunch(
            VersionActivationPolicy.BeginActivation(
                State(),
                ManagedAppVersion.Parse("0.10.6")));
        var store = new ExclusiveStateStore(pending);
        var process = new BlockingProcess();
        var first = new ManagedActivationCoordinator("managed", store, new HealthyRepository(), process);
        var second = new ManagedActivationCoordinator("managed", store, new HealthyRepository(), process);

        Task<ManagedLauncherResult> owner = first.RunAsync(TestContext.Current.CancellationToken).AsTask();
        await process.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        ManagedLauncherResult contended = await second.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.Busy, contended.Outcome);
        Assert.Equal([ManagedAppVersion.Parse("0.10.5")], process.StartedVersions);
        _ = process.Release.TrySetResult(ManagedProcessStartOutcome.Ready);
        Assert.Equal(ManagedLauncherOutcome.RolledBack, (await owner).Outcome);
        Assert.Equal(ManagedAppVersion.Parse("0.10.5"), store.State.ActiveVersion);
        Assert.Null(store.State.PendingActivation);
    }

    private sealed class ExclusiveStateStore(VersionManagerState state) : IVersionManagerStateStore
    {
        private int _writerOwned;

        internal VersionManagerState State { get; private set; } = state;

        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _writerOwned, 1, 0) != 0)
            {
                return ValueTask.FromResult(
                    VersionManagerWriteLeaseTestSupport.Busy());
            }
#pragma warning disable CA2000 // Ownership transfers to VersionManagerWriteLeaseResult.
            var result = new VersionManagerWriteLeaseResult(
                VersionManagerWriteLeaseIssue.None,
                new ReleaseHandle(this));
#pragma warning restore CA2000
            return ValueTask.FromResult(result);
        }

        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(
                new VersionManagerStateLoadResult(State, VersionManagerStateLoadIssue.None));
        }

        public ValueTask SaveAsync(VersionManagerState stateToSave, CancellationToken cancellationToken)
        {
            State = stateToSave;
            return ValueTask.CompletedTask;
        }

        private sealed class ReleaseHandle(ExclusiveStateStore owner) : IDisposable
        {
            public void Dispose()
            {
                _ = Interlocked.Exchange(ref owner._writerOwned, 0);
            }
        }
    }

    private sealed class BlockingProcess : IManagedApplicationProcess
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<ManagedProcessStartOutcome> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal List<ManagedAppVersion> StartedVersions { get; } = [];

        public ValueTask<ManagedProcessLifetimeStatus> GetLifetimeStatusAsync(
            string managedRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ManagedProcessLifetimeStatus.Active);
        }

        public async ValueTask<ManagedProcessStartResult> StartUntilReadyAsync(
            string managedRoot,
            ManagedAppVersion version,
            IManagedExecutableLaunchLease executableLease,
            TimeSpan readyDeadline,
            CancellationToken cancellationToken)
        {
            StartedVersions.Add(version);
            _ = Started.TrySetResult();
            ManagedProcessStartOutcome outcome = await Release.Task.WaitAsync(cancellationToken);
            return new(outcome, ExitCode: null);
        }
    }
}
