// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Repository;

/// <summary>Composes existing strict state readers and launcher verification into one read-only query.</summary>
public sealed class FileSystemLauncherInstallationSelfTest : ILauncherInstallationSelfTest
{
    private readonly string BootstrapFileName;
    private readonly int MaximumBootstrapBytes;
    private readonly string _managedRoot;
    private readonly IVersionManagerStateReader _appStateStore;
    private readonly ILauncherBootstrapStateStore _launcherStateStore;
    private readonly IInstalledLauncherRepository _launcherRepository;

    /// <summary>Creates a read-only query using caller-owned strict state readers and explicit Bootstrap identity.</summary>
    /// <param name="descriptor">The caller's exact Bootstrap filename and application identity.</param>
    /// <param name="maximumBootstrapBytes">The explicit positive Bootstrap byte ceiling, at most 200,000,000.</param>
    /// <param name="managedRoot">The caller's exact installation root.</param>
    /// <param name="appStateStore">The mandatory strict application-state reader.</param>
    /// <param name="launcherStateStore">The mandatory strict launcher-state store, used only for reading.</param>
    /// <param name="launcherRepository">The owner-bound installed-launcher verifier.</param>
    public FileSystemLauncherInstallationSelfTest(
        ProductDescriptor descriptor,
        int maximumBootstrapBytes,
        string managedRoot,
        IVersionManagerStateReader appStateStore,
        ILauncherBootstrapStateStore launcherStateStore,
        IInstalledLauncherRepository launcherRepository)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBootstrapBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBootstrapBytes,
            ManagedImmutableBootstrapIdentity.MaximumExecutableBytes);
        BootstrapFileName = descriptor.BootstrapExecutableFileName;
        MaximumBootstrapBytes = maximumBootstrapBytes;
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        _managedRoot = ManagedRootPathIdentity.Normalize(managedRoot);
        _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
        _launcherStateStore = launcherStateStore ?? throw new ArgumentNullException(nameof(launcherStateStore));
        _launcherRepository = launcherRepository ?? throw new ArgumentNullException(nameof(launcherRepository));
    }

    /// <inheritdoc />
    public async ValueTask<LauncherInstallationSelfTestResult> QueryAsync(
        CancellationToken cancellationToken)
    {
        VersionManagerStateLoadResult appLoaded = await _appStateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!appLoaded.IsSuccess || appLoaded.State is not { } appState)
        {
            return Failure(Map(appLoaded.Issue));
        }
        if (!appState.IsBoundToManagedRoot(_managedRoot))
        {
            return Failure(LauncherInstallationSelfTestIssue.ManagedRootMismatch);
        }
        if (appState.PendingActivation is not null || appState.PendingMutation is not null)
        {
            return Failure(LauncherInstallationSelfTestIssue.AppTransactionPending);
        }
        if (appState.ActiveVersion is not { } activeVersion)
        {
            return Failure(LauncherInstallationSelfTestIssue.NoActiveVersion);
        }
        ManagedVersionAdmission? admission = appState.Admissions.SingleOrDefault(
            item => item.Version == activeVersion);
        if (admission is null)
        {
            return Failure(LauncherInstallationSelfTestIssue.ActiveAdmissionMissing);
        }
        VersionManagerState.DurableSnapshotToken appSnapshot = appState.CreateDurableSnapshotToken();

        LauncherBootstrapStateLoadResult launcherLoaded = await _launcherStateStore.LoadAsync(
            cancellationToken).ConfigureAwait(false);
        if (!launcherLoaded.IsSuccess || launcherLoaded.State is not { } launcherState)
        {
            return Failure(Map(launcherLoaded.Issue));
        }
        if (!launcherState.IsBoundToManagedRoot(_managedRoot))
        {
            return Failure(LauncherInstallationSelfTestIssue.ManagedRootMismatch);
        }
        if (launcherState.Pending is not null)
        {
            return Failure(LauncherInstallationSelfTestIssue.LauncherActivationPending);
        }
        if (launcherState.Active is not { } activeLauncher || !activeLauncher.MatchesOwner(admission))
        {
            return Failure(LauncherInstallationSelfTestIssue.ActiveLauncherMismatch);
        }
        LauncherBootstrapState.DurableSnapshotToken launcherSnapshot =
            launcherState.CreateDurableSnapshotToken();

        InstalledLauncherResult verified = await _launcherRepository.VerifyAsync(
            _managedRoot,
            admission,
            cancellationToken).ConfigureAwait(false);
        if (!verified.IsVerified || verified.Identity is not { } verifiedLauncher)
        {
            return Failure(Map(verified.Issue));
        }
        if (verifiedLauncher != activeLauncher)
        {
            return Failure(LauncherInstallationSelfTestIssue.ActiveLauncherMismatch);
        }

        (ImmutableBootstrapObservation? bootstrap, LauncherInstallationSelfTestIssue bootstrapIssue) =
            await ObserveBootstrapAsync(cancellationToken).ConfigureAwait(false);
        if (bootstrap is null)
        {
            return Failure(bootstrapIssue);
        }

        VersionManagerStateLoadResult terminalApp = await _appStateStore.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!terminalApp.IsSuccess || terminalApp.State is not { } terminalAppState)
        {
            return Failure(LauncherInstallationSelfTestIssue.StateChanged);
        }
        LauncherBootstrapStateLoadResult terminalLauncher = await _launcherStateStore.LoadAsync(
            cancellationToken).ConfigureAwait(false);
        bool snapshotStable = terminalLauncher.IsSuccess &&
            terminalLauncher.State is { } terminalLauncherState &&
            appSnapshot.Matches(terminalAppState.CreateDurableSnapshotToken()) &&
            launcherSnapshot.Matches(terminalLauncherState.CreateDurableSnapshotToken());
        return snapshotStable
            ? new(
                LauncherInstallationSelfTestIssue.None,
                bootstrap,
                new ActiveLauncherAdmission(
                    admission,
                    verifiedLauncher.LauncherVersion,
                    verifiedLauncher.ProtocolVersion,
                    verifiedLauncher.ExecutableRelativePath,
                    verifiedLauncher.Size,
                    verifiedLauncher.Sha256))
            : Failure(LauncherInstallationSelfTestIssue.StateChanged);
    }

    private async ValueTask<(ImmutableBootstrapObservation?, LauncherInstallationSelfTestIssue)>
        ObserveBootstrapAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!RepositoryPathSafety.TryResolveRelativeFile(
                _managedRoot,
                BootstrapFileName,
                out string bootstrapPath))
            {
                return (null, LauncherInstallationSelfTestIssue.BootstrapInvalid);
            }
            byte[]? bytes = await RepositoryPathSafety.ReadBoundedFileAsync(
                bootstrapPath,
                MaximumBootstrapBytes,
                cancellationToken).ConfigureAwait(false);
            return bytes is null
                ? (null, LauncherInstallationSelfTestIssue.BootstrapInvalid)
                : (new ImmutableBootstrapObservation(
                    bootstrapPath,
                    bytes.LongLength,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()),
                    LauncherInstallationSelfTestIssue.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, LauncherInstallationSelfTestIssue.BootstrapUnavailable);
        }
    }

    private static LauncherInstallationSelfTestResult Failure(
        LauncherInstallationSelfTestIssue issue)
    {
        return new(issue, bootstrap: null, activeLauncher: null);
    }

    private static LauncherInstallationSelfTestIssue Map(VersionManagerStateLoadIssue issue)
    {
        return issue switch
        {
            VersionManagerStateLoadIssue.None => throw new InvalidOperationException(
                "A successful app-state load cannot be mapped to a self-test failure."),
            VersionManagerStateLoadIssue.Missing => LauncherInstallationSelfTestIssue.AppStateMissing,
            VersionManagerStateLoadIssue.Invalid => LauncherInstallationSelfTestIssue.AppStateInvalid,
            VersionManagerStateLoadIssue.Unavailable => LauncherInstallationSelfTestIssue.AppStateUnavailable,
            VersionManagerStateLoadIssue.ManagedRootMismatch =>
                LauncherInstallationSelfTestIssue.ManagedRootMismatch,
            _ => throw new ArgumentOutOfRangeException(nameof(issue)),
        };
    }

    private static LauncherInstallationSelfTestIssue Map(LauncherBootstrapStateLoadIssue issue)
    {
        return issue switch
        {
            LauncherBootstrapStateLoadIssue.None => throw new InvalidOperationException(
                "A successful launcher-state load cannot be mapped to a self-test failure."),
            LauncherBootstrapStateLoadIssue.Missing => LauncherInstallationSelfTestIssue.LauncherStateMissing,
            LauncherBootstrapStateLoadIssue.Invalid => LauncherInstallationSelfTestIssue.LauncherStateInvalid,
            LauncherBootstrapStateLoadIssue.Unavailable => LauncherInstallationSelfTestIssue.LauncherStateUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(issue)),
        };
    }

    private static LauncherInstallationSelfTestIssue Map(InstalledLauncherIssue issue)
    {
        return issue switch
        {
            InstalledLauncherIssue.None => throw new InvalidOperationException(
                "A verified launcher cannot be mapped to a self-test failure."),
            InstalledLauncherIssue.Unavailable => LauncherInstallationSelfTestIssue.ActiveLauncherUnavailable,
            InstalledLauncherIssue.InvalidManifest => LauncherInstallationSelfTestIssue.ActiveLauncherInvalid,
            InstalledLauncherIssue.Tampered => LauncherInstallationSelfTestIssue.ActiveLauncherTampered,
            InstalledLauncherIssue.ProtocolMismatch => LauncherInstallationSelfTestIssue.ActiveLauncherProtocolMismatch,
            InstalledLauncherIssue.UnsafePath => LauncherInstallationSelfTestIssue.ActiveLauncherUnsafePath,
            _ => throw new ArgumentOutOfRangeException(nameof(issue)),
        };
    }
}
