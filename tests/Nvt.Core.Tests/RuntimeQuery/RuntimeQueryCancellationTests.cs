// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks cooperative cancellation through both router constructors and startup passes.</summary>
public sealed class RuntimeQueryCancellationTests
{
    /// <summary>Both constructors reject cancellation before request validation or handler execution.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledRequestNeverStartsHandler(bool dictionary)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var router = CreateRouter(dictionary, (_, _, _) =>
        {
            calls++;
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        });
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            router.ExecuteAsync(null, "1", cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Throws<OperationCanceledException>(() => { _ = router.RouteAsync("probe", null, cancellation.Token); });
        Assert.Equal(0, calls);
    }

    /// <summary>The dictionary table and command factory preserve runtime arguments, invocation and token.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeHandlerReceivesOriginalToken(bool dictionary)
    {
        using var cancellation = new CancellationTokenSource();
        var args = new Dictionary<string, string> { ["value"] = " unchanged " };
        var expected = RuntimeQueryResponseEnvelope.Success(new object());
        Task<RuntimeQueryResponseEnvelope> Handler(RuntimeQueryInvocation invocation,
            IReadOnlyDictionary<string, string>? received, CancellationToken token)
        {
            Assert.Equal(RuntimeQueryInvocation.Runtime, invocation);
            Assert.Same(args, received);
            Assert.Equal(cancellation.Token, token);
            return Task.FromResult(expected);
        }
        var router = dictionary ? CreateRouter(true, Handler) : new RuntimeQueryCommandRouter(
            [RuntimeQueryCommand.FromArgs("probe", RuntimeQueryCommandRisk.ReadOnly,
                (received, token) => Handler(RuntimeQueryInvocation.Runtime, received, token))], false);
        Assert.Same(expected, await router.ExecuteAsync(new("1", "probe", args), "1", cancellation.Token));
    }

    /// <summary>Started handlers that ignore cancellation keep their task, response and cleanup behavior.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartedHandlerIgnoringTokenReturnsOriginalResponse(bool dictionary)
    {
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<RuntimeQueryResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = RuntimeQueryResponseEnvelope.Success(new object());
        var router = CreateRouter(dictionary, (_, _, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            return completion.Task;
        });
        var execution = router.RouteAsync("probe", null, cancellation.Token);
        Assert.Same(completion.Task, execution);
        cancellation.Cancel();
        Assert.False(execution.IsCompleted);
        completion.SetResult(expected);
        Assert.Same(expected, await execution.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    /// <summary>Started handlers can cancel their own waits using the forwarded token.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartedHandlerObservesCooperativeCancellation(bool dictionary)
    {
        using var cancellation = new CancellationTokenSource();
        var router = CreateRouter(dictionary, async (_, _, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return RuntimeQueryResponseEnvelope.Success(null);
        });
        var execution = router.ExecuteAsync(new("1", "probe", null), "1", cancellation.Token);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    /// <summary>Cancellation never replaces unrelated failures or another operation's cancellation exception.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerExceptionsRemainUnchangedAfterCancellation(bool otherCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        Exception expected = otherCancellation
            ? new OperationCanceledException(other.Token)
            : new InvalidOperationException("Handler failure.");
        var router = CreateRouter(false, (_, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromException<RuntimeQueryResponseEnvelope>(expected);
        });
        Assert.Same(expected, await Record.ExceptionAsync(() =>
            router.ExecuteAsync(new("1", "probe", null), "1", cancellation.Token)));
    }

    /// <summary>Every startup pass checks cancellation before its first command and between commands.</summary>
    [Theory]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrame, false)]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrame, true)]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, false)]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, true)]
    [InlineData(RuntimeQueryStartupPhase.AfterStartup, false)]
    [InlineData(RuntimeQueryStartupPhase.AfterStartup, true)]
    public async Task StartupCancellationPreventsNextCommand(RuntimeQueryStartupPhase phase, bool cancelBefore)
    {
        using var cancellation = new CancellationTokenSource();
        var executed = new List<string>();
        var command = RuntimeQueryCommand.FromArgs("first", RuntimeQueryCommandRisk.ReadOnly, (args, token) =>
        {
            Assert.Null(args);
            Assert.Equal(cancellation.Token, token);
            executed.Add("first");
            cancellation.Cancel();
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        }, phase);
        var router = new RuntimeQueryCommandRouter(
        [
            command,
            new("second", RuntimeQueryCommandRisk.ReadOnly, (_, _, _) =>
            {
                executed.Add("second");
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
            }, phase)
        ], false);
        var parsed = router.ParseStartupArguments(["--first", "--second"]);
        if (cancelBefore)
        {
            cancellation.Cancel();
        }
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            router.ExecuteStartupPhaseAsync(parsed.Calls, phase, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(cancelBefore ? Array.Empty<string>() : ["first"], executed);
    }

    /// <summary>One startup handler receives its invocation and retains a completed mutation after cancellation.</summary>
    [Fact]
    public async Task StartupHandlerIgnoringTokenReturnsCompletedMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = RuntimeQueryResponseEnvelope.Success(new object());
        var command = new RuntimeQueryCommand("probe", RuntimeQueryCommandRisk.ReadOnly, (invocation, _, token) =>
        {
            Assert.Equal(RuntimeQueryInvocation.Startup, invocation);
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            return Task.FromResult(expected);
        }, RuntimeQueryStartupPhase.AfterStartup);
        var router = new RuntimeQueryCommandRouter([command], false);
        var results = await router.ExecuteStartupPhaseAsync(router.ParseStartupArguments(["--probe"]).Calls,
            RuntimeQueryStartupPhase.AfterStartup, cancellation.Token);
        Assert.Same(expected, Assert.Single(results).Response);
    }

    private static RuntimeQueryCommandRouter CreateRouter(bool dictionary,
        Func<RuntimeQueryInvocation, IReadOnlyDictionary<string, string>?, CancellationToken, Task<RuntimeQueryResponseEnvelope>> handler) =>
        dictionary
            ? new(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, CancellationToken, Task<RuntimeQueryResponseEnvelope>>>
            {
                ["probe"] = (args, token) => handler(RuntimeQueryInvocation.Runtime, args, token)
            })
            : new([new("probe", RuntimeQueryCommandRisk.ReadOnly, handler)], false);
}
