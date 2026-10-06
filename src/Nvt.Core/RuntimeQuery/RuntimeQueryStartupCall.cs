// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>A command occurrence in startup argument order, including occurrences with issues.</summary>
/// <param name="Command">The registered command definition.</param>
/// <param name="Args">The unchanged startup value under its key, or null for a flag or missing value.</param>
public sealed record RuntimeQueryStartupCall(
    RuntimeQueryCommand Command,
    IReadOnlyDictionary<string, string>? Args)
{
    /// <summary>The phase from the command definition.</summary>
    public RuntimeQueryStartupPhase Phase => Command.StartupPhase;

    internal bool Confirmed { get; init; }
}
