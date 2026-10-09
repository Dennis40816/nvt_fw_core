// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.Avalonia.Tests.Icons;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Exercises the list through a real headless scroll viewport and caller-owned state.</summary>
[Collection(nameof(IconTestIsolation))]
public sealed partial class ConsoleListViewTests(IconSessionFixture fixture)
{
    private static readonly bool[] Variants = [false, true];
    private static readonly string[] Scenes = ["main", "expanded", "error", "search", "paused"];

    private async Task Run(Action action)
    {
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await fixture.Session.Dispatch(action, safety.Token);
    }

    private static object? Member(object owner, string name) => owner.GetType().GetProperty(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner)
        ?? owner.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner);
    private static object? Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);
    private static Panel Host(ConsoleListView view) => view.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_ItemsHost");
    private static ILogicalScrollable Scroll(ConsoleListView view) => (ILogicalScrollable)Host(view);
    private static ConsoleRowId Id(Control row) => (ConsoleRowId)Member(row, "RowId")!;
    private static Control Container(ConsoleListView view, long id) => Host(view).Children.Single(row => Id(row).Value == id);
    private static void Flush(Window window)
    {
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        for (var i = 0; i < 6; i++) { safety.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }
    private static ConsoleRow Row(long id, string message = "Message", string source = "runtime-query",
        LogLevel level = LogLevel.Info, int count = 1, ImmutableArray<ConsoleSearchHit> hits = default) =>
        new(new(id), level, source, new InMemoryLogTextContent(message), 0, null,
            Enumerable.Range(0, count).Select(n => id + n).ToImmutableArray(), Instant.AddSeconds(-id),
            Instant.AddSeconds(-id), id, hits.IsDefault ? [] : hits, "ignored");
    private static readonly DateTimeOffset Instant = new(2026, 1, 1, 14, 32, 15, TimeSpan.Zero);
    private static ConsoleProjection Project(IEnumerable<ConsoleRow> rows, int newCount = 0, long evicted = 0,
        ConsoleRowId? successor = null, DateTimeOffset? timeBase = null, DateTimeOffset? capturedAt = null)
    {
        var array = rows.ToImmutableArray();
        return new()
        {
            Version = array.Length, Generation = 1, LastSequence = array.IsEmpty ? 0 : array.Max(row => row.LastSequence),
            CapturedAt = capturedAt ?? Instant.AddHours(1), TimeBase = timeBase ?? Instant, Rows = array,
            LevelCounts = ImmutableDictionary<LogLevel, int>.Empty, Sources = [], SourceCounts = ImmutableDictionary<string, int>.Empty,
            RetainedMembership = array.ToImmutableDictionary(row => row.Id, row => row.MemberSequences),
            EventCount = array.Sum(row => row.Count), NewSincePauseCount = newCount, EvictedCount = evicted,
            Deduplicate = false, ResolvedAnchorId = successor,
        };
    }
    private static ConsoleProjection Many(int count, int first = 1) =>
        Project(Enumerable.Range(first, count).Select(i => Row(i, $"Message {i}")));
    private static Window Window(ConsoleListView view, double width = 1200, double height = 280, bool dark = false, bool square = false)
    {
        var window = new Window { Width = width, Height = height, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        var tokens = new Uri("avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml");
        window.Resources.MergedDictionaries.Add(new ResourceInclude(tokens) { Source = tokens });
        window.Styles.Add(new FluentTheme());
        var uri = new Uri("avares://Nvt.Core.Avalonia/LogConsole/ConsoleListStyles.axaml");
        window.Styles.Add(new StyleInclude(uri) { Source = uri });
        ThemeShapes.SetShape(window.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
        window.Show();
        Flush(window);
        return window;
    }

    /// <summary>Rows occupy the specified fixed DIP height.</summary>
    [Fact]
    public Task CollapsedRowsHaveTwentyDipHeight() => Run(() =>
    {
        using var projection = Many(30);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try { Assert.All(Host(view).Children, row => Assert.Equal(20, row.Bounds.Height)); }
        finally { window.Close(); }
    });

    /// <summary>Expanded height starts as an estimate and is corrected by bounded text measurement.</summary>
    [Fact]
    public Task ExpandedEstimateIsUpdatedAfterMeasure() => Run(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird\nfourth")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            view.ViewState = view.ViewState with { ExpandedIds = [new(1)] };
            var estimate = Scroll(view).Extent.Height;
            Flush(window);
            Assert.Equal(80, Container(view, 1).Bounds.Height);
            Assert.NotEqual(estimate, Scroll(view).Extent.Height);
        }
        finally { window.Close(); }
    });

    /// <summary>Projection changes and expansion preserve surviving realized row containers.</summary>
    [Fact]
    public Task ContainerIdentitySurvivesAppendTrimAndExpansion() => Run(() =>
    {
        using var initial = Many(10);
        using var appended = Many(11);
        using var trimmed = Many(10, 2);
        var view = new ConsoleListView { Projection = initial };
        var window = Window(view);
        try
        {
            var row = Container(view, 5);
            var source = Member(Host(view), "ItemsSource");
            view.Projection = appended; Flush(window);
            Assert.Same(row, Container(view, 5));
            view.Projection = trimmed; Flush(window);
            Assert.Same(row, Container(view, 5));
            view.ViewState = view.ViewState with { ExpandedIds = [new(5)] }; Flush(window);
            Assert.Same(row, Container(view, 5));
            view.ViewState = view.ViewState with { ExpandedIds = [] }; Flush(window);
            Assert.Same(row, Container(view, 5));
            Assert.Same(source, Member(Host(view), "ItemsSource"));
        }
        finally { window.Close(); }
    });

    /// <summary>A large list bounds realized containers and recycles during arbitrary scrolling.</summary>
    [Fact]
    public Task TenThousandRowsUseBoundedContainersAndStableSource() => Run(() =>
    {
        using var projection = Many(10000);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var source = Member(Host(view), "ItemsSource");
            var seen = new HashSet<Control>(Host(view).Children);
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            for (var i = 0; i <= 20; i++)
            {
                Scroll(view).Offset = new(0, i * 9500); Flush(window);
                Assert.InRange(Host(view).Children.Count, 1, 18);
                Assert.Same(source, Member(Host(view), "ItemsSource"));
                seen.UnionWith(Host(view).Children);
            }
            Assert.InRange(seen.Count, 1, 36);
        }
        finally { window.Close(); }
    });

    /// <summary>Hundreds of thousands of characters never become one giant text layout or per-line visuals.</summary>
    [Fact]
    public Task HugeExpandedMessageVirtualizesSegmentsAndReachesEnd() => Run(() =>
    {
        var content = new TrackedContent(string.Concat(Enumerable.Repeat("at Synthetic.Frame.Run()\r\n", 20000)) + "END OF TRACE");
        using var projection = Project([Row(1) with { TextContent = content }]);
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view, height: 280);
        try
        {
            Assert.True(Scroll(view).Extent.Height > 300000);
            Assert.True(Scroll(view).Offset.Y >= Scroll(view).Extent.Height - Scroll(view).Viewport.Height - 1);
            Assert.InRange(view.GetVisualDescendants().OfType<TextBlock>().Count(), 1, 8);
            var message = Member(Container(view, 1), "_message")!;
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.InRange(((IDictionary)Member(message, "_visible")!).Count, 1, 4);
            Assert.Equal(2048, content.LargestRead);
            Assert.True((int)Call(message, "TextOffsetAt", Scroll(view).Offset.Y)! > content.Length - 4096);
        }
        finally { window.Close(); }
    });

    /// <summary>Follow keeps the newest row visible on append.</summary>
    [Fact]
    public Task FollowingAppendsRemainAtEnd() => Run(() =>
    {
        using var initial = Many(100);
        using var next = Many(120);
        var view = new ConsoleListView { Projection = initial };
        var window = Window(view);
        try
        {
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
            view.Projection = next; Flush(window);
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Scrolling reports pause without changing state itself and captures the first visible row.</summary>
    [Fact]
    public Task ScrollAwayRequestsPauseWithoutMutatingState() => Run(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            ConsoleViewState? requested = null;
            var original = view.ViewState;
            view.ViewStateRequested += (_, state) => requested = state;
            Scroll(view).Offset = new(0, 405); Flush(window);
            Assert.Same(original, view.ViewState);
            var pause = Assert.IsType<ConsoleFollow.Paused>(Assert.IsType<ConsoleViewState>(requested).Follow);
            Assert.Equal(new ConsoleRowId(21), pause.Anchor.RowId);
            Assert.Equal(21, pause.Anchor.Sequence);
            Assert.Equal(5, pause.Anchor.PixelOffset);
            Assert.Equal(100, pause.Anchor.RowOrder.Length);
        }
        finally { window.Close(); }
    });

    /// <summary>Accepted pause keeps the same row and pixel offset through appends and retention trims.</summary>
    [Fact]
    public Task PausedProjectionUpdatesKeepReadingAnchorWithinOneRowHeight() => Run(() =>
    {
        using var initial = Many(100);
        using var appended = Many(110);
        using var trimmed = Many(100, 11);
        var view = new ConsoleListView { Projection = initial };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 605); Flush(window);
            var before = Container(view, 31).Bounds.Y;
            view.Projection = appended; Flush(window);
            Assert.InRange(Math.Abs(Container(view, 31).Bounds.Y - before), 0, 20);
            view.Projection = trimmed; Flush(window);
            Assert.InRange(Math.Abs(Container(view, 31).Bounds.Y - before), 0, 20);
        }
        finally { window.Close(); }
    });

    /// <summary>Eviction restores the projection's successor and reports a remapped anchor.</summary>
    [Fact]
    public Task EvictedAnchorMovesToSuccessorAndShowsRetentionNotice() => Run(() =>
    {
        using var initial = Many(100);
        using var trimmed = Project(Enumerable.Range(41, 60).Select(i => Row(i)), evicted: 40, successor: new(41));
        var view = new ConsoleListView { Projection = initial };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 605); Flush(window);
            view.Projection = trimmed; Flush(window);
            Assert.Equal(new ConsoleRowId(41), Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow).Anchor.RowId);
            Assert.Equal(0, Container(view, 41).Bounds.Y);
            var notice = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Retention");
            Assert.True(notice.IsVisible);
            Assert.Contains("40", notice.Text);
        }
        finally { window.Close(); }
    });

    /// <summary>Reaching the end, the button and the public command all request resume.</summary>
    [Fact]
    public Task EndButtonAndJumpMethodRequestResume() => Run(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 400); Flush(window);
            Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
            var jump = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Jump");
            Assert.True(jump.IsVisible);
            jump.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Flush(window);
            Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
            Assert.False(jump.IsVisible);
            Scroll(view).Offset = new(0, 400); Flush(window);
            view.JumpToLatest(); Flush(window);
            Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
            Scroll(view).Offset = new(0, 400); Flush(window);
            Scroll(view).Offset = new(0, Scroll(view).Extent.Height); Flush(window);
            Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
        }
        finally { window.Close(); }
    });

    /// <summary>Expansion reports one combined pause/expansion intent and never assigns input state.</summary>
    [Fact]
    public Task ExpansionRequestsPauseAndPreservesCallerOwnedFacts() => Run(() =>
    {
        using var projection = Project([Row(1, "first\nsecond"), Row(2)]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            ConsoleViewState? requested = null;
            var original = view.ViewState;
            view.ViewStateRequested += (_, state) => requested = state;
            Click(window, (Control)Member(Container(view, 1), "_message")!);
            Assert.Same(original, view.ViewState);
            Assert.IsType<ConsoleFollow.Paused>(requested!.Follow);
            Assert.Contains(new ConsoleRowId(1), requested.ExpandedIds);
            view.ViewState = requested; Flush(window);
            Click(window, (Control)Member(Container(view, 1), "_arrow")!);
            Assert.DoesNotContain(new ConsoleRowId(1), requested.ExpandedIds);
        }
        finally { window.Close(); }
    });

    /// <summary>Viewport width controls every row and no horizontal scrolling is enabled.</summary>
    [Theory]
    [InlineData(640, false, false)] [InlineData(960, false, false)] [InlineData(1200, false, false)]
    [InlineData(640, true, false)] [InlineData(960, true, false)] [InlineData(1200, true, false)]
    [InlineData(640, false, true)] [InlineData(960, false, true)] [InlineData(1200, false, true)]
    [InlineData(640, true, true)] [InlineData(960, true, true)] [InlineData(1200, true, true)]
    public Task LayoutUsesViewportInEveryThemeAndShape(int width, bool dark, bool square) => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 30).Select(i => Row(i, new string('W', 300),
            "A very long synthetic source name")));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view, width, dark: dark, square: square);
        try
        {
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PART_ScrollViewer");
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.Equal(Scroll(view).Viewport.Width, Scroll(view).Extent.Width);
            Assert.All(Host(view).Children, row => Assert.Equal(Scroll(view).Viewport.Width, row.Bounds.Width));
            var source = (Control)Member(Host(view).Children[0], "_source")!;
            Assert.Equal("A very long synthetic source name", ToolTip.GetTip(source));
            Assert.True((bool)Member(source, "Truncated")!);
            Assert.All(view.GetVisualDescendants().OfType<ScrollBar>().Where(s => s.Orientation == global::Avalonia.Layout.Orientation.Horizontal),
                bar => Assert.False(bar.IsVisible));
        }
        finally { window.Close(); }
    });

    /// <summary>Relative text ignores captured-at changes and uses the projection's pause clock.</summary>
    [Fact]
    public Task RelativeTimeUsesFrozenProjectionTimeBase() => Run(() =>
    {
        using var initial = Project([Row(1)], timeBase: Instant);
        using var next = Project([Row(1)], timeBase: Instant, capturedAt: Instant.AddHours(2));
        var view = new ConsoleListView { Projection = initial, TimeMode = ConsoleTimeMode.Relative,
            TimeOptions = new() { RelativeTimeTemplate = "−{0} s" } };
        var window = Window(view);
        try
        {
            view.ViewState = view.ViewState.Pause(initial, new(1)); Flush(window);
            var time = (TextBlock)Member(Container(view, 1), "_time")!;
            Assert.Equal("−1.0 s", time.Text);
            view.Projection = next; Flush(window);
            Assert.Equal("−1.0 s", time.Text);
            view.TimeMode = ConsoleTimeMode.Hidden; Flush(window);
            Assert.False(time.IsVisible);
        }
        finally { window.Close(); }
    });

    /// <summary>A simple row has no arrow, while newline and actual width truncation do.</summary>
    [Fact]
    public Task ArrowAndRepeatCountReflectRenderedContent() => Run(() =>
    {
        using var projection = Project([Row(1, "short"), Row(2, "first\nsecond"), Row(3, new string('W', 300), count: 4)]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Assert.False(((Control)Member(Container(view, 1), "_arrow")!).IsVisible);
            Assert.True(((Control)Member(Container(view, 2), "_arrow")!).IsVisible);
            Assert.True(((Control)Member(Container(view, 3), "_arrow")!).IsVisible);
            Assert.Equal("×4", ((TextBlock)Member(Container(view, 3), "_count")!).Text);
            Assert.Equal(string.Empty, ((TextBlock)Member(Container(view, 1), "_count")!).Text);
        }
        finally { window.Close(); }
    });

    /// <summary>Reattachment reuses externally held reading state and releases every old view.</summary>
    [Fact]
    public Task AttachDetachAndReattachOneHundredTimesLeavesNoReachableOldView() => Run(() =>
    {
        var references = DetachedViews();
        for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.All(references, weak => Assert.False(weak.IsAlive));
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> DetachedViews()
    {
        var result = new List<WeakReference>();
        using var projection = Many(50);
        var window = Window(new ConsoleListView());
        for (var i = 0; i < 100; i++)
        {
            var view = new ConsoleListView { Projection = projection, ViewState = new ConsoleViewState().Pause(projection, new(15)) };
            window.Content = view; Flush(window);
            var state = view.ViewState;
            window.Content = null; Flush(window);
            window.Content = view; Flush(window);
            Assert.Same(state, view.ViewState);
            Assert.Equal(new ConsoleRowId(15), ((ConsoleReadingAnchor)Call(Host(view), "CaptureAnchor")!).RowId);
            window.Content = null; Flush(window);
            result.Add(new(view));
        }
        window.Close();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return result;
    }

    /// <summary>Produces scope-specific list evidence in every theme/shape with fourteen complete collapsed rows.</summary>
    [Fact]
    public Task RenderListEvidenceForLightDarkPillAndSquare() => Run(() =>
    {
        var destination = Environment.GetEnvironmentVariable("NVT_CONSOLE_LIST_IMAGES_DIR");
        if (!string.IsNullOrWhiteSpace(destination)) Directory.CreateDirectory(destination);
        foreach (var dark in Variants)
        foreach (var square in Variants)
        foreach (var scene in Scenes)
        {
            var rows = Enumerable.Range(1, 18).Select(i => Row(i,
                i == 7 ? "Export retry: System.IO.IOException · output file is locked\n  at ExportWriter.Write()\n  Inner: Access denied · close the file and retry."
                : i == 10 ? "Report saved: C:\\Demo\\Exports\\樣品 A\\測試報告.csv"
                : i == 11 ? "Output folder: C:\\Demo\\Exports\\樣品 A"
                : i == 18 ? "Geometry cache hit · 986 entities" : $"Snapshot #{i:0000} · 24 outlines · consistent",
                i == 10 || i == 11 || i == 7 ? "export" : i % 3 == 0 ? "runtime-query" : "dxf",
                i == 7 ? LogLevel.Error : i % 4 == 0 ? LogLevel.Debug : LogLevel.Info,
                i == 18 ? 4 : 1,
                scene == "search" && i == 10 ? [new(ConsoleSearchArea.Message, 36, 4), new(ConsoleSearchArea.Source, 0, 6)] : []));
            using var projection = Project(rows, newCount: 7);
            var state = new ConsoleViewState();
            if (scene == "expanded") state = state with { ExpandedIds = [new(7)] };
            if (scene == "paused") state = state.Pause(projection, new(1));
            var view = new ConsoleListView { Projection = projection, ViewState = state };
            var window = Window(view, height: 420, dark: dark, square: square);
            try
            {
                // Reserve only the other slices' geometry; K2b does not build their header, toolbar or status bar.
                var shell = new Grid { RowDefinitions = new RowDefinitions("120,280,20") };
                window.Content = null;
                Grid.SetRow(view, 1); shell.Children.Add(view); window.Content = shell; Flush(window);
                if (scene == "main")
                    Assert.Equal(14, Host(view).Children.Count(row => row.Bounds.Y >= 0 && row.Bounds.Bottom <= 280));
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(new PixelSize(1200, 420), frame.PixelSize);
                if (!string.IsNullOrWhiteSpace(destination))
                    frame.Save(System.IO.Path.Combine(destination, $"list-{scene}-{(dark ? "dark" : "light")}-{(square ? "square" : "pill")}.png"), PngBitmapEncoderOptions.Default);
            }
            finally { window.Close(); }
        }
    });

    /// <summary>Segment boundaries preserve wrapping, surrogate pairs and original CR/LF content.</summary>
    [Fact]
    public Task ExpandedSegmentsMatchWholeTextWrappingAndPreserveUnicode() => Run(() =>
    {
        var text = string.Concat(Enumerable.Repeat("Synthetic stack frame 😀 with wrapping words\r\n", 140));
        using var projection = Project([Row(1, text)]);
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view, width: 640);
        try
        {
            var message = (Control)Member(Container(view, 1), "_message")!;
            var typeface = (Typeface)Member(message, "Typeface")!;
            var size = (double)Member(message, "FontSize")!;
            using var complete = new global::Avalonia.Media.TextFormatting.TextLayout(text, typeface, size, Brushes.Transparent,
                textWrapping: TextWrapping.Wrap, maxWidth: message.Bounds.Width, lineHeight: 20);
            Assert.Equal(complete.Height, Container(view, 1).Bounds.Height);
            var segments = ((IEnumerable)Member(message, "_segments")!).Cast<object>().ToArray();
            Assert.Equal(text.Length, segments.Sum(segment => (int)Member(segment, "Length")!));
            foreach (var segment in segments.Skip(1))
            {
                var offset = (int)Member(segment, "Offset")!;
                Assert.False(char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]));
                Assert.False(text[offset - 1] == '\r' && text[offset] == '\n');
            }
        }
        finally { window.Close(); }
    });

    /// <summary>Expanded reading retains a text offset through a fresh leased projection and a width change.</summary>
    [Fact]
    public Task PausedExpandedReadingRetainsTextAnchorAcrossProjectionAndResize() => Run(() =>
    {
        var text = string.Concat(Enumerable.Repeat("Synthetic frame with enough words to wrap\n", 4000));
        using var initial = Project([Row(1, text)]);
        using var next = Project([Row(1, text), Row(2)]);
        var view = new ConsoleListView { Projection = initial, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view);
        try
        {
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 20405); Flush(window);
            var before = (ConsoleReadingAnchor)Call(Host(view), "CaptureAnchor")!;
            Assert.True(before.TextOffset > 0);
            view.Projection = next; Flush(window);
            var after = (ConsoleReadingAnchor)Call(Host(view), "CaptureAnchor")!;
            Assert.Equal(before.RowId, after.RowId);
            Assert.InRange(Math.Abs(before.PixelOffset - after.PixelOffset), 0, 20);
            window.Width = 640; Flush(window);
            var resized = (ConsoleReadingAnchor)Call(Host(view), "CaptureAnchor")!;
            Assert.Equal(before.TextOffset, resized.TextOffset);
        }
        finally { window.Close(); }
    });

    /// <summary>Changing theme updates existing segment paint while preserving realized row identity.</summary>
    [Fact]
    public Task ThemeChangeRefreshesTextAndSearchPaintWithoutReplacingRows() => Run(() =>
    {
        using var projection = Project([Row(1, "first message\nsecond message", source: "export", level: LogLevel.Error,
            hits: [new(ConsoleSearchArea.Message, 0, 5), new(ConsoleSearchArea.Source, 0, 6)])]);
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view);
        try
        {
            var row = Container(view, 1);
            var message = Member(row, "_message")!;
            using var light = window.CaptureRenderedFrame();
            var before = Member(Member(message, "_key")!, "Foreground");
            window.RequestedThemeVariant = ThemeVariant.Dark; Flush(window);
            using var dark = window.CaptureRenderedFrame();
            Assert.NotNull(light);
            Assert.NotNull(dark);
            Assert.Same(row, Container(view, 1));
            Assert.NotSame(before, Member(Member(message, "_key")!, "Foreground"));
            Assert.Equal(ThemeVariant.Dark, Member(Member(message, "_key")!, "ThemeVariant"));
        }
        finally { window.Close(); }
    });

    /// <summary>Discarding offscreen text layouts preserves collapsed ellipsis when the segment becomes visible again.</summary>
    [Fact]
    public Task RecreatedCollapsedSegmentKeepsEllipsisAndSingleLine() => Run(() =>
    {
        using var projection = Project([Row(1, new string('W', 500), source: "export")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var message = (Control)Member(Container(view, 1), "_message")!;
            Call(message, "ClearLayouts");
            message.InvalidateVisual();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var layout = Assert.IsType<global::Avalonia.Media.TextFormatting.TextLayout>(((IDictionary)Member(message, "_visible")!)[0]);
            Assert.Single(layout.TextLines);
            Assert.True(layout.TextLines[0].HasCollapsed);
        }
        finally { window.Close(); }
    });

    private sealed class TrackedContent(string text) : ILogTextContent
    {
        public int LargestRead { get; private set; }
        public int Length => text.Length;
        public int ResidentCharacterCount => text.Length;
        public long Version => 0;
        public void Read(int offset, Span<char> destination)
        {
            LargestRead = Math.Max(LargestRead, destination.Length);
            text.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        public void Dispose() { }
    }
}
