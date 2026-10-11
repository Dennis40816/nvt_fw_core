// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Nvt.Core.Avalonia.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleLinkPaintTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchForegroundWinsOverHoveredLink(bool dark)
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        scene.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        scene.Controller.SetSearchText("example");
        ConsoleInteractionWindow.Pump(scene.Window);
        var presenter = scene.Row(1).MessageText;
        presenter.SetHoveredTarget(scene.Controller.GetLinks(scene.Controller.Projection.Rows[0]).Spans[0].Target);
        var commands = Render(presenter);
        var glyphs = commands.OfType<GlyphRunDrawing>().ToArray();
        Assert.Contains(glyphs, glyph => Equals(glyph.Foreground, Brush(presenter, "NfcAccentBrush")));
        Assert.Equal(Brush(presenter, "NfcWarningTextStrongBrush"), glyphs[^1].Foreground);
        Assert.IsType<GlyphRunDrawing>(commands[^1]);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestingLinkUsesSoftBorderUnderline(bool dark)
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        scene.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        ConsoleInteractionWindow.Pump(scene.Window);
        var presenter = scene.Row(1).MessageText;
        presenter.SetHoveredTarget(null);
        var underline = Assert.Single(Render(presenter).OfType<GeometryDrawing>(), drawing => drawing.Pen is not null);
        Assert.Equal(Brush(presenter, "NfcBorderSoftBrush"), underline.Pen!.Brush);
    }

    private static IBrush Brush(Control control, string key)
    {
        Assert.True(control.TryFindResource(key, control.ActualThemeVariant, out var value));
        return Assert.IsAssignableFrom<IBrush>(value);
    }
    private static Drawing[] Render(ConsoleTextPresenter presenter)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) presenter.Render(context);
        return Flatten(drawing).ToArray();
    }
    private static IEnumerable<Drawing> Flatten(Drawing drawing) => drawing is DrawingGroup group
        ? group.Children.SelectMany(Flatten) : [drawing];
}
