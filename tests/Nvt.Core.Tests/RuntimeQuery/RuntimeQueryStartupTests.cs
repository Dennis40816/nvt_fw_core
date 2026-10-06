// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks startup metadata, runtime rejection and startup phase execution.</summary>
public sealed class RuntimeQueryStartupTests
{
    /// <summary>Existing three-member registrations have no startup entry or validator.</summary>
    [Fact]
    public void ExistingRegistrationsKeepStartupDefaults()
    {
        var command = new RuntimeQueryCommand("theme", RuntimeQueryCommandRisk.ChangesState,
            _ => Task.FromResult(RuntimeQueryResponseEnvelope.Success(null)));
        Assert.Equal(RuntimeQueryStartupPhase.None, command.StartupPhase);
        Assert.Null(command.StartupValueKey);
        Assert.Null(command.StartupValidator);
    }

    /// <summary>Runtime requests check null, version, unknown name, startup-only status, then confirmation.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryStartupCases.Requests), MemberType = typeof(RuntimeQueryStartupCases))]
    public async Task RuntimeEntryKeepsExactErrorOrder(string json, string code, string message)
    {
        var router = new RuntimeQueryCommandRouter(
        [
            new("motion", RuntimeQueryCommandRisk.WritesData,
                _ => throw new InvalidOperationException("The handler must not run."), RuntimeQueryStartupPhase.BeforeFirstFrame),
            new("save", RuntimeQueryCommandRisk.WritesData,
                _ => throw new InvalidOperationException("The handler must not run."), RuntimeQueryStartupPhase.AfterStartup)
        ], requireConfirmation: true);
        var request = JsonSerializer.Deserialize<RuntimeQueryRequest>(json, RuntimeQueryProtocol.CompactJsonOptions);
        var expected = RuntimeQueryResponseEnvelope.Failure(code, message);
        Assert.Equal(expected, await router.ExecuteAsync(request, "1"));
        if (request?.Version == "1")
        {
            Assert.Equal(expected, await router.RouteAsync(request.Command, request.Args));
        }
    }

    /// <summary>Startup-only rejection works for every risk level with the confirmation guard on or off.</summary>
    [Fact]
    public async Task RuntimeEntryRejectsStartupOnlyBeforeConfirmationForEveryRisk()
    {
        foreach (var risk in Enum.GetValues<RuntimeQueryCommandRisk>())
        {
            foreach (var guard in new[] { false, true })
            {
                var router = new RuntimeQueryCommandRouter([new("motion", risk,
                    _ => throw new InvalidOperationException("The handler must not run."), RuntimeQueryStartupPhase.BeforeFirstFrame)], guard);
                var args = new Dictionary<string, string> { ["confirm"] = "invalid" };
                Assert.Equal(RuntimeQueryResponseEnvelope.Failure("STARTUP_ONLY", "Command 'motion' can be used only at startup."),
                    await router.RouteAsync(" MOTION ", args));
            }
        }
    }

    /// <summary>Startup and runtime handlers receive equal dictionaries and use the same argument helper.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryStartupCases.EqualArguments), MemberType = typeof(RuntimeQueryStartupCases))]
    public async Task BothEntriesPassEqualArguments(string[] arguments, string value, string key)
    {
        foreach (var guard in new[] { false, true })
        {
            var received = new List<IReadOnlyDictionary<string, string>?>();
            var validations = 0;
            var command = new RuntimeQueryCommand("theme", RuntimeQueryCommandRisk.WritesData, args =>
            {
                received.Add(args);
                Assert.True(RuntimeQueryArgumentParser.TryGetStringArg(args, key, out var parsed, out var error));
                Assert.Null(error);
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(parsed));
            }, RuntimeQueryStartupPhase.AfterStartup, key, args =>
            {
                validations++;
                Assert.True(RuntimeQueryArgumentParser.TryGetStringArg(args, key, out _, out var error));
                return error;
            });
            var router = new RuntimeQueryCommandRouter([command], guard);
            var startupArguments = guard ? arguments.Append("--confirm").ToArray() : arguments;
            var parsed = router.ParseStartupArguments(startupArguments);
            Assert.Empty(parsed.Issues);
            Assert.Empty(received);
            Assert.Equal(1, validations);
            var results = await router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.AfterStartup);
            var runtimeArgs = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value };
            if (guard)
            {
                runtimeArgs.Add("confirm", "true");
            }

            var runtimeResponse = await router.ExecuteAsync(new RuntimeQueryRequest("1", "theme", runtimeArgs), "1");
            Assert.Equal(2, received.Count);
            Assert.Equal(new[] { new KeyValuePair<string, string>(key, value) }, received[0]);
            Assert.Equal(received[0]!.ToArray(), received[1]!.ToArray());
            Assert.Equal(runtimeResponse, Assert.Single(results).Response);
            Assert.Equal(1, validations);
        }
    }

    /// <summary>Flag handlers receive null through both entries, and parsing does not invoke them.</summary>
    [Fact]
    public async Task BothFlagEntriesReceiveNoArguments()
    {
        var calls = 0;
        var router = new RuntimeQueryCommandRouter([new("diagnostics", RuntimeQueryCommandRisk.ReadOnly, args =>
        {
            calls++;
            Assert.Null(args);
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        }, RuntimeQueryStartupPhase.AfterStartup)], requireConfirmation: true);
        var parsed = router.ParseStartupArguments(["--diagnostics", "--confirm"]);
        Assert.Empty(parsed.Issues);
        Assert.Equal(0, calls);
        Assert.True(Assert.Single(await router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.AfterStartup)).Response.Ok);
        Assert.True((await router.RouteAsync("diagnostics", null)).Ok);
        Assert.Equal(2, calls);
    }

    /// <summary>Phase execution preserves command-line order, filters phases and includes the first failure.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryStartupCases.PhaseRuns), MemberType = typeof(RuntimeQueryStartupCases))]
    public async Task PhaseRunsFilterOrderAndStopAtFailure(RuntimeQueryStartupPhase phase, bool fail, string[] expectedNames)
    {
        var executed = new List<string>();
        var responses = new Dictionary<string, RuntimeQueryResponseEnvelope>();
        RuntimeQueryCommand Command(string name, RuntimeQueryStartupPhase startupPhase)
        {
            responses[name] = fail && name is "second" or "after-a"
                ? RuntimeQueryResponseEnvelope.Failure("STOP", "Command failed.")
                : RuntimeQueryResponseEnvelope.Success(name);
            return new(name, RuntimeQueryCommandRisk.WritesData, async args =>
            {
                Assert.Null(args);
                await Task.Yield();
                executed.Add(name);
                return responses[name];
            }, startupPhase);
        }

        var router = new RuntimeQueryCommandRouter(
        [
            Command("third", RuntimeQueryStartupPhase.BeforeFirstFrame),
            Command("second", RuntimeQueryStartupPhase.BeforeFirstFrame),
            Command("after-b", RuntimeQueryStartupPhase.AfterStartup),
            Command("first", RuntimeQueryStartupPhase.BeforeFirstFrame),
            Command("after-a", RuntimeQueryStartupPhase.AfterStartup)
        ], requireConfirmation: true);
        var parsed = router.ParseStartupArguments(["--after-a", "--first", "--after-b", "--second", "--third", "--confirm"]);
        Assert.Empty(parsed.Issues);
        Assert.Empty(executed);
        var results = await router.ExecuteStartupPhaseAsync(parsed.Calls, phase);
        Assert.Equal(expectedNames, executed);
        Assert.Equal(expectedNames, results.Select(result => result.Call.Command.Name));
        foreach (var result in results)
        {
            Assert.Same(parsed.Calls.Single(call => call.Command.Name == result.Call.Command.Name), result.Call);
            Assert.Same(responses[result.Call.Command.Name], result.Response);
        }
    }

    /// <summary>The router waits for a pending handler before starting the next call.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryStartupCases.AwaitedRuns), MemberType = typeof(RuntimeQueryStartupCases))]
    public async Task PhaseRunAwaitsEachHandler(string[] arguments, string[] pendingNames, string[] completedNames)
    {
        var executed = new List<string>();
        var release = new TaskCompletionSource<RuntimeQueryResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var router = new RuntimeQueryCommandRouter(
        [
            new("first", RuntimeQueryCommandRisk.ReadOnly, _ => { executed.Add("first"); return release.Task; },
                RuntimeQueryStartupPhase.AfterStartup),
            new("second", RuntimeQueryCommandRisk.ReadOnly, _ =>
            {
                executed.Add("second");
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
            }, RuntimeQueryStartupPhase.AfterStartup)
        ], requireConfirmation: false);
        var parsed = router.ParseStartupArguments(arguments);
        var task = router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.AfterStartup);
        Assert.False(task.IsCompleted);
        Assert.Equal(pendingNames, executed);
        release.SetResult(RuntimeQueryResponseEnvelope.Success(null));
        Assert.Equal(2, (await task).Count);
        Assert.Equal(completedNames, executed);
    }

    /// <summary>A caller cannot run an unconfirmed data write through the startup phase method.</summary>
    [Fact]
    public async Task PhaseRunRetainsConfirmationGuard()
    {
        var router = new RuntimeQueryCommandRouter([new("save", RuntimeQueryCommandRisk.WritesData,
            _ => throw new InvalidOperationException("The handler must not run."), RuntimeQueryStartupPhase.BeforeFirstFrame)],
            requireConfirmation: true);
        var parsed = router.ParseStartupArguments(["--save"]);
        Assert.Single(parsed.Issues);
        var result = Assert.Single(await router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.BeforeFirstFrame));
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("CONFIRMATION_REQUIRED",
            "Command 'save' writes files or changes data. Add --confirm to run it."), result.Response);
    }
}
