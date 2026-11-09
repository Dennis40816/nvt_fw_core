// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Hosting;

/// <summary>Reads the environment variables of the host process.</summary>
/// <remarks>This type is the one place in Core that reads a variable by a caller-supplied name. Hosts call it instead of calling <see cref="Environment.GetEnvironmentVariable(string)"/>.</remarks>
public static class HostEnvironment
{
    /// <summary>Gets the value of a process environment variable.</summary>
    /// <param name="name">The variable name. Windows compares it without regard to case; other systems compare it exactly.</param>
    /// <returns>The value, or null when the variable is not set.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public static string? GetVariable(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Environment.GetEnvironmentVariable(name);
    }
}
