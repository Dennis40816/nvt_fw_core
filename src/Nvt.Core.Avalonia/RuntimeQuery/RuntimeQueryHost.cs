// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>Starts and stops a caller-created runtime query server.</summary>
public sealed class RuntimeQueryHost
{
    private readonly Func<RuntimeQueryIpcServer> _factory;
    private readonly object _sync = new();
    private RuntimeQueryIpcServer? _server;
    // The last stop, shared with later callers until a new server starts. Guarded by _sync.
    private Task _stopTask = Task.CompletedTask;

    /// <summary>Creates a host that obtains a new server for each start after a stop.</summary>
    /// <param name="factory">Creates a server with the tool's handler and configuration.</param>
    public RuntimeQueryHost(Func<RuntimeQueryIpcServer> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Creates and starts one server under a lock, or does nothing when one is already present.</summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_server is not null)
            {
                return;
            }

            _server = _factory();
            _server.Start();
        }
    }

    /// <summary>Removes the current server under a lock and disposes it. Later calls return the same stop until the next start.</summary>
    public Task StopAsync()
    {
        lock (_sync)
        {
            if (_server is { } server)
            {
                _server = null;
                _stopTask = server.DisposeAsync().AsTask();
            }

            return _stopTask;
        }
    }
}
