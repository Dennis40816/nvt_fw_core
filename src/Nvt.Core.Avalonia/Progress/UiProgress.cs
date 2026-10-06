// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Threading;

namespace Nvt.Core.Avalonia.Progress;

/// <summary>Publishes reports inline on the UI thread and posts reports from other threads.</summary>
/// <typeparam name="T">The caller's progress value.</typeparam>
public sealed class UiProgress<T> : IProgress<T>
{
    private readonly Action<T> publish;

    /// <summary>Creates a progress adapter for the supplied UI callback.</summary>
    /// <param name="publish">The callback that receives each report on the UI thread.</param>
    public UiProgress(Action<T> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        this.publish = publish;
    }

    /// <summary>Delivers a report using NVT FW Combiner's inline-or-post ordering.</summary>
    /// <param name="value">The value to publish.</param>
    public void Report(T value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            publish(value);
        }
        else
        {
            Dispatcher.UIThread.Post(() => publish(value));
        }
    }
}
