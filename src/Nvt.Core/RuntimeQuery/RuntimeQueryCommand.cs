// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>A runtime query command with its risk and handler.</summary>
/// <param name="Name">The trimmed, lowercase invariant command name.</param>
/// <param name="Risk">The command's effect on state, files or data.</param>
/// <param name="Handler">The caller's command handler.</param>
public sealed record RuntimeQueryCommand(
    string Name,
    RuntimeQueryCommandRisk Risk,
    Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>> Handler);
