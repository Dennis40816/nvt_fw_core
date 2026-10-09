// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Icons;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed partial class ConsoleListViewTests
{
    private static readonly string[] FirstLineMetadata = ["_icon", "_level", "_source", "_count", "_arrow"];
    private static readonly string[] SearchPresenters = ["_message", "_source"];
    private static T Resource<T>(Control owner, string key)
    {
        Assert.True(owner.TryFindResource(key, owner.ActualThemeVariant, out var value), key);
        return Assert.IsAssignableFrom<T>(value);
    }

    /// <summary>Every list dimension resolves, and resource overrides drive measured row geometry.</summary>
    [Fact]
    public Task ListGeometryResourcesExistAndPresentersConsumeOverrides() => Run(() =>
    {
        using var projection = Project([Row(1, "first\nsecond", count: 2)]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var expected = new Dictionary<string, double>
            {
                ["RowHeight"] = 20, ["TimeWidth"] = 104, ["LevelWidth"] = 84, ["SourceWidth"] = 120,
                ["RepeatWidth"] = 48, ["ArrowWidth"] = 24, ["DragThreshold"] = 4, ["RetentionHeight"] = 20,
            };
            foreach (var (key, value) in expected)
                Assert.Equal(value, UiResourceResolver.GetDouble(view, "Nvt.Console.List." + key, double.NaN));
            Assert.Equal(16, UiResourceResolver.GetDouble(view, "Nvt.Font.Icon.Size"));
            Assert.Equal(4, UiResourceResolver.GetDouble(view, "NfcSpace4"));
            Assert.Equal(32, view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Jump").Height);
            Assert.Equal(new Thickness(16, 0), Resource<Thickness>(view, "Nvt.Console.List.RowPadding"));
            Assert.Equal(new Thickness(16, 0), Resource<Thickness>(view, "Nvt.Console.List.RetentionMargin"));
            Assert.Equal(new Thickness(16), Resource<Thickness>(view, "Nvt.Console.List.JumpMargin"));
            view.Resources["Nvt.Console.List.RowHeight"] = 24d;
            view.Resources["Nvt.Console.List.TimeWidth"] = 112d;
            view.Resources["Nvt.Console.List.LevelWidth"] = 88d;
            view.Resources["Nvt.Console.List.SourceWidth"] = 128d;
            view.Resources["Nvt.Console.List.RepeatWidth"] = 52d;
            view.Resources["Nvt.Console.List.ArrowWidth"] = 28d;
            view.Resources["Nvt.Font.Icon.Size"] = 18d;
            view.Resources["NfcSpace4"] = 6d;
            view.Resources["Nvt.Console.List.RowPadding"] = new Thickness(18, 0);
            view.TimeMode = ConsoleTimeMode.Relative; Flush(window);
            var row = Container(view, 1);
            Assert.Equal(24, row.Bounds.Height);
            Assert.Equal(24, Scroll(view).ScrollSize.Height);
            Assert.Equal(new Rect(18, 0, 112, 24), ((Control)Member(row, "_time")!).Bounds);
            Assert.Equal(new Rect(130, 0, 18, 24), ((Control)Member(row, "_icon")!).Bounds);
            Assert.Equal(new Rect(154, 0, 64, 24), ((Control)Member(row, "_level")!).Bounds);
            Assert.Equal(new Rect(218, 0, 128, 24), ((Control)Member(row, "_source")!).Bounds);
            var message = (Control)Member(row, "_message")!;
            Assert.Equal(row.Bounds.Width - 18 - 346 - 52 - 28, message.Bounds.Width);
            Assert.Equal(52, ((Control)Member(row, "_count")!).Bounds.Width);
            Assert.Equal(18, ((Control)Member(row, "_arrow")!).Bounds.Width);
            Assert.Equal(24, ((TextBlock)Member(row, "_time")!).LineHeight);
        }
        finally { window.Close(); }
    });

    /// <summary>Collapsed and expanded rows use the exact columns and align metadata with the first message line.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExactColumnGeometryKeepsExpandedMetadataOnFirstLine(bool hiddenTime) => Run(() =>
    {
        using var projection = Project([Row(1, "first\nsecond\nthird", count: 3)]);
        var view = new ConsoleListView { Projection = projection, TimeMode = hiddenTime ? ConsoleTimeMode.Hidden : ConsoleTimeMode.Absolute };
        var window = Window(view);
        try
        {
            Assert.Equal(20, Container(view, 1).Bounds.Height);
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Click(window, (Control)Member(Container(view, 1), "_message")!); Flush(window);
            var row = Container(view, 1);
            Assert.Equal(60, row.Bounds.Height);
            var shift = hiddenTime ? 104 : 0;
            if (!hiddenTime) Assert.Equal(new Rect(16, 0, 104, 20), ((Control)Member(row, "_time")!).Bounds);
            Assert.Equal(new Rect(120 - shift, 0, 16, 20), ((Control)Member(row, "_icon")!).Bounds);
            Assert.Equal(new Rect(140 - shift, 0, 64, 20), ((Control)Member(row, "_level")!).Bounds);
            Assert.Equal(new Rect(204 - shift, 0, 120, 20), ((Control)Member(row, "_source")!).Bounds);
            var message = (Control)Member(row, "_message")!;
            Assert.Equal(new Rect(324 - shift, 0, row.Bounds.Width - 412 + shift, 60), message.Bounds);
            Assert.Equal(new Rect(row.Bounds.Width - 88, 0, 48, 20), ((Control)Member(row, "_count")!).Bounds);
            Assert.Equal(new Rect(row.Bounds.Width - 36, 0, 16, 20), ((Control)Member(row, "_arrow")!).Bounds);
            Assert.All(FirstLineMetadata, name =>
                Assert.Equal(message.Bounds.Y, ((Control)Member(row, name)!).Bounds.Y));
        }
        finally { window.Close(); }
    });

    /// <summary>All six levels have catalog glyphs, text names and semantic colors; only Error/Fatal have danger backgrounds.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EveryLevelUsesCatalogNameColorAndErrorFatalBackground(bool dark) => Run(() =>
    {
        var levels = Enum.GetValues<LogLevel>();
        using var projection = Project(levels.Select((level, index) => Row(index + 1, level: level)));
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view, dark: dark);
        try
        {
            string[] glyphs = [NvtIcons.MoreHoriz, NvtIcons.BugReport, NvtIcons.Info, NvtIcons.Warning, NvtIcons.Error, NvtIcons.Cancel];
            string[] brushes = ["NfcTextMutedBrush", "NfcTextSecondaryBrush", "NfcInfoTextBrush", "NfcWarningTextBrush", "NfcDangerTextBrush", "NfcDangerTextStrongBrush"];
            for (var i = 0; i < levels.Length; i++)
            {
                var row = Container(view, i + 1);
                var icon = (TextBlock)Member(row, "_icon")!;
                var name = (TextBlock)Member(row, "_level")!;
                Assert.Equal(glyphs[i], icon.Text);
                Assert.Equal(levels[i].ToString(), name.Text);
                Assert.Equal(Resource<IBrush>(view, brushes[i]), icon.Foreground);
                Assert.Equal(icon.Foreground, name.Foreground);
                Assert.Equal(Resource<FontFamily>(view, "Nvt.Font.Icon.Family"), icon.FontFamily);
                Assert.Equal(Resource<double>(view, "Nvt.Font.Icon.Size"), icon.FontSize);
                Assert.Equal(Resource<FontWeight>(view, "Nvt.Font.Icon.Weight"), icon.FontWeight);
                if (levels[i] is LogLevel.Error or LogLevel.Fatal)
                    Assert.Equal(Resource<IBrush>(view, "NfcDangerSurfaceBrush"), ((Panel)row).Background);
                else Assert.Null(((Panel)row).Background);
            }
        }
        finally { window.Close(); }
    });

    private static IEnumerable<Drawing> Drawings(Drawing drawing) => drawing is DrawingGroup group
        ? group.Children.SelectMany(Drawings) : [drawing];

    /// <summary>Actual drawing commands paint hit backgrounds before ordinary and highlighted text, with no underline yet.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SearchPaintUsesThemeBrushesBeforeTextAndReservedUnderlineLayer(bool dark) => Run(() =>
    {
        using var projection = Project([Row(1, "hit plain", source: "hit source",
            hits: [new(ConsoleSearchArea.Message, 0, 3), new(ConsoleSearchArea.Source, 0, 3)])]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view, dark: dark);
        try
        {
            foreach (var name in SearchPresenters)
            {
                var presenter = (Control)Member(Container(view, 1), name)!;
                var drawing = new DrawingGroup();
                using (var context = drawing.Open()) presenter.Render(context);
                var commands = Drawings(drawing).ToArray();
                var background = Assert.IsType<GeometryDrawing>(commands[0]);
                Assert.Equal(Resource<IBrush>(view, "NfcWarningSurfaceBrush"), background.Brush);
                Assert.Null(background.Pen);
                Assert.True(background.GetBounds().Width > 0);
                var glyphs = commands.Skip(1).Select(command => Assert.IsType<GlyphRunDrawing>(command)).ToArray();
                Assert.True(glyphs.Length >= 2);
                Assert.Equal(Resource<IBrush>(view, name == "_message" ? "NfcTextBrush" : "NfcTextSecondaryBrush"), glyphs[0].Foreground);
                Assert.Equal(Resource<IBrush>(view, "NfcWarningTextStrongBrush"), glyphs[^1].Foreground);
                // The final underline layer is empty until links are introduced: no strokes over or under text.
                Assert.DoesNotContain(commands.OfType<GeometryDrawing>(), command => command.Pen is not null);
            }
        }
        finally { window.Close(); }
    });
}
