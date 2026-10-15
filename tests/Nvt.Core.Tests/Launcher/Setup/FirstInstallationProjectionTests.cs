// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Exercises mandatory policy projections at the planning boundary.</summary>
public sealed class FirstInstallationProjectionTests : FirstInstallationTestSupport
{
    /// <summary>An invalid or oversized descriptor projection cannot produce a plan or reach source selection.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(65_537)]
    public async Task PrepareRejectsInvalidAndOversizedPayloadProjection(long descriptorSize)
    {
        using var state = new SequencedStateStore(MissingState());
        var candidate = new RecordingCandidateSource(Candidate());
        var payload = new ProjectedPayloadSource(descriptorSize);
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(state,
            new SequencedRootProbe(ManagedInstallationRootStatus.Absent), payload, candidate,
            new RecordingRootMaterializer(state), handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            Root("invalid-projection"), TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.PayloadInvalid, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal(0, candidate.InspectCount);
    }

    /// <summary>A source adapter cannot admit an oversized registry identity through preparation.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(129)]
    public async Task PrepareRejectsInvalidAndOversizedCandidateProjection(int registryCharacters)
    {
        using var state = new SequencedStateStore(MissingState());
        var materializer = new RecordingRootMaterializer(state);
        using var payload = new RecordingPayloadSource(PayloadIdentity());
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(state,
            new SequencedRootProbe(ManagedInstallationRootStatus.Absent), payload,
            new ProjectedCandidateSource(Candidate(), registryCharacters), materializer, handoff);

        ManagedFirstInstallationPlanResult result = await experience.PrepareAsync(
            Root("invalid-candidate"), TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.SourceRejected, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Equal(0, materializer.MaterializeCount);
    }

    /// <summary>Verified version, admission identity and notes must agree with the captured package.</summary>
    [Theory]
    [InlineData("1.0.5", true, true)]
    [InlineData("1.0.4", false, true)]
    [InlineData("1.0.4", true, false)]
    public void CandidateRejectsContradictoryVerifiedValues(string version, bool sameIdentity, bool sameNotes)
    {
        FreshInstallationCandidate candidate = Candidate();
        var verified = new VerifiedUpdateCandidate(ManagedAppVersion.Parse(version),
            sameIdentity ? candidate.Package.Identity : "other-admission",
            sameNotes ? candidate.Package.ReleaseNotes : "other-notes");

        var exception = Assert.Throws<ArgumentException>(() =>
            new FreshInstallationCandidate(candidate.Identity, candidate.Package, verified));

        Assert.Equal("Fresh installation candidate values disagree.", exception.Message);
    }

    /// <summary>Every publication/package cross-check is independently authoritative.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CandidateRejectsEachContradictoryPublicationField(int changedField)
    {
        FreshInstallationCandidate candidate = Candidate();
        FreshInstallationCandidateIdentity identity = AlterPublication(candidate.Identity, changedField);
        var verified = new VerifiedUpdateCandidate(candidate.Package.Version, candidate.Package.Identity, candidate.Package.ReleaseNotes);

        Assert.Throws<ArgumentException>(() => new FreshInstallationCandidate(identity, candidate.Package, verified));
    }

    private static FreshInstallationCandidateIdentity AlterPublication(FreshInstallationCandidateIdentity expected, int field) =>
        new(128, expected.RegistryId, expected.RegistryRevision, expected.RegistryDigest, expected.CatalogSchemaVersion,
            field == 0 ? ManagedAppVersion.Parse("1.0.5") : expected.CatalogLatestVersion,
            expected.CatalogDigest, expected.CatalogPath, expected.SourceRoot, expected.SourceStatus,
            field == 1 ? "other.zip" : expected.PackagePath, field == 2 ? expected.PackageSize + 1 : expected.PackageSize,
            field == 3 ? new string('c', 64) : expected.PackageSha256,
            field == 4 ? new string('d', 64) : expected.ReleaseManifestSha256);

    private sealed class ProjectedPayloadSource(long descriptorSize) : IManagedDistributionPayloadSource
    {
        public ValueTask<ManagedDistributionPayloadInspectionResult> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var identity = new ManagedDistributionPayloadIdentity(65_536, ManagedAppVersion.Parse("1.0.4"),
                    new string('c', 40), 1, HashA, descriptorSize, HashB, Bootstrap);
                return ValueTask.FromResult(new ManagedDistributionPayloadInspectionResult(identity, ManagedDistributionPayloadIssue.None));
            }
            catch (ArgumentException)
            {
                return ValueTask.FromResult(new ManagedDistributionPayloadInspectionResult(null, ManagedDistributionPayloadIssue.Invalid));
            }
        }

        public ValueTask<ManagedDistributionPayloadEntryAdmissionResult> AdmitEntryAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Planning must inspect the exact payload.");

        public ValueTask<ManagedDistributionPayloadCaptureResult> CaptureExactAsync(
            ManagedDistributionPayloadIdentity expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid planning cannot capture bytes.");
    }

    private sealed class ProjectedCandidateSource(FreshInstallationCandidate candidate, int registryCharacters)
        : IFreshInstallationCandidateSource
    {
        public ValueTask<FreshInstallationCandidateResult> InspectFreshInstallationAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FreshInstallationCandidateIdentity expected = candidate.Identity;
            try
            {
                var identity = new FreshInstallationCandidateIdentity(128, new string('r', registryCharacters),
                    expected.RegistryRevision, expected.RegistryDigest, expected.CatalogSchemaVersion,
                    expected.CatalogLatestVersion, expected.CatalogDigest, expected.CatalogPath, expected.SourceRoot,
                    expected.SourceStatus, expected.PackagePath, expected.PackageSize,
                    expected.PackageSha256, expected.ReleaseManifestSha256);
                var projected = new FreshInstallationCandidate(identity, candidate.Package,
                    new(candidate.Package.Version, candidate.Package.Identity, candidate.Package.ReleaseNotes));
                return ValueTask.FromResult(new FreshInstallationCandidateResult(projected, FreshInstallationCandidateIssue.None));
            }
            catch (ArgumentException)
            {
                return ValueTask.FromResult(new FreshInstallationCandidateResult(null, FreshInstallationCandidateIssue.SourceRejected));
            }
        }

        public ValueTask<FreshInstallationCandidateResult> ReverifyFreshInstallationAsync(
            FreshInstallationCandidate expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid planning cannot reverify a candidate.");
    }
}
