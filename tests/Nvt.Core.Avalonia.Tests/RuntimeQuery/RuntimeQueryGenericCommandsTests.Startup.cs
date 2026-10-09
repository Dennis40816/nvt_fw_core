// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.RuntimeQuery;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

public sealed partial class RuntimeQueryGenericCommandsTests
{
    /// <summary>Help lists shared startup commands in registration order without invoking them.</summary>
    [AvaloniaFact]
    public async Task HelpIncludesBeforeFirstFrameAndRuntimeCommand()
    {
        IReadOnlyList<RuntimeQueryCommand> registered = [];
        var generic = RuntimeQueryGenericCommands.Create(Options() with { GetCommands = () => registered });
        registered =
        [
            generic[0],
            new("window-size", RuntimeQueryCommandRisk.ChangesState,
            (_, _, _) => throw new InvalidOperationException("Help must not run commands."),
                RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, "size"),
            generic[1]
        ];
        var router = new RuntimeQueryCommandRouter(registered, requireConfirmation: true);
        AssertSuccess(await router.RouteAsync("help", null, TestContext.Current.CancellationToken),
            """{"commands":[{"name":"help","risk":"ReadOnly"},{"name":"window-size","risk":"ChangesState"},{"name":"ping","risk":"ReadOnly"}]}""");
        Assert.Equal("window-size", Assert.Single(router.ParseStartupArguments(["--window-size=320,240"]).Calls).Command.Name);
    }

    /// <summary>A shared size command sets dimensions before the first layout and resizes the existing window at runtime.</summary>
    [AvaloniaFact]
    public async Task WindowSizeAppliesBeforeFirstLayoutAndAtRuntime()
    {
        var window = new Window { Width = 160, Height = 96 };
        var probe = new StartupLayoutProbe(() => new Size(window.Width, window.Height));
        window.Content = probe;
        var received = new List<RuntimeQueryInvocation>();
        Task<RuntimeQueryResponseEnvelope> ApplySize(RuntimeQueryInvocation invocation, IReadOnlyDictionary<string, string>? args, CancellationToken cancellationToken)
        {
            Assert.True(RuntimeQueryArgumentParser.TryGetIntListArg(args, "size", 1, 8192, out var dimensions, out var error));
            Assert.Null(error);
            Assert.Equal(2, dimensions.Count);
            received.Add(invocation);
            Assert.Equal(invocation == RuntimeQueryInvocation.Runtime, window.IsVisible);
            window.Width = dimensions[0];
            window.Height = dimensions[1];
            if (invocation == RuntimeQueryInvocation.Runtime)
            {
                window.UpdateLayout();
            }
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        }
        var command = new RuntimeQueryCommand("window-size", RuntimeQueryCommandRisk.ChangesState,
            ApplySize,
            RuntimeQueryStartupPhase.BeforeFirstFrameAndRuntime, "size");
        var router = new RuntimeQueryCommandRouter([command], requireConfirmation: true);
        try
        {
            Assert.Null(probe.FirstWindowSize);
            var parsed = router.ParseStartupArguments(["--window-size=320,240"]);
            Assert.Empty(parsed.Issues);
            Assert.True(Assert.Single(await router.ExecuteStartupPhaseAsync(parsed.Calls, RuntimeQueryStartupPhase.BeforeFirstFrame, TestContext.Current.CancellationToken)).Response.Ok);
            Assert.Null(probe.FirstWindowSize);
            Assert.False(window.IsVisible);
            window.Show();
            window.UpdateLayout();
            Assert.Equal(new Size(320, 240), probe.FirstWindowSize);
            Assert.Equal(new Size(320, 240), window.ClientSize);
            Assert.True((await router.RouteAsync("window-size", new Dictionary<string, string> { ["size"] = "640,480" })).Ok);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(new Size(640, 480), window.ClientSize);
            Assert.Equal(new[] { RuntimeQueryInvocation.Startup, RuntimeQueryInvocation.Runtime }, received);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class StartupLayoutProbe(Func<Size> getWindowSize) : Control
    {
        public Size? FirstWindowSize { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            FirstWindowSize ??= getWindowSize();
            return new Size(8, 8);
        }
    }
}
