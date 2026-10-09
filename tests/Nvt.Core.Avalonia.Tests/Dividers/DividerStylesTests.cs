// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Progress;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.Progress;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Dividers.DividerTestHost;
using Path = Avalonia.Controls.Shapes.Path;

namespace Nvt.Core.Avalonia.Tests.Dividers;

/// <summary>Checks divider geometry, native input, progress projection, and shipped palette contrast.</summary>
public sealed class DividerStylesTests(ITestOutputHelper output)
{
    /// <summary>Pins both header heights, directions, chevrons, state precedence, focus, and text contrast.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExpanderStatesUseCoreGeometryAndContrast(bool dark, bool square)
    {
        double textMinimum = double.MaxValue, disabledMinimum = double.MaxValue;
        foreach (bool section in new[] { false, true })
        foreach (ExpandDirection direction in new[] { ExpandDirection.Down, ExpandDirection.Up })
        {
            var expander = new Expander { Header = "Details", Content = new Border { Height = 24 },
                ExpandDirection = direction, Width = 240, VerticalAlignment = VerticalAlignment.Center };
            if (section) expander.Classes.Add("section");
            Window host = Create(expander, dark);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Show(host);
                ToggleButton header = Header(expander);
                Assert.Equal("Details", header.Content);
                Assert.Equal("Details", ControlAutomationPeer.CreatePeerForElement(header)!.GetName());
                foreach (bool expanded in new[] { false, true })
                foreach (DividerState state in States)
                {
                    expander.IsExpanded = expanded;
                    expander.IsEnabled = !state.Disabled;
                    SetState(header, state);
                    Flush(host);
                    Assert.Equal(section ? 44 : 32, header.Bounds.Height);
                    Assert.Equal(226, header.Bounds.Width);
                    Assert.Equal(new Thickness(12, 0), header.Padding);
                    Assert.Equal(new Thickness(0), header.BorderThickness);
                    Assert.Equal(new Thickness(1), expander.BorderThickness);
                    Assert.Null(header.FocusAdorner);
                    Assert.Equal(new CornerRadius(square ? 6 : 999), header.CornerRadius);
                    Assert.Equal(expanded, Part<Border>(expander, "ExpanderContent").IsVisible);
                    Border divider = Part<Border>(expander, "ExpanderContent");
                    Assert.Equal(expanded, divider.IsVisible);
                    Assert.Equal(direction == ExpandDirection.Up ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0), divider.BorderThickness);
                    Assert.Equal(ResourceColor(header, "NfcDividerBrush"), ColorOf(divider.BorderBrush));
                    Assert.Equal(new Thickness(24, 12, 12, 12), divider.Padding);
                    string fill = !state.Disabled && state.Pressed ? "Nvt.Controls.ExpanderPressedBrush"
                        : !state.Disabled && state.Over ? "NfcSelectionSurfaceBrush" : "NfcSurfaceSubtleBrush";
                    string text = state.Disabled ? "Nvt.Controls.ExpanderDisabledForegroundBrush" : state.Pressed ? "Nvt.Controls.ExpanderPressedForegroundBrush" : "NfcTextBrush";
                    Assert.Equal(ResourceColor(header, fill), ColorOf(header.Background));
                    Assert.Equal(ResourceColor(header, text), ColorOf(header.Foreground));
                    ContentPresenter presenter = Part<ContentPresenter>(header, "ExpanderHeaderPresenter");
                    Assert.Equal(ResourceColor(header, "Nvt.Divider.TransparentBrush"), ColorOf(presenter.Background));
                    Assert.Equal(ColorOf(header.Foreground), ColorOf(presenter.Foreground));
                    Assert.Equal(172, presenter.Bounds.Width);
                    Border ring = Part<Border>(header, "ExpanderFocusRing");
                    Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                    Assert.Equal(new CornerRadius(square ? 10 : 999), ring.CornerRadius);
                    if (ring.IsVisible) VerifyRing(header, ring);
                    var chevron = Part<Path>(header, "ExpanderChevron");
                    Assert.Equal(new Size(12, 6), chevron.Bounds.Size);
                    Assert.Equal(1.5, chevron.StrokeThickness);
                    Assert.Equal(ColorOf(header.Foreground), ColorOf(chevron.Stroke));
                    bool rotated = expanded != (direction == ExpandDirection.Up);
                    Assert.Equal(rotated ? -1 : 1, chevron.RenderTransform!.Value.M11, 5);
                    Point headerPosition = header.TranslatePoint(default, expander)!.Value;
                    Point contentPosition = Part<Border>(expander, "ExpanderContent").TranslatePoint(default, expander)!.Value;
                    if (expanded) Assert.Equal(direction == ExpandDirection.Down, headerPosition.Y < contentPosition.Y);
                    Color surface = ResourceColor(header, fill == "Nvt.Divider.TransparentBrush" ? "NfcSurfaceBrush" : fill);
                    double contrast = Contrast(ColorOf(header.Foreground), surface);
                    Assert.True(contrast >= (state.Disabled ? 3 : 4.5), $"{state.Name}: {contrast:F3}");
                    if (state.Disabled) disabledMinimum = Math.Min(disabledMinimum, contrast);
                    else textMinimum = Math.Min(textMinimum, contrast);
                }
            }
            finally { host.Close(); }
        }
        output.WriteLine($"{(dark ? "Dark" : "Light")}/{(square ? "Square" : "Pill")}: text {textMinimum:F3}:1; disabled {disabledMinimum:F3}:1");
    }

    /// <summary>Pins determinate and indeterminate thicknesses, including the static reduced-motion template.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgressThicknessAndPillEndsStayExact(bool dark)
    {
        foreach ((string role, double thickness) in new[] { ("", 6d), ("thin", 3d), ("thick", 10d) })
        foreach (bool indeterminate in new[] { false, true })
        foreach (bool reduced in new[] { false, true })
        {
            var bar = new ProgressBar { Width = 300, Value = 50, IsIndeterminate = indeterminate,
                VerticalAlignment = VerticalAlignment.Center };
            if (role.Length > 0) bar.Classes.Add(role);
            if (reduced) bar.Classes.Add("reducedMotion");
            Window host = Create(bar, dark);
            try
            {
                Show(host);
                foreach (ThemeShape shape in new[] { ThemeShape.Pill, ThemeShape.Square, ThemeShape.Pill })
                {
                    ThemeShapes.SetShape(host.Resources, shape);
                    Flush(host);
                    Assert.Equal(thickness, bar.Bounds.Height);
                    Assert.False(bar.ShowProgressText);
                    Border track = Part<Border>(bar, "ProgressBarRoot");
                    Assert.Equal(thickness, track.Bounds.Height);
                    Assert.Equal(new CornerRadius(shape == ThemeShape.Square ? 1 : thickness / 2), track.CornerRadius);
                    Assert.Equal(ResourceColor(bar, "Nvt.Progress.TrackBrush"), ColorOf(track.Background));
                    if (indeterminate && reduced)
                    {
                        Border indicator = Part<Border>(bar, "ReducedMotionIndicator");
                        Assert.Equal(new Size(100, thickness), indicator.Bounds.Size);
                        Assert.Equal(new CornerRadius(shape == ThemeShape.Square ? 1 : thickness / 2), indicator.CornerRadius);
                        Assert.Null(indicator.RenderTransform);
                    }
                    else
                    {
                        string[] parts = indeterminate ? ["IndeterminateProgressBarIndicator", "IndeterminateProgressBarIndicator2"] : ["PART_Indicator"];
                        foreach (string part in parts)
                        {
                            Border indicator = Part<Border>(bar, part);
                            Assert.Equal(thickness, indicator.Bounds.Height);
                            Assert.Equal(new CornerRadius(shape == ThemeShape.Square ? 1 : thickness / 2), indicator.CornerRadius);
                            Assert.Equal(ResourceColor(bar, "Nvt.Progress.IndicatorBrush"), ColorOf(indicator.Background));
                        }
                        if (!indeterminate) Assert.Equal(150, Part<Border>(bar, "PART_Indicator").Bounds.Width);
                    }
                }
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Proves that the existing ProgressIndicator identity, caller settings, values, and new tokens survive together.</summary>
    [AvaloniaFact]
    public void ProgressIndicatorProjectsFractionsAndUsesProgressBarTokens()
    {
        var indicator = new ProgressIndicator { Maximum = 1, Width = 300, VerticalAlignment = VerticalAlignment.Center,
            Progress = new ProgressUpdate(0.42, "Synthetic step") };
        Window host = Create(indicator);
        try
        {
            Show(host);
            Assert.Equal(typeof(ProgressBar), indicator.StyleKey);
            Assert.Equal(0.42, indicator.Value);
            Assert.Equal(126, Part<Border>(indicator, "PART_Indicator").Bounds.Width);
            Assert.Equal(6, indicator.Bounds.Height);
            Assert.Equal(ResourceColor(indicator, "Nvt.Progress.IndicatorBrush"), ColorOf(indicator.Foreground));
            Assert.Equal(ResourceColor(indicator, "Nvt.Progress.TrackBrush"), ColorOf(indicator.Background));
            indicator.Progress = new ProgressUpdate(0.7, "Next step");
            Flush(host);
            Assert.Equal(210, Part<Border>(indicator, "PART_Indicator").Bounds.Width);
            indicator.Progress = new ProgressUpdate(null, "Waiting");
            Assert.Equal(0.7, indicator.Value);
            Assert.Equal(1, indicator.Maximum);
            indicator.IsIndeterminate = true;
            indicator.Classes.Add("thin");
            Flush(host);
            Assert.True(indicator.IsIndeterminate);
            Assert.Equal(3, indicator.Bounds.Height);
            Assert.Equal(3, Part<Border>(indicator, "IndeterminateProgressBarIndicator").Bounds.Height);
        }
        finally { host.Close(); }
    }

    /// <summary>Keeps native vertical progress orientation and applies each thickness to its width.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerticalProgressBarsKeepTheirWidth(bool dark)
    {
        foreach ((string role, double thickness) in new[] { ("", 6d), ("thin", 3d), ("thick", 10d) })
        foreach (bool indeterminate in new[] { false, true })
        foreach (bool reduced in new[] { false, true })
        {
            var bar = new ProgressBar { Orientation = Orientation.Vertical, Height = 120, Value = 50,
                IsIndeterminate = indeterminate, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (role.Length > 0) bar.Classes.Add(role);
            if (reduced) bar.Classes.Add("reducedMotion");
            Window host = Create(bar, dark);
            try
            {
                Show(host);
                Assert.Equal(thickness, bar.Bounds.Width);
                Assert.Equal(120, bar.Bounds.Height);
                Border track = Part<Border>(bar, "ProgressBarRoot");
                Assert.Equal(new CornerRadius(thickness / 2), track.CornerRadius);
                if (indeterminate && reduced)
                    Assert.Equal(new Size(thickness, 40), Part<Border>(bar, "ReducedMotionIndicator").Bounds.Size);
                else if (!indeterminate)
                    Assert.Equal(new Size(thickness, 60), Part<Border>(bar, "PART_Indicator").Bounds.Size);
                else
                    Assert.Equal(thickness, Part<Border>(bar, "IndeterminateProgressBarIndicator").Bounds.Height);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Checks identical native and Border dividers in both orientations and weights.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeparatorAndBorderDividersMatch(bool dark)
    {
        foreach (bool vertical in new[] { false, true })
        foreach (bool strong in new[] { false, true })
        {
            var separator = new Separator();
            var border = new Border { Classes = { "divider" } };
            foreach (Control control in new Control[] { separator, border })
            {
                if (vertical) control.Classes.Add("vertical");
                if (strong) control.Classes.Add("strong");
            }
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16,
                Children = { new Grid { Width = 120, Height = 80, Children = { separator } },
                    new Grid { Width = 120, Height = 80, Children = { border } } } };
            Window host = Create(panel, dark);
            try
            {
                Show(host);
                Assert.Equal(vertical ? new Size(1, 80) : new Size(120, 1), separator.Bounds.Size);
                Assert.Equal(separator.Bounds.Size, border.Bounds.Size);
                Assert.Equal(new Thickness(0), separator.Margin);
                Assert.Equal(new Thickness(0), border.Margin);
                Assert.Equal(ColorOf(separator.Background), ColorOf(border.Background));
                Assert.Equal(ResourceColor(separator, strong ? "NfcBorderBrush" : "NfcDividerBrush"), ColorOf(border.Background));
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Checks splitter hit geometry, active line width, disabled precedence, focus, and resize cursors.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SplitterStatesKeepTheSixDipHitArea(bool dark, bool horizontal)
    {
        var (grid, splitter, _, _) = SplitGrid(horizontal);
        Window host = Create(grid, dark);
        try
        {
            Show(host);
            foreach (DividerState state in States.Concat([new DividerState("Dragging outside", Pressed: true)]))
            {
                SetState(splitter, state);
                Flush(host);
                bool active = !state.Disabled && (state.Over || state.Pressed);
                Border line = Part<Border>(splitter, "SplitterLine");
                Assert.Equal(6, horizontal ? splitter.Bounds.Height : splitter.Bounds.Width);
                Assert.Equal(!state.Disabled && state.Pressed ? 3 : active ? 2 : 1, horizontal ? line.Bounds.Height : line.Bounds.Width);
                Assert.Equal(ResourceColor(splitter, state.Disabled ? "NfcBorderMutedBrush" : state.Pressed ? "NfcAccentStrongBrush" : active ? "NfcAccentBrush" : "NfcBorderSoftBrush"), ColorOf(line.Background));
                Assert.Equal(horizontal ? "SizeNorthSouth" : "SizeWestEast", splitter.Cursor!.ToString());
                Assert.Null(splitter.FocusAdorner);
                Border ring = Part<Border>(splitter, "SplitterFocusRing");
                Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                if (ring.IsVisible) VerifyRing(splitter, ring);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Drags both native splitter directions and checks the exact change in both neighboring cells.</summary>
    [AvaloniaTheory]
    [InlineData(false, 27)]
    [InlineData(false, -19)]
    [InlineData(true, 27)]
    [InlineData(true, -19)]
    public void SplitterDragResizesNeighborsByDraggedDistance(bool horizontal, double distance)
    {
        var (grid, splitter, before, after) = SplitGrid(horizontal);
        Window host = Create(grid, width: 420, height: 360);
        try
        {
            Show(host);
            double first = Length(before), second = Length(after);
            Point start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), host)!.Value;
            Point end = start + (horizontal ? new Vector(0, distance) : new Vector(distance, 0));
            host.MouseDown(start, MouseButton.Left);
            host.MouseMove(end);
            Flush(host);
            Assert.Contains(":pressed", splitter.Classes);
            Assert.Equal(ResourceColor(splitter, "NfcAccentStrongBrush"), ColorOf(Part<Border>(splitter, "SplitterLine").Background));
            host.MouseUp(end, MouseButton.Left);
            Flush(host);
            Assert.Equal(first + distance, Length(before), 5);
            Assert.Equal(second - distance, Length(after), 5);
        }
        finally { host.Close(); }
        double Length(Control control) => horizontal ? control.Bounds.Height : control.Bounds.Width;
    }

    /// <summary>Shows the token-colored drag preview and resizes the neighbors only when the pointer is released.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewSplitterShowsTokenPreviewAndResizesOnRelease(bool horizontal)
    {
        var (grid, splitter, before, after) = SplitGrid(horizontal);
        splitter.ShowsPreview = true;
        Window host = Create(grid, width: 420, height: 360);
        try
        {
            Show(host);
            double first = Length(before), second = Length(after);
            Point start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), host)!.Value;
            Point end = start + (horizontal ? new Vector(0, 31) : new Vector(31, 0));
            host.MouseDown(start, MouseButton.Left);
            host.MouseMove(end);
            Flush(host);
            Color expected = ResourceColor(splitter, "NfcAccentBrush");
            Border[] previews = host.GetVisualDescendants().OfType<Border>()
                .Where(border => border.IsVisible && border.Opacity == 0.5 && ColorOf(border.Background) == expected).ToArray();
            Assert.NotEmpty(previews);
            Assert.Equal(first, Length(before), 5);
            Assert.Equal(second, Length(after), 5);
            host.MouseUp(end, MouseButton.Left);
            Flush(host);
            Assert.Equal(first + 31, Length(before), 5);
            Assert.Equal(second - 31, Length(after), 5);
        }
        finally { host.Close(); }
        double Length(Control control) => horizontal ? control.Bounds.Height : control.Bounds.Width;
    }

    /// <summary>Uses native Tab, Space, pointer focus, and arrow resizing without a second focus adorner.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeKeyboardAndPointerFocusKeepOneRing(bool horizontal)
    {
        var (grid, splitter, before, _) = SplitGrid(horizontal);
        var expander = new Expander { Header = "Options", Content = "Body" };
        AutomationProperties.SetName(expander, "Options disclosure");
        var leading = new Grid { Focusable = true, Height = 20 };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Children = { leading, expander, grid } };
        Grid.SetRow(expander, 1);
        Grid.SetRow(grid, 2);
        Window host = Create(content);
        try
        {
            Show(host);
            leading.Focus();
            Press(Key.Tab);
            ToggleButton header = Header(expander);
            Assert.Same(header, host.FocusManager!.GetFocusedElement());
            Assert.True(Part<Border>(header, "ExpanderFocusRing").IsVisible);
            Press(Key.Space);
            Assert.True(expander.IsExpanded);
            Assert.Equal("Options disclosure", ControlAutomationPeer.CreatePeerForElement(expander)!.GetName());
            Press(Key.Tab);
            Assert.Same(splitter, host.FocusManager.GetFocusedElement());
            Assert.True(Part<Border>(splitter, "SplitterFocusRing").IsVisible);
            double length = horizontal ? before.Bounds.Height : before.Bounds.Width;
            Press(horizontal ? Key.Down : Key.Right);
            Assert.Equal(length + splitter.KeyboardIncrement, horizontal ? before.Bounds.Height : before.Bounds.Width, 5);
            leading.Focus();
            Point center = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), host)!.Value;
            host.MouseDown(center, MouseButton.Left);
            host.MouseUp(center, MouseButton.Left);
            Flush(host);
            Assert.Same(splitter, host.FocusManager.GetFocusedElement());
            Assert.False(Part<Border>(splitter, "SplitterFocusRing").IsVisible);
            center = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), host)!.Value;
            host.MouseDown(center, MouseButton.Left);
            host.MouseUp(center, MouseButton.Left);
            Flush(host);
            Assert.False(expander.IsExpanded);
            Assert.False(Part<Border>(header, "ExpanderFocusRing").IsVisible);
            expander.IsEnabled = false;
            host.MouseDown(center, MouseButton.Left);
            host.MouseUp(center, MouseButton.Left);
            Flush(host);
            Assert.False(expander.IsExpanded);
            splitter.IsEnabled = false;
            length = horizontal ? before.Bounds.Height : before.Bounds.Width;
            center = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), host)!.Value;
            host.MouseDown(center, MouseButton.Left);
            host.MouseMove(center + (horizontal ? new Vector(0, 27) : new Vector(27, 0)));
            host.MouseUp(center, MouseButton.Left);
            Flush(host);
            Assert.Equal(length, horizontal ? before.Bounds.Height : before.Bounds.Width);
        }
        finally { host.Close(); }
        void Press(Key key)
        {
            host.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            host.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            Flush(host);
        }
    }

    private static void VerifyRing(Control owner, Border ring)
    {
        Assert.Equal(new Thickness(-4), ring.Margin);
        Assert.Equal(new Thickness(2), ring.BorderThickness);
        Assert.False(ring.IsHitTestVisible);
        Assert.Equal(owner.Bounds.Width + 8, ring.Bounds.Width);
        Assert.Equal(owner.Bounds.Height + 8, ring.Bounds.Height);
        foreach (string surface in new[] { "NfcSurfaceBrush", "NfcAppBackgroundBrush", "NfcSelectionSurfaceBrush" })
            Assert.True(Contrast(ColorOf(ring.BorderBrush), ResourceColor(owner, surface)) >= 3);
    }
}
