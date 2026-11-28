// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Launcher.Recovery.Execution;
using Nvt.Core.Tests.Launcher.Verification;

namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Fixed LinkedProbe entry points sharing the existing recovery fixtures, without an xUnit runner.</summary>
public static class RecoveryHardStopChild
{
    /// <summary>Runs the materializer and seed bootstrapper until the requested synchronous boundary blocks.</summary>
    public static async Task<int> RunSetupAsync(string parent, string boundary, string marker)
    {
        byte[] bytes = await SetupFixture.ReadAsync(Path.Combine(parent, "hard-stop-package.zip"));
        using var fixture = new SetupFixture(parent, PackageFixture.Create(includeLauncher: true), bytes);
        var hooks = new ManagedSetupTestHooks
        {
            BoundaryReached = observed => { if (observed.ToString() == boundary) Hold(marker, boundary); },
        };
        var result = await fixture.MaterializeAsync(hooks, token: CancellationToken.None);
        if (!result.IsSuccess) throw new InvalidOperationException("Materializer stopped before boundary: " + result.Issue);
        using var installation = result.Installation!;
        var recorded = await installation.RecordBootstrapLaunchAsync(CancellationToken.None);
        if (recorded != Nvt.Core.Launcher.Setup.ManagedFirstInstallationTransactionIssue.None)
            throw new InvalidOperationException("Bootstrap marker failed: " + recorded);
        var destination = new HardStopApplicationStore(fixture, marker, boundary);
        var bootstrapper = new ManagedVersionSeedBootstrapper(fixture.Root, destination,
            fixture.SeedStore(Path.Combine(fixture.Root, SetupFixture.Layout.SeedFileName)), fixture.Repository);
        var outcome = await bootstrapper.EnsureInitializedAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        throw new InvalidOperationException("Seed bootstrapper completed before boundary: " + outcome);
    }

    /// <summary>Reloads the serialized marker, obtains fresh evidence and writer custody, then blocks on the selected side.</summary>
    public static async Task<int> RunExecutorAsync(string scenario, string boundary, string marker)
    {
        var stored = await RecoveryHardStop.ReadJsonAsync<ManagedSetupMarker>(scenario);
        string parent = Path.GetDirectoryName(scenario)!;
        string state = stored.StatePathIdentity;
        using var fixture = RecoveryEvidenceFixture.Reopen(parent, Transaction(stored),
            File.Exists(RecoveryEvidenceFixture.LauncherPathFor(state)));
        string[] parts = boundary.Split(':');
        if (parts.Length != 2 || (parts[0] != "before" && parts[0] != "after"))
            throw new InvalidOperationException("Expected before:n or after:n boundary.");
        int stop = int.Parse(parts[1], CultureInfo.InvariantCulture);
        Action<int> hold = count => { if (count == stop) Hold(marker, boundary); };
        var executor = fixture.CreateExecutor(afterDelete: parts[0] == "after" ? hold : null,
            beforeDelete: parts[0] == "before" ? hold : null);
        var evidence = await executor.ObserveAsync(fixture.Transaction, CancellationToken.None);
        if (evidence.Issue != ManagedSetupRecoveryEvidenceIssue.None)
            throw new InvalidOperationException("Executor evidence failed: " + evidence.Issue);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        if (!lease.IsAcquired) throw new InvalidOperationException("Executor writer failed: " + lease.Issue);
        var outcome = await executor.ExecuteAsync(new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation,
            evidence.ExecutionToken!), lease, CancellationToken.None);
        throw new InvalidOperationException("Executor completed before boundary: " + outcome);
    }

    internal static ManagedSetupMarker Marker(ManagedSetupRecoveryTransaction transaction) => new(
        transaction.TransactionId, transaction.ManagedRootIdentity, transaction.StatePathIdentity,
        transaction.Payload, transaction.Candidate,
        [Path.GetFileName(transaction.ManagedRootIdentity),
            Path.GetFileName(FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(transaction.ManagedRootIdentity)),
            Path.GetFileName(FileSystemManagedInstallationRootProbe.GetStagingContainerPath(transaction.ManagedRootIdentity)) + "/" + transaction.TransactionId],
        transaction.Phase == ManagedSetupRecoveryPhase.Staging ? "staging" : "bootstrap-launch-recorded");

    internal static ManagedSetupRecoveryTransaction Transaction(ManagedSetupMarker marker) => new(
        marker.TransactionId, marker.ManagedRootIdentity, marker.StatePathIdentity,
        marker.Phase == "staging" ? ManagedSetupRecoveryPhase.Staging : ManagedSetupRecoveryPhase.BootstrapLaunchRecorded,
        marker.OwnedPaths, marker.Payload, marker.Candidate);

    internal static void Hold(string marker, string boundary)
    {
        SetupFixture.Write(marker + ".pending", boundary);
        new FileInfo(marker + ".pending").MoveTo(marker);
        TaskBlock.UntilComplete(Console.Out.WriteLineAsync("READY:" + boundary));
        TaskBlock.UntilComplete(new SignalWait("parent hard-stop", TimeSpan.FromSeconds(30)).WaitAsync());
    }
}

internal sealed class HardStopApplicationStore(SetupFixture fixture, string? marker = null, string? boundary = null)
    : IManagedSetupRecoveryStateStore
{
    private readonly VersionManagerStateFile _file = new(fixture.StatePath, SetupFixture.Ceiling);
    public string StatePathIdentity => _file.StatePathIdentity;
    public async ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes = await _file.ReadAsync(cancellationToken);
        if (bytes is null) return new(null, File.Exists(StatePathIdentity)
            ? VersionManagerStateLoadIssue.Invalid : VersionManagerStateLoadIssue.Missing);
        return bytes.AsSpan().SequenceEqual("synthetic-bound-state"u8)
            ? new(fixture.Seed.BindToManagedRoot(fixture.Root), VersionManagerStateLoadIssue.None)
            : new(null, VersionManagerStateLoadIssue.Invalid);
    }
    public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken) =>
        _file.TryAcquireWriteLeaseAsync(waitTimeout, cancellationToken);
    public async ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken)
    {
        if (!ManagedVersionSeedPolicy.IsCanonicalBoundFirstRunState(state, fixture.Root, fixture.Admission))
            throw new InvalidOperationException("Unexpected first durable application state.");
        if (boundary == "BeforeFirstDurableApplicationStateWrite") RecoveryHardStopChild.Hold(marker!, boundary);
        await _file.WriteAsync("synthetic-bound-state"u8.ToArray(), cancellationToken);
        if (boundary == "AfterFirstDurableApplicationStateWrite") RecoveryHardStopChild.Hold(marker!, boundary);
    }
}
