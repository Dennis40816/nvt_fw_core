// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;

namespace Nvt.Core.Launcher.Recovery;

internal delegate ValueTask<ManagedVersionInstallResult> ManagedSetupPayloadInstaller(
    WindowsStableRelativeWriteRoot root, string sourceRoot, UpdateCatalogVersionSnapshot package,
    IProgress<ManagedFirstInstallationProgress>? progress, CancellationToken cancellationToken);

internal enum ManagedSetupBoundary
{
    BeforeStagingMarkerWrite, AfterStagingMarkerWrite, BeforePromotion, AfterPromotion,
    BeforeRootPromotedMarkerWrite, AfterRootPromotedMarkerWrite,
    BeforeBootstrapLaunchMarkerWrite, AfterBootstrapLaunchMarkerWrite,
    BeforeSeedWrite, AfterSeedWrite,
}

internal sealed record ManagedSetupTestHooks
{
    internal Action<ManagedSetupBoundary>? BoundaryReached { get; init; }
    internal Func<string, CancellationToken, ValueTask>? StagingCustodyAcquired { get; init; }
    internal Func<string, CancellationToken, ValueTask>? DestinationCustodyAcquired { get; init; }
    internal Func<string, CancellationToken, ValueTask>? ClosedRootVerified { get; init; }
    internal Action<string>? BeforeRepositoryStagingDelete { get; init; }
    internal Action<string>? BeforeRepositoryStagingCleanup { get; init; }
    internal Action<int, WindowsSetupStagingCleanupState>? RepositoryStagingCleanupAttemptObserved { get; init; }
    internal Func<TimeSpan, CancellationToken, ValueTask>? RepositoryStagingCleanupDelay { get; init; }
    internal Func<int, int>? RepositoryStagingDeleteOpenStatusOverride { get; init; }
    internal Func<int, int>? RepositoryOwnedDeletionObservationStatusOverride { get; init; }
    internal Action? AfterMarkerTopologyProof { get; init; }
    internal Action<string>? AfterRootPromotion { get; init; }
}
