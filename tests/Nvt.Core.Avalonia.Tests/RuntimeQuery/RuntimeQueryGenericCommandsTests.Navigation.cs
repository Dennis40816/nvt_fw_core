// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

public sealed partial class RuntimeQueryGenericCommandsTests
{
    /// <summary>Page lists the tool's names in its order and reads the current page.</summary>
    [AvaloniaFact]
    public async Task PageListsPagesAndCurrentPage()
    {
        var navigation = new TestNavigation();
        var router = Router(Options(navigation: navigation));
        AssertSuccess(await router.RouteAsync("page", null, TestContext.Current.CancellationToken), """{"pages":["zeta","alpha"],"currentPage":"zeta"}""");
        AssertSuccess(await router.RouteAsync("page", Arg("name", "alpha"), TestContext.Current.CancellationToken), """{"currentPage":"alpha"}""");
        AssertSuccess(await router.RouteAsync("page", null, TestContext.Current.CancellationToken), """{"pages":["zeta","alpha"],"currentPage":"alpha"}""");
        Assert.Equal(1, navigation.SwitchCalls);
    }

    /// <summary>Each switch decision returns exact data or an exact failure without a confirmation flag.</summary>
    [AvaloniaTheory]
    [InlineData(RuntimeQueryPageResult.Switched, null, null)]
    [InlineData(RuntimeQueryPageResult.NeedsConfirmation, "USER_CONFIRMATION_REQUIRED", "Page switching requires confirmation.")]
    [InlineData(RuntimeQueryPageResult.Rejected, "PAGE_REJECTED", "The page switch was rejected.")]
    public async Task PageReturnsToolDecision(RuntimeQueryPageResult result, string? code, string? message)
    {
        var navigation = new TestNavigation { Result = result };
        var response = await Router(Options(navigation: navigation)).RouteAsync("page", Arg("name", "alpha"), TestContext.Current.CancellationToken);
        if (code is null)
        {
            AssertSuccess(response, """{"currentPage":"alpha"}""");
            Assert.Equal("alpha", navigation.CurrentPage);
        }
        else
        {
            AssertFailure(response, code, message!);
            Assert.Equal("zeta", navigation.CurrentPage);
        }
        Assert.Equal(1, navigation.SwitchCalls);
    }

    /// <summary>Switching to the current page succeeds without changing the page.</summary>
    [AvaloniaFact]
    public async Task PageSwitchToCurrentPageSucceeds()
    {
        var navigation = new TestNavigation();
        AssertSuccess(await Router(Options(navigation: navigation)).RouteAsync("page", Arg("name", "zeta"), TestContext.Current.CancellationToken),
            """{"currentPage":"zeta"}""");
        Assert.Equal("zeta", navigation.CurrentPage);
        Assert.Equal(1, navigation.SwitchCalls);
    }

    /// <summary>Unknown names use ordinal comparison and list only tool-supplied pages.</summary>
    [AvaloniaTheory]
    [InlineData("Alpha")]
    [InlineData("settings")]
    [InlineData(" alpha ")]
    public async Task PageRejectsUnknownName(string name)
    {
        var navigation = new TestNavigation();
        AssertFailure(await Router(Options(navigation: navigation)).RouteAsync("page", Arg("name", name), TestContext.Current.CancellationToken),
            "UNKNOWN_PAGE", $"Unknown page '{name}'. Valid pages: zeta, alpha.");
        Assert.Equal("zeta", navigation.CurrentPage);
        Assert.Equal(0, navigation.SwitchCalls);
    }

    /// <summary>A supplied name key requires a nonblank value.</summary>
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task PageRejectsMissingNameValue(string? name)
    {
        var navigation = new TestNavigation();
        AssertFailure(await Router(Options(navigation: navigation)).RouteAsync("page", Arg("name", name!), TestContext.Current.CancellationToken),
            "INVALID_ARGUMENTS", "Argument '--name' requires a page name.");
        Assert.Equal(0, navigation.SwitchCalls);
    }

    /// <summary>An approved exit produces its response before the posted close action runs.</summary>
    [AvaloniaFact]
    public async Task ExitReturnsClosingBeforeCloseRuns()
    {
        var events = new List<string>();
        var options = Options() with
        {
            DecideExit = () => { events.Add("decision"); return RuntimeQueryExitResult.Closing; },
            Close = () => events.Add("close")
        };
        var response = await Router(options).RouteAsync("exit", null, TestContext.Current.CancellationToken);
        AssertSuccess(response, """{"closing":true}""");
        events.Add("response");
        string[] beforeClose = ["decision", "response"];
        Assert.Equal(beforeClose, events);
        Dispatcher.UIThread.RunJobs();
        string[] afterClose = [.. beforeClose, "close"];
        Assert.Equal(afterClose, events);
    }

    /// <summary>Confirmation and rejection return exact failures and never schedule a close.</summary>
    [AvaloniaTheory]
    [InlineData(RuntimeQueryExitResult.NeedsConfirmation, "USER_CONFIRMATION_REQUIRED", "Exit requires confirmation.")]
    [InlineData(RuntimeQueryExitResult.Rejected, "EXIT_REJECTED", "Exit was rejected.")]
    public async Task ExitDoesNotCloseAfterFailure(RuntimeQueryExitResult result, string code, string message)
    {
        var decisions = 0;
        var closes = 0;
        var options = Options() with
        {
            DecideExit = () => { decisions++; return result; },
            Close = () => closes++
        };
        AssertFailure(await Router(options).RouteAsync("exit", null, TestContext.Current.CancellationToken), code, message);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, decisions);
        Assert.Equal(0, closes);
    }
}
