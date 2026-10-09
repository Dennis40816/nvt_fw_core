// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.Theme;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Dividers.DividerTestHost;
using Path = Avalonia.Controls.Shapes.Path;

namespace Nvt.Core.Avalonia.Tests.Dividers;

/// <summary>Guards token ownership, accessible contrast, motion policy, and attached resource changes.</summary>
public sealed class DividerContractTests(ITestOutputHelper output)
{
    /// <summary>Rejects literal colors and radii throughout each shipped style, including templates.</summary>
    [Theory]
    [InlineData("ExpanderStyles")]
    [InlineData("ProgressStyles")]
    [InlineData("DividerStyles")]
    public void StyleColorsAndRadiiComeFromTokens(string file)
    {
        string[] properties = ["Background", "Foreground", "BorderBrush", "Fill", "Stroke", "Color", "CornerRadius", "BoxShadow"];
        XElement source = ThemeContractTests.ReadExtracted(file).Root!;
        foreach (XElement element in source.Descendants())
        {
            Assert.False(element.Name.LocalName is "SolidColorBrush" or "Color" or "CornerRadius" or "BoxShadows", element.ToString());
            foreach (XAttribute attribute in element.Attributes().Where(attribute => properties.Contains(attribute.Name.LocalName.Split('.').Last())))
                AssertBinding(attribute.Value);
            if (element.Name.LocalName != "Setter" || !properties.Contains(element.Attribute("Property")?.Value.Split('.').Last())) continue;
            if (element.Attribute("Value") is { } value) AssertBinding(value.Value);
            else
            {
                XElement binding = Assert.Single(element.Elements());
                Assert.Equal("MultiBinding", binding.Name.LocalName);
                Assert.Equal("{StaticResource Nvt.Progress.RadiusConverter}", binding.Attribute("Converter")!.Value);
                Assert.Equal(["CornerRadius", "Bounds"], binding.Elements().Select(child => child.Attribute("Path")!.Value));
            }
        }
        static void AssertBinding(string value) => Assert.True(
            value.StartsWith("{DynamicResource ", StringComparison.Ordinal) ||
            value.StartsWith("{TemplateBinding ", StringComparison.Ordinal) ||
            value.StartsWith("{ReflectionBinding ", StringComparison.Ordinal), value);
    }

    /// <summary>Resolves every resource in both themes and measures indicator, focus, and disabled contrast.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourcesResolveAndActiveIndicatorsMeetContrast(bool dark)
    {
        var bar = new ProgressBar { Value = 50, Width = 300 };
        var expander = new Expander { Header = "Details" };
        Window host = Create(new StackPanel { Children = { bar, expander } }, dark);
        try
        {
            Show(host);
            foreach (string file in StyleFiles)
            foreach (Match match in Regex.Matches(ThemeContractTests.ReadExtracted(file).ToString(),
                @"\{DynamicResource (N(?:vt|fc)[^}]+)\}", RegexOptions.CultureInvariant))
                _ = Resource(bar, match.Groups[1].Value);
            Assert.Equal(6d, Resource(bar, "Nvt.Progress.Height"));
            Assert.Equal(3d, Resource(bar, "Nvt.Progress.ThinHeight"));
            Assert.Equal(10d, Resource(bar, "Nvt.Progress.ThickHeight"));
            double indicatorTrack = Contrast(ColorOf(bar.Foreground), ColorOf(bar.Background));
            double indicatorSurface = Contrast(ColorOf(bar.Foreground), ResourceColor(bar, "NfcSurfaceBrush"));
            Assert.True(indicatorTrack >= 3, $"Indicator against track: {indicatorTrack:F3}");
            Assert.True(indicatorSurface >= 3, $"Indicator against surface: {indicatorSurface:F3}");
            double ring = SurfaceKeys
                .Select(key => Contrast(ResourceColor(bar, "Nvt.Focus.RingBrush"), ResourceColor(bar, key))).Min();
            Assert.True(ring >= 3);
            double disabled = Contrast(ResourceColor(bar, "NfcTextDisabledBrush"), ResourceColor(bar, "NfcSurfaceBrush"));
            Assert.True(disabled >= 3);
            output.WriteLine($"{(dark ? "Dark" : "Light")}: indicator/track {indicatorTrack:F3}:1; indicator/surface {indicatorSurface:F3}:1; focus minimum {ring:F3}:1; disabled text {disabled:F3}:1");
        }
        finally { host.Close(); }
    }

    /// <summary>Changes attached corners, themes, and the progress palette without replacing control identities.</summary>
    [AvaloniaFact]
    public void ShapeAndDictionaryReplacementUpdateAttachedControls()
    {
        var expander = new Expander { Header = "Details", IsExpanded = true };
        var splitter = new GridSplitter { Height = 80 };
        var bar = new ProgressBar { Value = 50, Width = 300 };
        Window host = Create(new StackPanel { Children = { expander, bar, splitter } });
        try
        {
            Show(host);
            var templates = (expander.Template, bar.Template, splitter.Template);
            foreach (bool dark in new[] { true, false })
            foreach (ThemeShape shape in new[] { ThemeShape.Square, ThemeShape.Pill })
            {
                host.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                ThemeShapes.SetShape(host.Resources, shape);
                Flush(host);
                double radius = shape == ThemeShape.Square ? 6 : 999;
                Assert.Equal(new CornerRadius(radius), Header(expander).CornerRadius);
                Assert.Equal(new CornerRadius(radius), splitter.CornerRadius);
                Assert.Equal(new CornerRadius(shape == ThemeShape.Square ? 1 : 999), bar.CornerRadius);
                Assert.Equal(new CornerRadius(shape == ThemeShape.Square ? 2 : 999), Part<Border>(splitter, "SplitterFocusRing").CornerRadius);
                Assert.Equal(new CornerRadius(shape == ThemeShape.Square ? 1 : 3), Part<Border>(bar, "ProgressBarRoot").CornerRadius);
                Assert.Equal(templates, (expander.Template, bar.Template, splitter.Template));
            }
            var palette = new ResourceDictionary
            {
                ["Nvt.Progress.Height"] = 8d,
                ["Nvt.Progress.TrackBrush"] = Brushes.Gold,
                ["Nvt.Progress.IndicatorBrush"] = Brushes.Purple,
            };
            host.Resources.MergedDictionaries.Add(palette);
            Flush(host);
            Assert.Equal(8, bar.Bounds.Height);
            Assert.Equal(new CornerRadius(4), Part<Border>(bar, "ProgressBarRoot").CornerRadius);
            Assert.Equal(Colors.Gold, ColorOf(bar.Background));
            Assert.Equal(Colors.Purple, ColorOf(Part<Border>(bar, "PART_Indicator").Background));
            host.Resources.MergedDictionaries[1] = new ResourceDictionary
            {
                ["Nvt.Progress.Height"] = 10d,
                ["Nvt.Progress.TrackBrush"] = Brushes.Navy,
                ["Nvt.Progress.IndicatorBrush"] = Brushes.Yellow,
            };
            Flush(host);
            Assert.Equal(10, bar.Bounds.Height);
            Assert.Equal(Colors.Navy, ColorOf(bar.Background));
            Assert.Equal(Colors.Yellow, ColorOf(Part<Border>(bar, "PART_Indicator").Background));
        }
        finally { host.Close(); }
    }

    /// <summary>Uses 150 ms color and chevron transitions and removes indeterminate motion at either policy root.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReducedMotionWorksOnControlsAndAncestors(bool ancestor)
    {
        var bar = new ProgressBar { Width = 300, IsIndeterminate = true };
        var expander = new Expander { Header = "Details" };
        var splitter = new GridSplitter { Height = 60 };
        var root = new StackPanel { Children = { expander, bar, splitter } };
        Window host = Create(root);
        try
        {
            Show(host, freeze: false);
            ToggleButton header = Header(expander);
            Path chevron = Part<Path>(header, "ExpanderChevron");
            Assert.All(new Animatable[] { header, bar, splitter, chevron }, visual =>
            {
                Assert.NotNull(visual.Transitions);
                Assert.All(visual.Transitions, transition => Assert.Equal(TimeSpan.FromMilliseconds(150), ((TransitionBase)transition).Duration));
            });
            var normalTemplate = bar.Template;
            if (ancestor) root.Classes.Add("reducedMotion");
            else foreach (Control control in new Control[] { bar, expander, splitter }) control.Classes.Add("reducedMotion");
            Flush(host);
            Assert.All(new Animatable[] { header, bar, splitter, chevron }, visual => Assert.Null(visual.Transitions));
            Assert.NotSame(normalTemplate, bar.Template);
            Assert.DoesNotContain(bar.GetVisualDescendants(), visual => visual is Control { Name: "IndeterminateProgressBarIndicator" or "IndeterminateProgressBarIndicator2" });
            Assert.Equal(new Size(100, 6), Part<Border>(bar, "ReducedMotionIndicator").Bounds.Size);
            bar.IsIndeterminate = false;
            Flush(host);
            Assert.Same(normalTemplate, bar.Template);
            Assert.Equal(6, bar.Bounds.Height);
        }
        finally { host.Close(); }
    }

    /// <summary>Advances the retained native indeterminate animation and checks that its track height stays fixed.</summary>
    [AvaloniaFact]
    public void NativeIndeterminateAnimationMovesInsideTheSameTrack()
    {
        using var clock = new DividerSnapshotClock();
        var bar = new ProgressBar { IsIndeterminate = true, Width = 300, VerticalAlignment = VerticalAlignment.Center };
        clock.Attach(bar);
        Window host = Create(bar);
        try
        {
            Show(host);
            clock.Prepare(bar, host);
            Border indicator = Part<Border>(bar, "IndeterminateProgressBarIndicator");
            clock.Tick(TimeSpan.Zero);
            var initial = indicator.RenderTransform!.Value;
            clock.Tick(TimeSpan.FromMilliseconds(800));
            Flush(host);
            Assert.NotEqual(initial, indicator.RenderTransform.Value);
            Point position = indicator.TranslatePoint(default, bar)!.Value;
            output.WriteLine($"Native animation phase: {position}, opacity {indicator.Opacity}");
            Assert.True(position.X < bar.Bounds.Width && position.X + indicator.Bounds.Width > 0);
            Assert.Equal(6, bar.Bounds.Height);
            Assert.Equal(6, indicator.Bounds.Height);
            Assert.Equal(6, Part<Border>(bar, "ProgressBarRoot").Bounds.Height);
        }
        finally { host.Close(); }
    }
}
