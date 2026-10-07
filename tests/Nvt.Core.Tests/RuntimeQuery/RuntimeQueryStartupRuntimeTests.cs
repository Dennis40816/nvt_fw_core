// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks shared startup and runtime commands and invocation timing.</summary>
public sealed class RuntimeQueryStartupRuntimeTests
{
    /// <summary>The new phase preserves the numeric values of existing phases.</summary>
    [Fact]
    public void StartupPhaseAppendsWithoutChangingExistingValues()
    {
        Assert.Equal(0, (int)RuntimeQueryStartupPhase.None);
        Assert.Equal(1, (int)RuntimeQueryStartupPhase.BeforeFirstFrame);
        Assert.Equal(2, (int)RuntimeQueryStartupPhase.AfterStartup);
        Assert.Equal(3, (int)RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime);
    }

    /// <summary>Both early phase values run mixed commands in order and stop after failure.</summary>
    [Theory]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrame, false)]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrame, true)]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, false)]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, true)]
    public async Task BeforeFirstFramePassRunsMixedCommandsInOrderAndStopsOnFailure(
        RuntimeQueryStartupPhase pass, bool fail)
    {
        var executed = new List<string>();
        RuntimeQueryCommand Early(string name) => new(name, RuntimeQueryCommandRisk.ReadOnly, _ =>
        {
            executed.Add(name);
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        }, RuntimeQueryStartupPhase.BeforeFirstFrame);
        var shared = new RuntimeQueryCommand("window-size", RuntimeQueryCommandRisk.ChangesState,
            _ => throw new InvalidOperationException("The fallback must not run."),
            RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime)
        {
            InvocationHandler = (invocation, _) =>
            {
                Assert.Equal(RuntimeQueryInvocation.Startup, invocation);
                executed.Add("window-size");
                return Task.FromResult(fail
                    ? RuntimeQueryResponseEnvelope.Failure("STOP", "Command failed.")
                    : RuntimeQueryResponseEnvelope.Success(null));
            }
        };
        var router = new RuntimeQueryCommandRouter(
        [
            Early("first"), shared, Early("last"),
            new("after", RuntimeQueryCommandRisk.ReadOnly, _ => throw new InvalidOperationException("Wrong pass."),
                RuntimeQueryStartupPhase.AfterStartup),
            new("runtime", RuntimeQueryCommandRisk.ReadOnly, _ => throw new InvalidOperationException("Runtime only."))
        ], requireConfirmation: false);
        var parsed = router.ParseStartupArguments(["--first", "--after", "--window-size", "--runtime", "--last"]);
        Assert.Empty(parsed.Issues);
        Assert.Equal("--runtime", Assert.Single(parsed.RemainingArguments));
        string[] registrationNames = ["first", "window-size", "last", "after", "runtime"];
        Assert.Equal(registrationNames, router.RegisteredCommands);
        var results = await router.ExecuteStartupPhaseAsync(parsed.Calls, pass);
        string[] expected = fail ? ["first", "window-size"] : ["first", "window-size", "last"];
        Assert.Equal(expected, executed);
        Assert.Equal(expected, results.Select(result => result.Call.Command.Name));
        Assert.Equal(!fail, results[^1].Response.Ok);
    }

    /// <summary>The mixed pass keeps command-line order when registration order differs.</summary>
    [Fact]
    public async Task BeforeFirstFramePassPreservesCommandLineOrder()
    {
        var executed = new List<string>();
        RuntimeQueryCommand Command(string name, RuntimeQueryStartupPhase phase) => new(name,
            RuntimeQueryCommandRisk.ReadOnly, _ =>
            {
                executed.Add(name);
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
            }, phase);
        var router = new RuntimeQueryCommandRouter(
        [
            Command("first", RuntimeQueryStartupPhase.BeforeFirstFrame),
            Command("shared", RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime),
            Command("last", RuntimeQueryStartupPhase.BeforeFirstFrame)
        ], requireConfirmation: false);
        var parsed = router.ParseStartupArguments(["--last", "--shared", "--first"]);
        await router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.BeforeFirstFrame);
        string[] expected = ["last", "shared", "first"];
        Assert.Equal(expected, executed);
    }

    /// <summary>The timing-aware handler receives validated startup values and guarded runtime arguments.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task BothEntriesDeliverTimingAndRespectConfirmation(
        bool guard, bool receivesConfirmation, bool equalsForm)
    {
        var received = new List<(RuntimeQueryInvocation Invocation, IReadOnlyDictionary<string, string>? Args)>();
        var validations = 0;
        var command = new RuntimeQueryCommand("window-size", RuntimeQueryCommandRisk.WritesData,
            _ => throw new InvalidOperationException("The fallback must not run."),
            RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, "dimensions", args =>
            {
                validations++;
                Assert.Equal("800,600", Assert.Single(args!).Value);
                Assert.Equal("dimensions", Assert.Single(args!).Key);
                return null;
            })
        {
            ReceivesConfirmation = receivesConfirmation,
            InvocationHandler = (invocation, args) =>
            {
                received.Add((invocation, args));
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(args!["dimensions"]));
            }
        };
        var router = new RuntimeQueryCommandRouter([command], guard);
        string[] arguments = equalsForm ? ["--window-size=800,600"] : ["--window-size", "800,600"];
        var parsed = router.ParseStartupArguments(guard ? [.. arguments, "--confirm"] : arguments);
        Assert.Empty(parsed.Issues);
        Assert.Empty(parsed.RemainingArguments);
        Assert.Equal(RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, Assert.Single(parsed.Calls).Phase);
        Assert.Equal(1, validations);
        Assert.Empty(received);
        var startup = Assert.Single(await router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.BeforeFirstFrame));
        var runtimeArgs = new Dictionary<string, string> { ["dimensions"] = "800,600", ["confirm"] = "true" };
        var runtime = await router.ExecuteAsync(new("1", " WINDOW-SIZE ", runtimeArgs), "1");
        Assert.Equal(startup.Response, runtime);
        Assert.Equal(new[] { RuntimeQueryInvocation.Startup, RuntimeQueryInvocation.Runtime },
            received.Select(call => call.Invocation));
        Assert.Equal(new KeyValuePair<string, string>("dimensions", "800,600"), Assert.Single(received[0].Args!));
        Assert.Equal("800,600", received[1].Args!["dimensions"]);
        Assert.Equal(!guard || receivesConfirmation, received[1].Args!.ContainsKey("confirm"));
        if (!guard || receivesConfirmation)
        {
            Assert.Same(runtimeArgs, received[1].Args);
        }
        Assert.Equal(1, validations);
    }

    /// <summary>Unconfirmed startup writes and missing, false or invalid runtime confirmation block both handlers.</summary>
    [Theory]
    [InlineData(null, "CONFIRMATION_REQUIRED")]
    [InlineData("false", "CONFIRMATION_REQUIRED")]
    [InlineData("invalid", "INVALID_ARGUMENTS")]
    public async Task BothEntriesRejectUnconfirmedWritesBeforeInvokingHandlers(string? confirmation, string code)
    {
        var command = new RuntimeQueryCommand("save", RuntimeQueryCommandRisk.WritesData,
            _ => throw new InvalidOperationException("The fallback must not run."),
            RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime)
        {
            InvocationHandler = (_, _) => throw new InvalidOperationException("The timing-aware handler must not run.")
        };
        var router = new RuntimeQueryCommandRouter([command], requireConfirmation: true);
        var parsed = router.ParseStartupArguments(["--save"]);
        Assert.Equal("--save writes files or changes data. Add --confirm to use it.", Assert.Single(parsed.Issues).Message);
        var startup = Assert.Single(await router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.BeforeFirstFrame));
        Assert.Equal("CONFIRMATION_REQUIRED", startup.Response.Error!.Code);
        var args = confirmation is null ? null : new Dictionary<string, string> { ["confirm"] = confirmation };
        Assert.Equal(code, (await router.RouteAsync("save", args)).Error!.Code);
    }

    /// <summary>Grammar issues skip validators and validator failures keep their exact message.</summary>
    [Theory]
    [InlineData("--window-size=", 0, "--window-size requires a value.")]
    [InlineData("--window-size=invalid", 1, "  Unsupported size.\nChoose positive dimensions.  ")]
    public void StartupParserKeepsGrammarAndValidatorRules(string argument, int expectedValidations, string message)
    {
        var validations = 0;
        var router = new RuntimeQueryCommandRouter(
        [
            new("window-size", RuntimeQueryCommandRisk.ChangesState,
                _ => throw new InvalidOperationException("Parsing must not invoke handlers."),
                RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, "size", _ =>
                {
                    validations++;
                    return RuntimeQueryResponseEnvelope.Failure("INVALID_ARGUMENTS", "  Unsupported size.\nChoose positive dimensions.  ");
                })
        ], requireConfirmation: false);
        var parsed = router.ParseStartupArguments([argument]);
        Assert.Equal(message, Assert.Single(parsed.Issues).Message);
        Assert.Equal(expectedValidations, validations);
        Assert.Single(parsed.Calls);
        Assert.Empty(parsed.RemainingArguments);
    }

    /// <summary>Invocation timing preserves startup eligibility and runtime behavior for every phase.</summary>
    [Theory]
    [InlineData(RuntimeQueryStartupPhase.None, RuntimeQueryStartupPhase.None, true)]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrame, RuntimeQueryStartupPhase.BeforeFirstFrame, false)]
    [InlineData(RuntimeQueryStartupPhase.AfterStartup, RuntimeQueryStartupPhase.AfterStartup, true)]
    [InlineData(RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, RuntimeQueryStartupPhase.BeforeFirstFrame, true)]
    public async Task InvocationHandlerKeepsExistingPhaseBehavior(
        RuntimeQueryStartupPhase phase, RuntimeQueryStartupPhase pass, bool runtimeAllowed)
    {
        var received = new List<RuntimeQueryInvocation>();
        var command = new RuntimeQueryCommand("flag", RuntimeQueryCommandRisk.ReadOnly,
            _ => throw new InvalidOperationException("The fallback must not run."), phase)
        {
            InvocationHandler = (invocation, args) =>
            {
                Assert.Null(args);
                received.Add(invocation);
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
            }
        };
        var router = new RuntimeQueryCommandRouter([command], requireConfirmation: true);
        var parsed = router.ParseStartupArguments(["--flag"]);
        Assert.Empty(parsed.Issues);
        var results = await router.ExecuteStartupPhaseAsync(parsed.Calls, pass);
        if (phase == RuntimeQueryStartupPhase.None)
        {
            Assert.Empty(results);
            Assert.Equal("--flag", Assert.Single(parsed.RemainingArguments));
        }
        else
        {
            Assert.True(Assert.Single(results).Response.Ok);
            Assert.Equal(RuntimeQueryInvocation.Startup, Assert.Single(received));
        }
        var runtime = await router.RouteAsync("flag", null);
        Assert.Equal(runtimeAllowed, runtime.Ok);
        if (runtimeAllowed)
        {
            Assert.Equal(RuntimeQueryInvocation.Runtime, received[^1]);
        }
        else
        {
            Assert.Equal("STARTUP_ONLY", runtime.Error!.Code);
            Assert.Single(received);
        }
    }

    /// <summary>Commands using the legacy handler also run through both entries with the new phase.</summary>
    [Fact]
    public async Task NewPhaseFallsBackToLegacyHandler()
    {
        var calls = 0;
        var router = new RuntimeQueryCommandRouter(
        [
            new("flag", RuntimeQueryCommandRisk.ReadOnly, args =>
            {
                Assert.Null(args);
                calls++;
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
            }, RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime)
        ], requireConfirmation: false);
        var parsed = router.ParseStartupArguments(["--flag"]);
        Assert.True(Assert.Single(await router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.BeforeFirstFrame)).Response.Ok);
        Assert.True((await router.RouteAsync("flag", null)).Ok);
        Assert.Equal(2, calls);
    }

    /// <summary>A pending timing-aware handler blocks later before-first-frame calls.</summary>
    [Fact]
    public async Task BeforeFirstFramePassAwaitsInvocationHandler()
    {
        var release = new TaskCompletionSource<RuntimeQueryResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterCalled = false;
        var shared = new RuntimeQueryCommand("shared", RuntimeQueryCommandRisk.ReadOnly,
            _ => throw new InvalidOperationException("The fallback must not run."),
            RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime)
        {
            InvocationHandler = (invocation, _) =>
            {
                Assert.Equal(RuntimeQueryInvocation.Startup, invocation);
                return release.Task;
            }
        };
        var router = new RuntimeQueryCommandRouter(
        [
            shared,
            new("later", RuntimeQueryCommandRisk.ReadOnly, _ =>
            {
                laterCalled = true;
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
            }, RuntimeQueryStartupPhase.BeforeFirstFrame)
        ], requireConfirmation: false);
        var parsed = router.ParseStartupArguments(["--shared", "--later"]);
        var execution = router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.BeforeFirstFrame);
        Assert.False(execution.IsCompleted);
        Assert.False(laterCalled);
        release.SetResult(RuntimeQueryResponseEnvelope.Success(null));
        Assert.Equal(2, (await execution.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)).Count);
        Assert.True(laterCalled);
    }
}
