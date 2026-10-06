// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Persistence;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Persistence;

/// <summary>Frozen persistence port signatures and adapter exception mapping.</summary>
public sealed class StateStorePortTests
{
    /// <summary>Only the original three adapter failure categories map to Unavailable.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task TrySavePreservesExceptionMapping(int shape)
    {
        Exception? error = shape switch
        {
            0 => null,
            1 => new IOException("synthetic"),
            2 => new UnauthorizedAccessException("synthetic"),
            3 => new InvalidOperationException("synthetic"),
            _ => new ArgumentException("synthetic"),
        };
        IVersionManagerStateStore store = new SyntheticPort(error);
        VersionManagerState state = VersionManagerState.Create(null, null, null, [], null, null, false);
        if (shape == 4)
        {
            Exception? observed = await Record.ExceptionAsync(async () => await store.TrySaveAsync(state, TestContext.Current.CancellationToken));
            Assert.Same(error, observed);
        }
        else
        {
            VersionManagerStateSaveResult saved = await store.TrySaveAsync(state, TestContext.Current.CancellationToken);
            Assert.Equal(shape == 0, saved.IsSuccess);
        }
    }

    /// <summary>Cancellation is propagated through the retained default save port.</summary>
    [Fact]
    public async Task TrySavePropagatesCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var error = new OperationCanceledException(cancellation.Token);
        IVersionManagerStateStore store = new SyntheticPort(error);
        VersionManagerState state = VersionManagerState.Create(null, null, null, [], null, null, false);
        Exception? observed = await Record.ExceptionAsync(async () => await store.TrySaveAsync(state, cancellation.Token));
        Assert.Same(error, observed);
    }

    private sealed class SyntheticPort(Exception? error) : IVersionManagerStateStore
    {
        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new VersionManagerStateLoadResult(null, VersionManagerStateLoadIssue.Missing));

        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new VersionManagerWriteLeaseResult(VersionManagerWriteLeaseIssue.Unavailable));

        public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken) =>
            error is null ? ValueTask.CompletedTask : ValueTask.FromException(error);
    }
}
