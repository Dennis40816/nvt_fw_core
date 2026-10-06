// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Startup inputs and literal expected outputs in one table class.</summary>
public static class RuntimeQueryStartupCases
{
    /// <summary>Grammar, pass-through, confirmation and validator cases.</summary>
    public static IEnumerable<object[]> Parsing =>
    [
        Case([], [], [], [], [], [], []),
        Case(["--theme", "dark"], ["theme"], ["dark"], [], [], [], ["theme"]),
        Case(["--theme=dark"], ["theme"], ["dark"], [], [], [], ["theme"]),
        Case(["--theme", "a=b=c"], ["theme"], ["a=b=c"], [], [], [], ["theme"]),
        Case(["--theme=a=b=c"], ["theme"], ["a=b=c"], [], [], [], ["theme"]),
        Case(["--theme=="], ["theme"], ["="], [], [], [], ["theme"]),
        Case(["--theme=--dark"], ["theme"], ["--dark"], [], [], [], ["theme"]),
        Case(["--theme", " dark "], ["theme"], [" dark "], [], [], [], ["theme"]),
        Case(["--theme= dark "], ["theme"], [" dark "], [], [], [], ["theme"]),
        Case(["--diagnostics"], ["diagnostics"], [null], [], [], [], ["diagnostics"]),
        Case(["--theme"], ["theme"], [null], [], ["--theme"], ["--theme requires a value."], []),
        Case(["--theme", "--diagnostics"], ["theme", "diagnostics"], [null, null], [],
            ["--theme"], ["--theme requires a value."], ["diagnostics"]),
        Case(["--theme", "--page", "home"], ["theme"], [null], ["--page", "home"],
            ["--theme"], ["--theme requires a value."], []),
        Case(["--diagnostics=yes"], ["diagnostics"], [null], [], ["--diagnostics"],
            ["--diagnostics does not take a value."], []),
        Case(["--diagnostics="], ["diagnostics"], [null], [], ["--diagnostics"],
            ["--diagnostics does not take a value."], []),
        Case(["--diagnostics", "bare"], ["diagnostics"], [null], ["bare"], [], [], ["diagnostics"]),
        Case(["--theme", "dark", "--theme=light"], ["theme", "theme"], ["dark", "light"], [],
            ["--theme"], ["--theme is given more than once."], ["theme"]),
        Case(["--diagnostics", "--diagnostics"], ["diagnostics", "diagnostics"], [null, null], [],
            ["--diagnostics"], ["--diagnostics is given more than once."], ["diagnostics"]),
        Case(["--theme", "--theme=dark"], ["theme", "theme"], [null, "dark"], [],
            ["--theme", "--theme"], ["--theme requires a value.", "--theme is given more than once."], []),
        Case(["--Theme", "dark", "--Theme=light"], [], [], ["--Theme", "dark", "--Theme=light"], [], [], []),
        Case(["bare", "--unknown=x=y", "--probe", "value", "-x", "", "--", "tail"], [], [],
            ["bare", "--unknown=x=y", "--probe", "value", "-x", "", "--", "tail"], [], [], []),
        Case(["--page", "home", "--theme", "dark", "--load-report", "a.json"], ["theme"], ["dark"],
            ["--page", "home", "--load-report", "a.json"], [], [], ["theme"]),
        Case(["--page", "home", "--help"], [], [], ["--page", "home", "--help"], [], [], []),
        Case(["--theme", ""], ["theme"], [null], [], ["--theme"], ["--theme requires a value."], []),
        Case(["--theme", " \t "], ["theme"], [null], [], ["--theme"], ["--theme requires a value."], []),
        Case(["--theme="], ["theme"], [null], [], ["--theme"], ["--theme requires a value."], []),
        Case(["--theme= \t "], ["theme"], [null], [], ["--theme"], ["--theme requires a value."], []),
        Case(["--theme", "", "--diagnostics"], ["theme", "diagnostics"], [null, null], [],
            ["--theme"], ["--theme requires a value."], ["diagnostics"]),
        Case(["--theme=invalid"], ["theme"], ["invalid"], [], ["--theme"],
            ["  Theme value 'invalid' is not supported.\nChoose dark or light.  "], ["theme"]),
        Case(["--theme=dark", "--theme=invalid"], ["theme", "theme"], ["dark", "invalid"], [],
            ["--theme"], ["--theme is given more than once."], ["theme"]),
        Case(["--motion=off", "--theme=dark", "--diagnostics"], ["motion", "theme", "diagnostics"],
            ["off", "dark", null], [], [], [], ["motion", "theme", "diagnostics"]),
        Case(["--save=a.bin", "--export"], ["save", "export"], ["a.bin", null], [],
            ["--save", "--export"], ["--save writes files or changes data. Add --confirm to use it.",
                "--export writes files or changes data. Add --confirm to use it."], ["save", "export"], guard: true),
        Case(["--confirm", "--save=a.bin", "--export"], ["save", "export"], ["a.bin", null], [],
            [], [], ["save", "export"], guard: true),
        Case(["--save", "a.bin", "--export", "--confirm"], ["save", "export"], ["a.bin", null], [],
            [], [], ["save", "export"], guard: true),
        Case(["--save=a.bin", "--confirm"], ["save"], ["a.bin"], ["--confirm"], [], [], ["save"]),
        Case(["--confirm=value", "bare"], [], [], ["--confirm=value", "bare"], [], [], []),
        Case(["--confirm", "bare"], [], [], ["bare"], [], [], [], guard: true),
        Case(["--confirm=true", "--export"], ["export"], [null], [], ["--confirm", "--export"],
            ["--confirm does not take a value.", "--export writes files or changes data. Add --confirm to use it."],
            ["export"], guard: true),
        Case(["--confirm", "--confirm"], [], [], [], ["--confirm"],
            ["--confirm is given more than once."], [], guard: true),
        Case(["--Confirm", "--export"], ["export"], [null], ["--Confirm"], ["--export"],
            ["--export writes files or changes data. Add --confirm to use it."], ["export"], guard: true),
        Case(["--motion=off", "--theme=dark"], ["motion", "theme"], ["off", "dark"], [],
            [], [], ["motion", "theme"], guard: true),
        Case(["--save", "--theme=invalid"], ["save", "theme"], [null, "invalid"], [],
            ["--save", "--save", "--theme"], ["--save requires a value.",
                "--save writes files or changes data. Add --confirm to use it.",
                "  Theme value 'invalid' is not supported.\nChoose dark or light.  "], ["theme"], guard: true),
        Case(["--page", "home", "--theme", "dark", "--help", "--confirm", "--unknown=a=b", "bare"], [], [],
            ["--page", "home", "--theme", "dark", "--help", "--confirm", "--unknown=a=b", "bare"],
            [], [], [], startup: false),
        Case(["--page", "home", "--theme", "dark", "--help", "--confirm", "--unknown=a=b", "bare"], [], [],
            ["--page", "home", "--theme", "dark", "--help", "--confirm", "--unknown=a=b", "bare"],
            [], [], [], guard: true, startup: false)
    ];

    /// <summary>Request error order with startup-only and runtime commands.</summary>
    public static IEnumerable<object[]> Requests =>
    [
        ["null", "INVALID_REQUEST", "Request is null."],
        ["""{"version":"2","command":"motion","args":{"confirm":"invalid"}}""",
            "UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'."],
        ["""{"version":"2","command":"missing"}""",
            "UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'."],
        ["""{"version":"1","command":" MISSING ","args":{"confirm":"invalid"}}""",
            "UNKNOWN_COMMAND", "Unknown query command ' MISSING '."],
        ["""{"version":"1","command":null}""", "UNKNOWN_COMMAND", "Unknown query command ''."],
        ["""{"version":"1","command":" MOTION ","args":{"confirm":"invalid"}}""",
            "STARTUP_ONLY", "Command 'motion' can be used only at startup."],
        ["""{"version":"1","command":"motion","args":{"confirm":"true"}}""",
            "STARTUP_ONLY", "Command 'motion' can be used only at startup."],
        ["""{"version":"1","command":"save"}""", "CONFIRMATION_REQUIRED",
            "Command 'save' writes files or changes data. Add --confirm to run it."]
    ];

    /// <summary>Both startup value forms produce the same handler arguments as runtime requests.</summary>
    public static IEnumerable<object[]> EqualArguments =>
    [
        [new[] { "--theme", "dark" }, "dark", "value"],
        [new[] { "--theme=dark" }, "dark", "value"],
        [new[] { "--theme", "a=b=c" }, "a=b=c", "value"],
        [new[] { "--theme=a=b=c" }, "a=b=c", "value"],
        [new[] { "--theme", " dark " }, " dark ", "setting"],
        [new[] { "--theme= dark " }, " dark ", "setting"]
    ];

    /// <summary>Phase filters and literal execution order, with and without a handler failure.</summary>
    public static IEnumerable<object[]> PhaseRuns =>
    [
        [RuntimeQueryStartupPhase.BeforeFirstFrame, false, new[] { "first", "second", "third" }],
        [RuntimeQueryStartupPhase.BeforeFirstFrame, true, new[] { "first", "second" }],
        [RuntimeQueryStartupPhase.AfterStartup, false, new[] { "after-a", "after-b" }],
        [RuntimeQueryStartupPhase.AfterStartup, true, new[] { "after-a" }],
        [RuntimeQueryStartupPhase.None, false, Array.Empty<string>()]
    ];

    /// <summary>A pending handler blocks the next call until its response arrives.</summary>
    public static IEnumerable<object[]> AwaitedRuns =>
    [
        [new[] { "--first", "--second" }, new[] { "first" }, new[] { "first", "second" }]
    ];

    private static object[] Case(string[] arguments, string[] calls, string?[] values, string[] remaining,
        string[] options, string[] messages, string[] validated, bool guard = false, bool startup = true) =>
        [arguments, calls, values, remaining, options, messages, validated, guard, startup];
}
