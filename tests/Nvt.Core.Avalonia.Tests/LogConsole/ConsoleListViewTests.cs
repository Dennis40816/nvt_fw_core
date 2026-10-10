// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
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
    private static readonly bool[] _variants = [false, true];
    private static readonly string[] _scenes = ["main", "expanded", "error", "search", "paused"];

    private async Task RunAsync(Action action)
    {
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await fixture.Session.Dispatch(() =>
        {
            using var contents = new RowContentScope();
            action();
        }, safety.Token);
    }

    private static Control Element(ConsoleRowPresenter row, string name) => name switch
    {
        "_message" => row.MessageText,
        "_source" => row.SourceText,
        "_time" => row.TimeLabel,
        "_icon" => row.IconLabel,
        "_level" => row.LevelLabel,
        "_count" => row.CountLabel,
        "_arrow" => row.ArrowLabel,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
    private static ConsoleItemsHost Host(ConsoleListView view) => view.GetVisualDescendants().OfType<ConsoleItemsHost>().Single(p => p.Name == "PART_ItemsHost");
    private static ConsoleItemsHost Scroll(ConsoleListView view) => Host(view);
    private static ConsoleRowId Id(Control row) => ((ConsoleRowPresenter)row).RowId!.Value;
    private static ConsoleRowPresenter Container(ConsoleListView view, long id) => (ConsoleRowPresenter)Host(view).Children.Single(row => Id(row).Value == id);
    private static void Flush(Window window)
    {
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        for (var i = 0; i < 6; i++) { safety.Token.ThrowIfCancellationRequested(); window.Dispatcher.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }
    // Each dispatched test owns synthetic row content until its window and projections have been released.
    [ThreadStatic]
    private static RowContentScope? _rowContents;
    private sealed class RowContentScope : IDisposable
    {
        private readonly List<ILogTextContent> _contents = [];
        internal RowContentScope() => _rowContents = this;
        internal void Own(ILogTextContent content) => _contents.Add(content);
        public void Dispose()
        {
            _rowContents = null;
            foreach (var content in _contents) content.Dispose();
        }
    }
    private static ConsoleRow Row(long id, string message = "Message", string source = "runtime-query",
        LogLevel level = LogLevel.Info, int count = 1, ImmutableArray<ConsoleSearchHit> hits = default)
    {
        InMemoryLogTextContent? content = new(message);
        try
        {
            var row = new ConsoleRow(new(id), level, source, content, 0, null,
                Enumerable.Range(0, count).Select(n => id + n).ToImmutableArray(), _instant.AddSeconds(-id),
                _instant.AddSeconds(-id), id, hits.IsDefault ? [] : hits, "ignored");
            _rowContents!.Own(content);
            content = null; // The test scope now owns disposal; failed construction stays locally owned.
            return row;
        }
        finally { content?.Dispose(); }
    }
    private static readonly DateTimeOffset _instant = new(2026, 1, 1, 14, 32, 15, TimeSpan.Zero);
    private static ConsoleProjection Project(IEnumerable<ConsoleRow> rows, int newCount = 0, long evicted = 0,
        ConsoleRowId? successor = null, DateTimeOffset? timeBase = null, DateTimeOffset? capturedAt = null)
    {
        var array = rows.ToImmutableArray();
        return new()
        {
            Version = array.Length,
            Generation = 1,
            LastSequence = array.IsEmpty ? 0 : array.Max(row => row.LastSequence),
            CapturedAt = capturedAt ?? _instant.AddHours(1),
            TimeBase = timeBase ?? _instant,
            Rows = array,
            LevelCounts = ImmutableDictionary<LogLevel, int>.Empty,
            Sources = [],
            SourceCounts = ImmutableDictionary<string, int>.Empty,
            RetainedMembership = array.ToImmutableDictionary(row => row.Id, row => row.MemberSequences),
            EventCount = array.Sum(row => row.Count),
            NewSincePauseCount = newCount,
            EvictedCount = evicted,
            Deduplicate = false,
            ResolvedAnchorId = successor,
        };
    }
    private static ConsoleProjection Many(int count, int first = 1) =>
        Project(Enumerable.Range(first, count).Select(i => Row(i, $"Message {i}")));
    private static Window Window(ConsoleListView view, double width = 1200, double height = 280, bool dark = false, bool square = false)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
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
    public Task CollapsedRowsHaveTwentyDipHeight() => RunAsync(() =>
    {
        using var projection = Many(30);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try { Assert.All(Host(view).Children, row => Assert.Equal(20, row.Bounds.Height)); }
        finally { window.Close(); }
    });

    /// <summary>Expanded height starts as an estimate and is corrected by bounded text measurement.</summary>
    [Fact]
    public Task ExpandedEstimateIsUpdatedAfterMeasure() => RunAsync(() =>
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
    public Task ContainerIdentitySurvivesAppendTrimAndExpansion() => RunAsync(() =>
    {
        using var initial = Many(10);
        using var appended = Many(11);
        using var trimmed = Many(10, 2);
        var view = new ConsoleListView { Projection = initial };
        var window = Window(view);
        try
        {
            var row = Container(view, 5);
            var source = Host(view).ItemsSource;
            view.Projection = appended; Flush(window);
            Assert.Same(row, Container(view, 5));
            view.Projection = trimmed; Flush(window);
            Assert.Same(row, Container(view, 5));
            view.ViewState = view.ViewState with { ExpandedIds = [new(5)] }; Flush(window);
            Assert.Same(row, Container(view, 5));
            view.ViewState = view.ViewState with { ExpandedIds = [] }; Flush(window);
            Assert.Same(row, Container(view, 5));
            Assert.Same(source, Host(view).ItemsSource);
        }
        finally { window.Close(); }
    });

    /// <summary>A large list bounds realized containers and recycles during arbitrary scrolling.</summary>
    [Fact]
    public Task TenThousandRowsUseBoundedContainersAndStableSource() => RunAsync(() =>
    {
        using var projection = Many(10000);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var source = Host(view).ItemsSource;
            var seen = new HashSet<Control>(Host(view).Children);
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Assert.All(Enumerable.Range(0, 21), i =>
            {
                Scroll(view).Offset = new(0, i * 9500); Flush(window);
                Assert.InRange(Host(view).Children.Count, 1, 18);
                Assert.Same(source, Host(view).ItemsSource);
                seen.UnionWith(Host(view).Children);
            });
            Assert.InRange(seen.Count, 1, 36);
        }
        finally { window.Close(); }
    });

    /// <summary>Hundreds of thousands of characters never become one giant text layout or per-line visuals.</summary>
    [Fact]
    public Task HugeExpandedMessageVirtualizesSegmentsAndReachesEnd() => RunAsync(() =>
    {
        using var content = new TrackedContent(string.Concat(Enumerable.Repeat("at Synthetic.Frame.Run()\r\n", 20000)) + "END OF TRACE");
        using var projection = Project([Row(1) with { TextContent = content }]);
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view, height: 280);
        try
        {
            Assert.True(Scroll(view).Extent.Height > 300000);
            Assert.True(Scroll(view).Offset.Y >= Scroll(view).Extent.Height - Scroll(view).Viewport.Height - 1);
            Assert.InRange(view.GetVisualDescendants().OfType<TextBlock>().Count(), 1, 8);
            var message = Container(view, 1).MessageText;
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.InRange(message.VisibleLayouts.Count, 1, 4);
            Assert.Equal(2048, content.LargestRead);
            Assert.True((int)message.TextOffsetAt(Scroll(view).Offset.Y)! > content.Length - 4096);
        }
        finally { window.Close(); }
    });

    /// <summary>Follow keeps the newest row visible on append.</summary>
    [Fact]
    public Task FollowingAppendsRemainAtEnd() => RunAsync(() =>
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
    public Task ScrollAwayRequestsPauseWithoutMutatingState() => RunAsync(() =>
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
    public Task PausedProjectionUpdatesKeepReadingAnchorWithinOneRowHeight() => RunAsync(() =>
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
    public Task EvictedAnchorMovesToSuccessorAndShowsRetentionNotice() => RunAsync(() =>
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
            Assert.Equal(string.Format(view.TimeOptions.Culture,
                Resource<string>(view, "Nvt.Console.List.RetentionFormat"), 40), notice.Text);
        }
        finally { window.Close(); }
    });

    /// <summary>Reaching the end, the button and the public command all request resume.</summary>
    [Fact]
    public Task EndButtonAndJumpMethodRequestResume() => RunAsync(() =>
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
    public Task ExpansionRequestsPauseAndPreservesCallerOwnedFacts() => RunAsync(() =>
    {
        using var projection = Project([Row(1, "first\nsecond"), Row(2)]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            ConsoleViewState? requested = null;
            var original = view.ViewState;
            view.ViewStateRequested += (_, state) => requested = state;
            Click(window, Container(view, 1).MessageText);
            Assert.Same(original, view.ViewState);
            Assert.IsType<ConsoleFollow.Paused>(requested!.Follow);
            Assert.Contains(new ConsoleRowId(1), requested.ExpandedIds);
            view.ViewState = requested; Flush(window);
            Click(window, (Control)Container(view, 1).ArrowLabel);
            Assert.DoesNotContain(new ConsoleRowId(1), requested.ExpandedIds);
        }
        finally { window.Close(); }
    });

    /// <summary>Viewport width controls every row and no horizontal scrolling is enabled.</summary>
    [Theory]
    [InlineData(640, false, false)]
    [InlineData(960, false, false)]
    [InlineData(1200, false, false)]
    [InlineData(640, true, false)]
    [InlineData(960, true, false)]
    [InlineData(1200, true, false)]
    [InlineData(640, false, true)]
    [InlineData(960, false, true)]
    [InlineData(1200, false, true)]
    [InlineData(640, true, true)]
    [InlineData(960, true, true)]
    [InlineData(1200, true, true)]
    public Task LayoutUsesViewportInEveryThemeAndShape(int width, bool dark, bool square) => RunAsync(() =>
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
            var source = ((ConsoleRowPresenter)Host(view).Children[0]).SourceText;
            Assert.Equal("A very long synthetic source name", ToolTip.GetTip(source));
            Assert.True((bool)source.Truncated);
            Assert.All(view.GetVisualDescendants().OfType<ScrollBar>().Where(s => s.Orientation == global::Avalonia.Layout.Orientation.Horizontal),
                bar => Assert.False(bar.IsVisible));
        }
        finally { window.Close(); }
    });

    /// <summary>Relative text ignores captured-at changes and uses the projection's pause clock.</summary>
    [Fact]
    public Task RelativeTimeUsesFrozenProjectionTimeBase() => RunAsync(() =>
    {
        using var initial = Project([Row(1)], timeBase: _instant);
        using var next = Project([Row(1)], timeBase: _instant, capturedAt: _instant.AddHours(2));
        var view = new ConsoleListView
        {
            Projection = initial,
            TimeMode = ConsoleTimeMode.Relative,
            TimeOptions = new() { RelativeTimeTemplate = "−{0} s" }
        };
        var window = Window(view);
        try
        {
            view.ViewState = view.ViewState.Pause(initial, new(1)); Flush(window);
            var time = (TextBlock)Container(view, 1).TimeLabel;
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
    public Task ArrowAndRepeatCountReflectRenderedContent() => RunAsync(() =>
    {
        using var projection = Project([Row(1, "short"), Row(2, "first\nsecond"), Row(3, new string('W', 300), count: 4)]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            Assert.False(((Control)Container(view, 1).ArrowLabel).IsVisible);
            Assert.True(((Control)Container(view, 2).ArrowLabel).IsVisible);
            Assert.True(((Control)Container(view, 3).ArrowLabel).IsVisible);
            Assert.Equal("×4", ((TextBlock)Container(view, 3).CountLabel).Text);
            Assert.Equal(string.Empty, ((TextBlock)Container(view, 1).CountLabel).Text);
        }
        finally { window.Close(); }
    });

    /// <summary>Reattachment reuses externally held reading state and releases every old view.</summary>
    [Fact]
    public Task AttachDetachAndReattachOneHundredTimesLeavesNoReachableOldView() => RunAsync(() =>
    {
        var references = DetachedViews();
        Assert.All(Enumerable.Range(0, 3), _ => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); });
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
            Assert.Equal(new ConsoleRowId(15), ((ConsoleReadingAnchor)Host(view).CaptureAnchor()!).RowId);
            window.Content = null; Flush(window);
            result.Add(new(view));
        }
        window.Close();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.Dispatcher.RunJobs();
        return result;
    }

    /// <summary>Renders every theme and shape with fourteen complete collapsed rows.</summary>
    [Fact]
    public Task ListFramesRenderForLightDarkPillAndSquare() => RunAsync(() =>
    {
        Assert.All(_variants, dark =>
            Assert.All(_variants, square =>
                Assert.All(_scenes, scene =>
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
                    state = scene switch
                    {
                        "expanded" => state with { ExpandedIds = [new(7)] },
                        "paused" => state.Pause(projection, new(1)),
                        _ => state,
                    };
                    var view = new ConsoleListView { Projection = projection, ViewState = state };
                    var window = Window(view, height: 420, dark: dark, square: square);
                    try
                    {
                        // Reserve only the other slices' geometry; K2b does not build their header, toolbar or status bar.
                        var shell = new Grid { RowDefinitions = new RowDefinitions("120,280,20") };
                        window.Content = null;
                        Grid.SetRow(view, 1); shell.Children.Add(view); window.Content = shell; Flush(window);
                        AssertMainSceneRows(view, scene);
                        using var frame = window.CaptureRenderedFrame();
                        Assert.NotNull(frame);
                        Assert.Equal(new PixelSize(1200, 420), frame.PixelSize);
                    }
                    finally { window.Close(); }
                })));
    });

    private static void AssertMainSceneRows(ConsoleListView view, string scene)
    {
        if (scene == "main") Assert.Equal(14, Host(view).Children.Count(row => row.Bounds.Y >= 0 && row.Bounds.Bottom <= 280));
    }

    /// <summary>Segment boundaries preserve wrapping, surrogate pairs and original CR/LF content.</summary>
    [Fact]
    public Task ExpandedSegmentsMatchWholeTextWrappingAndPreserveUnicode() => RunAsync(() =>
    {
        var text = string.Concat(Enumerable.Repeat("Synthetic stack frame 😀 with wrapping words\r\n", 140));
        using var projection = Project([Row(1, text)]);
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view, width: 640);
        try
        {
            var message = Container(view, 1).MessageText;
            var typeface = (Typeface)message.Typeface;
            var size = (double)message.FontSize;
            using var complete = new global::Avalonia.Media.TextFormatting.TextLayout(text, typeface, size, Brushes.Transparent,
                textWrapping: TextWrapping.Wrap, maxWidth: message.Bounds.Width, lineHeight: 20);
            Assert.Equal(complete.Height, Container(view, 1).Bounds.Height);
            var segments = message.Segments.ToArray();
            Assert.Equal(text.Length, segments.Sum(segment => (int)segment.Length));
            Assert.All(segments.Skip(1), segment =>
            {
                var offset = (int)segment.Offset;
                Assert.False(char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]));
                Assert.False(text[offset - 1] == '\r' && text[offset] == '\n');
            });
        }
        finally { window.Close(); }
    });

    /// <summary>Expanded reading retains a text offset through a fresh leased projection and a width change.</summary>
    [Fact]
    public Task PausedExpandedReadingRetainsTextAnchorAcrossProjectionAndResize() => RunAsync(() =>
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
            var before = (ConsoleReadingAnchor)Host(view).CaptureAnchor()!;
            Assert.True(before.TextOffset > 0);
            view.Projection = next; Flush(window);
            var after = (ConsoleReadingAnchor)Host(view).CaptureAnchor()!;
            Assert.Equal(before.RowId, after.RowId);
            Assert.InRange(Math.Abs(before.PixelOffset - after.PixelOffset), 0, 20);
            window.Width = 640; Flush(window);
            var resized = (ConsoleReadingAnchor)Host(view).CaptureAnchor()!;
            Assert.Equal(before.TextOffset, resized.TextOffset);
        }
        finally { window.Close(); }
    });

    /// <summary>Changing theme updates existing segment paint while preserving realized row identity.</summary>
    [Fact]
    public Task ThemeChangeRefreshesTextAndSearchPaintWithoutReplacingRows() => RunAsync(() =>
    {
        using var projection = Project([Row(1, "first message\nsecond message", source: "export", level: LogLevel.Error,
            hits: [new(ConsoleSearchArea.Message, 0, 5), new(ConsoleSearchArea.Source, 0, 6)])]);
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view);
        try
        {
            var row = Container(view, 1);
            var message = row.MessageText;
            using var light = window.CaptureRenderedFrame();
            var before = message.CurrentLayout!.Foreground;
            window.RequestedThemeVariant = ThemeVariant.Dark; Flush(window);
            using var dark = window.CaptureRenderedFrame();
            Assert.NotNull(light);
            Assert.NotNull(dark);
            Assert.Same(row, Container(view, 1));
            Assert.NotSame(before, message.CurrentLayout!.Foreground);
            Assert.Equal(ThemeVariant.Dark, message.CurrentLayout!.ThemeVariant);
        }
        finally { window.Close(); }
    });

    /// <summary>Discarding offscreen text layouts preserves collapsed ellipsis when the segment becomes visible again.</summary>
    [Fact]
    public Task RecreatedCollapsedSegmentKeepsEllipsisAndSingleLine() => RunAsync(() =>
    {
        using var projection = Project([Row(1, new string('W', 500), source: "export")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var message = Container(view, 1).MessageText;
            message.ClearLayouts();
            message.InvalidateVisual();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var layout = Assert.IsType<global::Avalonia.Media.TextFormatting.TextLayout>(message.VisibleLayouts[0]);
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
