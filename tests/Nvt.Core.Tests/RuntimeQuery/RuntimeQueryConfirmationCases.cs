// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Inputs and literal expected outputs for command risk and confirmation.</summary>
public static class RuntimeQueryConfirmationCases
{
    /// <summary>Every risk level with missing, valid, false, invalid and blank confirmation.</summary>
    public static IEnumerable<object?[]> Confirmations
    {
        get
        {
            foreach (var risk in new[] { RuntimeQueryCommandRisk.ReadOnly, RuntimeQueryCommandRisk.ChangesState, RuntimeQueryCommandRisk.WritesData })
            {
                foreach (var row in ConfirmationValues)
                {
                    var text = (string?)row[0];
                    var requiresConfirmation = risk == RuntimeQueryCommandRisk.WritesData;
                    yield return [risk, text, !requiresConfirmation || (bool)row[1]!,
                        requiresConfirmation ? row[2] : null, requiresConfirmation ? row[3] : null];
                }
            }
        }
    }

    private static IEnumerable<object?[]> ConfirmationValues =>
    [
        [null, false, "CONFIRMATION_REQUIRED", "Command 'probe' writes files or changes data. Add --confirm to run it."],
        ["true", true, null, null],
        ["1", true, null, null],
        ["yes", true, null, null],
        ["false", false, "CONFIRMATION_REQUIRED", "Command 'probe' writes files or changes data. Add --confirm to run it."],
        ["0", false, "CONFIRMATION_REQUIRED", "Command 'probe' writes files or changes data. Add --confirm to run it."],
        ["invalid", false, "INVALID_ARGUMENTS", "Argument '--confirm' must be true/false (or 1/0, on/off)."],
        [" \t ", false, "CONFIRMATION_REQUIRED", "Command 'probe' writes files or changes data. Add --confirm to run it."],
        ["", false, "CONFIRMATION_REQUIRED", "Command 'probe' writes files or changes data. Add --confirm to run it."],
        [" TrUe ", true, null, null],
        ["on", true, null, null],
        ["off", false, "CONFIRMATION_REQUIRED", "Command 'probe' writes files or changes data. Add --confirm to run it."],
        ["no", false, "CONFIRMATION_REQUIRED", "Command 'probe' writes files or changes data. Add --confirm to run it."]
    ];

    /// <summary>Errors that precede the enabled guard.</summary>
    public static IEnumerable<object?[]> Requests =>
    [
        ["null", "INVALID_REQUEST", "Request is null."],
        ["""{"version":"2","command":"probe","args":{"confirm":"invalid"}}""", "UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'."],
        ["""{"version":"2","command":"missing","args":{"confirm":"invalid"}}""", "UNSUPPORTED_VERSION", "Unsupported request version '2'. Expected '1'."],
        ["""{"version":"1","command":" MISSING ","args":{"confirm":"invalid"}}""", "UNKNOWN_COMMAND", "Unknown query command ' MISSING '."]
    ];

    /// <summary>Arguments after ordinal removal of the confirmation key.</summary>
    public static IEnumerable<object?[]> Arguments =>
    [
        [null, null],
        [new Dictionary<string, string>(StringComparer.Ordinal), null],
        [Args("confirm", "true"), null],
        [Args("confirm", "invalid"), null],
        [Args("CaseKey", " unchanged ", "empty", ""), Args("CaseKey", " unchanged ", "empty", "")],
        [Args("confirm", "true", "CaseKey", " unchanged ", "casekey", "different", "empty", "", "Confirm", "keep"),
            Args("CaseKey", " unchanged ", "casekey", "different", "empty", "", "Confirm", "keep")],
        [new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["confirm"] = "true", ["CaseKey"] = " unchanged " },
            Args("CaseKey", " unchanged ")],
        [new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Confirm"] = "keep", ["CaseKey"] = " unchanged " },
            Args("Confirm", "keep", "CaseKey", " unchanged ")]
    ];

    /// <summary>Invalid lists, entries, names and handlers with their exception types.</summary>
    public static IEnumerable<object?[]> InvalidCommands =>
    [
        [null, typeof(ArgumentNullException)],
        [new RuntimeQueryCommand[] { null! }, typeof(ArgumentNullException)],
        [new[] { Command(null!) }, typeof(ArgumentNullException)],
        [new[] { new RuntimeQueryCommand("probe", RuntimeQueryCommandRisk.ReadOnly, null!) }, typeof(ArgumentNullException)],
        [new[] { Command("probe"), Command("probe") }, typeof(ArgumentException)],
        [new[] { Command(" probe") }, typeof(ArgumentException)],
        [new[] { Command("Probe") }, typeof(ArgumentException)],
        [new[] { Command("probe ") }, typeof(ArgumentException)]
    ];

    /// <summary>Names whose registration order differs from their ordinal sort order.</summary>
    public static IEnumerable<object?[]> RegistrationOrders =>
    [
        [new[] { "zeta", "probe", "alpha" }],
        [Array.Empty<string>()]
    ];

    private static RuntimeQueryCommand Command(string name) =>
        new(name, RuntimeQueryCommandRisk.ReadOnly, _ => Task.FromResult(RuntimeQueryResponseEnvelope.Success(null)));

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
