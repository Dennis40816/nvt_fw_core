// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.RuntimeQuery;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

/// <summary>Checks generic commands through a real router with confirmation enabled.</summary>
[Collection("RuntimeQuery")]
public sealed partial class RuntimeQueryGenericCommandsTests
{
    /// <summary>The factory preserves command order and adds no startup options.</summary>
    [AvaloniaFact]
    public void FactoryReturnsSixCommandsWithoutStartupOptions()
    {
        var commands = RuntimeQueryGenericCommands.Create(Options());
        string[] expectedNames = ["help", "ping", "focus", "page", "screenshot", "exit"];
        Assert.Equal(expectedNames, commands.Select(command => command.Name));
        Assert.Equal(new[]
        {
            RuntimeQueryCommandRisk.ReadOnly, RuntimeQueryCommandRisk.ReadOnly,
            RuntimeQueryCommandRisk.ChangesState, RuntimeQueryCommandRisk.ChangesState,
            RuntimeQueryCommandRisk.ChangesState, RuntimeQueryCommandRisk.ChangesState
        }, commands.Select(command => command.Risk));
        Assert.All(commands, command =>
        {
            Assert.Equal(RuntimeQueryStartupPhase.None, command.StartupPhase);
            Assert.Null(command.StartupValueKey);
            Assert.Null(command.StartupValidator);
        });
        var router = new RuntimeQueryCommandRouter(commands, requireConfirmation: true);
        string[] startup = ["--page", "alpha", "--help", "--focus", "--screenshot", "--exit", "--ping"];
        var parsed = router.ParseStartupArguments(startup);
        Assert.Empty(parsed.Calls);
        Assert.Empty(parsed.Issues);
        Assert.Equal(startup, parsed.RemainingArguments);
    }

    /// <summary>Help reads the completed list after registration and preserves order and risk names.</summary>
    [AvaloniaFact]
    public async Task HelpReturnsRegisteredNamesAndRisksInOrder()
    {
        IReadOnlyList<RuntimeQueryCommand>? registered = null;
        var calls = 0;
        var options = Options() with { GetCommands = () => { calls++; return registered!; } };
        var generic = RuntimeQueryGenericCommands.Create(options);
        Assert.Equal(0, calls);
        registered =
        [
            new("zeta", RuntimeQueryCommandRisk.WritesData, _ => Task.FromResult(RuntimeQueryResponseEnvelope.Success(null))),
            generic[0], generic[1], generic[2]
        ];
        var router = new RuntimeQueryCommandRouter(registered, requireConfirmation: true);
        AssertSuccess(await router.RouteAsync("help", null),
            """{"commands":[{"name":"zeta","risk":"WritesData"},{"name":"help","risk":"ReadOnly"},{"name":"ping","risk":"ReadOnly"},{"name":"focus","risk":"ChangesState"}]}""");
        Assert.Equal(1, calls);
    }

    /// <summary>Tool help text, including empty text, passes through unchanged.</summary>
    [AvaloniaTheory]
    [InlineData("  Tool help\r\n--custom <value>\n  ")]
    [InlineData("")]
    public async Task HelpReturnsToolTextUnchanged(string text)
    {
        var options = Options() with
        {
            HelpText = text,
            GetCommands = () => throw new InvalidOperationException("Custom help must not read the command list.")
        };
        var response = await Router(options).RouteAsync("help", null);
        Assert.True(response.Ok);
        Assert.Null(response.Error);
        Assert.Same(text, response.Data);
    }

    /// <summary>Ping returns the supplied identity and the current process ID.</summary>
    [AvaloniaFact]
    public async Task PingReturnsToolIdentityAndProcessId()
    {
        AssertSuccess(await Router(Options()).RouteAsync("ping", null),
            $"{{\"toolName\":\"Synthetic tool\",\"version\":\"2.3-test\",\"processId\":{Environment.ProcessId}}}");
    }

    /// <summary>Focus restores a minimized main window before activating it.</summary>
    [AvaloniaFact]
    public async Task FocusRestoresAndActivatesMainWindow()
    {
        var window = new Window { Width = 160, Height = 96 };
        var other = new Window { Width = 160, Height = 96 };
        try
        {
            window.Show();
            other.Show();
            Dispatcher.UIThread.RunJobs();
            window.WindowState = WindowState.Minimized;
            var activations = 0;
            window.Activated += (_, _) =>
            {
                Assert.Equal(WindowState.Normal, window.WindowState);
                activations++;
            };
            AssertSuccess(await Router(Options(window)).RouteAsync("focus", null), """{"focused":true}""");
            Assert.Equal(WindowState.Normal, window.WindowState);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsActive);
            Assert.Equal(1, activations);
        }
        finally
        {
            other.Close();
            window.Close();
        }
    }

    /// <summary>Focus reports the exact missing-window failure.</summary>
    [AvaloniaFact]
    public async Task FocusWithoutMainWindowReturnsFailure()
    {
        AssertFailure(await Router(Options()).RouteAsync("focus", null),
            "NO_MAIN_WINDOW", "The main window is not available.");
    }

    private static RuntimeQueryGenericCommandOptions Options(Window? window = null, TestNavigation? navigation = null) =>
        new("Synthetic tool", "2.3-test", () => Array.Empty<RuntimeQueryCommand>(), () => window,
            navigation ?? new TestNavigation(), () => RuntimeQueryExitResult.Closing, () => { });

    private static RuntimeQueryCommandRouter Router(RuntimeQueryGenericCommandOptions options) =>
        new(RuntimeQueryGenericCommands.Create(options), requireConfirmation: true);

    private static Dictionary<string, string> Arg(string key, string value) => new(StringComparer.Ordinal) { [key] = value };

    private static void AssertSuccess(RuntimeQueryResponseEnvelope response, string data)
    {
        Assert.True(response.Ok);
        Assert.Null(response.Error);
        Assert.Equal(data, JsonSerializer.Serialize(response.Data, RuntimeQueryProtocol.CompactJsonOptions));
    }

    private static void AssertFailure(RuntimeQueryResponseEnvelope response, string code, string message) =>
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure(code, message), response);

    // The headless UI thread owns these mutable navigation values.
    private sealed class TestNavigation : IRuntimeQueryNavigation
    {
        public IReadOnlyList<string> Pages { get; } = Array.AsReadOnly(new[] { "zeta", "alpha" });
        public string CurrentPage { get; private set; } = "zeta";
        public RuntimeQueryPageResult Result { get; init; } = RuntimeQueryPageResult.Switched;
        public int SwitchCalls { get; private set; }

        public RuntimeQueryPageResult SwitchPage(string name)
        {
            SwitchCalls++;
            if (Result == RuntimeQueryPageResult.Switched)
            {
                CurrentPage = name;
            }
            return Result;
        }
    }
}
