// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Tests.Launcher.Coordination;

public sealed partial class LauncherBootstrapCoordinatorTests
{
    private sealed class RecordingLauncherRepository(params ManagedLauncherIdentity[] identities)
        : IInstalledLauncherRepository
    {
        public InstalledLauncherIssue ForcedIssue { get; init; }
        public Dictionary<ManagedAppVersion, InstalledLauncherIssue> Issues { get; } = [];
        public int VerifyCount { get; private set; }

        public ValueTask<InstalledLauncherResult> VerifyAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerifyCount++;
            ManagedLauncherIdentity? identity = identities.SingleOrDefault(
                candidate => candidate.OwnerAppVersion == admission.Version);
            InstalledLauncherIssue issue = Issues.GetValueOrDefault(admission.Version, ForcedIssue);
            return ValueTask.FromResult(issue == InstalledLauncherIssue.None && identity is not null
                ? new InstalledLauncherResult(identity, InstalledLauncherIssue.None)
                : new InstalledLauncherResult(null, issue == InstalledLauncherIssue.None
                    ? InstalledLauncherIssue.Unavailable
                    : issue));
        }

        public async ValueTask<InstalledLauncherLaunchResult> AcquireLaunchLeaseAsync(
            string managedRoot,
            ManagedVersionAdmission admission,
            CancellationToken cancellationToken)
        {
            InstalledLauncherResult verified = await VerifyAsync(
                managedRoot,
                admission,
                cancellationToken);
            return verified.IsVerified
                ? new(verified.Identity, NoOpExecutableLease.Instance, InstalledLauncherIssue.None)
                : new(null, null, verified.Issue);
        }
    }

    private sealed class NoOpExecutableLease : IManagedExecutableLaunchLease
    {
        internal static readonly NoOpExecutableLease Instance = new();
        public string ExecutablePath => "SyntheticApp.Launcher.exe";
        public string WorkingDirectory => ".";
        public bool TryValidateForStart()
        {
            return true;
        }
        public void Dispose() { }
    }
}
