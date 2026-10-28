// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Coordination;
using Xunit;
using static Nvt.Core.Tests.Launcher.Coordination.ManagedMutationTestSupport;

namespace Nvt.Core.Tests.Launcher.Coordination;

public sealed partial class ManagedMutationCoordinatorSourceTransactionTests
{
    /// <summary>A callback fault propagates unchanged and releases custody for the next committing transaction.</summary>
    [Fact]
    public async Task CallbackFailureReleasesWriterAndGateWithoutReplacingException()
    {
        using var store = new MemoryStateStore(Initial());
        using ManagedMutationCoordinator coordinator = Create(store);
        var failure = new IOException("Synthetic source callback failure.");

        IOException thrown = await Assert.ThrowsAsync<IOException>(() => coordinator.RunSourceTransactionAsync<bool>(
            false, async (scope, token) =>
            {
                Assert.True(await scope.AcquireWriterAsync(token));
                _ = await scope.ReloadAsync(token);
                throw failure;
            }, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, thrown);
        Assert.False(store.HoldsWriter);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(CoordinationStateCodec.Encode(Initial()), CoordinationStateCodec.Encode(store.State));
        bool saved = await coordinator.RunSourceTransactionAsync(false, async (scope, token) =>
        {
            Assert.True(await scope.AcquireWriterAsync(token));
            ManagedMutationSnapshot durable = await scope.ReloadAsync(token);
            return await scope.TrySaveAsync(durable.State!, Proposed(durable.State!), true, token);
        }, TestContext.Current.CancellationToken);
        Assert.True(saved);
        Assert.Equal(2, store.WriteLeaseCount);
        Assert.Equal(1, store.SaveCount);
        Assert.False(store.HoldsWriter);
        Assert.Equal(CoordinationStateCodec.Encode(Proposed(Initial())), CoordinationStateCodec.Encode(store.State));
    }
}
