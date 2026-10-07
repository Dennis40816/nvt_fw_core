// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>A runtime query command with its risk and handler.</summary>
/// <param name="Name">The trimmed, lowercase invariant command name.</param>
/// <param name="Risk">The command's effect on state, files or data.</param>
/// <param name="Handler">The caller's command handler.</param>
/// <param name="StartupPhase">When the tool can run this command at startup.</param>
/// <param name="StartupValueKey">The argument key for one startup value, or null for a flag.</param>
/// <param name="StartupValidator">Checks startup arguments without running the handler or causing side effects.</param>
public sealed record RuntimeQueryCommand(
    string Name,
    RuntimeQueryCommandRisk Risk,
    Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>> Handler,
    RuntimeQueryStartupPhase StartupPhase = RuntimeQueryStartupPhase.None,
    string? StartupValueKey = null,
    Func<IReadOnlyDictionary<string, string>?, RuntimeQueryResponseEnvelope?>? StartupValidator = null)
{
    /// <summary>Whether the runtime handler receives the confirm argument when the router requires confirmation.</summary>
    /// <remarks>
    /// Defaults to false. Commands that write data still require confirmation before their handlers run.
    /// This property does not change startup argument handling.
    /// </remarks>
    public bool ReceivesConfirmation { get; init; }
}
