// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Nvt.Core.Time;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Checks independent custody cleanup when the admission session fails to dispose.</summary>
public sealed class FirstInstallationAdmissionDisposalTests : FirstInstallationTestSupport
{
    [Fact]
    public async Task AdmissionDisposeFailureStillDisposesLeaseAndPromotionAndPreservesCancellation()
    {
        var failure = new IOException("Admission timer disposal failure.");
        var observed = new List<Exception>();
        var disposals = new List<string>();
        var manual = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var time = new AdmissionTimerDisposalProvider(manual, () =>
        {
            disposals.Add("admission");
            throw failure;
        });
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var caller = new CancellationTokenSource();
        harness.Materializer.Transaction.BootstrapLeaseAcquiredAction = () =>
        {
            harness.Materializer.Transaction.LastBootstrapLease!.DisposeAction = () => disposals.Add("lease");
            Cancel(caller);
        };
        harness.Materializer.Transaction.DisposeAction = () => disposals.Add("promotion");
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, harness.Materializer, harness.Handoff,
            timeProvider: time, observerDiagnostics: observed.Add);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(harness.Plan, caller.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(new ManagedFirstInstallationLaunchFailure(
            ManagedFirstInstallationLaunchStage.PostPromotion, ManagedFirstInstallationLaunchIssue.Cancelled),
            result.LaunchFailure);
        Assert.Same(failure, Assert.Single(observed));
        Assert.Collection(disposals,
            disposal => Assert.Equal("admission", disposal),
            disposal => Assert.Equal("lease", disposal),
            disposal => Assert.Equal("promotion", disposal));
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.True(harness.Payload.LastCapture!.Disposed);
        Assert.False(harness.State.LeaseActive);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    [Fact]
    public async Task NullDiagnosticsWithAdmissionDisposeFailureSilentlyPreservesRecovery()
    {
        var disposals = new List<string>();
        var manual = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var time = new AdmissionTimerDisposalProvider(manual, () =>
        {
            disposals.Add("admission");
            throw new IOException("Admission timer disposal failure.");
        });
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Handoff.StartIssue = ImmutableBootstrapStartIssue.Damaged;
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, harness.Materializer, harness.Handoff,
            timeProvider: time, observerDiagnostics: null);

        // Null diagnostics means silent.
        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(new ManagedFirstInstallationLaunchFailure(
            ManagedFirstInstallationLaunchStage.BootstrapStart, ManagedFirstInstallationLaunchIssue.Damaged),
            result.LaunchFailure);
        Assert.Equal("admission", Assert.Single(disposals));
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.True(harness.Payload.LastCapture!.Disposed);
        Assert.False(harness.State.LeaseActive);
        Assert.Equal(1, harness.Materializer.Transaction.RecordLaunchCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    // Decorates disposal only: the shared manual provider owns every clock read and timer schedule.
    private sealed class AdmissionTimerDisposalProvider(ManualTimeProvider manual, Action onDispose)
        : DelegateTimeProvider(manual.GetUtcNow, manual.GetTimestamp, manual.TimestampFrequency)
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = manual.CreateTimer(callback, state, dueTime, period);
            if (dueTime == ManagedFirstInstallationExperience.DefaultAdmissionOperationCutoff)
            {
                return new DisposalObserverTimer(timer, onDispose);
            }
            return timer;
        }
    }

    private sealed class DisposalObserverTimer(ITimer inner, Action onDispose) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

        public void Dispose()
        {
            inner.Dispose();
            onDispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
