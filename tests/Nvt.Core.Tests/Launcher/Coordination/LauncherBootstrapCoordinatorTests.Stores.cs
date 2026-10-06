// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

namespace Nvt.Core.Tests.Launcher.Coordination;

public sealed partial class LauncherBootstrapCoordinatorTests
{
    private sealed class RecordingAppStateStore(VersionManagerState state) : IVersionManagerStateStore
    {
        public int BusyLeaseAt { get; init; } = int.MaxValue;
        public int FailLoadAfter { get; init; } = int.MaxValue;
        public int FailSaveAt { get; init; } = int.MaxValue;
        public VersionManagerState? StateAfterFirstLoad { get; init; }
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }
        public List<TimeSpan> WriterLeaseTimeouts { get; } = [];
        public VersionManagerState ReadyState => StateAfterFirstLoad ?? state;

        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriterLeaseTimeouts.Add(waitTimeout);
            if (WriterLeaseTimeouts.Count == BusyLeaseAt)
            {
                return ValueTask.FromResult(VersionManagerWriteLeaseTestSupport.Busy());
            }
#pragma warning disable CA2000 // Ownership transfers to VersionManagerWriteLeaseResult.
            var result = new VersionManagerWriteLeaseResult(
                VersionManagerWriteLeaseIssue.None,
                new NoOpLease());
#pragma warning restore CA2000
            return ValueTask.FromResult(result);
        }

        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            VersionManagerState published = LoadCount > 1 && StateAfterFirstLoad is not null
                ? StateAfterFirstLoad
                : state;
            return ValueTask.FromResult(LoadCount > FailLoadAfter
                ? new VersionManagerStateLoadResult(null, VersionManagerStateLoadIssue.Unavailable)
                : new VersionManagerStateLoadResult(published, VersionManagerStateLoadIssue.None));
        }

        public ValueTask SaveAsync(VersionManagerState value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (SaveCount == FailSaveAt)
            {
                throw new IOException("Injected application-state power cut.");
            }
            state = value;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLauncherStateStore(LauncherBootstrapState? load) : ILauncherBootstrapStateStore
    {
        public int FailSaveAt { get; init; } = int.MaxValue;
        public LauncherBootstrapState? Current { get; private set; } = load;
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }

        public ValueTask<LauncherBootstrapStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return ValueTask.FromResult(Current is null
                ? new LauncherBootstrapStateLoadResult(null, LauncherBootstrapStateLoadIssue.Missing)
                : new LauncherBootstrapStateLoadResult(Current, LauncherBootstrapStateLoadIssue.None));
        }

        public ValueTask<LauncherBootstrapStateSaveResult> TrySaveAsync(
            LauncherBootstrapState state,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (SaveCount == FailSaveAt)
            {
                return ValueTask.FromResult(new LauncherBootstrapStateSaveResult(
                    LauncherBootstrapStateSaveIssue.Unavailable));
            }
            Current = state;
            return ValueTask.FromResult(new LauncherBootstrapStateSaveResult(
                LauncherBootstrapStateSaveIssue.None));
        }
    }

    private sealed class NoOpLease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
