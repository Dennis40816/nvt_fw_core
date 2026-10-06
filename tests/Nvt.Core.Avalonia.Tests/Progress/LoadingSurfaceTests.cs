// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Focus;
using Nvt.Core.Avalonia.Progress;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Progress;

/// <summary>Characterizes NFC's wrapper tree, focus, padding and real Skia pixels.</summary>
public sealed class LoadingSurfaceTests
{
    /// <summary>The template preserves the scrim, centered content host and ancestor-padding binding.</summary>
    [AvaloniaFact]
    public void TemplateKeepsNfcStructureAndInnerPaddingBinding()
    {
        var state = new LoadingTestState("Synthetic loading", 0.42, true, true, false);
        var surface = new LoadingSurface { Content = state, Padding = new Thickness(3) };
        AutomationProperties.SetName(surface, state.AccessibleStatus);
        Window host = CreateHost(surface, false);
        try
        {
            Grid scrim = FindScrim(surface);
            ContentControl inner = Assert.IsType<ContentControl>(Assert.Single(scrim.Children));
            Assert.Same(scrim, Assert.Single(surface.GetVisualChildren()));
            Assert.True(surface.Focusable);
            Assert.True(surface.IsHitTestVisible);
            Assert.True(FocusOnRevealBehavior.GetIsEnabled(surface));
            Assert.Equal(state.AccessibleStatus, AutomationProperties.GetName(surface));
            Assert.Equal(HorizontalAlignment.Center, inner.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, inner.VerticalAlignment);
            Assert.Equal(430, inner.Width);
            Assert.Equal(new Thickness(28, 26), inner.Padding);
            Assert.Same(state, inner.Content);
            Assert.Same(surface.ContentTemplate, inner.ContentTemplate);
            Assert.True(Application.Current!.TryGetResource("NfcModalScrimBrush", host.ActualThemeVariant, out object? expected));
            Assert.Same(expected, scrim.Background);

            Border card = FindCard(surface);
            Assert.Equal(inner.Padding, card.Padding);
            inner.Padding = new Thickness(11, 13);
            host.UpdateLayout();
            Assert.Equal(inner.Padding, card.Padding);
            surface.Padding = new Thickness(99);
            Assert.Equal(new Thickness(11, 13), card.Padding);

            var replacement = new LoadingTestState("Replacement content", null, false, false, true);
            surface.Content = replacement;
            host.UpdateLayout();
            Assert.Same(replacement, inner.Content);
            Assert.Equal("Replacement content", Assert.Single(surface.GetVisualDescendants().OfType<TextBlock>()).Text);

            surface.ContentTemplate = (IDataTemplate)host.FindResource("ForegroundLoadingStatusTemplate")!;
            host.UpdateLayout();
            Assert.Same(surface.ContentTemplate, inner.ContentTemplate);
            Assert.IsType<ProgressBar>(FindProgress(surface));

            host.RequestedThemeVariant = ThemeVariant.Dark;
            Assert.True(Application.Current!.TryGetResource("NfcModalScrimBrush", host.ActualThemeVariant, out object? dark));
            Assert.Same(dark, scrim.Background);
            Assert.NotSame(expected, dark);
            var overrideBrush = new SolidColorBrush(Colors.Purple);
            host.Resources["NfcModalScrimBrush"] = overrideBrush;
            Assert.Same(overrideBrush, scrim.Background);
        }
        finally { host.Close(); }
    }

    /// <summary>Core supplies no width, padding, automation name or content.</summary>
    [AvaloniaFact]
    public void GeometryAndAutomationRemainWithTheTool()
    {
        var surface = new LoadingSurface();
        var uri = new Uri("avares://Nvt.Core.Avalonia/Progress/ProgressStyles.axaml");
        var host = new Window { Content = surface };
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
        try
        {
            host.Show();
            host.UpdateLayout();
            ContentControl inner = Assert.IsType<ContentControl>(Assert.Single(FindScrim(surface).Children));
            Assert.True(double.IsNaN(inner.Width));
            Assert.Equal(default, inner.Padding);
            Assert.Null(surface.Content);
            Assert.Null(surface.ContentTemplate);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetName(surface)));
        }
        finally { host.Close(); }
    }

    /// <summary>Attachment and each reveal use the existing focus behavior with Tab navigation.</summary>
    [AvaloniaFact]
    public void SurfaceTakesFocusOnAttachmentAndReveal()
    {
        var surface = new LoadingSurface
        {
            Content = new LoadingTestState("Focus fixture", null, true, true, false),
        };
        var other = new Grid { Focusable = true };
        NavigationMethod? navigation = null;
        surface.GotFocus += (_, args) => navigation = args.NavigationMethod;
        Window host = CreateHost(new Grid { Children = { other, surface } }, false);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Same(surface, host.FocusManager?.GetFocusedElement());
            Assert.Equal(NavigationMethod.Tab, navigation);
            surface.IsVisible = false;
            Assert.True(other.Focus());
            surface.IsVisible = true;
            Assert.Same(other, host.FocusManager?.GetFocusedElement());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(surface, host.FocusManager?.GetFocusedElement());
        }
        finally { host.Close(); }
    }

    /// <summary>The scrim intercepts pointer input outside the centered content.</summary>
    [AvaloniaFact]
    public void ScrimBlocksInputToTheUnderlyingContent()
    {
        int presses = 0;
        var underneath = new Border { Background = Brushes.White };
        underneath.PointerPressed += (_, _) => presses++;
        var surface = new LoadingSurface
        {
            Content = new LoadingTestState("Input fixture", null, true, true, false),
        };
        var layer = new Grid { Children = { underneath, surface } };
        Window host = CreateHost(layer, false);
        surface.ContentTemplate = (IDataTemplate)host.FindResource("CoreLoadingStatusTemplate")!;
        host.UpdateLayout();
        try
        {
            Dispatcher.UIThread.RunJobs();
            using var visibleFrame = host.CaptureRenderedFrame();
            Assert.NotNull(visibleFrame);
            Assert.Same(FindScrim(surface), host.InputHitTest(new Point(5, 5)));
            host.MouseDown(new Point(5, 5), MouseButton.Left);
            host.MouseUp(new Point(5, 5), MouseButton.Left);
            Assert.Equal(0, presses);
            surface.IsVisible = false;
            host.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var hiddenFrame = host.CaptureRenderedFrame();
            Assert.NotNull(hiddenFrame);
            host.MouseDown(new Point(5, 5), MouseButton.Left);
            host.MouseUp(new Point(5, 5), MouseButton.Left);
            Assert.Equal(1, presses);
        }
        finally { host.Close(); }
    }

    /// <summary>The frozen compiled wrapper and Core controls render identical pixels in both themes.</summary>
    /// <param name="phase">The representative tool-owned state.</param>
    /// <param name="dark">Whether the host uses the dark theme.</param>
    [AvaloniaTheory]
    [InlineData("unknown", false)]
    [InlineData("unknown", true)]
    [InlineData("known", false)]
    [InlineData("known", true)]
    [InlineData("reducedMotion", false)]
    [InlineData("reducedMotion", true)]
    [InlineData("failure", false)]
    [InlineData("failure", true)]
    [InlineData("retry", false)]
    [InlineData("retry", true)]
    [InlineData("collapse", false)]
    [InlineData("collapse", true)]
    [InlineData("completion", false)]
    [InlineData("completion", true)]
    public void RenderedPixelsMatchTheFrozenNfcWrapper(string phase, bool dark)
    {
        LoadingTestState state = phase switch
        {
            "unknown" => new("Synthetic unknown", null, true, true, false),
            "known" => new("Synthetic 42%", 0.42, true, true, false),
            "reducedMotion" => new("Synthetic 42%", 0.42, false, true, false),
            "failure" => new("Synthetic failure", null, false, false, true),
            "retry" => new("Synthetic retry", 0, true, true, false),
            "collapse" => new("Synthetic failure", null, false, false, true, false),
            "completion" => new("Synthetic complete", 1, false, false, false, false),
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };
        var frozen = new FrozenNfcLoadingSurface { DataContext = state };
        var surface = new LoadingSurface { Content = state, IsVisible = state.IsVisible };
        AutomationProperties.SetName(surface, state.AccessibleStatus);
        byte[] baseline = Capture(frozen, dark);
        byte[] actual = Capture(surface, dark);
        Assert.Equal(baseline, actual);
        if (state.IsVisible)
        {
            Assert.True(baseline.Distinct().Count() > 4);
        }
    }

    private static Window CreateHost(Control control, bool dark)
    {
        var host = new Window
        {
            Width = 640,
            Height = 360,
            Background = Brushes.White,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        foreach (string source in new[]
        {
            "avares://Nvt.Core.Avalonia/Progress/ProgressStyles.axaml",
            "avares://Nvt.Core.Avalonia.Tests/Progress/LoadingTestStyles.axaml",
        })
        {
            var uri = new Uri(source);
            host.Styles.Add(new StyleInclude(uri) { Source = uri });
        }
        if (control is LoadingSurface surface)
        {
            surface.ContentTemplate = (IDataTemplate)host.FindResource("CoreLoadingStatusTemplate")!;
        }
        host.Show();
        host.Content = control;
        host.UpdateLayout();
        return host;
    }

    private static Grid FindScrim(Control surface) =>
        Assert.Single(surface.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Children.Any(child => child is ContentControl));

    private static Border FindCard(Control surface) =>
        Assert.Single(surface.GetVisualDescendants().OfType<Border>(), border => border.Name == "FixtureCard");

    private static ProgressBar FindProgress(Control surface) =>
        Assert.Single(surface.GetVisualDescendants().OfType<ProgressBar>());

    private static byte[] Capture(Control control, bool dark)
    {
        Window host = CreateHost(control, dark);
        try
        {
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();
            if (control.IsVisible)
            {
                Assert.Equal(new Thickness(28, 26), FindCard(control).Padding);
                ProgressBar bar = FindProgress(control);
                var state = Assert.IsType<LoadingTestState>(
                    control is LoadingSurface surface ? surface.Content : control.DataContext);
                Assert.Equal(state.ShouldAnimate, bar.IsIndeterminate);
                Assert.Equal(state.Fraction ?? 0, bar.Value);
                Assert.NotNull(bar.Template);
            }
            using var frame = host.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(640, 360), frame.PixelSize);
            Assert.Equal(new Vector(96, 96), frame.Dpi);
            Assert.Equal(PixelFormat.Rgba8888, frame.Format);
            int stride = frame.PixelSize.Width * 4;
            byte[] pixels = new byte[stride * frame.PixelSize.Height];
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                frame.CopyPixels(new PixelRect(frame.PixelSize), handle.AddrOfPinnedObject(), pixels.Length, stride);
            }
            finally { handle.Free(); }
            return pixels;
        }
        finally { host.Close(); }
    }
}