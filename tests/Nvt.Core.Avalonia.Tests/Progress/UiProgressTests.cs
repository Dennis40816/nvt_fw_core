// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.Progress;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Progress;

/// <summary>Characterizes NFC's inline delivery and ordered background dispatch.</summary>
public sealed class UiProgressTests
{
    /// <summary>Background reports publish on the UI thread in their report order.</summary>
    [AvaloniaFact]
    public void BackgroundReportsPostToTheUiThreadInOrder()
    {
        var received = new List<int>();
        bool backgroundHasAccess = true;
        var reporter = new UiProgress<int>(value =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            received.Add(value);
        });
        var background = new Thread(() =>
        {
            backgroundHasAccess = Dispatcher.UIThread.CheckAccess();
            for (int value = 0; value < 20; value++)
            {
                reporter.Report(value);
            }
        });
        background.Start();
        Assert.True(background.Join(TimeSpan.FromSeconds(10)));

        Assert.False(backgroundHasAccess);
        Assert.Empty(received);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Enumerable.Range(0, 20), received);
    }

    /// <summary>UI reports run inline and can precede an earlier background report awaiting dispatch.</summary>
    [AvaloniaFact]
    public void UiReportRunsInlineBeforeAnAlreadyQueuedReport()
    {
        var received = new List<int>();
        var reporter = new UiProgress<int>(received.Add);
        var background = new Thread(() => reporter.Report(1));
        background.Start();
        Assert.True(background.Join(TimeSpan.FromSeconds(10)));

        reporter.Report(2);
        Assert.Equal([2], received);
        reporter.Report(3);
        Assert.Equal([2, 3], received);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([2, 3, 1], received);
    }

    /// <summary>The constructor rejects a missing callback.</summary>
    [AvaloniaFact]
    public void ConstructorRejectsNullPublish()
    {
        Assert.Equal("publish", Assert.Throws<ArgumentNullException>(() => new UiProgress<int>(null!)).ParamName);
    }
}