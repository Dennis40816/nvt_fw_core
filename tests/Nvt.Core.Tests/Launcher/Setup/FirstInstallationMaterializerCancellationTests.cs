// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Characterizes the materializer's typed-result boundary without inventing promotion signals.</summary>
public sealed class FirstInstallationMaterializerCancellationTests : FirstInstallationTestSupport
{
    [Theory]
    [InlineData(true, ManagedFirstInstallationOutcome.RecoveryRequired, true)]
    [InlineData(false, ManagedFirstInstallationOutcome.Cancelled, false)]
    public async Task MaterializerMustReturnTypedRecoveryAfterPromotionStarts(
        bool returnTypedResult, ManagedFirstInstallationOutcome expected, bool recoveryOwned)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var cancellation = new CancellationTokenSource();
        var materializer = new CancellingMaterializer(harness.Materializer, cancellation, returnTypedResult);
        ManagedFirstInstallationExperience experience = Create(harness.State, harness.Roots, harness.Payload,
            harness.CandidateSource, materializer, harness.Handoff);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(harness.Plan, cancellation.Token);

        Assert.Equal(expected, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.Equal(recoveryOwned, result.IsRecoveryOwned);
        Assert.Null(result.LaunchFailure);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.True(harness.Payload.LastCapture!.Disposed);
        Assert.False(harness.State.LeaseActive);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    private sealed class CancellingMaterializer(
        RecordingRootMaterializer inner,
        CancellationTokenSource cancellation,
        bool returnTypedResult) : IManagedFirstInstallationRootMaterializer
    {
        public ValueTask<ManagedFirstInstallationMaterializationIssue> AdmitDestinationAsync(
            string managedRoot, CancellationToken cancellationToken) =>
            inner.AdmitDestinationAsync(managedRoot, cancellationToken);

        public async ValueTask<ManagedFirstInstallationMaterializationResult> MaterializeAsync(
            string managedRoot,
            string statePathIdentity,
            IManagedDistributionPayloadCapture payload,
            FreshInstallationCandidate candidate,
            VersionManagerState seed,
            CancellationToken cancellationToken)
        {
            ManagedFirstInstallationMaterializationResult promoted = await inner.MaterializeAsync(
                managedRoot, statePathIdentity, payload, candidate, seed, cancellationToken);
            Cancel(cancellation);
            // The adapter owns custody until it returns a receipt, including on contract violations.
            promoted.Installation!.Dispose();
            return returnTypedResult
                ? new(null, ManagedFirstInstallationMaterializationIssue.RecoveryRequired)
                : throw new OperationCanceledException(cancellation.Token);
        }
    }
}
