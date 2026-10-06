// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks startup parsing without executing handlers.</summary>
public sealed class RuntimeQueryStartupParserTests
{
    /// <summary>Every row compares exact calls, arguments, remaining tokens, issues and validator calls.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryStartupCases.Parsing), MemberType = typeof(RuntimeQueryStartupCases))]
    public void ParseStartupArgumentsMatchesExactRows(string[] arguments, string[] names, string?[] values,
        string[] remaining, string[] options, string[] messages, string[] validated, bool guard, bool startup)
    {
        var original = arguments.ToArray();
        var validations = new List<(string Name, IReadOnlyDictionary<string, string>? Args)>();
        var handlerCalls = 0;
        RuntimeQueryCommand Command(string name, RuntimeQueryCommandRisk risk, RuntimeQueryStartupPhase phase, string? key)
        {
            return new(name, risk, _ =>
            {
                handlerCalls++;
                return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
            }, startup ? phase : RuntimeQueryStartupPhase.None, key, args =>
            {
                validations.Add((name, args));
                return name == "theme" && args?["value"] == "invalid"
                    ? RuntimeQueryResponseEnvelope.Failure("INVALID_THEME",
                        "  Theme value 'invalid' is not supported.\nChoose dark or light.  ")
                    : null;
            });
        }

        RuntimeQueryCommand[] commands =
        [
            Command("theme", RuntimeQueryCommandRisk.ChangesState, RuntimeQueryStartupPhase.AfterStartup, "value"),
            Command("motion", RuntimeQueryCommandRisk.ChangesState, RuntimeQueryStartupPhase.BeforeFirstFrame, "value"),
            Command("diagnostics", RuntimeQueryCommandRisk.ReadOnly, RuntimeQueryStartupPhase.AfterStartup, null),
            Command("save", RuntimeQueryCommandRisk.WritesData, RuntimeQueryStartupPhase.AfterStartup, "path"),
            Command("export", RuntimeQueryCommandRisk.WritesData, RuntimeQueryStartupPhase.AfterStartup, null),
            Command("page", RuntimeQueryCommandRisk.ChangesState, RuntimeQueryStartupPhase.None, null),
            Command("help", RuntimeQueryCommandRisk.ReadOnly, RuntimeQueryStartupPhase.None, null)
        ];
        var router = new RuntimeQueryCommandRouter(commands, guard);
        var previous = CultureInfo.CurrentCulture;
        RuntimeQueryStartupParseResult result;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            result = router.ParseStartupArguments(arguments);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal(names, result.Calls.Select(call => call.Command.Name));
        Assert.Equal(values.Length, result.Calls.Count);
        for (var index = 0; index < result.Calls.Count; index++)
        {
            var call = result.Calls[index];
            Assert.Same(commands.Single(command => command.Name == names[index]), call.Command);
            Assert.Equal(call.Command.StartupPhase, call.Phase);
            if (values[index] is null)
            {
                Assert.Null(call.Args);
            }
            else
            {
                Assert.NotNull(call.Args);
                Assert.Equal(new[] { new KeyValuePair<string, string>(call.Command.StartupValueKey!, values[index]!) }, call.Args);
            }
        }

        Assert.Equal(remaining, result.RemainingArguments);
        Assert.Equal(options, result.Issues.Select(issue => issue.Option));
        Assert.Equal(messages, result.Issues.Select(issue => issue.Message));
        Assert.Equal(validated, validations.Select(validation => validation.Name));
        foreach (var validation in validations)
        {
            Assert.Same(result.Calls.First(call => call.Command.Name == validation.Name).Args, validation.Args);
        }

        Assert.Equal(0, handlerCalls);
        Assert.Equal(original, arguments);
    }

    /// <summary>The dictionary constructor and an empty command list pass every token through.</summary>
    [Fact]
    public void RoutersWithoutStartupDefinitionsPassEveryArgumentThrough()
    {
        string[] arguments = ["--page", "home", "--help", "--confirm", "--theme=dark", "bare", ""];
        RuntimeQueryCommandRouter[] routers =
        [
            new(new Dictionary<string, Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>>
            {
                ["theme"] = _ => throw new InvalidOperationException("The handler must not run.")
            }),
            new(Array.Empty<RuntimeQueryCommand>(), requireConfirmation: false),
            new(Array.Empty<RuntimeQueryCommand>(), requireConfirmation: true)
        ];
        foreach (var router in routers)
        {
            var result = router.ParseStartupArguments(arguments);
            Assert.Empty(result.Calls);
            Assert.Empty(result.Issues);
            Assert.Equal(arguments, result.RemainingArguments);
        }
    }
}
