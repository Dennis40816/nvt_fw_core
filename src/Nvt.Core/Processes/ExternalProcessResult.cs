// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Processes;

/// <summary>Observed result from an external process invocation.</summary>
/// <param name="ExitCode">The direct child's exit code.</param>
/// <param name="TimedOut">Whether the execution timeout expired.</param>
/// <param name="StandardOutput">Bounded text captured from standard output.</param>
/// <param name="StandardError">Bounded text captured from standard error.</param>
public sealed record ExternalProcessResult(
    int ExitCode,
    bool TimedOut,
    string StandardOutput,
    string StandardError)
{
    /// <summary>
    /// Cleanup fact observed within the host cleanup deadline. A consumer may treat captured output as a
    /// successful result or protocol input only when this is <see cref="ExternalProcessCleanup.Complete"/>;
    /// any other value fails closed before that use. Captured text may still be quoted as error diagnostics
    /// for a timeout or a non-zero exit regardless of this value.
    /// </summary>
    public ExternalProcessCleanup Cleanup { get; init; } = ExternalProcessCleanup.Complete;
}
