// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Tests.Launcher.Coordination;

/// <summary>Fixture builders for rollback-safe launcher activation tests.</summary>
public sealed partial class LauncherBootstrapCoordinatorTests
{
    private static string Root => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "synthetic-launcher-root"));
    private static string StatePath => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "synthetic-launcher-state.json"));

    private static LauncherBootstrapCoordinator Create(
        RecordingAppStateStore appStore,
        RecordingLauncherStateStore launcherStore,
        RecordingLauncherRepository repository,
        RecordingLauncherProcess process)
    {
        process.AppStateStore = appStore;
        return new(Root, StatePath, appStore, launcherStore, repository, process);
    }

    private static ManagedLauncherIdentity Identity(
        ManagedAppVersion owner,
        string launcherVersion,
        char hash)
    {
        return ManagedLauncherIdentity.Create(
            CoordinationFixture.Product, 200_000_000, owner,
            $"admission-{owner}",
            new string('c', 64),
            ManagedAppVersion.Parse(launcherVersion),
            protocolVersion: 1,
            "launcher/SyntheticApp.Launcher.exe",
            size: 123,
            new string(hash, 64));
    }

    private static VersionManagerState AppState(
        ManagedAppVersion active,
        ManagedAppVersion? previous = null,
        string? managedRoot = null)
    {
        ManagedAppVersion[] versions = previous is { } prior ? [active, prior] : [active];
        return VersionManagerState.Create(
            updateSource: null,
            active,
            previous ?? active,
            versions.Select(version => new ManagedVersionAdmission(
                version,
                $"admission-{version}",
                new string('c', 64))),
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: managedRoot ?? Root);
    }

    private static VersionManagerState AppStateWithPendingActivation()
    {
        ManagedVersionAdmission current = new(App100, $"admission-{App100}", new string('c', 64));
        ManagedVersionAdmission candidate = new(App101, $"admission-{App101}", new string('c', 64));
        return VersionManagerState.Create(
            updateSource: null,
            App100,
            App100,
            [current, candidate],
            new PendingVersionActivation(
                App101,
                candidate.AdmissionIdentity,
                App100,
                App100),
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: Root);
    }

    private static VersionManagerState AppStateWithPendingMutation()
    {
        ManagedVersionAdmission current = new(App100, $"admission-{App100}", new string('c', 64));
        ManagedVersionAdmission candidate = new(App101, $"admission-{App101}", new string('c', 64));
        return VersionManagerState.Create(
            updateSource: null,
            App100,
            App100,
            [current],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            new PendingManagedVersionMutation(ManagedVersionMutationKind.Install, candidate),
            managedRootIdentity: Root);
    }
}
