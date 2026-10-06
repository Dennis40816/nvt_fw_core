// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;

namespace Nvt.Core.Processes;

/// <summary>Names one handle that a contained Windows child may inherit.</summary>
public readonly record struct ProcessInheritedHandle
{
    /// <summary>Creates one environment-bound inherited handle.</summary>
    public ProcessInheritedHandle(string environmentVariable, IntPtr handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariable);
        if (environmentVariable.Contains('=', StringComparison.Ordinal))
        {
            throw new ArgumentException("Environment variable names cannot contain '='.", nameof(environmentVariable));
        }
        if (handle.ToInt64() <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handle));
        }
        EnvironmentVariable = environmentVariable;
        Handle = handle;
    }

    /// <summary>Environment variable that receives the short-lived duplicate value.</summary>
    public string EnvironmentVariable { get; }

    /// <summary>Non-inheritable original handle retained by the parent.</summary>
    public IntPtr Handle { get; }

    /// <summary>Parses a decimal Windows handle returned by an anonymous pipe.</summary>
    public static ProcessInheritedHandle Parse(string environmentVariable, string handle)
    {
        return long.TryParse(handle, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            ? new(environmentVariable, new IntPtr(value))
            : throw new ArgumentException("Inherited handle must be a positive decimal value.", nameof(handle));
    }
}

