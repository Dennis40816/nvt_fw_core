// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Transport;

/// <summary>Outcome of starting the stable launcher after a desktop shutdown.</summary>
public enum StableLauncherStartOutcome
{
    /// <summary>The launcher remained running through the immediate start observation.</summary>
    Started,
    /// <summary>The launcher process could not be created.</summary>
    ProcessCreationFailed,
    /// <summary>The launcher exited during the immediate start observation.</summary>
    ExitedImmediately,
    /// <summary>Exact handoff authority or the launch protocol could not be established.</summary>
    HandoffFailed,
}

/// <summary>Path-free stable launcher start result; exit code is present only for an observed exit.</summary>
public sealed record StableLauncherStartResult(StableLauncherStartOutcome Outcome, int? ExitCode = null)
{
    /// <summary>Whether the launcher process remained running through start observation.</summary>
    public bool IsStarted => Outcome == StableLauncherStartOutcome.Started;
}

/// <summary>Hands a drained desktop shutdown back to the stable launcher.</summary>
public interface IStableLauncherHandoff
{
    /// <summary>Starts the stable launcher after pending activation was persisted.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The typed start outcome and an exit code when immediately observed.</returns>
    ValueTask<StableLauncherStartResult> TryStartLauncherAsync(CancellationToken cancellationToken);
}

