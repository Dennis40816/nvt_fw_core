// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks confirmation delivery without changing the router's confirmation guard.</summary>
public sealed class RuntimeQueryCommandConfirmationDeliveryTests
{
    /// <summary>The enabled guard preserves confirmation only for commands that request it.</summary>
    [Theory]
    [InlineData(false, RuntimeQueryCommandRisk.ReadOnly)]
    [InlineData(true, RuntimeQueryCommandRisk.ReadOnly)]
    [InlineData(false, RuntimeQueryCommandRisk.ChangesState)]
    [InlineData(true, RuntimeQueryCommandRisk.ChangesState)]
    [InlineData(false, RuntimeQueryCommandRisk.WritesData)]
    [InlineData(true, RuntimeQueryCommandRisk.WritesData)]
    public async Task GuardOnDeliversConfirmationOnlyWhenRequested(bool receivesConfirmation, RuntimeQueryCommandRisk risk)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["confirm"] = "true", ["CaseKey"] = " unchanged ", ["Confirm"] = "keep"
        };
        var original = args.ToArray();
        var calls = 0;
        var command = new RuntimeQueryCommand("probe", risk, received =>
        {
            calls++;
            Assert.NotNull(received);
            Assert.Equal(" unchanged ", received["CaseKey"]);
            Assert.Equal("keep", received["Confirm"]);
            Assert.Equal(receivesConfirmation, received.ContainsKey("confirm"));
            Assert.Equal(receivesConfirmation ? 3 : 2, received.Count);
            if (receivesConfirmation)
            {
                Assert.Equal("true", received["confirm"]);
            }
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        });
        Assert.False(command.ReceivesConfirmation);
        if (receivesConfirmation)
        {
            command = command with { ReceivesConfirmation = true };
        }
        var router = new RuntimeQueryCommandRouter([command], requireConfirmation: true);

        Assert.Equal(RuntimeQueryResponseEnvelope.Success(null), await router.RouteAsync("probe", args));
        Assert.Equal(1, calls);
        Assert.Equal(original, args.ToArray());
    }

    /// <summary>The disabled guard passes the original arguments for both confirmation delivery settings.</summary>
    [Theory]
    [InlineData(false, RuntimeQueryCommandRisk.ReadOnly)]
    [InlineData(true, RuntimeQueryCommandRisk.ReadOnly)]
    [InlineData(false, RuntimeQueryCommandRisk.ChangesState)]
    [InlineData(true, RuntimeQueryCommandRisk.ChangesState)]
    [InlineData(false, RuntimeQueryCommandRisk.WritesData)]
    [InlineData(true, RuntimeQueryCommandRisk.WritesData)]
    public async Task GuardOffPreservesArgumentsRegardlessOfConfirmationDelivery(bool receivesConfirmation, RuntimeQueryCommandRisk risk)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal) { ["confirm"] = "invalid", ["value"] = " unchanged " };
        var expected = RuntimeQueryResponseEnvelope.Success(null);
        var calls = 0;
        var command = new RuntimeQueryCommand("probe", risk, received =>
        {
            calls++;
            Assert.Same(args, received);
            return Task.FromResult(expected);
        }) { ReceivesConfirmation = receivesConfirmation };
        var router = new RuntimeQueryCommandRouter([command], requireConfirmation: false);

        Assert.Same(expected, await router.RouteAsync("probe", args));
        Assert.Equal(1, calls);
    }

    /// <summary>Receiving confirmation preserves the existing risk checks and parser failures.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryConfirmationCases.Confirmations), MemberType = typeof(RuntimeQueryConfirmationCases))]
    public async Task OptInPreservesConfirmationGuard(
        RuntimeQueryCommandRisk risk, string? confirm, bool runs, string? code, string? message)
    {
        var args = confirm is null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { ["confirm"] = confirm };
        var expected = RuntimeQueryResponseEnvelope.Success(null);
        var calls = 0;
        var command = new RuntimeQueryCommand("probe", risk, received =>
        {
            calls++;
            Assert.Same(args, received);
            return Task.FromResult(expected);
        }) { ReceivesConfirmation = true };
        var router = new RuntimeQueryCommandRouter([command], requireConfirmation: true);

        Assert.Equal(runs ? expected : RuntimeQueryResponseEnvelope.Failure(code!, message!), await router.RouteAsync("probe", args));
        Assert.Equal(runs ? 1 : 0, calls);
    }

    /// <summary>Receiving runtime confirmation does not change startup flag consumption or handler arguments.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptInPreservesStartupConfirmation(bool requireConfirmation)
    {
        var calls = 0;
        var command = new RuntimeQueryCommand("probe", RuntimeQueryCommandRisk.WritesData, args =>
        {
            calls++;
            Assert.Null(args);
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        }, RuntimeQueryStartupPhase.AfterStartup) { ReceivesConfirmation = true };
        var router = new RuntimeQueryCommandRouter([command], requireConfirmation);
        var parsed = router.ParseStartupArguments(["--probe", "--confirm"]);

        Assert.Empty(parsed.Issues);
        Assert.Equal(requireConfirmation, Assert.Single(parsed.Calls).Confirmed);
        Assert.Equal(requireConfirmation ? Array.Empty<string>() : ["--confirm"], parsed.RemainingArguments);
        Assert.Equal(0, calls);
        var results = await router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.AfterStartup);
        Assert.Equal(RuntimeQueryResponseEnvelope.Success(null), Assert.Single(results).Response);
        Assert.Equal(1, calls);
    }
}
