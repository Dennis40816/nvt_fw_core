// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Checks recovery mapping and independent cleanup after irreversible promotion.</summary>
public sealed class FirstInstallationFailureIsolationTests : FirstInstallationTestSupport
{
    [Fact]
    public async Task AdapterCancellationAfterPromotionRequiresRecovery()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var caller = new CancellationTokenSource();
        harness.Materializer.Transaction.RecordLaunchAction = () =>
            throw new TaskCanceledException("Adapter-owned cancellation.");

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, caller.Token);

        Assert.False(caller.IsCancellationRequested);
        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Null(result.LaunchFailure);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.False(harness.State.LeaseActive);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostPromotionAdapterFailureReportsDiagnosticsWithoutChangingRecovery(bool throwFromDiagnostics)
    {
        var failure = new IOException("Post-promotion adapter failure.");
        var observed = new List<Exception>();
        Action<Exception> diagnostics = exception =>
        {
            observed.Add(exception);
            if (throwFromDiagnostics)
            {
                throw new InvalidOperationException("Diagnostics failure.");
            }
        };
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: diagnostics);
        harness.Materializer.Transaction.RecordLaunchAction = () => throw failure;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Same(failure, Assert.Single(observed));
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingLeaseDisposeStillDisposesPromotionAndPreservesCancellation(bool throwFromDiagnostics)
    {
        var leaseFailure = new IOException("Executable lease disposal failure.");
        var promotionFailure = new IOException("Promoted installation disposal failure.");
        var observed = new List<Exception>();
        var disposals = new List<string>();
        Action<Exception> diagnostics = exception =>
        {
            observed.Add(exception);
            if (throwFromDiagnostics)
            {
                throw new InvalidOperationException("Diagnostics failure.");
            }
        };
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: diagnostics);
        using var caller = new CancellationTokenSource();
        harness.Materializer.Transaction.BootstrapLeaseAcquiredAction = () =>
        {
            harness.Materializer.Transaction.LastBootstrapLease!.DisposeAction = () =>
            {
                disposals.Add("lease");
                throw leaseFailure;
            };
            Cancel(caller);
        };
        harness.Materializer.Transaction.DisposeAction = () =>
        {
            disposals.Add("promotion");
            throw promotionFailure;
        };

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, caller.Token);

        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.Equal(ManagedFirstInstallationOutcome.InstalledButLaunchFailed, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.True(result.IsRecoveryOwned);
        Assert.Equal(new ManagedFirstInstallationLaunchFailure(
            ManagedFirstInstallationLaunchStage.PostPromotion, ManagedFirstInstallationLaunchIssue.Cancelled),
            result.LaunchFailure);
        Assert.Equal(["lease", "promotion"], disposals);
        Assert.Collection(observed,
            exception => Assert.Same(leaseFailure, exception),
            exception => Assert.Same(promotionFailure, exception));
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    [Fact]
    public async Task PromotionDisposeFailurePreservesCompletedResult()
    {
        var failure = new IOException("Promoted installation disposal failure.");
        var observed = new List<Exception>();
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: observed.Add);
        harness.Materializer.Transaction.DisposeAction = () => throw failure;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.Completed, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.Same(failure, Assert.Single(observed));
        Assert.Equal(1, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
    }

    [Fact]
    public async Task PrePromotionDisposeFailureEscapesAfterPayloadAndWriterRelease()
    {
        var failure = new IOException("Payload disposal failure.");
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.CandidateSource.ReverificationOverride = new(null, FreshInstallationCandidateIssue.SourceChanged);
        harness.Payload.Observe = stage =>
        {
            if (stage == "payload-dispose")
            {
                throw failure;
            }
        };

        IOException actual = await Assert.ThrowsAsync<IOException>(() =>
            harness.Experience.InstallAndLaunchAsync(harness.Plan, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, actual);
        Assert.True(harness.Payload.LastCapture!.Disposed);
        Assert.False(harness.State.LeaseActive);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    public async Task FatalAdapterFailuresEscapeAfterPromotion(Type exceptionType)
    {
        var failure = (Exception)Activator.CreateInstance(exceptionType)!;
        var observed = new List<Exception>();
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: observed.Add);
        harness.Materializer.Transaction.RecordLaunchAction = () => throw failure;

        Exception? actual = await Record.ExceptionAsync(() =>
            harness.Experience.InstallAndLaunchAsync(harness.Plan, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, actual);
        Assert.Empty(observed);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.False(harness.State.LeaseActive);
    }

    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    public async Task FatalDiagnosticsFailuresEscapeRecoveryMapping(Type exceptionType)
    {
        var failure = (Exception)Activator.CreateInstance(exceptionType)!;
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: _ => throw failure);
        harness.Materializer.Transaction.RecordLaunchAction = () => throw new IOException("Adapter failure.");

        Exception? actual = await Record.ExceptionAsync(() =>
            harness.Experience.InstallAndLaunchAsync(harness.Plan, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, actual);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
    }

    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    public async Task FatalLeaseDisposeFailureStillDisposesPromotion(Type exceptionType)
    {
        var failure = (Exception)Activator.CreateInstance(exceptionType)!;
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var caller = new CancellationTokenSource();
        harness.Materializer.Transaction.BootstrapLeaseAcquiredAction = () =>
        {
            harness.Materializer.Transaction.LastBootstrapLease!.DisposeAction = () => throw failure;
            Cancel(caller);
        };

        Exception? actual = await Record.ExceptionAsync(() =>
            harness.Experience.InstallAndLaunchAsync(harness.Plan, caller.Token).AsTask());

        Assert.Same(failure, actual);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.True(harness.Materializer.Transaction.LastBootstrapLease!.Disposed);
        Assert.False(harness.State.LeaseActive);
    }
}
