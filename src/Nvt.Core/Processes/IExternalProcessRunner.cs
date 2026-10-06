// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Processes;

/// <summary>Runs a prepared external process without shell command construction.</summary>
public interface IExternalProcessRunner
{
    /// <summary>Runs the process and returns captured exit status.</summary>
    ValueTask<ExternalProcessResult> RunAsync(
        ExternalProcessStartInfo startInfo,
        CancellationToken cancellationToken);
}
