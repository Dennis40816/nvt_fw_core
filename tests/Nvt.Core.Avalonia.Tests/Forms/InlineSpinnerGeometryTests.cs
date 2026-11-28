// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using Path = Avalonia.Controls.Shapes.Path;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Forms;

/// <summary>Measures stroked Skia glyphs rather than their layout boxes.</summary>
public sealed class InlineSpinnerGeometryTests(ITestOutputHelper output)
{
    /// <summary>Both spinner placements share the ComboBox edge inset in every palette and shape.</summary>
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void SpinnerGlyphsShareTheEdgeInset(bool dark, bool square, bool unit)
    {
        var right = Number(Location.Right, unit);
        var left = Number(Location.Left, unit);
        var combo = (ComboBox)InlineInputTestHost.Sample("combobox");
        var scrubber = InlineScrubberTestHost.Sample();
        var panel = new StackPanel
        {
            Width = 240,
            Margin = new Thickness(24),
            Spacing = 16,
            Children = { right, left, combo, scrubber }
        };
        Window host = InlineScrubberTestHost.Create(panel, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            FormsTestHost.Show(host);
            Rect chevron = Glyph(combo.GetVisualDescendants().OfType<Path>().Single(), combo);
            Rect grip = Bounds(Part<Border>(scrubber, "InlineGrip"), scrubber);
            double inset = combo.Bounds.Width - chevron.Right;
            output.WriteLine($"ComboBox glyph={chevron}; right inset={inset:F3}; scrubber grip={grip}; right inset={scrubber.Bounds.Width - grip.Right:F3}");
            MeasureSpinner(right, "Right");
            MeasureSpinner(left, "Left");
            AssertStatesKeepGeometry(host, right, inset);
            AssertStatesKeepGeometry(host, left, inset);
            right.Width = left.Width = 112;
            right.Maximum = left.Maximum = decimal.MaxValue;
            right.Value = left.Value = 1234567890.125m;
            Flush(host);
            AssertStatesKeepGeometry(host, right, inset);
            AssertStatesKeepGeometry(host, left, inset);
        }
        finally { host.Close(); }
    }

    /// <summary>Loading the optional sheet and removing its class restores identical default pixels.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnclassifiedControlsKeepTheirPixels(bool dark, bool square)
    {
        var right = Number(Location.Right, true);
        var left = Number(Location.Left, true);
        var combo = (ComboBox)InlineInputTestHost.Sample("combobox");
        var scrubber = InlineScrubberTestHost.Sample();
        var panel = new StackPanel
        {
            Width = 240,
            Margin = new Thickness(24),
            Spacing = 16,
            Children = { right, left, combo, scrubber }
        };
        RemoveInlineClasses(panel);
        Window host = InlineScrubberTestHost.Create(panel, dark, inline: false);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            FormsTestHost.Show(host);
            AssertDefaultPixels(host, panel);
        }
        finally { host.Close(); }
    }

    private static void RemoveInlineClasses(Panel panel)
    {
        foreach (Control control in panel.Children)
        {
            control.Classes.Remove("inlineInput");
            if (control is NumericUpDown { InnerRightContent: Control unit }) unit.Classes.Remove("inlineInputUnit");
        }
    }

    private static void AssertDefaultPixels(Window host, Panel panel)
    {
        foreach (FormState state in FormsTestHost.States.Concat(FormsTestHost.ReadOnlyStates))
        {
            foreach (TemplatedControl control in panel.Children.OfType<TemplatedControl>())
                FormsTestHost.SetState(control, state);
            Flush(host);
            byte[] before = Pixels(host);
            InlineInputTestHost.Include(host);
            Flush(host);
            Assert.Equal(before, Pixels(host));
            foreach (Control control in panel.Children) control.Classes.Add("inlineInput");
            Flush(host);
            RemoveInlineClasses(panel);
            Flush(host);
            Assert.Equal(before, Pixels(host));
            host.Styles.RemoveAt(host.Styles.Count - 1);
        }
    }

    private static byte[] Pixels(Window host)
    {
        using var frame = new RenderTargetBitmap(new PixelSize((int)host.Width, (int)host.Height), new Vector(96, 96));
        frame.Render(host);
        byte[] bytes = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
        GCHandle pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(frame.PixelSize), pinned.AddrOfPinnedObject(), bytes.Length, frame.PixelSize.Width * 4); }
        finally { pinned.Free(); }
        return bytes;
    }

    private static void AssertStatesKeepGeometry(Window host, NumericUpDown numeric, double inset)
    {
        Control presenter = Part<TextPresenter>(numeric, "PART_TextPresenter");
        Rect text = Bounds(presenter, numeric);
        Rect viewport = Bounds(Part<ScrollViewer>(numeric, "PART_ScrollViewer"), numeric);
        Rect? unit = numeric.InnerRightContent is TextBlock label ? Bounds(label, numeric) : null;
        foreach (FormState state in new FormState[]
        {
            new("Rest"), new("Hover", Hover: true), new("Pointer editing", Focus: true),
            new("Keyboard focus", Focus: true), new("Error", Error: true, Focus: true),
            new("Read-only", ReadOnly: true), new("Disabled", Disabled: true),
        })
        {
            InlineInputTestHost.State(numeric, state, keyboardFocus: state.Name != "Pointer editing");
            Flush(host);
            AssertSpinnerInset(numeric, inset);
            Assert.Equal(text, Bounds(presenter, numeric));
            Assert.Equal(viewport, Bounds(Part<ScrollViewer>(numeric, "PART_ScrollViewer"), numeric));
            AssertUnit(numeric, unit, viewport);
        }
        InlineInputTestHost.State(numeric, new("Rest"));
        Flush(host);
    }

    private static void AssertUnit(NumericUpDown numeric, Rect? expected, Rect viewport)
    {
        if (numeric.InnerRightContent is not TextBlock unit) return;
        Rect bounds = Bounds(unit, numeric);
        Assert.Equal(expected, bounds);
        Assert.Equal(unit.DesiredSize.Width, unit.Bounds.Width);
        Assert.True(viewport.Width > 0);
        Assert.True(viewport.Right <= bounds.Left);
        Assert.True(bounds.Right <= numeric.Bounds.Width - 1);
        foreach (RepeatButton button in numeric.GetVisualDescendants().OfType<RepeatButton>())
            Assert.False(bounds.Intersects(Bounds(button, numeric)));
    }

    private void MeasureSpinner(NumericUpDown numeric, string placement)
    {
        foreach (string name in new[] { "PART_IncreaseButton", "PART_DecreaseButton" })
        {
            RepeatButton button = Part<RepeatButton>(numeric, name);
            Rect glyph = Glyph(button.GetVisualDescendants().OfType<Path>().Single(), numeric);
            output.WriteLine($"{placement}/{name}: glyph={glyph}; left={glyph.Left:F3}; right={numeric.Bounds.Width - glyph.Right:F3}; hit={Bounds(button, numeric)}");
        }
    }

    private static void AssertSpinnerInset(NumericUpDown numeric, double inset)
    {
        foreach (Path path in numeric.GetVisualDescendants().OfType<Path>())
        {
            Rect glyph = Glyph(path, numeric);
            double actual = numeric.ButtonSpinnerLocation == Location.Left ? glyph.Left : numeric.Bounds.Width - glyph.Right;
            Assert.InRange(actual, inset - 0.5, inset + 0.5);
            Assert.InRange(actual, 8, 10);
        }
        foreach (RepeatButton button in numeric.GetVisualDescendants().OfType<RepeatButton>())
        {
            Assert.True(button.Bounds.Width >= 28);
            Assert.True(button.Bounds.Height >= 15);
        }
    }

    private static NumericUpDown Number(Location location, bool unit)
    {
        var numeric = (NumericUpDown)InlineInputTestHost.Sample("numericupdown");
        numeric.ButtonSpinnerLocation = location;
        if (unit) numeric.InnerRightContent = new TextBlock { Text = "mm", Classes = { "inlineInputUnit" }, VerticalAlignment = VerticalAlignment.Center };
        return numeric;
    }

    private static Rect Bounds(Control part, Control owner) => new(part.TranslatePoint(default, owner)!.Value, part.Bounds.Size);

    private static Rect Glyph(Path path, Control owner)
    {
        var pen = new Pen(path.Stroke, path.StrokeThickness, lineCap: path.StrokeLineCap, lineJoin: path.StrokeJoin);
        Rect ink = path.RenderedGeometry!.GetRenderBounds(pen);
        return ink.Translate(path.TranslatePoint(default, owner)!.Value - default(Point));
    }
}
