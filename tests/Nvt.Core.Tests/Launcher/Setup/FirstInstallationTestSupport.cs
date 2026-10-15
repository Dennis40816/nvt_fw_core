// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Supplies deterministic first-install ports and synthetic admitted values.</summary>
public abstract class FirstInstallationTestSupport : SetupTestWorkspace
{
    internal const string HashA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string HashB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    internal static readonly ManagedImmutableBootstrapIdentity Bootstrap = ManagedImmutableBootstrapIdentity.Create(
        Transport.TransportFixture.Descriptor,
        "Fixture.Bootstrap.exe",
        1024,
        HashB);

    internal ManagedFirstInstallationExperience Create(
        IVersionManagerStateStore state,
        IManagedInstallationRootProbe roots,
        IManagedDistributionPayloadSource payload,
        IFreshInstallationCandidateSource source,
        IManagedFirstInstallationRootMaterializer materializer,
        IImmutableBootstrapLeaseHandoff handoff,
        TimeSpan? admissionOperationCutoff = null,
        TimeSpan? completionOperationCutoff = null,
        TimeProvider? timeProvider = null,
        Action<Exception>? observerDiagnostics = null)
    {
        return new(
            StatePath(),
            state,
            roots,
            payload,
            source,
            materializer,
            handoff,
            admissionOperationCutoff,
            completionOperationCutoff,
            timeProvider ?? new ManualTimeProvider(DateTimeOffset.UnixEpoch),
            observerDiagnostics);
    }

    internal static ManagedDistributionPayloadIdentity PayloadIdentity()
    {
        return new(
            65_536,
            ManagedAppVersion.Parse("1.0.4"),
            new string('c', 40),
            2048,
            HashA,
            512,
            HashB,
            Bootstrap);
    }

    internal FreshInstallationCandidate Candidate()
    {
        string sourceRoot = Root("source");
        UpdateCatalogVersionSnapshot package = UpdateCatalogVersionSnapshot.Create(
            200_000_000, 65_536, ManagedAppVersion.Parse("1.0.4"), DateTimeOffset.UnixEpoch,
            new UpdateCatalogPackagePath("packages/Fixture-v1.0.4-test.zip"), 4096,
            HashA, HashB, "Release 1.0.4", UpdateNotificationPolicy.ManualOnly);
        var identity = new FreshInstallationCandidateIdentity(
            128,
            "fixture-registry",
            1,
            HashA,
            1,
            package.Version,
            HashB,
            Path.Combine(sourceRoot, "update-catalog.v1.json"),
            sourceRoot,
            FreshInstallationSourceStatus.Latest,
            package.PackagePath.Value!,
            package.PackageSize,
            package.PackageSha256,
            package.ReleaseManifestSha256);
        return new(
            identity,
            package,
            new VerifiedUpdateCandidate(package.Version, package.Identity, package.ReleaseNotes));
    }

    internal static VersionManagerStateLoadResult MissingState()
    {
        return new(null, VersionManagerStateLoadIssue.Missing);
    }

    internal static VersionManagerStateLoadResult BoundState(
        string root,
        FreshInstallationCandidate candidate)
    {
        ManagedVersionAdmission admission = Admission(candidate);
        return new(
            VersionManagerState.Create(
                updateSource: null,
                activeVersion: admission.Version,
                lastKnownGoodVersion: admission.Version,
                [admission],
                pendingActivation: null,
                failedActivationVersion: null,
                retentionReviewDue: false,
                managedRootIdentity: root),
            VersionManagerStateLoadIssue.None);
    }

    internal static ManagedVersionAdmission Admission(FreshInstallationCandidate candidate)
    {
        return new(
            candidate.Package.Version,
            candidate.Package.Identity,
            candidate.Package.ReleaseManifestSha256);
    }

    internal string StatePath()
    {
        return Path.Combine(Root("state"), "version-manager.v1.json");
    }

    internal static ImmutableBootstrapStartResult StartResultOwningLease(
        LeaseOwningBootstrapLaunch ownedLaunch,
        ImmutableBootstrapStartIssue issue = ImmutableBootstrapStartIssue.None)
    {
        return new(ownedLaunch, issue);
    }

    internal async Task<InstallHarness> CreateInstallHarnessAsync(
        ManagedFirstInstallationMaterializationIssue materializationIssue =
            ManagedFirstInstallationMaterializationIssue.None,
        ImmutableBootstrapAdmissionOutcome admissionOutcome =
            ImmutableBootstrapAdmissionOutcome.Admitted,
        VersionManagerStateLoadResult? finalState = null,
        Action<Exception>? observerDiagnostics = null)
    {
        string root = Root($"install-{Guid.NewGuid():N}");
        FreshInstallationCandidate candidate = Candidate();
        SequencedStateStore state = new(
            MissingState(),
            MissingState(),
            finalState ?? BoundState(root, candidate));
        SequencedRootProbe roots = new(
            ManagedInstallationRootStatus.Absent,
            ManagedInstallationRootStatus.Absent);
        RecordingPayloadSource payload = new(PayloadIdentity());
        RecordingCandidateSource source = new(candidate);
        RecordingRootMaterializer materializer = new(state) { Issue = materializationIssue };
        SetupBootstrapHandoff handoff = new(state) { AdmissionOutcome = admissionOutcome };
        ManagedFirstInstallationExperience experience = Create(
            state,
            roots,
            payload,
            source,
            materializer,
            handoff,
            observerDiagnostics: observerDiagnostics);
        ManagedFirstInstallationPlanResult prepared = await experience.PrepareAsync(
            root,
            TestContext.Current.CancellationToken);
        return new()
        {
            Experience = experience,
            Plan = Assert.IsType<ManagedFirstInstallationPlan>(prepared.Plan),
            State = state,
            Roots = roots,
            Payload = payload,
            CandidateSource = source,
            Materializer = materializer,
            Handoff = handoff,
        };
    }

}
