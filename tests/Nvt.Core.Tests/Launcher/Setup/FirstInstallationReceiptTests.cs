// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Checks malformed immutable source and promoted-root receipts.</summary>
public sealed class FirstInstallationReceiptTests : FirstInstallationTestSupport
{
    /// <summary>A purported successful candidate without its identity is a changed-source result.</summary>
    [Fact]
    public async Task PrepareRejectsCandidateSuccessWithoutCandidate()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var state = new SequencedStateStore(MissingState());
        var source = new RecordingCandidateSource(Candidate()) { InspectionOverride = new(null, FreshInstallationCandidateIssue.None) };
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(state,
            new SequencedRootProbe(ManagedInstallationRootStatus.Absent), harness.Payload, source,
            new RecordingRootMaterializer(state), handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(Root("missing-candidate"), TestContext.Current.CancellationToken);

        Assert.Null(result.Plan);
        Assert.Equal(ManagedFirstInstallationOutcome.SourceChanged, result.Outcome);
    }

    /// <summary>All malformed reverify success shapes stop before materialization.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallRejectsMissingOrMismatchedReverifiedCandidate(bool mismatched)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        FreshInstallationCandidate other = OtherCandidate(harness.Plan.Candidate);
        harness.CandidateSource.ReverificationOverride = new(mismatched ? other : null, FreshInstallationCandidateIssue.None);

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.SourceChanged, result.Outcome);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
        Assert.Equal(0, harness.Handoff.StartCount);
        Assert.True(harness.Payload.LastCapture!.Disposed);
    }

    /// <summary>A materialization success issue without custody cannot launch or finalize.</summary>
    [Fact]
    public async Task InstallRejectsMaterializationSuccessWithoutInstallation()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Materializer.MaterializationOverride = new(null, ManagedFirstInstallationMaterializationIssue.None);

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.StateUnavailable, result.Outcome);
        Assert.Equal(0, harness.Handoff.StartCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    /// <summary>A promoted receipt must bind both the exact root and the exact seed admission.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallRejectsMismatchedPromotedRootOrAdmission(bool mismatchedRoot)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var promoted = new RecordingPromotedInstallation(harness.State);
        ManagedVersionAdmission admission = Admission(harness.Plan.Candidate);
        promoted.Initialize(mismatchedRoot ? Root("other-root") : harness.Plan.ManagedRoot,
            mismatchedRoot ? admission : admission with { AdmissionIdentity = "other-admission" });
        harness.Materializer.MaterializationOverride = new(promoted, ManagedFirstInstallationMaterializationIssue.None);

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(0, promoted.RecordLaunchCount);
        Assert.Equal(0, promoted.CompleteCount);
        Assert.Equal(1, promoted.DisposeCount);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    /// <summary>A reviewed plan for another exact state path is rejected before writer acquisition.</summary>
    [Fact]
    public async Task InstallRejectsPlanForAnotherStatePathBeforeAnyPort()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var foreign = new ManagedFirstInstallationPlan(harness.Plan.ManagedRoot, Root("other-state.json"),
            harness.Plan.Payload, harness.Plan.Candidate);

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(foreign, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(0, harness.State.LeaseAttemptCount);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
    }

    /// <summary>A pre-cancelled confirmation cannot mutate or launch a prepared installation.</summary>
    [Fact]
    public async Task InstallAlreadyCancelledTouchesNoWriterOrMaterializer()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        using var cancellation = new CancellationTokenSource();
        Cancel(cancellation);

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(harness.Plan, cancellation.Token);

        Assert.Equal(ManagedFirstInstallationOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, harness.State.LeaseAttemptCount);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
    }

    /// <summary>Malformed inspection success and failure shapes cannot enter source selection.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareRejectsMalformedPayloadInspection(bool attachIdentityToFailure)
    {
        using var state = new SequencedStateStore(MissingState());
        var source = new RecordingCandidateSource(Candidate());
        using var payload = new RecordingPayloadSource(PayloadIdentity())
        {
            InspectionOverride = new(attachIdentityToFailure ? PayloadIdentity() : null,
                attachIdentityToFailure ? ManagedDistributionPayloadIssue.Invalid : ManagedDistributionPayloadIssue.None),
        };
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(state,
            new SequencedRootProbe(ManagedInstallationRootStatus.Absent), payload, source,
            new RecordingRootMaterializer(state), handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            Root("malformed-inspection"), TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.PayloadInvalid, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal(0, source.InspectCount);
    }

    /// <summary>Undefined executable-lease issues retain damaged-custody recovery ownership.</summary>
    [Fact]
    public async Task InstallRejectsUndefinedBootstrapLeaseIssue()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Materializer.Transaction.BootstrapLeaseResultOverride = new(null, (ManagedExecutableLaunchIssue)999);

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(ManagedFirstInstallationLaunchIssue.Damaged, result.LaunchFailure!.Issue);
        Assert.Equal(0, harness.Handoff.StartCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
    }

    private static FreshInstallationCandidate OtherCandidate(FreshInstallationCandidate candidate)
    {
        FreshInstallationCandidateIdentity expected = candidate.Identity;
        var identity = new FreshInstallationCandidateIdentity(128, expected.RegistryId,
            expected.RegistryRevision + 1, expected.RegistryDigest, expected.CatalogSchemaVersion,
            expected.CatalogLatestVersion, expected.CatalogDigest, expected.CatalogPath, expected.SourceRoot,
            expected.SourceStatus, expected.PackagePath, expected.PackageSize, expected.PackageSha256,
            expected.ReleaseManifestSha256);
        return new(identity, candidate.Package, new(candidate.Package.Version, candidate.Package.Identity, candidate.Package.ReleaseNotes));
    }
}
