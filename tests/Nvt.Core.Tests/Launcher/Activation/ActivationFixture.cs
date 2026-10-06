// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Contracts;

namespace Nvt.Core.Tests.Launcher.Activation;

internal static class ActivationFixture
{
    internal static readonly ManagedAppVersion Older = ManagedAppVersion.Parse("1.0.0");
    internal static readonly ManagedAppVersion Active = ManagedAppVersion.Parse("1.0.1");
    internal static readonly ManagedAppVersion Candidate = ManagedAppVersion.Parse("1.0.2");
    internal static readonly ManagedVersionAdmission[] Admissions =
    [
        Admission(Older), Admission(Active), Admission(Candidate),
    ];
    internal static readonly string Root = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(), "core-activation-" + Guid.NewGuid().ToString("N")));
    internal static readonly string Source = Path.TrimEndingDirectorySeparator(Path.Combine(Root, "source"));
    internal const string Digest = ContractFixture.ManifestSha;
    internal const string OtherDigest = ContractFixture.PackageSha;

    internal static ManagedVersionAdmission Admission(ManagedAppVersion version) =>
        new(version, $"admission-{version}", Digest);

    internal static ManagedLauncherIdentity CreateLauncher(ManagedAppVersion version) =>
        ContractFixture.CreateIdentity(version, $"admission-{version}");

    internal static VersionManagerState State(
        PendingVersionActivation? pending = null,
        ManagedAppVersion? failed = null,
        PendingManagedVersionMutation? mutation = null) =>
        VersionManagerState.Create(Source, Active, Older, Admissions, pending, failed, false,
            mutation, Root);

    internal static VersionManagerState InPhase(int phase)
    {
        if (phase < 0)
        {
            return State();
        }
        ManagedAppVersion candidate = phase == (int)VersionActivationPhase.ActiveLaunchRecorded
            ? Active : Candidate;
        return State(new(candidate, Admission(candidate).AdmissionIdentity, Active, Older,
            (VersionActivationPhase)phase));
    }

    internal static LauncherBootstrapState LauncherState(int phase = -1)
    {
        ManagedLauncherIdentity active = CreateLauncher(Active);
        ManagedLauncherIdentity older = CreateLauncher(Older);
        PendingLauncherActivation? pending = phase < 0 ? null : PendingLauncherActivation.Create(
            phase == (int)LauncherActivationPhase.ActiveLaunchRecorded ? active : CreateLauncher(Candidate),
            active, older, (LauncherActivationPhase)phase);
        return LauncherBootstrapState.Create(Root, active, older, pending, failed: null);
    }

    internal static InstalledVersionSnapshot Row(
        ManagedAppVersion version,
        ManagedVersionIntegrity integrity = ManagedVersionIntegrity.Healthy,
        bool active = false,
        bool lastKnownGood = false,
        ManagedVersionAdmissionState admissionState = ManagedVersionAdmissionState.Admitted) =>
        new(version, $"admission-{version}", integrity,
            integrity == ManagedVersionIntegrity.Damaged ? ManagedVersionDamageReason.ContentMismatch : null,
            active, lastKnownGood, admissionState,
            admissionState == ManagedVersionAdmissionState.RecoveryCandidate ? Admission(version) : null);
}
