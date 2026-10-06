// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Frozen command-line inputs and literal expectations, reusable by the source tool.</summary>
public static class RuntimeQueryCommandLineCases
{
    /// <summary>The product pipe name, used only by cases that cannot connect.</summary>
    public const string PipeName = "freeformhelper.runtime.v1";
    /// <summary>The frozen protocol version.</summary>
    public const string Version = "1";
    /// <summary>The synthetic success response.</summary>
    public const string SuccessReply = "{\"ok\":true,\"data\":{\"value\":7},\"error\":null}";
    /// <summary>The synthetic failure response.</summary>
    public const string FailedReply = "{\"ok\":false,\"data\":null,\"error\":{\"code\":\"TEST_FAILURE\",\"message\":\"Rejected.\"}}";

    /// <summary>The source tool's 19 commands in their frozen order.</summary>
    public static IReadOnlyList<string> SupportedCommands { get; } = new[]
    {
        "help", "status", "selection", "terminal", "terminal-links", "pad", "notch", "multi-owner",
        "notch-stage", "notch-validation", "load-project", "run-step", "clear-step", "select-cad",
        "select-regular", "clear-selection", "set-tofull", "simulation", "export-notch"
    };

    private const string SupportedCommandText =
        "help, status, selection, terminal, terminal-links, pad, notch, multi-owner, notch-stage, notch-validation, " +
        "load-project, run-step, clear-step, select-cad, select-regular, clear-selection, set-tofull, simulation, export-notch";
    private const string UnexpectedToken = "Unexpected token 'bare'. Options must start with '--'.";
    private const string TimeoutError = "--timeout-ms must be in [1, 120000].";

    /// <summary>Invalid inputs and exact pretty failure output.</summary>
    public static TheoryData<string[], string> ParseErrors => new()
    {
        { ["query", "help", "bare"], InvalidArguments(UnexpectedToken) },
        { ["query", "help", "--json-compact", "bare"], InvalidArguments(UnexpectedToken) },
        { ["query", " MISSING "], InvalidArguments("Unsupported query command 'missing'. Supported: " + SupportedCommandText + ".") },
        { ["query"], InvalidArguments("Usage: query <" + SupportedCommandText.Replace(", ", "|", StringComparison.Ordinal) + "> [--key value]") },
        { ["query", "help", "--timeout-ms", "0"], InvalidArguments(TimeoutError) },
        { ["query", "help", "--timeout-ms=-1"], InvalidArguments(TimeoutError) },
        { ["query", "help", "--timeout-ms=120001"], InvalidArguments(TimeoutError) },
        { ["query", "help", "--timeout-ms"], InvalidArguments(TimeoutError) },
        { ["query", "help", "--timeout-ms", "--json-compact"], InvalidArguments(TimeoutError) },
        { ["query", "help", "--timeout-ms=abc"], InvalidArguments(TimeoutError) },
        // An empty command is distinct from the frozen query-only usage case.
        { ["query", ""], InvalidArguments("Unsupported query command ''. Supported: " + SupportedCommandText + ".") }
    };

    /// <summary>Option inputs, exact request frames and exact stdout.</summary>
    public static TheoryData<string[], string, string> OptionForms => new()
    {
        { ["query", " HELP ", "--key", "value"], Request("help", "{\"key\":\"value\"}"), SuccessJson },
        { ["query", "help", "--key=value"], Request("help", "{\"key\":\"value\"}"), SuccessJson },
        { ["query", "help", "--key"], Request("help", "{\"key\":\"true\"}"), SuccessJson },
        { ["query", "help", "--key", "--other"], Request("help", "{\"key\":\"true\",\"other\":\"true\"}"), SuccessJson },
        { ["query", "help", "--key=first", "--KEY", "last"], Request("help", "{\"key\":\"last\"}"), SuccessJson },
        { ["query", "help", "--key="], Request("help", "{\"key\":\"\"}"), SuccessJson },
        { ["query", "help", "--key=a=b"], Request("help", "{\"key\":\"a=b\"}"), SuccessJson },
        { ["query", "help", "--key", " spaced value "], Request("help", "{\"key\":\" spaced value \"}"), SuccessJson },
        { ["query", "help", "--=x"], Request("help", "{\"=x\":\"true\"}"), SuccessJson },
        { ["query", "help", "--a=b=c"], Request("help", "{\"a\":\"b=c\"}"), SuccessJson }
    };

    /// <summary>Three frozen timeout rows that inspect parsing without a pipe connection.</summary>
    public static TheoryData<string[], int> Timeouts => new()
    {
        { ["query", "help"], 1500 },
        { ["query", "help", "--timeout-ms", "1"], 1 },
        { ["query", "help", "--timeout-ms", "120000"], 120000 }
    };

    /// <summary>Output switches, exact request frames and exact stdout.</summary>
    public static TheoryData<string[], string, string> OutputModes => new()
    {
        { ["query", "help"], Request("help", "null"), SuccessJson },
        { ["query", "help", "--json-pretty"], Request("help", "null"), SuccessJson },
        { ["query", "help", "--json-compact"], Request("help", "null"), SuccessReply + Environment.NewLine },
        { ["query", "help", "--json-compact", "--json-pretty"], Request("help", "null"), SuccessJson },
        { ["query", "help", "--json-pretty", "--JSON-COMPACT"], Request("help", "null"), SuccessReply + Environment.NewLine },
        { ["query", "help", "--timeout-ms=120000", "--json-compact"], Request("help", "null"), SuccessReply + Environment.NewLine },
        { ["query", " status "], Request("status", "null"), SuccessJson }
    };

    /// <summary>Query verb casing and exact failure output.</summary>
    public static TheoryData<string[], string> QueryVerbs => new()
    {
        { ["query", "help", "bare"], InvalidArguments(UnexpectedToken) },
        { ["QUERY", "help", "bare"], InvalidArguments(UnexpectedToken) },
        { ["Query", "help", "bare"], InvalidArguments(UnexpectedToken) },
        { ["qUeRy", "help", "bare"], InvalidArguments(UnexpectedToken) }
    };

    /// <summary>Non-query inputs and their empty stdout.</summary>
    public static TheoryData<string[], string> UnhandledArguments => new()
    {
        { Array.Empty<string>(), string.Empty },
        { ["help"], string.Empty },
        { ["status", "query"], string.Empty },
        { ["--query"], string.Empty },
        { [" query", "help"], string.Empty },
        { ["query ", "help"], string.Empty }
    };

    /// <summary>The frozen absent-instance input and exact failure output.</summary>
    public static TheoryData<string[], string> NoRunningInstance => new()
    {
        { ["query", "help", "--timeout-ms", "1"], FailureJson("INSTANCE_NOT_RUNNING", "No running FreeformHelper instance responded within timeout.") }
    };

    /// <summary>The frozen failed-response input, reply and exact stdout.</summary>
    public static TheoryData<string[], string, string> FailedResponse => new()
    {
        { ["query", "help", "--json-compact"], FailedReply, FailedReply + Environment.NewLine }
    };

    /// <summary>The source Program entry input and exact failure output.</summary>
    public static TheoryData<string[], string> ProgramParseError => new()
    {
        { ["query", "help", "bare"], InvalidArguments(UnexpectedToken) }
    };

    private static string SuccessJson => string.Join(Environment.NewLine,
        "{", "  \"ok\": true,", "  \"data\": {", "    \"value\": 7", "  },", "  \"error\": null", "}") + Environment.NewLine;

    private static string Request(string command, string args) =>
        "{\"version\":\"1\",\"command\":\"" + command + "\",\"args\":" + args + "}" + Environment.NewLine;

    private static string InvalidArguments(string message) => FailureJson("INVALID_ARGUMENTS", message);

    private static string FailureJson(string code, string message)
    {
        // The frozen serializer escapes apostrophes and angle brackets in pretty output.
        var escaped = message.Replace("'", "\\u0027", StringComparison.Ordinal)
            .Replace("<", "\\u003C", StringComparison.Ordinal).Replace(">", "\\u003E", StringComparison.Ordinal);
        return string.Join(Environment.NewLine,
            "{", "  \"ok\": false,", "  \"data\": null,", "  \"error\": {",
            "    \"code\": \"" + code + "\",", "    \"message\": \"" + escaped + "\"", "  }", "}") + Environment.NewLine;
    }
}
