// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Contracts;

namespace Nvt.Core.Tests.Launcher.Activation;

/// <summary>Locks validation and durable-state invariants at the launcher boundary.</summary>
public sealed class LauncherBootstrapContractTests
{
    private const string LauncherPath = ContractFixture.LauncherPath;
    private const string LauncherSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly ManagedAppVersion App100 = ManagedAppVersion.Parse("1.0.0");
    private static readonly ManagedAppVersion App101 = ManagedAppVersion.Parse("1.0.1");

    /// <summary>Missing owner admission identities never become launcher authority.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LauncherIdentityRejectsMissingOwnerAdmissionIdentity(string? value)
    {
        _ = Assert.Throws<ArgumentException>(() => CreateIdentity(ownerAdmissionIdentity: value!));
    }

    /// <summary>Owner admission identities remain within the persisted contract limit.</summary>
    [Fact]
    public void LauncherIdentityRejectsOversizedOwnerAdmissionIdentity()
    {
        _ = Assert.Throws<ArgumentException>(() =>
            CreateIdentity(ownerAdmissionIdentity: new string('a', 2049)));
    }

    /// <summary>Malformed release-manifest identities are rejected before activation.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void LauncherIdentityRejectsInvalidOwnerManifestIdentity(string? value)
    {
        _ = Assert.Throws<ArgumentException>(() =>
            CreateIdentity(ownerReleaseManifestSha256: value!));
    }

    /// <summary>Only the launcher protocol implemented by this application is admitted.</summary>
    [Fact]
    public void LauncherIdentityRejectsUnsupportedProtocol()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateIdentity(protocolVersion: 2));
    }

    /// <summary>The executable identity is bound to its one canonical relative path.</summary>
    [Fact]
    public void LauncherIdentityRejectsNonCanonicalExecutablePath()
    {
        _ = Assert.Throws<ArgumentException>(() => CreateIdentity(executableRelativePath: "launcher.exe"));
    }

    /// <summary>Empty, negative, and oversized launchers are rejected.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ManagedImmutableBootstrapIdentity.MaximumExecutableBytes + 1)]
    public void LauncherIdentityRejectsUnsafeExecutableSize(long value)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateIdentity(size: value));
    }

    /// <summary>The embedded Bootstrap shares the canonical executable-size ceiling.</summary>
    [Fact]
    public void ImmutableBootstrapIdentityAdmitsCanonicalMaximum()
    {
        var identity = ManagedImmutableBootstrapIdentity.Create(ContractFixture.Descriptor,
            ContractFixture.BootstrapFileName,
            200_000_000,
            LauncherSha);

        Assert.Equal(200_000_000, identity.Length);
    }

    /// <summary>An oversized embedded Bootstrap is rejected before any resource content is read.</summary>
    [Fact]
    public void ImmutableBootstrapIdentityRejectsAboveCanonicalMaximum()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManagedImmutableBootstrapIdentity.Create(ContractFixture.Descriptor,
                ContractFixture.BootstrapFileName,
                ManagedImmutableBootstrapIdentity.MaximumExecutableBytes + 1,
                LauncherSha));
    }

    /// <summary>The executable content identity must be one lowercase SHA-256.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void LauncherIdentityRejectsInvalidExecutableIdentity(string? value)
    {
        _ = Assert.Throws<ArgumentException>(() => CreateIdentity(sha256: value));
    }

    /// <summary>All three owner fields participate in exact ownership matching.</summary>
    [Fact]
    public void LauncherIdentityMatchesOnlyItsExactOwnerAdmission()
    {
        ManagedLauncherIdentity identity = CreateIdentity();
        ManagedVersionAdmission exact = Admission(App100, "admission-1.0.0", 'c');

        Assert.True(identity.MatchesOwner(exact));
        Assert.False(identity.MatchesOwner(Admission(App101, "admission-1.0.0", 'c')));
        Assert.False(identity.MatchesOwner(Admission(App100, "other-admission", 'c')));
        Assert.False(identity.MatchesOwner(Admission(App100, "admission-1.0.0", 'd')));
        _ = Assert.Throws<ArgumentNullException>(() => identity.MatchesOwner(null!));
    }

    /// <summary>Pending state requires a candidate and a declared phase.</summary>
    [Fact]
    public void PendingLauncherActivationRejectsMissingCandidateAndUnknownPhase()
    {
        _ = Assert.Throws<ArgumentNullException>(() =>
            PendingLauncherActivation.Create(null!, null, null, LauncherActivationPhase.Requested));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PendingLauncherActivation.Create(CreateIdentity(), null, null, (LauncherActivationPhase)99));
    }

    /// <summary>Durable state cannot split its active pair or misstate prior authority.</summary>
    [Fact]
    public void LauncherStateRejectsSplitActivePairAndMismatchedPendingSnapshot()
    {
        ManagedLauncherIdentity active = CreateIdentity();
        ManagedLauncherIdentity candidate = CreateIdentity(App101, "admission-1.0.1");

        _ = Assert.Throws<ArgumentException>(() =>
            LauncherBootstrapState.Create(Root, active, null, pending: null, failed: null));
        _ = Assert.Throws<ArgumentException>(() =>
            LauncherBootstrapState.Create(Root, null, active, pending: null, failed: null));

        PendingLauncherActivation mismatched = PendingLauncherActivation.Create(
            candidate,
            previousActive: null,
            previousLastKnownGood: null,
            LauncherActivationPhase.Requested);
        _ = Assert.Throws<ArgumentException>(() =>
            LauncherBootstrapState.Create(Root, active, active, mismatched, failed: null));
    }

    /// <summary>An active-process guard must identify the currently active launcher.</summary>
    [Fact]
    public void LauncherStateRejectsActiveGuardForDifferentCandidate()
    {
        ManagedLauncherIdentity active = CreateIdentity();
        ManagedLauncherIdentity candidate = CreateIdentity(App101, "admission-1.0.1");
        PendingLauncherActivation guard = PendingLauncherActivation.Create(
            candidate,
            active,
            active,
            LauncherActivationPhase.ActiveLaunchRecorded);

        _ = Assert.Throws<ArgumentException>(() =>
            LauncherBootstrapState.Create(Root, active, active, guard, failed: null));
    }

    private static string Root => Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

    private static ManagedLauncherIdentity CreateIdentity(
        ManagedAppVersion? owner = null,
        string ownerAdmissionIdentity = "admission-1.0.0",
        string ownerReleaseManifestSha256 =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
        int protocolVersion = ManagedLauncherIdentity.SupportedProtocolVersion,
        string executableRelativePath = LauncherPath,
        long size = 123,
        string? sha256 = LauncherSha)
    {
        return ManagedLauncherIdentity.Create(
            ContractFixture.Descriptor,
            ManagedImmutableBootstrapIdentity.MaximumExecutableBytes,
            owner ?? App100,
            ownerAdmissionIdentity,
            ownerReleaseManifestSha256,
            ManagedAppVersion.Parse("1.0.0"),
            protocolVersion,
            executableRelativePath,
            size,
            sha256!);
    }

    private static ManagedVersionAdmission Admission(
        ManagedAppVersion version,
        string admissionIdentity,
        char manifestHash)
    {
        return new(version, admissionIdentity, new string(manifestHash, 64));
    }
}
