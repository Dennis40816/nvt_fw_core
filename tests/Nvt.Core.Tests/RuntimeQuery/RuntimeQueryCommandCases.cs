// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Input and literal output tables that contain no Core runtime query types.</summary>
public static class RuntimeQueryCommandCases
{
    /// <summary>Command names with the expected routing result and message.</summary>
    public static IEnumerable<object?[]> Commands =>
    [
        ["probe", true, null],
        [" PROBE ", true, null],
        ["\tPrObE\r\n", true, null],
        ["\u2003PROBE\u2003", true, null],
        [" PING ", true, null],
        ["MISSING", false, "Unknown query command 'MISSING'."],
        [" missing ", false, "Unknown query command ' missing '."],
        ["", false, "Unknown query command ''."],
        [" \t ", false, "Unknown query command ' \t '."],
        [null, false, "Unknown query command ''."],
        ["pro be", false, "Unknown query command 'pro be'."],
        ["'missing'", false, "Unknown query command ''missing''."]
    ];

    // JSON permits null fields while keeping the table independent of either envelope type.
    /// <summary>Requests with the expected result of the request check.</summary>
    public static IEnumerable<object?[]> Requests =>
    [
        ["null", "1", "INVALID_REQUEST", "Request is null."],
        ["null", null, "INVALID_REQUEST", "Request is null."],
        ["""{"version":"2","command":"probe"}""", "1", "UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'."],
        ["""{"version":"2","command":"missing"}""", "1", "UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'."],
        ["""{"version":"2","command":null}""", "1", "UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'."],
        ["""{"version":"2","command":""}""", "1", "UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'."],
        ["""{"version":null,"command":"probe"}""", "1", "UNSUPPORTED_VERSION", "Unsupported request version ''. Expected '1'."],
        ["""{"version":" 1 ","command":"probe"}""", "1", "UNSUPPORTED_VERSION", "Unsupported request version ' 1 '. Expected '1'."],
        ["""{"version":"V1","command":"probe"}""", "v1", "UNSUPPORTED_VERSION", "Unsupported request version 'V1'. Expected 'v1'."],
        ["""{"version":"1","command":"missing"}""", "1", "UNKNOWN_COMMAND", "Unknown query command 'missing'."],
        ["""{"version":"1","command":null}""", "1", "UNKNOWN_COMMAND", "Unknown query command ''."],
        ["""{"version":"1","command":" \t "}""", "1", "UNKNOWN_COMMAND", "Unknown query command ' \t '."],
        ["""{"version":"1","command":" PROBE ","args":{"CaseKey":" unchanged "}}""", "1", null, null],
        ["""{"version":"synthetic-v7","command":"probe"}""", "synthetic-v7", null, null]
    ];

    /// <summary>Argument dictionaries without the requested key.</summary>
    public static IEnumerable<object?[]> MissingArguments =>
    [
        [null],
        [new Dictionary<string, string>()],
        [new Dictionary<string, string> { ["other"] = "1" }],
        [new Dictionary<string, string>(StringComparer.Ordinal) { ["VALUE"] = "1" }]
    ];

    /// <summary>Integer inputs and ranges with the expected results and messages.</summary>
    public static IEnumerable<object?[]> Integers =>
    [
        [null, 1, 2, false, 0, null],
        ["", 1, 2, false, 0, null],
        [" \t ", 1, 2, false, 0, null],
        ["1", 1, 2, true, 1, null],
        ["2", 1, 2, true, 2, null],
        [" +1 ", 1, 2, true, 1, null],
        ["-2", -2, 2, true, -2, null],
        ["2147483647", int.MinValue, int.MaxValue, true, int.MaxValue, null],
        ["-2147483648", int.MinValue, int.MaxValue, true, int.MinValue, null],
        ["2147483648", 1, 2, false, 0, "Argument '--value' must be an integer."],
        ["1,000", 1, 2000, false, 0, "Argument '--value' must be an integer."],
        ["1.0", 1, 2, false, 0, "Argument '--value' must be an integer."],
        ["1e0", 1, 2, false, 0, "Argument '--value' must be an integer."],
        ["0x1", 1, 2, false, 0, "Argument '--value' must be an integer."],
        ["１", 1, 2, false, 0, "Argument '--value' must be an integer."],
        ["invalid", 1, 2, false, 0, "Argument '--value' must be an integer."],
        ["0", 1, 2, false, 0, "Argument '--value' must be in [1, 2]."],
        ["3", 1, 2, false, 0, "Argument '--value' must be in [1, 2]."]
    ];

    /// <summary>Integer list inputs with the expected results and messages.</summary>
    public static IEnumerable<object?[]> IntegerLists =>
    [
        [null, false, Array.Empty<int>(), null],
        ["", false, Array.Empty<int>(), null],
        [" \t ", false, Array.Empty<int>(), null],
        ["1", true, new[] { 1 }, null],
        ["-2,0,+2", true, new[] { -2, 0, 2 }, null],
        [" 1, 2 ", true, new[] { 1, 2 }, null],
        ["1,, ,2,", true, new[] { 1, 2 }, null],
        ["2,1,1", true, new[] { 2, 1, 1 }, null],
        [", ,,,", false, Array.Empty<int>(), "Argument '--value' cannot be empty."],
        ["1,x", false, Array.Empty<int>(), "Argument '--value' must be a comma-separated integer list."],
        ["1,2147483648", false, Array.Empty<int>(), "Argument '--value' must be a comma-separated integer list."],
        ["1,3", false, Array.Empty<int>(), "Argument '--value' values must be in [-2, 2]."],
        ["1,-3", false, Array.Empty<int>(), "Argument '--value' values must be in [-2, 2]."],
        ["3,x", false, Array.Empty<int>(), "Argument '--value' values must be in [-2, 2]."],
        ["x,3", false, Array.Empty<int>(), "Argument '--value' must be a comma-separated integer list."],
        ["1;2", false, Array.Empty<int>(), "Argument '--value' must be a comma-separated integer list."]
    ];

    /// <summary>Double inputs and cultures with the expected results and messages.</summary>
    public static IEnumerable<object?[]> Doubles =>
    [
        [null, 0d, 2000d, "en-US", false, 0d, null],
        ["", 0d, 2000d, "en-US", false, 0d, null],
        [" \t ", 0d, 2000d, "en-US", false, 0d, null],
        ["0", 0d, 2000d, "en-US", true, 0d, null],
        ["2000", 0d, 2000d, "en-US", true, 2000d, null],
        [" +1.5 ", 0d, 2000d, "en-US", true, 1.5d, null],
        ["-1.5", -2d, 2d, "en-US", true, -1.5d, null],
        ["1e3", 0d, 2000d, "en-US", true, 1000d, null],
        ["1,234.5", 0d, 2000d, "en-US", true, 1234.5d, null],
        ["1,234.5", 0d, 2000d, "fr-FR", true, 1234.5d, null],
        ["1.5", 0d, 2000d, "fr-FR", true, 1.5d, null],
        ["1,5", 0d, 2000d, "fr-FR", true, 15d, null],
        ["1,,2", 0d, 2000d, "en-US", true, 12d, null],
        ["1 234,5", 0d, 2000d, "fr-FR", false, 0d, "Argument '--value' must be numeric."],
        ["1.234,5", 0d, 2000d, "fr-FR", false, 0d, "Argument '--value' must be numeric."],
        ["invalid", 0d, 2000d, "en-US", false, 0d, "Argument '--value' must be numeric."],
        ["NaN", 0d, 2000d, "en-US", false, 0d, "Argument '--value' must be in [0, 2000]."],
        ["Infinity", 0d, 2000d, "en-US", false, 0d, "Argument '--value' must be in [0, 2000]."],
        ["-Infinity", 0d, 2000d, "en-US", false, 0d, "Argument '--value' must be in [0, 2000]."],
        ["1e309", 0d, 2000d, "en-US", false, 0d, "Argument '--value' must be in [0, 2000]."],
        ["-0.1", 0d, 2000d, "en-US", false, 0d, "Argument '--value' must be in [0, 2000]."],
        ["2000.1", 0d, 2000d, "en-US", false, 0d, "Argument '--value' must be in [0, 2000]."],
        ["1.5", 1.5d, 2.5d, "fr-FR", true, 1.5d, null],
        ["2.5", 1.5d, 2.5d, "fr-FR", true, 2.5d, null],
        ["1.4", 1.5d, 2.5d, "fr-FR", false, 0d, "Argument '--value' must be in [1,5, 2,5]."],
        ["2.6", 1.5d, 2.5d, "en-US", false, 0d, "Argument '--value' must be in [1.5, 2.5]."]
    ];

    /// <summary>String inputs with the expected results and messages.</summary>
    public static IEnumerable<object?[]> Strings =>
    [
        [null, false, "", null],
        ["", false, "", null],
        [" \t\r\n ", false, "", null],
        ["\u2003", false, "", null],
        ["value", true, "value", null],
        [" \t value \r\n", true, "value", null],
        ["\u2003value\u2003", true, "value", null],
        [" keep  inner spaces ", true, "keep  inner spaces", null],
        ["invalid", true, "invalid", null]
    ];

    /// <summary>Boolean spellings with the expected results and messages.</summary>
    public static IEnumerable<object?[]> Booleans =>
    [
        [null, false, false, null],
        ["", false, false, null],
        [" \t ", false, false, null],
        ["true", true, true, null],
        ["TRUE", true, true, null],
        [" TrUe ", true, true, null],
        ["1", true, true, null],
        [" 1 ", true, true, null],
        ["on", true, true, null],
        ["ON", true, true, null],
        [" On ", true, true, null],
        ["yes", true, true, null],
        ["YES", true, true, null],
        [" YeS ", true, true, null],
        ["false", true, false, null],
        ["FALSE", true, false, null],
        [" FaLsE ", true, false, null],
        ["0", true, false, null],
        [" 0 ", true, false, null],
        ["off", true, false, null],
        ["OFF", true, false, null],
        [" OfF ", true, false, null],
        ["no", true, false, null],
        ["NO", true, false, null],
        [" No ", true, false, null],
        ["2", false, false, "Argument '--value' must be true/false (or 1/0, on/off)."],
        ["+1", false, false, "Argument '--value' must be true/false (or 1/0, on/off)."],
        ["enabled", false, false, "Argument '--value' must be true/false (or 1/0, on/off)."],
        ["y", false, false, "Argument '--value' must be true/false (or 1/0, on/off)."],
        ["n", false, false, "Argument '--value' must be true/false (or 1/0, on/off)."]
    ];

    // These rows isolate routing and argument forwarding from the frozen product assertions.
    /// <summary>Requests ported from the source use-case tests.</summary>
    public static IEnumerable<object?[]> FrozenUseCaseRequests =>
    [
        ["ExecuteAsync_QueryStatus_IncludesCadLoadSpinnerDebugTelemetry", "status", null],
        ["ExecuteAsync_QueryExportNotch_RejectsUnsupportedFormat", "export-notch", Args("format", "binary", "path", "build/perf/notch_table.bin")],
        ["ExecuteAsync_QueryExportNotch_ReturnsFailureWhenWorkflowNotReady", "export-notch", Args("format", "csv", "path", "synthetic.csv")],
        ["ExecuteAsync_QueryExportNotch_AcceptsVersionedCFormat_WhenWorkflowNotReady", "export-notch", Args("format", "c-v22", "path", "synthetic.c")],
        ["ExecuteAsync_NotchPadAndInspector_SameResolvedDisplayWithRoundedZeroTarget", "notch", Args("cad-id", "274")],
        ["ExecuteAsync_NotchPadAndInspector_SameResolvedDisplayWithRoundedZeroTarget", "pad", Args("cad-id", "274")],
        ["ExecuteAsync_QuerySelectCad_ReturnsSelectionTimings", "select-cad", Args("cad-id", "101")],
        ["ExecuteAsync_QueryNotchValidation_UsesTypedPayloadWhenAvailable", "notch-validation", Args("regular-id", "100")],
        ["ExecuteAsync_QueryNotchValidation_FallsBackToLegacyValuesPayload", "notch-validation", Args("regular-id", "100")],
        ["ExecuteAsync_QueryNotchValidation_ProducesStableRowPayloadShape", "notch-validation", Args("regular-id", "100")],
        ["ExecuteAsync_QueryMultiOwner_OverrideReusesOneResolvedResultAndReturnsOverrideEvidence", "multi-owner", Args("cad-id", "31", "overlap-percent", "1.0")],
        ["ExecuteAsync_QueryMultiOwner_OverrideReusesOneResolvedResultAndReturnsOverrideEvidence", "multi-owner", Args("cad-id", "31")],
        ["ExecuteAsync_QueryMultiOwner_PreservesVisibilityAndNotReadyErrorsWithoutFallbackCompensation", "multi-owner", Args("cad-id", "31")],
        ["ExecuteAsync_QueryPad_IncludesSharedNotchDisplayProjection", "pad", Args("cad-id", "21")],
        ["ExecuteAsync_QuerySimulation_WhenAfterIsWithinEmsTolerance_ReportsWorkspaceAndRegularAsSafe", "simulation", Args("regular-id", "0")],
        ["ExecuteAsync_QuerySimulation_ReturnsWorkspaceAndRegularSnapshot", "simulation", Args("regular-id", "0")],
        ["ExecuteAsync_QuerySimulation_WhenWorkspaceMissing_AutoBuildsWorkspace", "simulation", Args("regular-id", "0")],
        ["ExecuteAsync_NotchReadersShareSingleResolvedPathAcrossOverlayAllocationQueryAndInspector", "notch", Args("cad-id", "40")],
        ["ExecuteAsync_NotchReadersShareSingleResolvedPathAcrossOverlayAllocationQueryAndInspector", "notch-stage", Args("cad-id", "40")],
        ["ExecuteAsync_NotchReadersShareSingleResolvedPathAcrossOverlayAllocationQueryAndInspector", "pad", Args("cad-id", "40")]
    ];

    private static Dictionary<string, string> Args(params string[] pairs)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < pairs.Length; index += 2)
        {
            args.Add(pairs[index], pairs[index + 1]);
        }

        return args;
    }
}
