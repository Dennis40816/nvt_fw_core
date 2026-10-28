// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Integration;

internal static class IntegrationTestData
{
    internal static string Hash => new('a', 64);
    internal static DateTimeOffset PublishedAt => new(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);
    internal static VersionManagementLimits Limits => new(128, 134_217_728, 65_536, 512, 16, 128);

    internal static ProductDescriptor Descriptor(string registryId = "fixture-registry") => new(
        "Fixture", "synthetic-runtime", registryId, "Fixture.exe", "Launcher.exe", "Bootstrap.exe",
        static version => $"Fixture-{version}", new LauncherProtocolNames(
            "AppReady", "AppVersion", "LauncherReady", "LauncherVersion", "Admission", "Context",
            "Start", "LifetimeContext", "LifetimeHandle", "LifetimeJob", "StatePath", "Role", "Identity"));

    internal static UpdateCatalogVersionSnapshot Package(
        int patch = 1, long bytes = 42, string notes = "", int pathCharacters = 12,
        UpdateNotificationPolicy policy = UpdateNotificationPolicy.Notify) =>
        UpdateCatalogVersionSnapshot.Create(long.MaxValue, int.MaxValue,
            ManagedAppVersion.Parse($"1.0.{patch}"), PublishedAt,
            new UpdateCatalogPackagePath(new string('p', pathCharacters)), bytes, Hash, Hash, notes, policy);

    internal static UpdateSourceRegistryEntry Entry(
        int index = 0, UpdateSourceRegistryEntryStatus status = UpdateSourceRegistryEntryStatus.Latest) =>
        UpdateSourceRegistryEntry.Create(
            Path.GetFullPath(Path.Combine("registry-fixture", $"source-{index}", "catalog.json")), status);

    internal static UpdateSourceRegistrySnapshot Snapshot(
        long revision = 1, char digest = 'a', string registryId = "fixture-registry",
        IReadOnlyList<UpdateSourceRegistryEntry>? entries = null,
        VersionManagementLimits? limits = null) => UpdateSourceRegistrySnapshot.Create(
            limits ?? Limits, Descriptor(registryId), registryId, revision, PublishedAt,
            UpdateCatalogPublicationAssertion.Create("1.0.1", 1, Hash), new string(digest, 64),
            entries ?? [Entry()]);

    internal static UpdateSourceRegistryLoadResult Success(UpdateSourceRegistrySnapshot snapshot) =>
        new(snapshot, UpdateSourceRegistryLoadIssue.None);

    internal static DelegateRegistry Stub(UpdateSourceRegistryLoadResult outcome) =>
        new(_ => ValueTask.FromResult(outcome));

    internal static ManualTimeProvider Clock() => new(DateTimeOffset.UnixEpoch);

    internal sealed class DelegateRegistry(Func<CancellationToken, ValueTask<UpdateSourceRegistryLoadResult>> load)
        : IUpdateSourceRegistry
    {
        public ValueTask<UpdateSourceRegistryLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            load(cancellationToken);
    }

    internal sealed class GatedRegistry(bool synchronous = false) : IUpdateSourceRegistry
    {
        // Guard: Interlocked/Volatile protects the count; Started publishes the captured token.
        private int _loadCount;
        // Guard: the test reads the captured token after awaiting Started.Task.
        private CancellationToken _receivedToken;
        internal int LoadCount => Volatile.Read(ref _loadCount);
        internal CancellationToken ReceivedToken => _receivedToken;
        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Guard: TaskCompletionSource synchronizes the adapter and test continuations.
        internal TaskCompletionSource<UpdateSourceRegistryLoadResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<UpdateSourceRegistryLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _loadCount);
            _receivedToken = cancellationToken;
            _ = Started.TrySetResult();
            return synchronous
                ? ValueTask.FromResult(TaskBlock.UntilComplete(Completion.Task))
                : new(Completion.Task);
        }
    }

    // Both layers use the same clock. The outer deadline stays beyond the inner deadline
    // so advancing the stalled replica's timeout cannot expire a healthy backup.
    internal static ReplicatedUpdateSourceRegistry WithManualPrimary(
        IUpdateSourceRegistry primary, IUpdateSourceRegistry backup, ManualTimeProvider clock) => new(
            [new ReplicatedUpdateSourceRegistry([primary], TimeSpan.FromMilliseconds(100), clock), backup],
            TimeSpan.FromSeconds(45), clock);

    internal static async Task<UpdateSourceRegistryLoadResult> LoadUntilRecoveredAsync(IUpdateSourceRegistry registry)
    {
        while (true)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            UpdateSourceRegistryLoadResult outcome = await registry.LoadAsync(TestContext.Current.CancellationToken);
            if (outcome.Replicas![0].Issue != UpdateSourceRegistryLoadIssue.RegistryTimedOut)
            {
                return outcome;
            }
            await Task.Yield();
        }
    }
}
