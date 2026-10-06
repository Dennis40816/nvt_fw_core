// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Avalonia.Threading;
using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>Runs runtime query handlers on the registered UI dispatcher.</summary>
public static class RuntimeQueryUiThread
{
    /// <summary>Wraps a server handler with UI dispatch and caller-owned dispatcher failure mapping.</summary>
    /// <param name="handler">Receives the original request, version and cancellation token on the UI thread.</param>
    /// <param name="error">Maps dispatcher unavailability with a null detail to the caller's error.</param>
    /// <returns>A server handler that preserves the inner response and lets inner exceptions escape.</returns>
    public static Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>> Wrap(
        Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>> handler,
        Func<RuntimeQueryFailure, string?, RuntimeQueryError> error)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(error);

        return async (request, version, cancellationToken) =>
        {
            if (UiThread.TryGetRunningDispatcher(out var dispatcher))
            {
                return await dispatcher!.InvokeAsync(() => handler(request, version, cancellationToken));
            }

            return new RuntimeQueryResponseEnvelope(
                Ok: false, Data: null, Error: error(RuntimeQueryFailure.DispatcherUnavailable, null));
        };
    }
}
