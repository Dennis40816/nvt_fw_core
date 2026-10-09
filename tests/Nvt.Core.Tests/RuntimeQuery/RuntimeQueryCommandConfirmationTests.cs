// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Text.Json;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks opt-in confirmation and compatibility with the dictionary constructor.</summary>
public sealed class RuntimeQueryCommandConfirmationTests
{
    /// <summary>The disabled guard keeps every existing command result and the original arguments.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.Commands), MemberType = typeof(RuntimeQueryCommandCases))]
    public async Task GuardOffMatchesExistingCommandRows(string? command, bool success, string? message)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal) { ["CaseKey"] = " unchanged ", ["confirm"] = "invalid" };
        var expected = RuntimeQueryResponseEnvelope.Success(null);
        var calls = 0;
        Task<RuntimeQueryResponseEnvelope> Handler(RuntimeQueryInvocation invocation, IReadOnlyDictionary<string, string>? received, CancellationToken cancellationToken)
        {
            calls++;
            Assert.Same(args, received);
            return Task.FromResult(expected);
        }

        var handlers = new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, CancellationToken, Task<RuntimeQueryResponseEnvelope>>>(StringComparer.Ordinal)
        {
            ["probe"] = (args, token) => Handler(RuntimeQueryInvocation.Runtime, args, token),
            ["ping"] = (args, token) => Handler(RuntimeQueryInvocation.Runtime, args, token)
        };
        var dictionaryRouter = new RuntimeQueryCommandRouter(handlers);
        var router = new RuntimeQueryCommandRouter(handlers.Select(pair =>
            RuntimeQueryCommand.FromArgs(pair.Key, RuntimeQueryCommandRisk.WritesData, pair.Value)).ToArray(), requireConfirmation: false);
        var baseline = await dictionaryRouter.RouteAsync(command, args, TestContext.Current.CancellationToken);
        var response = await router.RouteAsync(command, args, TestContext.Current.CancellationToken);
        Assert.Equal(baseline, response);
        Assert.Equal(success ? expected : RuntimeQueryResponseEnvelope.Failure("UNKNOWN_COMMAND", message!), response);
        Assert.Equal(success ? 2 : 0, calls);
    }

    /// <summary>The disabled guard keeps every existing request result.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.Requests), MemberType = typeof(RuntimeQueryCommandCases))]
    public async Task GuardOffMatchesExistingRequestRows(string json, string? version, string? code, string? message)
    {
        var request = JsonSerializer.Deserialize<RuntimeQueryRequest>(json, RuntimeQueryProtocol.CompactJsonOptions);
        var baseline = await AssertGuardOffMatchesAsync(request, version!);
        Assert.Equal(code is null ? RuntimeQueryResponseEnvelope.Success(null) : RuntimeQueryResponseEnvelope.Failure(code, message!), baseline);
    }

    /// <summary>The disabled guard keeps every existing source request row.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.FrozenUseCaseRequests), MemberType = typeof(RuntimeQueryCommandCases))]
    public async Task GuardOffMatchesExistingUseCaseRows(string sourceTest, string command, IReadOnlyDictionary<string, string>? args)
    {
        var response = await AssertGuardOffMatchesAsync(new RuntimeQueryRequest("1", command, args), "1",
            _ => RuntimeQueryResponseEnvelope.Success(sourceTest), command);
        Assert.Equal(RuntimeQueryResponseEnvelope.Success(sourceTest), response);
    }

    /// <summary>The disabled guard keeps every existing argument helper row.</summary>
    [Fact]
    public async Task GuardOffMatchesEveryExistingArgumentRow()
    {
        foreach (var row in RuntimeQueryCommandCases.Integers)
        {
            await AssertArgumentRowAsync(row, args =>
            {
                var parsed = RuntimeQueryArgumentParser.TryGetIntArg(args, "value", (int)row[1]!, (int)row[2]!, out var value, out var error);
                return RuntimeQueryResponseEnvelope.Success(new { parsed, value, error });
            });
        }

        foreach (var row in RuntimeQueryCommandCases.IntegerLists)
        {
            await AssertArgumentRowAsync(row, args =>
            {
                var parsed = RuntimeQueryArgumentParser.TryGetIntListArg(args, "value", -2, 2, out var value, out var error);
                return RuntimeQueryResponseEnvelope.Success(new { parsed, value, error });
            });
        }

        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var row in RuntimeQueryCommandCases.Doubles)
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo((string)row[3]!);
                await AssertArgumentRowAsync(row, args =>
                {
                    var parsed = RuntimeQueryArgumentParser.TryGetDoubleArg(args, "value", (double)row[1]!, (double)row[2]!, out var value, out var error);
                    return RuntimeQueryResponseEnvelope.Success(new { parsed, value, error });
                });
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        foreach (var row in RuntimeQueryCommandCases.Strings)
        {
            await AssertArgumentRowAsync(row, args =>
            {
                var parsed = RuntimeQueryArgumentParser.TryGetStringArg(args, "value", out var value, out var error);
                return RuntimeQueryResponseEnvelope.Success(new { parsed, value, error });
            });
        }

        foreach (var row in RuntimeQueryCommandCases.Booleans)
        {
            await AssertArgumentRowAsync(row, args =>
            {
                var parsed = RuntimeQueryArgumentParser.TryGetBoolArg(args, "value", out var value, out var error);
                return RuntimeQueryResponseEnvelope.Success(new { parsed, value, error });
            });
        }

        foreach (var row in RuntimeQueryCommandCases.MissingArguments)
        {
            await AssertGuardOffMatchesAsync(new RuntimeQueryRequest("1", "probe", (IReadOnlyDictionary<string, string>?)row[0]), "1", args =>
            {
                var integer = RuntimeQueryArgumentParser.TryGetIntArg(args, "value", 1, 2, out var intValue, out var intError);
                var list = RuntimeQueryArgumentParser.TryGetIntListArg(args, "value", 1, 2, out var listValue, out var listError);
                var number = RuntimeQueryArgumentParser.TryGetDoubleArg(args, "value", 1, 2, out var doubleValue, out var doubleError);
                var text = RuntimeQueryArgumentParser.TryGetStringArg(args, "value", out var textValue, out var textError);
                var boolean = RuntimeQueryArgumentParser.TryGetBoolArg(args, "value", out var boolValue, out var boolError);
                return RuntimeQueryResponseEnvelope.Success(new
                {
                    integer, intValue, intError, list, listValue, listError, number, doubleValue, doubleError,
                    text, textValue, textError, boolean, boolValue, boolError
                });
            });
        }
    }

    /// <summary>Only commands that write data validate and require confirmation.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryConfirmationCases.Confirmations), MemberType = typeof(RuntimeQueryConfirmationCases))]
    public async Task GuardOnChecksRiskAndConfirmation(RuntimeQueryCommandRisk risk, string? confirm, bool runs, string? code, string? message)
    {
        var args = confirm is null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { ["confirm"] = confirm };
        var expected = RuntimeQueryResponseEnvelope.Success(null);
        var calls = 0;
        Task<RuntimeQueryResponseEnvelope> Handler(RuntimeQueryInvocation invocation, IReadOnlyDictionary<string, string>? received, CancellationToken cancellationToken)
        {
            calls++;
            Assert.Null(received);
            return Task.FromResult(expected);
        }

        var router = new RuntimeQueryCommandRouter([new("probe", risk, Handler)], requireConfirmation: true);
        var task = router.RouteAsync(" PROBE ", args, TestContext.Current.CancellationToken);
        var response = await task;
        Assert.Equal(runs ? expected : RuntimeQueryResponseEnvelope.Failure(code!, message!), response);
        Assert.Equal(code, response.Error?.Code);
        Assert.Equal(message, response.Error?.Message);
        Assert.Equal(runs ? 1 : 0, calls);

        calls = 0;
        var disabled = new RuntimeQueryCommandRouter([new("probe", risk, (_, received, _) =>
        {
            calls++;
            Assert.Same(args, received);
            return Task.FromResult(expected);
        })], requireConfirmation: false);
        Assert.Same(expected, await disabled.RouteAsync(" PROBE ", args, TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    /// <summary>Null, version and unknown-command errors precede the enabled guard.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryConfirmationCases.Requests), MemberType = typeof(RuntimeQueryConfirmationCases))]
    public async Task ExecuteAsyncKeepsErrorOrder(string json, string code, string message)
    {
        var router = new RuntimeQueryCommandRouter([new("probe", RuntimeQueryCommandRisk.WritesData,
            (_, _, _) => throw new InvalidOperationException("The handler must not run."))], requireConfirmation: true);
        var request = JsonSerializer.Deserialize<RuntimeQueryRequest>(json, RuntimeQueryProtocol.CompactJsonOptions);
        var response = await router.ExecuteAsync(request, "1", TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure(code, message), response);
        Assert.Equal(code, response.Error?.Code);
        Assert.Equal(message, response.Error?.Message);
    }

    /// <summary>The enabled guard copies other keys and values without changing the request.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryConfirmationCases.Arguments), MemberType = typeof(RuntimeQueryConfirmationCases))]
    public async Task GuardOnPassesAnOrdinalCopyOrNull(IReadOnlyDictionary<string, string>? args, IReadOnlyDictionary<string, string>? expected)
    {
        var original = args?.ToArray();
        var calls = 0;
        var router = new RuntimeQueryCommandRouter([new("probe", RuntimeQueryCommandRisk.ReadOnly, (_, received, _) =>
        {
            calls++;
            if (expected is null)
            {
                Assert.Null(received);
            }
            else
            {
                var copy = Assert.IsType<Dictionary<string, string>>(received);
                Assert.NotSame(args, copy);
                Assert.Same(StringComparer.Ordinal, copy.Comparer);
                Assert.Equal(expected.ToArray(), copy.ToArray());
            }

            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        })], requireConfirmation: true);
        Assert.Equal(RuntimeQueryResponseEnvelope.Success(null), await router.ExecuteAsync(new RuntimeQueryRequest("1", "probe", args), "1", TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
        Assert.Equal(original, args?.ToArray());
    }

    /// <summary>Registration rejects null values, duplicate names and names routing cannot reach.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryConfirmationCases.InvalidCommands), MemberType = typeof(RuntimeQueryConfirmationCases))]
    public void ConstructorRejectsInvalidCommands(IReadOnlyList<RuntimeQueryCommand>? commands, Type exceptionType)
    {
        Assert.Throws(exceptionType, () => new RuntimeQueryCommandRouter(commands!, requireConfirmation: false));
        Assert.Throws(exceptionType, () => new RuntimeQueryCommandRouter(commands!, requireConfirmation: true));
    }

    /// <summary>Registration preserves order, creates a snapshot and keeps the names read-only.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryConfirmationCases.RegistrationOrders), MemberType = typeof(RuntimeQueryConfirmationCases))]
    public async Task ConstructorPreservesRegistrationOrder(string[] names)
    {
        var commands = names.Select(name => new RuntimeQueryCommand(name, RuntimeQueryCommandRisk.WritesData,
            (_, _, _) => Task.FromResult(RuntimeQueryResponseEnvelope.Success(name)))).ToList();
        var router = new RuntimeQueryCommandRouter(commands, requireConfirmation: true);
        commands.Clear();
        Assert.Equal(names, router.RegisteredCommands);
        var list = Assert.IsAssignableFrom<IList<string>>(router.RegisteredCommands);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add("extra"));
        foreach (var name in names)
        {
            Assert.Equal(RuntimeQueryResponseEnvelope.Success(name),
                await router.RouteAsync(name, new Dictionary<string, string> { ["confirm"] = "true" }, TestContext.Current.CancellationToken));
        }
    }

    private static Task<RuntimeQueryResponseEnvelope> AssertArgumentRowAsync(
        object?[] row, Func<IReadOnlyDictionary<string, string>?, RuntimeQueryResponseEnvelope> result)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal) { ["value"] = (string)row[0]!, ["confirm"] = "invalid" };
        return AssertGuardOffMatchesAsync(new RuntimeQueryRequest("1", "probe", args), "1", result);
    }

    private static async Task<RuntimeQueryResponseEnvelope> AssertGuardOffMatchesAsync(
        RuntimeQueryRequest? request, string version,
        Func<IReadOnlyDictionary<string, string>?, RuntimeQueryResponseEnvelope>? result = null, string name = "probe")
    {
        var calls = 0;
        Task<RuntimeQueryResponseEnvelope> Handler(RuntimeQueryInvocation invocation, IReadOnlyDictionary<string, string>? args, CancellationToken cancellationToken)
        {
            calls++;
            Assert.Same(request!.Args, args);
            return Task.FromResult(result?.Invoke(args) ?? RuntimeQueryResponseEnvelope.Success(null));
        }

        var dictionaryRouter = new RuntimeQueryCommandRouter(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, CancellationToken, Task<RuntimeQueryResponseEnvelope>>>(StringComparer.Ordinal)
        {
            [name] = (args, token) => Handler(RuntimeQueryInvocation.Runtime, args, token)
        });
        var router = new RuntimeQueryCommandRouter([new(name, RuntimeQueryCommandRisk.WritesData, Handler)], requireConfirmation: false);
        var baseline = await dictionaryRouter.ExecuteAsync(request, version, TestContext.Current.CancellationToken);
        var baselineCalls = calls;
        calls = 0;
        var response = await router.ExecuteAsync(request, version, TestContext.Current.CancellationToken);
        Assert.Equal(JsonSerializer.Serialize(baseline, RuntimeQueryProtocol.CompactJsonOptions),
            JsonSerializer.Serialize(response, RuntimeQueryProtocol.CompactJsonOptions));
        Assert.Equal(baselineCalls, calls);
        return response;
    }
}
