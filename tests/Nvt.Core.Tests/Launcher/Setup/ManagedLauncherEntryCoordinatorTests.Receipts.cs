// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

public sealed partial class ManagedLauncherEntryCoordinatorTests
{
    /// <summary>All malformed admission categories fail closed and dispose the launch receipt.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Admitted, 0, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Admitted, null, ImmutableBootstrapExitIssue.Busy)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Busy, null, ImmutableBootstrapExitIssue.Busy)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, 22, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, 22, ImmutableBootstrapExitIssue.Unknown)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, 0, ImmutableBootstrapExitIssue.Unknown)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, 1, ImmutableBootstrapExitIssue.Unknown)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, null, ImmutableBootstrapExitIssue.InvalidInheritedContext)]
    [InlineData((ImmutableBootstrapAdmissionOutcome)999, null, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapAdmissionOutcome.Admitted, null, (ImmutableBootstrapExitIssue)999)]
    public async Task MalformedAdmissionReceiptRetainsTerminationUncertainty(
        ImmutableBootstrapAdmissionOutcome outcome, int? exitCode, ImmutableBootstrapExitIssue issue)
    {
        string root = Root("malformed-admission");
        using var launch = new ReceiptLaunch(new(outcome, exitCode, issue), new(ImmutableBootstrapCompletionOutcome.Ready, 0));
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present), new ReceiptHandoff(launch));

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.True(launch.Disposed);
        Assert.Equal(0, launch.CompletionWaitCount);
    }

    /// <summary>Malformed success, failure, exit and enum completion projections never become READY.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapCompletionOutcome.Ready, null, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Ready, 1, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Ready, 0, ImmutableBootstrapExitIssue.StartFailed)]
    [InlineData(ImmutableBootstrapCompletionOutcome.RolledBack, 0, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Failed, null, ImmutableBootstrapExitIssue.StartFailed)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Failed, 0, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Failed, 15, ImmutableBootstrapExitIssue.Busy)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Unavailable, 18, ImmutableBootstrapExitIssue.StateUnavailable)]
    [InlineData(ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed, null, ImmutableBootstrapExitIssue.None)]
    [InlineData((ImmutableBootstrapCompletionOutcome)999, null, ImmutableBootstrapExitIssue.None)]
    [InlineData(ImmutableBootstrapCompletionOutcome.Ready, 0, (ImmutableBootstrapExitIssue)999)]
    public async Task MalformedCompletionReceiptRetainsTerminationUncertainty(
        ImmutableBootstrapCompletionOutcome outcome, int? exitCode, ImmutableBootstrapExitIssue issue)
    {
        string root = Root("malformed-completion");
        using var launch = new ReceiptLaunch(new(ImmutableBootstrapAdmissionOutcome.Admitted), new(outcome, exitCode, issue));
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present), new ReceiptHandoff(launch));

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.True(launch.Disposed);
        Assert.Equal(1, launch.CompletionWaitCount);
    }

    /// <summary>Metadata success requires an exact Bootstrap identity before any state read.</summary>
    [Fact]
    public async Task PayloadAdmissionSuccessWithoutIdentityRetainsItsExactException()
    {
        string root = Root("missing-payload-identity");
        var payload = new EntryPayloadSource
        {
            AdmissionOverride = new(ManagedAppVersion.Parse("1.0.4"), null, ManagedDistributionPayloadIssue.None),
        };
        var state = new EntryStateStore(BoundState(root));
        ManagedLauncherEntryCoordinator coordinator = Create(root, state,
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready), payloadSource: payload);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.RunAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("Successful payload admission returned no Bootstrap identity.", exception.Message);
        Assert.Equal(0, state.LoadCount);
    }

    /// <summary>An attached identity cannot turn typed metadata failure into success.</summary>
    [Fact]
    public async Task PayloadFailureWithIdentityStopsBeforeStateRead()
    {
        string root = Root("failed-payload-with-identity");
        var payload = new EntryPayloadSource
        {
            AdmissionOverride = new(ManagedAppVersion.Parse("1.0.4"), _bootstrap, ManagedDistributionPayloadIssue.Invalid),
        };
        var state = new EntryStateStore(BoundState(root));
        ManagedLauncherEntryCoordinator coordinator = Create(root, state,
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready), payloadSource: payload);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.PayloadInvalid, result.Outcome);
        Assert.Equal(0, state.LoadCount);
    }

    private sealed class ReceiptHandoff(ReceiptLaunch launch) : IImmutableBootstrapHandoff
    {
        public ValueTask<ImmutableBootstrapStartResult> StartAsync(string managedRoot,
            ManagedImmutableBootstrapIdentity expectedIdentity, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ImmutableBootstrapStartResult(launch, ImmutableBootstrapStartIssue.None));
    }

    // Guard: one coordinator invocation owns and serializes this receipt's observations.
    private sealed class ReceiptLaunch(ImmutableBootstrapAdmissionResult admission,
        ImmutableBootstrapCompletionResult completion) : IImmutableBootstrapLaunch
    {
        internal bool Disposed { get; private set; }
        internal int CompletionWaitCount { get; private set; }

        public ValueTask<ImmutableBootstrapAdmissionResult> WaitForAdmissionAsync(ImmutableBootstrapWaitBudget budget,
            CancellationToken cancellationToken) => ValueTask.FromResult(admission);

        public ValueTask<ImmutableBootstrapCompletionResult> WaitForCompletionAsync(ImmutableBootstrapWaitBudget budget,
            CancellationToken cancellationToken)
        {
            CompletionWaitCount++;
            return ValueTask.FromResult(completion);
        }

        public void Dispose() => Disposed = true;
    }
}
