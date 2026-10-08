// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Restores process environment after a test in the shared serial process collection.</summary>
internal sealed class ProtocolEnvironmentScope : IDisposable
{
    private readonly (string Key, string? Value)[] _originals;

    internal ProtocolEnvironmentScope(params (string Key, string? Value)[] values)
    {
        _originals = values.Select(static value =>
            (value.Key, Environment.GetEnvironmentVariable(value.Key))).ToArray();
        foreach ((string key, string? value) in values)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public void Dispose()
    {
        foreach ((string key, string? value) in _originals)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
