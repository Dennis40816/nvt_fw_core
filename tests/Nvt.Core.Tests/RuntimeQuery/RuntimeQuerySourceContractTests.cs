// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Ports the routing boundary of source use-case tests with synthetic handlers.</summary>
public sealed class RuntimeQuerySourceContractTests
{
    /// <summary>Each ported source request reaches its handler with unchanged arguments.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.FrozenUseCaseRequests), MemberType = typeof(RuntimeQueryCommandCases))]
    public async Task FrozenUseCaseRequestsReachTheHandlerWithUnchangedArguments(
        string sourceTest, string command, IReadOnlyDictionary<string, string>? args)
    {
        var expected = RuntimeQueryResponseEnvelope.Success(sourceTest);
        var calls = 0;
        var router = new RuntimeQueryCommandRouter(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>(StringComparer.Ordinal)
        {
            [command] = received =>
            {
                calls++;
                Assert.Same(args, received);
                return Task.FromResult(expected);
            }
        });
        Assert.Same(expected, await router.ExecuteAsync(new RuntimeQueryRequest("1", command, args), "1"));
        Assert.Equal(1, calls);
    }

    /// <summary>A handler exception passes through the router and the request check unchanged.</summary>
    [Fact]
    public async Task HandlerExceptionsEscapeRouteAndExecute()
    {
        // Port of the source test ExecuteAsync_QueryNotchValidation_RejectsInvalidFirmwareNullSentinel.
        // The source handler throws. The router and request check must let that exception escape.
        var expected = new InvalidOperationException("NullValue must be in [0,65535].");
        var router = new RuntimeQueryCommandRouter(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>
        {
            ["notch-validation"] = _ => throw expected
        });
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => router.ExecuteAsync(
            new RuntimeQueryRequest("1", "notch-validation", new Dictionary<string, string> { ["regular-id"] = "100" }), "1"));
        Assert.Same(expected, exception);
        Assert.Equal("NullValue must be in [0,65535].", exception.Message);
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => { _ = router.RouteAsync("notch-validation", null); }));
    }
}
