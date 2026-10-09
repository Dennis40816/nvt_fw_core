// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Verifies console state and view contracts.</summary>
public sealed class ConsoleControllerTests
{
    /// <summary>Level And Source Filters Project Raw Counts Independently Of Search And Dedupe.</summary>
    [AvaloniaFact]
    public void LevelAndSourceFiltersProjectRawCountsIndependentlyOfSearchAndDedupe()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "match");
        fixture.Store.Add(LogLevel.Info, "app", "match");
        fixture.Store.Add(LogLevel.Error, "app", "other");
        fixture.Store.Add(LogLevel.Error, "dxf", "match");
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.SetSelectedSources(["app"]);
        controller.ToggleLevelCommand.Execute(LogLevel.Error);
        controller.SetSearchText("MATCH");
        controller.ToggleDedupeCommand.Execute(null);
        ConsoleTestView.Pump();
        Assert.Equal(4, controller.Projection.EventCount);
        Assert.Equal(2, Assert.Single(controller.Projection.Rows).Count);
        Assert.Equal(2, controller.Projection.LevelCounts[LogLevel.Info]);
        Assert.Equal(1, controller.Projection.LevelCounts[LogLevel.Error]);
        Assert.Equal(0, controller.Projection.SourceCounts["dxf"]);
        Assert.Equal(0, controller.Projection.SourceCounts["idle"]);
        controller.ToggleLevelCommand.Execute(LogLevel.Error);
        controller.ToggleOnlyMatchesCommand.Execute(null);
        ConsoleTestView.Pump();
        Assert.Equal(2, controller.Projection.RowCount);
        Assert.NotEmpty(controller.Projection.Rows[0].SearchHits);
        Assert.Empty(controller.Projection.Rows[1].SearchHits);
        Assert.Equal(1, controller.Projection.SourceCounts["dxf"]);
    }

    /// <summary>Search Matches Source Ids And Whitespace Restores All Rows.</summary>
    [AvaloniaFact]
    public void SearchMatchesSourceIdsAndWhitespaceRestoresAllRows()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "first line\nneedle");
        fixture.Store.Add(LogLevel.Warn, "dxf", "other");
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.SetSearchText("NEEDLE");
        ConsoleTestView.Pump();
        Assert.Equal("app", Assert.Single(controller.Projection.Rows).SourceId);
        controller.SetSearchText("DXF");
        ConsoleTestView.Pump();
        Assert.Equal(ConsoleSearchArea.Source, Assert.Single(Assert.Single(controller.Projection.Rows).SearchHits).Area);
        controller.SetSearchText("  ");
        ConsoleTestView.Pump();
        Assert.Equal(2, controller.Projection.RowCount);
        Assert.Empty(controller.Filter.SearchText);
        foreach (var level in Enum.GetValues<LogLevel>()) controller.ToggleLevelCommand.Execute(level);
        ConsoleTestView.Pump();
        Assert.True(controller.Projection.IsEmpty);
        Assert.Contains("no levels", controller.FilterSummary, StringComparison.Ordinal);
    }

    /// <summary>Store And Filter Changes In One Ui Turn Publish One Projection.</summary>
    [AvaloniaFact]
    public void StoreAndFilterChangesInOneUiTurnPublishOneProjection()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var publications = 0;
        controller.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(controller.Projection)) publications++; };
        for (var index = 0; index < 20; index++)
        {
            fixture.Store.Add(LogLevel.Info, "app", "entry " + index);
            fixture.Fence();
        }
        controller.SetSearchText("entry");
        controller.ToggleDedupeCommand.Execute(null);
        Assert.Equal(0, publications);
        ConsoleTestView.Pump();
        Assert.Equal(1, publications);
        Assert.Equal(20, controller.Projection.EventCount);
    }

    /// <summary>Producer Notifications Publish Only On Registered Ui Thread.</summary>
    [AvaloniaFact]
    public void ProducerNotificationsPublishOnlyOnRegisteredUiThread()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var publicationThread = 0;
        controller.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(controller.Projection)) publicationThread = Environment.CurrentManagedThreadId;
        };
        fixture.Store.Add(LogLevel.Info, "app", "background");
        fixture.Fence();
        Assert.Equal(0, publicationThread);
        ConsoleTestView.Pump();
        Assert.Equal(Environment.CurrentManagedThreadId, publicationThread);
        Exception? error = null;
        var thread = new Thread(() => error = Record.Exception(() => controller.SetSearchText("wrong thread")));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)));
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Dispose Aborts Pending Work Unsubscribes And Is Safe To Repeat.</summary>
    [AvaloniaFact]
    public void DisposeAbortsPendingWorkUnsubscribesAndIsSafeToRepeat()
    {
        using var fixture = new ConsoleTestStore();
        var controller = fixture.Controller();
        var publications = 0;
        controller.PropertyChanged += (_, _) => publications++;
        fixture.Store.Add(LogLevel.Info, "app", "queued");
        fixture.Fence();
        controller.Dispose();
        controller.Dispose();
        fixture.Store.Add(LogLevel.Info, "app", "after dispose");
        fixture.Fence();
        ConsoleTestView.Pump();
        Assert.Equal(0, publications);
        Assert.False(controller.ClearCommand.CanExecute(null));
        Assert.Throws<ObjectDisposedException>(() => controller.SetSearchText("closed"));
    }

    /// <summary>Replacement Keeps Old Lease Readable During Notification Then Releases It.</summary>
    [AvaloniaFact]
    public void ReplacementKeepsOldLeaseReadableDuringNotificationThenReleasesIt()
    {
        using var fixture = new ConsoleTestStore();
        var content = new TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        using var controller = fixture.Controller();
        var oldContent = Assert.Single(controller.Projection.Rows).TextContent;
        controller.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(controller.Projection))
            {
                var buffer = new char[4];
                oldContent.Read(0, buffer);
                Assert.Equal("text", new string(buffer));
                Assert.False(content.Disposed);
            }
        };
        fixture.Store.Clear();
        fixture.Fence();
        ConsoleTestView.Pump();
        Assert.Throws<ObjectDisposedException>(() => oldContent.Read(0, new char[4]));
        fixture.Until(() => content.Disposed);
        Assert.True(controller.Projection.IsEmpty);
    }

    /// <summary>Disposing With Retirement Queued Releases Both Projections.</summary>
    [AvaloniaFact]
    public void DisposingWithRetirementQueuedReleasesBothProjections()
    {
        using var fixture = new ConsoleTestStore();
        var content = new TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        var controller = fixture.Controller();
        controller.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(controller.Projection)) controller.Dispose();
        };
        controller.SetSearchText("text");
        ConsoleTestView.Pump();
        fixture.Store.Clear();
        fixture.Fence();
        fixture.Until(() => content.Disposed);
        controller.Dispose();
    }

    /// <summary>Pause Dedupe Remaps Expansion Selection And Order Then Resume Restores Following.</summary>
    [AvaloniaFact]
    public void PauseDedupeRemapsExpansionSelectionAndOrderThenResumeRestoresFollowing()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "a");
        fixture.Store.Add(LogLevel.Info, "app", "b");
        fixture.Store.Add(LogLevel.Info, "app", "a");
        fixture.Fence();
        using var controller = fixture.Controller();
        var first = controller.Projection.Rows[0];
        controller.RequestViewState(controller.ViewState with { Selection = [first.Id.Value], ExpandedIds = [first.Id], IsExpanded = false });
        controller.Pause(first.Id, 2, 3);
        controller.ToggleDedupeCommand.Execute(null);
        ConsoleTestView.Pump();
        var row = controller.Projection.Rows[0];
        Assert.Equal("a", row.GetFirstLine().Text);
        Assert.Contains(row.Id, controller.ViewState.ExpandedIds);
        Assert.Contains(first.Id.Value, controller.ViewState.Selection);
        Assert.Equal(row.Id, controller.Projection.ResolvedAnchorId);
        fixture.Store.Add(LogLevel.Info, "app", "a");
        fixture.Fence();
        ConsoleTestView.Pump();
        Assert.Equal(1, controller.Projection.NewSincePauseCount);
        controller.Resume();
        ConsoleTestView.Pump();
        Assert.IsType<ConsoleFollow.Following>(controller.ViewState.Follow);
        Assert.Equal("a", controller.Projection.Rows[^1].GetFirstLine().Text);
        Assert.False(controller.ViewState.IsExpanded);
    }

    /// <summary>Eviction Removes Selection And Clear Advances Generation.</summary>
    [AvaloniaFact]
    public void EvictionRemovesSelectionAndClearAdvancesGeneration()
    {
        using var fixture = new ConsoleTestStore(maxEntries: 1);
        fixture.Store.Add(LogLevel.Info, "app", "first");
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.RequestViewState(controller.ViewState with { Selection = [controller.Projection.Rows[0].Id.Value] });
        fixture.Store.Add(LogLevel.Info, "app", "next");
        fixture.Fence();
        ConsoleTestView.Pump();
        Assert.Empty(controller.ViewState.Selection);
        var generation = controller.Projection.Generation;
        controller.ClearCommand.Execute(null);
        fixture.Fence();
        ConsoleTestView.Pump();
        Assert.True(controller.Projection.Generation > generation);
        Assert.True(controller.Projection.IsEmpty);
    }

    /// <summary>Time Modes Use Injected Template Culture And Zone And Pause Base.</summary>
    [AvaloniaFact]
    public void TimeModesUseInjectedTemplateCultureAndZoneAndPauseBase()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "time", DateTimeOffset.Parse("2026-01-01T11:59:57.700Z", CultureInfo.InvariantCulture));
        fixture.Fence();
        using var controller = fixture.Controller(new ConsoleProjectionOptions
        {
            Culture = CultureInfo.GetCultureInfo("fr-FR"), RelativeTimeTemplate = "−{0} s",
            AbsoluteTimeZone = TimeZoneInfo.CreateCustomTimeZone("display", TimeSpan.FromHours(8), "display", "display"),
        });
        Assert.Equal("19:59:57.700", controller.Projection.Rows[0].TimeText);
        controller.Pause();
        controller.SetTimeModeCommand.Execute(ConsoleTimeMode.Relative);
        ConsoleTestView.Pump();
        Assert.Equal("−2,3 s", controller.Projection.Rows[0].TimeText);
        Assert.Equal(((ConsoleFollow.Paused)controller.ViewState.Follow).PausedAt, controller.Projection.TimeBase);
        controller.SetTimeModeCommand.Execute(ConsoleTimeMode.Hidden);
        ConsoleTestView.Pump();
        Assert.Empty(controller.Projection.Rows[0].TimeText);
    }

    /// <summary>Reset Filters Preserves Reading Time And Export Flags.</summary>
    [AvaloniaFact]
    public void ResetFiltersPreservesReadingTimeAndExportFlags()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        controller.SetSelectedSources(["dxf"]);
        controller.SetSearchText("query");
        controller.ToggleLevelCommand.Execute(LogLevel.Info);
        controller.ToggleDedupeCommand.Execute(null);
        controller.Pause();
        controller.SetTimeModeCommand.Execute(ConsoleTimeMode.Hidden);
        controller.ToggleExportTimeCommand.Execute(null);
        controller.ResetFiltersCommand.Execute(null);
        ConsoleTestView.Pump();
        Assert.Empty(controller.Filter.SelectedSources);
        Assert.Empty(controller.Filter.SearchText);
        Assert.Equal(6, controller.Filter.EnabledLevels.Count);
        Assert.False(controller.Filter.Deduplicate);
        Assert.Equal(ConsoleTimeMode.Hidden, controller.Filter.TimeMode);
        Assert.IsType<ConsoleFollow.Paused>(controller.ViewState.Follow);
        Assert.False(controller.ExportOptions.IncludeTime);
    }

    /// <summary>Reentrant disposal ends publication and keeps both leases valid until the callback returns.</summary>
    [AvaloniaFact]
    public void DisposeDuringViewStatePublicationStopsNotificationsAndDefersLeaseRelease()
    {
        using var fixture = new ConsoleTestStore();
        var content = new TrackedContent();
        fixture.Store.Add(new LogWrite(LogLevel.Info, "app", content));
        fixture.Fence();
        using var controller = fixture.Controller();
        var previous = Assert.Single(controller.Projection.Rows).TextContent;
        ILogTextContent? current = null;
        var notifications = new List<string?>();
        var laterSubscriberCalls = 0;
        controller.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(controller.ViewState)) return;
            notifications.Add(args.PropertyName);
            current = Assert.Single(controller.Projection.Rows).TextContent;
            controller.Dispose();
            controller.Dispose();
            var buffer = new char[4];
            previous.Read(0, buffer);
            Assert.Equal("text", new string(buffer));
            current.Read(0, buffer);
            Assert.Equal("text", new string(buffer));
        };
        controller.SetSearchText("text");
        controller.PropertyChanged += (_, args) =>
        {
            laterSubscriberCalls++;
            notifications.Add(args.PropertyName);
        };
        ConsoleTestView.Pump();
        Assert.Equal([nameof(controller.ViewState)], notifications);
        Assert.Equal(0, laterSubscriberCalls);
        Assert.NotNull(current);
        Assert.Throws<ObjectDisposedException>(() => previous.Read(0, new char[4]));
        Assert.Throws<ObjectDisposedException>(() => current.Read(0, new char[4]));
        fixture.Store.Clear();
        fixture.Fence();
        fixture.Until(() => content.Disposed);
    }

    /// <summary>A subscriber pumping the dispatcher cannot retire its borrowed projection.</summary>
    [AvaloniaFact]
    public void DispatcherPumpDuringPublicationKeepsBorrowedProjectionAlive()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "text");
        fixture.Fence();
        using var controller = fixture.Controller();
        var previous = controller.Projection.Rows[0].TextContent;
        var publications = 0;
        controller.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(controller.Projection)) return;
            if (++publications != 1) return;
            controller.ToggleDedupeCommand.Execute(null);
            ConsoleTestView.Pump();
            var buffer = new char[4];
            previous.Read(0, buffer);
            Assert.Equal("text", new string(buffer));
        };
        controller.SetSearchText("text");
        ConsoleTestView.Pump();
        Assert.Equal(2, publications);
        Assert.True(controller.Projection.Deduplicate);
        Assert.Throws<ObjectDisposedException>(() => previous.Read(0, new char[4]));
    }

    /// <summary>Clear's admission fence rejects a queued refresh while the writer remains held.</summary>
    [AvaloniaFact]
    public void QueuedRefreshCannotPublishClearedGenerationWhileWriterIsHeld()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "old generation");
        fixture.Fence();
        using var controller = fixture.Controller();
        var previous = controller.Projection;
        var publications = 0;
        controller.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(controller.Projection)) return;
            publications++;
            Assert.True(fixture.Store.IsCurrent(controller.Projection.Generation));
        };
        controller.SetSearchText("old");
        fixture.Store.Clear();
        // The explicit scheduler holds the reset; UI work runs against the last published snapshot.
        using (var stale = fixture.Store.CaptureSnapshot())
        {
            Assert.Equal(previous.Generation, stale.Generation);
            Assert.False(fixture.Store.IsCurrent(stale.Generation));
        }
        ConsoleTestView.Pump();
        Assert.Equal(0, publications);
        Assert.Same(previous, controller.Projection);
        fixture.Fence();
        ConsoleTestView.Pump();
        Assert.Equal(1, publications);
        Assert.True(controller.Projection.Generation > previous.Generation);
        Assert.True(controller.Projection.IsEmpty);
    }

    /// <summary>Pause uses the injected clock at the intent even after an idle interval.</summary>
    [AvaloniaFact]
    public void PauseUsesCurrentInjectedClockAfterIdleInterval()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "time", fixture.UtcNow.AddSeconds(-2));
        fixture.Fence();
        using var controller = fixture.Controller();
        var capturedAt = controller.Projection.CapturedAt;
        fixture.UtcNow = fixture.UtcNow.AddSeconds(30);
        controller.Pause();
        var paused = Assert.IsType<ConsoleFollow.Paused>(controller.ViewState.Follow);
        Assert.Equal(fixture.UtcNow, paused.PausedAt);
        Assert.NotEqual(capturedAt, paused.PausedAt);
        controller.SetTimeModeCommand.Execute(ConsoleTimeMode.Relative);
        ConsoleTestView.Pump();
        Assert.Equal(paused.PausedAt, controller.Projection.TimeBase);
        var text = controller.Projection.Rows[0].TimeText;
        Assert.StartsWith("32", text, StringComparison.Ordinal);
        fixture.UtcNow = fixture.UtcNow.AddSeconds(10);
        fixture.Store.Add(LogLevel.Info, "app", "next");
        fixture.Fence();
        ConsoleTestView.Pump();
        Assert.Equal(paused.PausedAt, controller.Projection.TimeBase);
        Assert.Equal(text, controller.Projection.Rows[0].TimeText);
    }

    private sealed class TrackedContent : ILogTextContent
    {
        // Dispose runs on the writer; test assertions read via Volatile, writes use Interlocked.
        private int _disposed;
        internal bool Disposed => Volatile.Read(ref _disposed) != 0;
        public int Length => 4;
        public int ResidentCharacterCount => 4;
        public long Version => 0;
        public void Read(int offset, Span<char> destination) => "text".AsSpan(offset, destination.Length).CopyTo(destination);
        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }
}
