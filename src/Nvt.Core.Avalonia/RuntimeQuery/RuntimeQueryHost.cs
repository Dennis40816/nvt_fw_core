// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>Starts and stops a caller-created runtime query server.</summary>
public sealed class RuntimeQueryHost
{
    private readonly Func<RuntimeQueryIpcServer> _factory;
    private readonly object _sync = new();
    private RuntimeQueryIpcServer? _server;

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

    /// <summary>Removes the current server under a lock and disposes it, or completes immediately when none is present.</summary>
    public Task StopAsync()
    {
        RuntimeQueryIpcServer? server;
        lock (_sync)
        {
            server = _server;
            _server = null;
        }

        return server?.DisposeAsync().AsTask() ?? Task.CompletedTask;
    }
}
