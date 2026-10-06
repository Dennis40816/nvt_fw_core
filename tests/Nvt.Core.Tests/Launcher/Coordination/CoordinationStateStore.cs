// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Tests.Launcher.Persistence;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

internal class CoordinationStateStore : IVersionManagerStateStore, IDisposable
{
    private readonly StateFileWorkspace _workspace = new();
    private readonly VersionManagerStateFile _file;
    private VersionManagerWriteLeaseResult? _lease;
    internal CoordinationStateStore(VersionManagerState state, int failOnSave = int.MaxValue)
    {
        StatePath = _workspace.PathFor();
        _file = new(StatePath, 1_048_576);
        FailOnSave = failOnSave;
        File.WriteAllBytes(StatePath, CoordinationStateCodec.Encode(state));
    }
    internal string StatePath { get; }
    internal bool HoldsWriter => _lease?.HoldsStatePath(StatePath) == true;
    internal VersionManagerState State => CoordinationStateCodec.Decode(File.ReadAllBytes(StatePath));
    internal int SaveCount { get; private set; }
    internal int WriteLeaseCount { get; private set; }
    internal int FailOnSave { get; set; }
    internal List<TimeSpan> Waits { get; } = [];
    internal List<string> Trace { get; } = [];
    internal Action? BeforeAcquire { get; set; }
    internal Action<VersionManagerState>? BeforeSave { get; set; }
    internal Action? BeforeLoad { get; set; }
    internal VersionManagerStateLoadIssue LoadIssue { get; set; }
    internal void ReplaceState(VersionManagerState replacement) => File.WriteAllBytes(StatePath, CoordinationStateCodec.Encode(replacement));
    public async ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        BeforeAcquire?.Invoke();
        WriteLeaseCount++;
        Waits.Add(waitTimeout);
        Trace.Add($"acquire:{waitTimeout.Ticks}");
        _lease = await _file.TryAcquireWriteLeaseAsync(waitTimeout, cancellationToken);
        Trace.Add($"writer:{_lease.Issue}");
        return _lease;
    }
    public async ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        BeforeLoad?.Invoke();
        Trace.Add("load:app");
        if (LoadIssue != VersionManagerStateLoadIssue.None) { return new(null, LoadIssue); }
        byte[]? bytes = await _file.ReadAsync(cancellationToken);
        if (bytes is null) { return new(null, VersionManagerStateLoadIssue.Missing); }
        try { return new(CoordinationStateCodec.Decode(bytes), VersionManagerStateLoadIssue.None); }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { return new(null, VersionManagerStateLoadIssue.Invalid); }
    }
    public async ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken)
    {
        Assert.True(_lease?.HoldsStatePath(StatePath));
        BeforeSave?.Invoke(state);
        SaveCount++;
        Trace.Add($"save:app:{state.PendingActivation?.Phase.ToString() ?? state.PendingMutation?.Kind.ToString() ?? "committed"}");
        if (SaveCount == FailOnSave) { throw new IOException("Injected state commit failure."); }
        await _file.WriteAsync(CoordinationStateCodec.Encode(state), cancellationToken);
    }
    public void Dispose()
    {
        _lease?.Dispose();
        _workspace.Dispose();
    }
}

internal sealed class CoordinationLauncherStateStore : ILauncherBootstrapStateStore
{
    private readonly LauncherBootstrapStateFile _file;
    private readonly CoordinationStateStore _app;
    internal CoordinationLauncherStateStore(CoordinationStateStore app, LauncherBootstrapState? state = null)
    {
        _app = app;
        _file = new(app.StatePath, ".synthetic-launcher.json", 65_536);
        if (state is not null) { File.WriteAllBytes(_file.StatePathIdentity, CoordinationStateCodec.Encode(state)); }
    }
    internal int FailOnSave { get; set; } = int.MaxValue;
    internal int SaveCount { get; private set; }
    internal LauncherBootstrapState? State => File.Exists(_file.StatePathIdentity)
        ? CoordinationStateCodec.DecodeLauncher(File.ReadAllBytes(_file.StatePathIdentity)) : null;
    public async ValueTask<LauncherBootstrapStateLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        _app.Trace.Add("load:launcher");
        byte[]? bytes = await _file.ReadAsync(cancellationToken);
        return bytes is null ? new(null, LauncherBootstrapStateLoadIssue.Missing)
            : new(CoordinationStateCodec.DecodeLauncher(bytes), LauncherBootstrapStateLoadIssue.None);
    }
    public async ValueTask<LauncherBootstrapStateSaveResult> TrySaveAsync(LauncherBootstrapState state, CancellationToken cancellationToken)
    {
        using VersionManagerWriteLeaseResult competing = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            _app.StatePath, TimeSpan.Zero, cancellationToken);
        Assert.Equal(VersionManagerWriteLeaseIssue.Busy, competing.Issue);
        _app.Trace.Add($"save:launcher:{state.Pending?.Phase.ToString() ?? "committed"}");
        SaveCount++;
        if (SaveCount == FailOnSave) { return new(LauncherBootstrapStateSaveIssue.Unavailable); }
        return await _file.TryWriteAsync(CoordinationStateCodec.Encode(state), cancellationToken);
    }
}
