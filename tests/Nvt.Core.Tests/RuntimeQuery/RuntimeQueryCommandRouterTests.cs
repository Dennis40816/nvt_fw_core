// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Text.Json;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Characterizes the command router and the request check.</summary>
public sealed class RuntimeQueryCommandRouterTests
{
    private static readonly string[] RegisteredNames = ["zeta", "alpha", "Case", " padded "];
    /// <summary>Routing trims and lowercases names and reports unknown names with the source message.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.Commands), MemberType = typeof(RuntimeQueryCommandCases))]
    public async Task RouteAsyncMatchesFrozenNamesAndMessages(string? command, bool success, string? message)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var args = new Dictionary<string, string> { ["CaseKey"] = " unchanged " };
            var expected = RuntimeQueryResponseEnvelope.Success(new object());
            var calls = 0;
            Task<RuntimeQueryResponseEnvelope> Handler(IReadOnlyDictionary<string, string>? received)
            {
                calls++;
                Assert.Same(args, received);
                return Task.FromResult(expected);
            }

            var router = new RuntimeQueryCommandRouter(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>
            {
                ["probe"] = Handler,
                ["ping"] = Handler,
                [""] = _ => throw new InvalidOperationException("Blank names must not reach handlers.")
            });
            var response = await router.RouteAsync(command, args);
            if (success)
            {
                Assert.Same(expected, response);
                Assert.Equal(1, calls);
            }
            else
            {
                Assert.Equal(RuntimeQueryResponseEnvelope.Failure("UNKNOWN_COMMAND", message!), response);
                Assert.Equal(0, calls);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>The request check runs in the source order: null request, version, then command.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.Requests), MemberType = typeof(RuntimeQueryCommandCases))]
    public async Task ExecuteAsyncChecksNullThenOrdinalVersionThenCommand(
        string json, string? expectedVersion, string? code, string? message)
    {
        var request = JsonSerializer.Deserialize<RuntimeQueryRequest>(json, RuntimeQueryProtocol.CompactJsonOptions);
        var expected = RuntimeQueryResponseEnvelope.Success(new object());
        var calls = 0;
        var router = new RuntimeQueryCommandRouter(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>
        {
            ["probe"] = args =>
            {
                calls++;
                Assert.Same(request!.Args, args);
                return Task.FromResult(expected);
            }
        });
        var response = await router.ExecuteAsync(request, expectedVersion!);
        if (code is null)
        {
            Assert.Same(expected, response);
            Assert.Equal(1, calls);
        }
        else
        {
            Assert.Equal(RuntimeQueryResponseEnvelope.Failure(code, message!), response);
            Assert.Equal(0, calls);
        }
    }

    /// <summary>The handler receives null arguments, and the router returns its task unchanged.</summary>
    [Fact]
    public async Task RouteAsyncPassesNullArgumentsAndReturnsTheHandlerTaskUnchanged()
    {
        var completion = new TaskCompletionSource<RuntimeQueryResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var router = new RuntimeQueryCommandRouter(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>
        {
            ["probe"] = args =>
            {
                Assert.Null(args);
                return completion.Task;
            }
        });
        var actual = router.RouteAsync("probe", null);
        Assert.Same(completion.Task, actual);
        var response = RuntimeQueryResponseEnvelope.Failure("HANDLER_CODE", "Handler message.");
        completion.SetResult(response);
        Assert.Same(response, await actual);
    }

    /// <summary>Registered names keep their order and text, and the list is read-only.</summary>
    [Fact]
    public void RegisteredCommandsPreserveOrderAndTextAndRejectMutation()
    {
        var handlers = new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>
        {
            ["zeta"] = _ => Task.FromResult(RuntimeQueryResponseEnvelope.Success(null)),
            ["alpha"] = _ => Task.FromResult(RuntimeQueryResponseEnvelope.Success(null)),
            ["Case"] = _ => Task.FromResult(RuntimeQueryResponseEnvelope.Success(null)),
            [" padded "] = _ => Task.FromResult(RuntimeQueryResponseEnvelope.Success(null))
        };
        var router = new RuntimeQueryCommandRouter(handlers);
        Assert.Equal(RegisteredNames, router.RegisteredCommands);
        var list = Assert.IsAssignableFrom<IList<string>>(router.RegisteredCommands);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add("extra"));
        Assert.Throws<NotSupportedException>(() => list[0] = "changed");
        Assert.Equal(RegisteredNames, handlers.Keys);
    }

    /// <summary>An empty handler table registers no commands.</summary>
    [Fact]
    public async Task EmptyHandlerTableAddsNoCommands()
    {
        var router = new RuntimeQueryCommandRouter(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>());
        Assert.Empty(router.RegisteredCommands);
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("UNKNOWN_COMMAND", "Unknown query command 'help'."),
            await router.RouteAsync("help", null));
    }

    /// <summary>Lookup uses the comparer and keys of the caller handler table.</summary>
    [Fact]
    public async Task RouteAsyncUsesTheSuppliedHandlerTableComparerAndKeys()
    {
        var expected = RuntimeQueryResponseEnvelope.Success(null);
        var handlers = new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>(StringComparer.Ordinal)
        {
            ["PROBE"] = _ => Task.FromResult(expected)
        };
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("UNKNOWN_COMMAND", "Unknown query command 'PROBE'."),
            await new RuntimeQueryCommandRouter(handlers).RouteAsync("PROBE", null));
        var insensitive = new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>(handlers, StringComparer.OrdinalIgnoreCase);
        Assert.Same(expected, await new RuntimeQueryCommandRouter(insensitive).RouteAsync("probe", null));
    }

    /// <summary>A null handler table throws, as in the source.</summary>
    [Fact]
    public void ConstructorRetainsTheSourceNullHandlerCheck()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new RuntimeQueryCommandRouter(null!));
        Assert.Equal("handlers", exception.ParamName);
    }

    /// <summary>A wrong version fails before the handler parses any argument.</summary>
    [Fact]
    public async Task ExecuteAsyncChecksVersionBeforeHandlerArgumentParsing()
    {
        var calls = 0;
        var router = new RuntimeQueryCommandRouter(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>
        {
            ["probe"] = args =>
            {
                calls++;
                RuntimeQueryArgumentParser.TryGetIntArg(args, "limit", 1, 2, out _, out var error);
                return Task.FromResult(error!);
            }
        });
        var args = new Dictionary<string, string> { ["limit"] = "invalid" };
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'."),
            await router.ExecuteAsync(new RuntimeQueryRequest("2", "probe", args), "1"));
        Assert.Equal(0, calls);
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("INVALID_ARGUMENTS", "Argument '--limit' must be an integer."),
            await router.ExecuteAsync(new RuntimeQueryRequest("1", "probe", args), "1"));
        Assert.Equal(1, calls);
    }
}
