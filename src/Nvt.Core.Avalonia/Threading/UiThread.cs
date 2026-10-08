// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Threading;

namespace Nvt.Core.Avalonia.Threading;

/// <summary>Stores the running UI dispatcher independently of Avalonia's global dispatcher slot.</summary>
public static class UiThread
{
    private static Dispatcher? s_runningDispatcher;

    /// <summary>Registers the dispatcher only when it supports run loops.</summary>
    /// <param name="dispatcher">The UI dispatcher to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is null.</exception>
    public static void RegisterRunningDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (dispatcher.SupportsRunLoops)
        {
            Volatile.Write(ref s_runningDispatcher, dispatcher);
        }
    }

    /// <summary>Gets the registered dispatcher when it supports run loops and an application is current.</summary>
    /// <param name="dispatcher">The registered dispatcher on success; otherwise null.</param>
    /// <returns>Whether a running dispatcher and current application are available.</returns>
    public static bool TryGetRunningDispatcher(out Dispatcher? dispatcher)
    {
        dispatcher = null;
        if (global::Avalonia.Application.Current is null)
        {
            return false;
        }

        var currentDispatcher = Volatile.Read(ref s_runningDispatcher);
        if (currentDispatcher is null || !currentDispatcher.SupportsRunLoops)
        {
            return false;
        }

        dispatcher = currentDispatcher;
        return true;
    }

    /// <summary>Gets the running dispatcher and current application only on the dispatcher's thread.</summary>
    /// <param name="dispatcher">The running dispatcher on success; otherwise null.</param>
    /// <param name="application">The current application on success; otherwise null.</param>
    /// <returns>Whether the calling thread has access to the running UI dispatcher and current application.</returns>
    public static bool IsCurrent(out Dispatcher? dispatcher, out global::Avalonia.Application? application)
    {
        application = null;
        if (!TryGetRunningDispatcher(out dispatcher) || !dispatcher!.CheckAccess())
        {
            return false;
        }

        application = global::Avalonia.Application.Current;
        if (application is null)
        {
            dispatcher = null;
            return false;
        }

        return true;
    }
}
