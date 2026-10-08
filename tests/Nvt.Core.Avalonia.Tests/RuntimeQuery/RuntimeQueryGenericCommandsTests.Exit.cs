// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.RuntimeQuery;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

public sealed partial class RuntimeQueryGenericCommandsTests
{
    /// <summary>Exit passes the same confirmation and returns the same response with either router setting.</summary>
    [AvaloniaTheory]
    [InlineData(null, false)]
    [InlineData("--confirm", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    [InlineData(" TrUe ", true)]
    [InlineData("", false)]
    [InlineData(" \t ", false)]
    public async Task ExitRequestPreservesConfirmationAcrossRouterSettings(string? confirm, bool confirmed)
    {
        foreach (var requireConfirmation in new[] { true, false })
        {
            var requests = new List<RuntimeQueryExitRequest>();
            var closes = 0;
            var options = Options() with
            {
                DecideExit = () => throw new InvalidOperationException("The request delegate replaces the legacy delegate."),
                DecideExitRequest = request =>
                {
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    requests.Add(request);
                    return request.Confirmed ? RuntimeQueryExitResult.Closing : RuntimeQueryExitResult.NeedsConfirmation;
                },
                Close = () => closes++
            };
            var router = new RuntimeQueryCommandRouter(RuntimeQueryGenericCommands.Create(options), requireConfirmation);
            var args = confirm switch
            {
                null => null,
                "--confirm" => ParseExitConfirmationFlag(router),
                _ => Arg("confirm", confirm)
            };
            args?.Add("ignored", "unchanged");
            var response = await router.RouteAsync("exit", args, TestContext.Current.CancellationToken);

            Assert.Equal(new RuntimeQueryExitRequest(confirmed), Assert.Single(requests));
            if (confirmed)
            {
                AssertSuccess(response, """{"closing":true}""");
            }
            else
            {
                AssertFailure(response, "USER_CONFIRMATION_REQUIRED", "Exit requires confirmation.");
            }
            Assert.Equal(0, closes);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(confirmed ? 1 : 0, closes);
        }
    }

    /// <summary>Invalid confirmation returns the router's parser error without deciding or scheduling a close.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExitRequestRejectsInvalidConfirmationBeforeCallingDelegate(bool requireConfirmation)
    {
        var closes = 0;
        var options = Options() with
        {
            DecideExit = () => throw new InvalidOperationException("An invalid request must not call the legacy delegate."),
            DecideExitRequest = _ => throw new InvalidOperationException("An invalid request must not call the request delegate."),
            Close = () => closes++
        };
        var args = Arg("confirm", "invalid");
        var router = new RuntimeQueryCommandRouter(RuntimeQueryGenericCommands.Create(options), requireConfirmation);
        var guardedWriter = new RuntimeQueryCommandRouter(
            [new("probe", RuntimeQueryCommandRisk.WritesData, (_, _, _) => throw new InvalidOperationException("The guard must reject invalid confirmation."))],
            requireConfirmation: true);
        var response = await router.RouteAsync("exit", args, TestContext.Current.CancellationToken);

        AssertFailure(response, "INVALID_ARGUMENTS", "Argument '--confirm' must be true/false (or 1/0, on/off).");
        Assert.Equal(await guardedWriter.RouteAsync("probe", args, TestContext.Current.CancellationToken), response);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, closes);
    }

    /// <summary>Every request delegate result keeps the existing response and close behavior.</summary>
    [AvaloniaTheory]
    [InlineData(RuntimeQueryExitResult.Closing, null, null)]
    [InlineData(RuntimeQueryExitResult.NeedsConfirmation, "USER_CONFIRMATION_REQUIRED", "Exit requires confirmation.")]
    [InlineData(RuntimeQueryExitResult.Rejected, "EXIT_REJECTED", "Exit was rejected.")]
    public async Task ExitRequestReturnsToolDecision(RuntimeQueryExitResult result, string? code, string? message)
    {
        foreach (var requireConfirmation in new[] { true, false })
        {
            var decisions = 0;
            var closes = 0;
            var options = Options() with
            {
                DecideExitRequest = request =>
                {
                    Assert.True(request.Confirmed);
                    decisions++;
                    return result;
                },
                Close = () => closes++
            };
            var router = new RuntimeQueryCommandRouter(RuntimeQueryGenericCommands.Create(options), requireConfirmation);
            var response = await router.RouteAsync("exit", Arg("confirm", "true"), TestContext.Current.CancellationToken);

            AssertExitDecision(response, code, message);
            Assert.Equal(1, decisions);
            Assert.Equal(0, closes);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(result == RuntimeQueryExitResult.Closing ? 1 : 0, closes);
        }
    }

    /// <summary>The legacy delegate ignores missing, valid, false and invalid confirmation with either router setting.</summary>
    [AvaloniaTheory]
    [InlineData(RuntimeQueryExitResult.Closing, null, null)]
    [InlineData(RuntimeQueryExitResult.NeedsConfirmation, "USER_CONFIRMATION_REQUIRED", "Exit requires confirmation.")]
    [InlineData(RuntimeQueryExitResult.Rejected, "EXIT_REJECTED", "Exit was rejected.")]
    public async Task ExitLegacyDecisionIgnoresConfirmationArguments(RuntimeQueryExitResult result, string? code, string? message)
    {
        foreach (var requireConfirmation in new[] { true, false })
        {
            foreach (var confirm in new[] { null, "--confirm", "true", "false", "invalid" })
            {
                var decisions = 0;
                var closes = 0;
                var options = Options() with
                {
                    DecideExit = () => { decisions++; return result; },
                    Close = () => closes++
                };
                Assert.Null(options.DecideExitRequest);
                var router = new RuntimeQueryCommandRouter(RuntimeQueryGenericCommands.Create(options), requireConfirmation);
                var args = confirm switch
                {
                    null => null,
                    "--confirm" => ParseExitConfirmationFlag(router),
                    _ => Arg("confirm", confirm)
                };
                var response = await router.RouteAsync("exit", args, TestContext.Current.CancellationToken);

                AssertExitDecision(response, code, message);
                Assert.Equal(1, decisions);
                Assert.Equal(0, closes);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(result == RuntimeQueryExitResult.Closing ? 1 : 0, closes);
            }
        }
    }

    /// <summary>An approved request returns its response before Close runs on the UI thread.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExitRequestReturnsResponseBeforeCloseRuns(bool requireConfirmation)
    {
        var events = new List<string>();
        string[] beforeClose = ["decision", "response"];
        var options = Options() with
        {
            DecideExitRequest = request =>
            {
                Assert.True(request.Confirmed);
                events.Add("decision");
                return RuntimeQueryExitResult.Closing;
            },
            Close = () =>
            {
                Assert.True(Dispatcher.UIThread.CheckAccess());
                Assert.Equal(beforeClose, events);
                events.Add("close");
            }
        };
        var router = new RuntimeQueryCommandRouter(RuntimeQueryGenericCommands.Create(options), requireConfirmation);
        var response = await router.RouteAsync("exit", Arg("confirm", "true"), TestContext.Current.CancellationToken);

        AssertSuccess(response, """{"closing":true}""");
        events.Add("response");
        Assert.Equal(beforeClose, events);
        Dispatcher.UIThread.RunJobs();
        string[] afterClose = [.. beforeClose, "close"];
        Assert.Equal(afterClose, events);
    }

    private static void AssertExitDecision(RuntimeQueryResponseEnvelope response, string? code, string? message)
    {
        if (code is null)
        {
            AssertSuccess(response, """{"closing":true}""");
        }
        else
        {
            AssertFailure(response, code, message!);
        }
    }

    private static Dictionary<string, string> ParseExitConfirmationFlag(RuntimeQueryCommandRouter router)
    {
        var parser = typeof(RuntimeQueryCommandLine).GetMethod("TryParseCommandLine", BindingFlags.Static | BindingFlags.NonPublic)!;
        string[] commandLine = ["query", "exit", "--confirm"];
        object?[] parameters = [commandLine, router.RegisteredCommands, null, null, null, null, null];
        Assert.True((bool)parser.Invoke(null, parameters)!);
        Assert.Null(parameters[6]);
        Assert.Equal("exit", parameters[2]);
        var args = Assert.IsType<Dictionary<string, string>>(parameters[3]);
        Assert.Equal(new KeyValuePair<string, string>("confirm", "true"), Assert.Single(args));
        return args;
    }
}
