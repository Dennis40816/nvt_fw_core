// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Pins adapter-owned cancellation at each filtered post-promotion boundary.</summary>
public sealed class FirstInstallationAdapterCancellationTests : FirstInstallationTestSupport
{
    [Fact]
    public async Task BootstrapStartAdapterCancellationRequiresRecovery()
    {
        var failure = new TaskCanceledException("Start adapter-owned cancellation.");
        var observed = new List<Exception>();
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: observed.Add);
        using var caller = new CancellationTokenSource();
        harness.Handoff.StartAction = () => throw failure;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, caller.Token);

        AssertRecoveryOwnedAndReleased(harness, result);
        Assert.False(caller.IsCancellationRequested);
        Assert.Same(failure, Assert.Single(observed));
        Assert.Equal(1, harness.Handoff.StartCount);
        Assert.Same(harness.Materializer.Transaction.LastBootstrapLease, harness.Handoff.ReceivedLease);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.Null(harness.Handoff.LastLaunch);
    }

    [Fact]
    public async Task BootstrapLeaseAdapterCancellationRequiresRecovery()
    {
        var failure = new TaskCanceledException("Lease adapter-owned cancellation.");
        var observed = new List<Exception>();
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: observed.Add);
        using var caller = new CancellationTokenSource();
        harness.Materializer.Transaction.BootstrapLeaseAcquireAction = () => throw failure;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, caller.Token);

        AssertRecoveryOwnedAndReleased(harness, result);
        Assert.False(caller.IsCancellationRequested);
        Assert.Same(failure, Assert.Single(observed));
        Assert.Equal(1, harness.Materializer.Transaction.BootstrapLeaseAcquireCount);
        Assert.Null(harness.Materializer.Transaction.LastBootstrapLease);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    [Fact]
    public async Task BootstrapAdmissionAdapterCancellationRequiresRecovery()
    {
        var failure = new TaskCanceledException("Admission adapter-owned cancellation.");
        var observed = new List<Exception>();
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: observed.Add);
        using var caller = new CancellationTokenSource();
        harness.Handoff.AdmissionAction = () => throw failure;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, caller.Token);

        AssertRecoveryOwnedAndReleased(harness, result);
        Assert.False(caller.IsCancellationRequested);
        Assert.Same(failure, Assert.Single(observed));
        Assert.Equal(1, harness.Handoff.StartCount);
        Assert.True(harness.Handoff.LastLaunch!.Disposed);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.DoesNotContain("admitted", harness.State.Events);
    }

    [Fact]
    public async Task BootstrapCompletionAdapterCancellationRequiresRecovery()
    {
        var failure = new TaskCanceledException("Completion adapter-owned cancellation.");
        var observed = new List<Exception>();
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: observed.Add);
        using var caller = new CancellationTokenSource();
        harness.Handoff.CompletionAction = () => throw failure;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, caller.Token);

        AssertRecoveryOwnedAndReleased(harness, result);
        Assert.False(caller.IsCancellationRequested);
        Assert.Same(failure, Assert.Single(observed));
        Assert.Equal(1, harness.Handoff.StartCount);
        Assert.True(harness.Handoff.LastLaunch!.Disposed);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.Contains("admitted", harness.State.Events);
        Assert.DoesNotContain("ready", harness.State.Events);
    }

    [Fact]
    public async Task FinalWriterLeaseAdapterCancellationRequiresRecovery()
    {
        var failure = new TaskCanceledException("Final writer adapter-owned cancellation.");
        var observed = new List<Exception>();
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: observed.Add);
        using var caller = new CancellationTokenSource();
        harness.State.SecondLeaseAction = () => throw failure;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, caller.Token);

        AssertRecoveryOwnedAndReleased(harness, result);
        Assert.False(caller.IsCancellationRequested);
        // An unfiltered finalization catch returns recovery too, but incorrectly skips diagnostics.
        Assert.Same(failure, Assert.Single(observed));
        Assert.Equal(2, harness.State.LeaseAttemptCount);
        Assert.Equal(1, harness.State.LeaseCount);
        Assert.Equal(2, harness.State.LoadCount);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
    }

    [Fact]
    public async Task FinalStateRecheckAdapterCancellationRequiresRecovery()
    {
        var failure = new TaskCanceledException("Final state recheck adapter-owned cancellation.");
        var observed = new List<Exception>();
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: observed.Add);
        using var caller = new CancellationTokenSource();
        // The install recheck has finished before the successful READY receipt enables this hook.
        harness.Handoff.CompletionAction = () => harness.State.Observe = stage =>
        {
            if (stage == "state-load")
            {
                throw failure;
            }
        };

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, caller.Token);

        AssertRecoveryOwnedAndReleased(harness, result);
        Assert.False(caller.IsCancellationRequested);
        Assert.Same(failure, Assert.Single(observed));
        Assert.Equal(2, harness.State.LeaseCount);
        Assert.Equal(2, harness.State.LoadCount);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
    }

    [Fact]
    public async Task FinalCompletionRecordAdapterCancellationRequiresRecovery()
    {
        var failure = new TaskCanceledException("Completion record adapter-owned cancellation.");
        var observed = new List<Exception>();
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: observed.Add);
        using var caller = new CancellationTokenSource();
        harness.Materializer.Transaction.CompleteAction = () => throw failure;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, caller.Token);

        AssertRecoveryOwnedAndReleased(harness, result);
        Assert.False(caller.IsCancellationRequested);
        Assert.Same(failure, Assert.Single(observed));
        Assert.Equal(2, harness.State.LeaseCount);
        Assert.Equal(3, harness.State.LoadCount);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
    }

    private static void AssertRecoveryOwnedAndReleased(
        InstallHarness harness, ManagedFirstInstallationResult result)
    {
        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Null(result.LaunchFailure);
        Assert.True(harness.Payload.LastCapture!.Disposed);
        Assert.False(harness.State.LeaseActive);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        // Recording launch without completing the transaction retains the recovery marker.
        Assert.Equal(1, harness.Materializer.Transaction.RecordLaunchCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }
}
