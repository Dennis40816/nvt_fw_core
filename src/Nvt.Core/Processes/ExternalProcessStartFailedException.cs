// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Processes;

/// <summary>
/// The operating system refused to start an external process, for example because the executable was removed,
/// blocked, or invalid between a host-side check and the launch. No process was created, and the invocation's
/// capacity reservation is released before this start-time failure is thrown.
/// </summary>
public sealed class ExternalProcessStartFailedException : Exception
{
    /// <summary>Creates a start-time failure that retains the underlying exception.</summary>
    /// <param name="startException">The exception raised while attempting to start the process.</param>
    public ExternalProcessStartFailedException(Exception startException)
        : base($"The external process could not be started ({startException.GetType().Name}).", startException)
    {
    }
}
