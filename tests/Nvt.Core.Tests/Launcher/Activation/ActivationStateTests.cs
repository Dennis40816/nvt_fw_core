// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Contracts;
using static Nvt.Core.Tests.Launcher.Activation.ActivationFixture;

namespace Nvt.Core.Tests.Launcher.Activation;

/// <summary>Characterizes immutable state identities, normalization, admission boundaries and registry authority.</summary>
public sealed class ActivationStateTests
{
    /// <summary>All durable application fields participate in generation comparison.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void EveryApplicationDurableFieldInvalidatesSnapshot(int field)
    {
        VersionManagerState initial = State();
        VersionManagerState changed = field switch
        {
            0 => VersionManagerState.Create(Source, Active, Older, Admissions, null, null, false,
                managedRootIdentity: Path.Combine(Root, "other")),
            1 => initial.WithUpdateSource(Source + "-other", null),
            2 => initial.Rebuild(Older, Older, null, null),
            3 => initial.Rebuild(Active, Active, null, null),
            4 => VersionManagerState.Create(Source, Active, Older,
                Admissions.Select(a => a.Version == Candidate ? a with { AdmissionIdentity = "other" } : a),
                null, null, false, managedRootIdentity: Root),
            5 => VersionActivationPolicy.BeginActivation(initial, Candidate),
            6 => initial.Rebuild(Active, Older, null, Candidate),
            7 => initial.WithRetentionReviewDue(true),
            8 => initial.WithPendingMutation(new(ManagedVersionMutationKind.Delete, Admission(Older))),
            _ => initial.WithUpdateSource(Source, new(1, Digest, false)),
        };
        Assert.False(initial.CreateDurableSnapshotToken().Matches(changed.CreateDurableSnapshotToken()));
        Assert.True(initial.CreateDurableSnapshotToken().Matches(State().CreateDurableSnapshotToken()));
        Assert.Throws<ArgumentNullException>(() => initial.CreateDurableSnapshotToken().Matches(null!));
    }

    /// <summary>Revision, exact digest and manual pin each remain durable authority.</summary>
    [Fact]
    public void RegistryRevisionDigestAndManualPinParticipateInTokens()
    {
        VersionManagerState initial = State().WithUpdateSource(Source, new(1, Digest, false));
        Assert.False(initial.CreateDurableSnapshotToken().Matches(
            State().WithUpdateSource(Source, new(2, Digest, false)).CreateDurableSnapshotToken()));
        Assert.False(initial.CreateDurableSnapshotToken().Matches(
            State().WithUpdateSource(Source, new(1, OtherDigest, false)).CreateDurableSnapshotToken()));
        Assert.False(initial.CreateDurableSnapshotToken().Matches(
            State().WithUpdateSource(Source, new(1, Digest, true)).CreateDurableSnapshotToken()));
    }

    /// <summary>Every launcher durable field participates in exact snapshot comparison.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryLauncherDurableFieldInvalidatesSnapshot(int field)
    {
        LauncherBootstrapState initial = LauncherState();
        LauncherBootstrapState changed = field switch
        {
            0 => LauncherBootstrapState.Create(Root + "-other", initial.Active, initial.LastKnownGood, null, null),
            1 => LauncherBootstrapState.Create(Root, CreateLauncher(Candidate), initial.LastKnownGood, null, null),
            2 => LauncherBootstrapState.Create(Root, initial.Active, CreateLauncher(Candidate), null, null),
            3 => initial.Begin(CreateLauncher(Candidate)),
            _ => LauncherBootstrapState.Create(Root, initial.Active, initial.LastKnownGood, null, CreateLauncher(Candidate)),
        };
        Assert.False(initial.CreateDurableSnapshotToken().Matches(changed.CreateDurableSnapshotToken()));
        Assert.True(initial.CreateDurableSnapshotToken().Matches(LauncherState().CreateDurableSnapshotToken()));
        Assert.Throws<ArgumentNullException>(() => initial.CreateDurableSnapshotToken().Matches(null!));
    }

    /// <summary>Root normalization is pure, preserves platform comparison, and forbids rebinding.</summary>
    [Fact]
    public void RootNormalizationAndExactRootOwnershipArePreserved()
    {
        VersionManagerState normalized = VersionManagerState.Create(null, Active, Older, Admissions,
            null, null, false, managedRootIdentity: Path.Combine(Root, "child", "..") + Path.DirectorySeparatorChar);
        Assert.Equal(Root, normalized.ManagedRootIdentity);
        Assert.True(normalized.IsBoundToManagedRoot(Root));
        Assert.False(normalized.IsBoundToManagedRoot(Root + "-other"));
        Assert.Equal(OperatingSystem.IsWindows(), normalized.IsBoundToManagedRoot(Root.ToUpperInvariant()));
        LauncherBootstrapState launcher = LauncherBootstrapState.Create(
            Path.Combine(Root, "child", ".."), CreateLauncher(Active), CreateLauncher(Older), null, null);
        Assert.Equal(Root, launcher.ManagedRootIdentity);
        Assert.True(launcher.IsBoundToManagedRoot(Root));
        Assert.False(launcher.IsBoundToManagedRoot(Root + "-other"));
        VersionManagerState unbound = VersionManagerState.Create(null, Active, Older, Admissions, null, null, false);
        Assert.False(unbound.IsBoundToManagedRoot(Root));
        Assert.True(unbound.BindToManagedRoot(Root).IsBoundToManagedRoot(Root));
        Assert.Equal("Managed-version state is already bound to a managed root.",
            Assert.Throws<InvalidOperationException>(() => normalized.BindToManagedRoot(Root)).Message);
    }

    /// <summary>Malformed admissions, references and transaction kinds fail in source check order.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void StateRejectsInconsistentAdmissionsAndTransactions(int fault)
    {
        Assert.Throws<ArgumentException>(() =>
        {
            _ = fault switch
            {
                0 => VersionManagerState.Create(null, null, null, [Admission(Active), Admission(Active)], null, null, false),
                1 => VersionManagerState.Create(null, null, null,
                    [Admission(Active) with { AdmissionIdentity = " " }], null, null, false),
                2 => VersionManagerState.Create(null, ManagedAppVersion.Parse("9.0.0"), null, Admissions, null, null, false),
                3 => State(new(Candidate, "wrong-owner", Active, Older)),
                4 => State(new(Candidate, Admission(Candidate).AdmissionIdentity, Active, Older, (VersionActivationPhase)99)),
                5 => State(mutation: new((ManagedVersionMutationKind)99, Admission(Older))),
                6 => State(mutation: new(ManagedVersionMutationKind.Install, Admission(Older))),
                7 => State(mutation: new(ManagedVersionMutationKind.Delete, Admission(Older) with { ReleaseManifestSha256 = OtherDigest })),
                _ => State(new(Candidate, Admission(Candidate).AdmissionIdentity, ManagedAppVersion.Parse("9.0.0"), Older)),
            };
        });
    }

    /// <summary>Only ordinary active guards bind prior application values to the current committed pair.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ApplicationPriorStateBindingRetainsSourcePredicate(int phase)
    {
        var journal = new PendingVersionActivation(Candidate, Admission(Candidate).AdmissionIdentity,
            Older, Active, (VersionActivationPhase)phase);
        if (phase == 3)
        {
            Assert.Equal("Pending activation identity differs from installed admission.",
                Assert.Throws<ArgumentException>(() => State(journal)).Message);
        }
        else
        {
            Assert.Equal(journal, State(journal).PendingActivation);
        }
    }

    /// <summary>Launcher prior authority must match all identity fields for both recorded owners.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LauncherPriorStateBindsBothExactIdentities(bool changeFallback)
    {
        ManagedLauncherIdentity wrong = ContractFixture.CreateIdentity(Active, "other-admission");
        PendingLauncherActivation journal = PendingLauncherActivation.Create(CreateLauncher(Candidate),
            changeFallback ? CreateLauncher(Active) : wrong,
            changeFallback ? wrong : CreateLauncher(Older), LauncherActivationPhase.Requested);
        ArgumentException error = Assert.Throws<ArgumentException>(() => LauncherBootstrapState.Create(
            Root, CreateLauncher(Active), CreateLauncher(Older), journal, null));
        Assert.Equal("pending", error.ParamName);
        Assert.StartsWith("Launcher pending transaction does not preserve exact prior state.", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Both phase enums preserve their original numeric values and reject undefined journal phases.</summary>
    [Fact]
    public void PhaseNumbersAndUndefinedPhaseRejectionArePreserved()
    {
        Assert.Equal(0, (int)VersionActivationPhase.Requested);
        Assert.Equal(1, (int)VersionActivationPhase.CandidateLaunchRecorded);
        Assert.Equal(2, (int)VersionActivationPhase.RollbackLaunchRecorded);
        Assert.Equal(3, (int)VersionActivationPhase.ActiveLaunchRecorded);
        Assert.Equal(0, (int)LauncherActivationPhase.Requested);
        Assert.Equal(1, (int)LauncherActivationPhase.CandidateLaunchRecorded);
        Assert.Equal(2, (int)LauncherActivationPhase.RollbackLaunchRecorded);
        Assert.Equal(3, (int)LauncherActivationPhase.ActiveLaunchRecorded);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PendingLauncherActivation.Create(CreateLauncher(Candidate), null, null, (LauncherActivationPhase)(-1)));
        Assert.Throws<ArgumentNullException>(() =>
            PendingLauncherActivation.Create(null!, null, null, (LauncherActivationPhase)99));
    }

    /// <summary>Registry revision permits zero only with a manual pin and absent digest; positive revisions have no ceiling.</summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(long.MaxValue, false)]
    public void RegistryRevisionBoundaryAndManualPinArePreserved(long revision, bool manualPin)
    {
        string? digest = revision == 0 ? null : Digest;
        if (revision < 0 || (revision == 0 && !manualPin))
        {
            Assert.Equal("Registry revision and digest are inconsistent.",
                Assert.Throws<ArgumentException>(() => new VersionSourceRegistryState(revision, digest, manualPin)).Message);
        }
        else
        {
            var state = new VersionSourceRegistryState(revision, digest, manualPin);
            Assert.Equal(revision, state.AcceptedRevision);
            Assert.Equal(digest, state.AcceptedDigest);
            Assert.Equal(manualPin, state.IsManualPin);
        }
        Assert.Throws<ArgumentException>(() => new VersionSourceRegistryState(0, Digest, true));
        Assert.Throws<ArgumentException>(() => new VersionSourceRegistryState(1, null, true));
    }

    /// <summary>Exact SHA-256 length boundaries and lowercase characters are enforced for state and registry authority.</summary>
    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void AdmissionAndRegistryDigestLengthBoundariesArePreserved(int length)
    {
        string digest = new('a', length);
        if (length == 64)
        {
            Assert.Equal(digest, new VersionSourceRegistryState(1, digest, false).AcceptedDigest);
            Assert.Single(VersionManagerState.Create(null, Active, Active,
                [Admission(Active) with { ReleaseManifestSha256 = digest }], null, null, false).Admissions);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => new VersionSourceRegistryState(1, digest, false));
            Assert.Throws<ArgumentException>(() => VersionManagerState.Create(null, Active, Active,
                [Admission(Active) with { ReleaseManifestSha256 = digest }], null, null, false));
        }
    }

    /// <summary>Owner admission strings enforce the exact launcher limit without imposing it on application state.</summary>
    [Theory]
    [InlineData(2047)]
    [InlineData(2048)]
    [InlineData(2049)]
    public void LauncherOwnerAdmissionLengthBoundaryIsPreserved(int length)
    {
        string admission = new('a', length);
        if (length > 2048)
        {
            Assert.Throws<ArgumentException>(() => ContractFixture.CreateIdentity(ownerAdmissionIdentity: admission));
        }
        else
        {
            Assert.Equal(admission, ContractFixture.CreateIdentity(ownerAdmissionIdentity: admission).OwnerAdmissionIdentity);
        }
        Assert.Single(VersionManagerState.Create(null, Active, Active,
            [Admission(Active) with { AdmissionIdentity = admission }], null, null, false).Admissions);
    }

    /// <summary>Launcher executable size and explicit positive ceiling preserve both exact upper boundaries.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(199999999)]
    [InlineData(200000000)]
    [InlineData(200000001)]
    public void LauncherExecutableSizeAndLimitBoundariesArePreserved(long value)
    {
        if (value is <= 0 or > 200_000_000)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ContractFixture.CreateIdentity(size: value));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ContractFixture.CreateIdentity(size: 1, maximumExecutableBytes: value));
        }
        else
        {
            Assert.Equal(value, ContractFixture.CreateIdentity(size: value).Size);
            Assert.Equal(value, ContractFixture.CreateIdentity(size: value, maximumExecutableBytes: value).Size);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ContractFixture.CreateIdentity(size: value + 1, maximumExecutableBytes: value));
        }
    }

    /// <summary>Descriptor-bound launcher paths retain the inherited 512-character boundary.</summary>
    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    public void LauncherExecutablePathLengthBoundaryIsPreserved(int length)
    {
        string path = new('a', length);
        if (length > 512)
        {
            Assert.Throws<ArgumentException>(() => ContractFixture.CreateDescriptor(launcherPath: path));
        }
        else
        {
            ProductDescriptor descriptor = ContractFixture.CreateDescriptor(launcherPath: path);
            Assert.Equal(path, ContractFixture.CreateIdentity(descriptor: descriptor, executableRelativePath: path).ExecutableRelativePath);
        }
    }

    /// <summary>Registry sources must be present, absolute and already normalized in the source check order.</summary>
    [Fact]
    public void RegistrySourceNormalizationAndValidationOrderArePreserved()
    {
        var registry = new VersionSourceRegistryState(1, Digest, false);
        Assert.Equal(Source, State().WithUpdateSource(Source, registry).UpdateSource);
        Assert.Throws<ArgumentException>(() => State().WithUpdateSource(null, registry));
        Assert.Throws<ArgumentException>(() => State().WithUpdateSource("relative", registry));
        Assert.Throws<ArgumentException>(() => State().WithUpdateSource(
            Path.Combine(Source, "child", ".."), registry));
        Assert.Null(State().WithUpdateSource("   ", null).UpdateSource);
        Assert.Equal("relative", State().WithUpdateSource("relative", null).UpdateSource);
        ArgumentException error = Assert.Throws<ArgumentException>(() => VersionManagerState.Create(
            "relative", null, null, [Admission(Active), Admission(Active)], null, null, false,
            managedRootIdentity: " ", sourceRegistryState: registry));
        Assert.Equal("updateSource", error.ParamName);
        Assert.StartsWith("Registry-managed update source must be fully qualified.", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => VersionManagerState.Create(
            "relative", null, null, null!, null, null, false, sourceRegistryState: registry));
    }

    /// <summary>Every version reference independently requires a committed admission.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryStateVersionReferenceRequiresAdmission(int field)
    {
        ManagedAppVersion absent = ManagedAppVersion.Parse("9.0.0");
        Assert.Equal("State references a version without an installed admission.",
            Assert.Throws<ArgumentException>(() => VersionManagerState.Create(null,
                field == 0 ? absent : Active, field == 1 ? absent : Older, Admissions,
                field == 3 ? new(absent, "absent", Active, Older) : null,
                field == 2 ? absent : null, false)).Message);
    }

    /// <summary>Every ordinary active-guard identity field is checked independently.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ActiveGuardRequiresExactCandidateAndBothPriorVersions(int field)
    {
        var journal = new PendingVersionActivation(field == 0 ? Candidate : Active,
            field == 3 ? "wrong" : Admission(field == 0 ? Candidate : Active).AdmissionIdentity,
            field == 1 ? Older : Active, field == 2 ? Active : Older,
            VersionActivationPhase.ActiveLaunchRecorded);
        Assert.Equal("Pending activation identity differs from installed admission.",
            Assert.Throws<ArgumentException>(() => State(journal)).Message);
    }

    /// <summary>Both prior-version references require membership outside ordinary active guards.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothPriorVersionReferencesRequireAdmission(bool changeFallback)
    {
        ManagedAppVersion absent = ManagedAppVersion.Parse("9.0.0");
        Assert.Equal("Pending activation identity differs from installed admission.",
            Assert.Throws<ArgumentException>(() => State(new(Candidate,
                Admission(Candidate).AdmissionIdentity, changeFallback ? Active : absent,
                changeFallback ? absent : Older))).Message);
    }

    /// <summary>Install journals validate nonblank admission and lowercase digest even without a committed target.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstallJournalRejectsMalformedAdmissionIdentity(bool malformedDigest)
    {
        ManagedVersionAdmission fresh = Admission(ManagedAppVersion.Parse("9.0.0")) with
        {
            AdmissionIdentity = malformedDigest ? "fresh" : " ",
            ReleaseManifestSha256 = malformedDigest ? new string('A', 64) : Digest,
        };
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            State(mutation: new(ManagedVersionMutationKind.Install, fresh)));
        Assert.Equal("pendingMutation", error.ParamName);
        Assert.StartsWith("Pending managed-version mutation is inconsistent.", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Malformed registry digests fail consistently for both pinned and automatic authority.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void RegistryRejectsMalformedDigest(string digest)
    {
        Assert.Throws<ArgumentException>(() => new VersionSourceRegistryState(1, digest, false));
        Assert.Throws<ArgumentException>(() => new VersionSourceRegistryState(1, digest, true));
    }

    /// <summary>Invalid root normalization precedes split launcher-state checks.</summary>
    [Fact]
    public void RootValidationPrecedesLauncherStateValidation()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LauncherBootstrapState.Create(null!, CreateLauncher(Active), null, null, null));
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            LauncherBootstrapState.Create(" ", CreateLauncher(Active), null, null, null));
        Assert.Equal("managedRoot", error.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PendingLauncherActivation.Create(CreateLauncher(Candidate), null, null,
                LauncherActivationPhase.Requested).WithPhase((LauncherActivationPhase)99));
    }

    /// <summary>Mutation completion preserves unrelated durable fields and requires an existing journal.</summary>
    [Fact]
    public void MutationCompletionRequiresJournalAndPreservesAuthority()
    {
        VersionManagerState state = State();
        Assert.Equal("No managed-version mutation is pending.",
            Assert.Throws<InvalidOperationException>(() =>
                state.CompletePendingMutation(Admissions, Older, null)).Message);
        VersionManagerState journal = state.WithPendingMutation(new(ManagedVersionMutationKind.Delete, Admission(Older)));
        VersionManagerState completed = journal.CompletePendingMutation(
            Admissions.Where(a => a.Version != Older), Active, null);
        Assert.Null(completed.PendingMutation);
        Assert.Equal(Active, completed.LastKnownGoodVersion);
        Assert.Equal(state.UpdateSource, completed.UpdateSource);
        Assert.Equal(state.ManagedRootIdentity, completed.ManagedRootIdentity);
    }
}
