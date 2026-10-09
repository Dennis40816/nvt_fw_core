// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Focus.ToolTipTestHost;

namespace Nvt.Core.Avalonia.Tests.Focus;

/// <summary>Guards tooltip style tokens, shared shapes, text contrast, and template foreground rules.</summary>
public sealed class ToolTipStylesTests
{
    private static readonly string[] ExpectedSelectors =
        ["ToolTip", "ToolTip TextBlock, ToolTip SelectableTextBlock", "ToolTip /template/ ContentPresenter"];
    private static readonly string[] RadiusKeys = ["Nvt.Shape.ControlCornerRadius", "Nvt.ToolTip.CornerRadius"];
    /// <summary>Both themes resolve the palette and exact geometry with at least 4.5:1 text contrast.</summary>
    [AvaloniaTheory]
    [InlineData(false, false, 14.629)]
    [InlineData(false, true, 14.629)]
    [InlineData(true, false, 14.390)]
    [InlineData(true, true, 14.390)]
    public void StylesResolveTokensAndContrastInBothThemesAndShapes(bool dark, bool square, double expectedContrast)
    {
        var text = new TextBlock { Text = "Custom tooltip text" };
        var selectable = new SelectableTextBlock { Text = "Selectable tooltip text" };
        var tip = new ToolTip { Content = new StackPanel { Children = { text, selectable } } };
        var window = Create(tip, dark);
        try
        {
            ThemeShapes.SetShape(window.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            window.Show();
            Flush(window);
            Assert.Equal(Color.Parse(dark ? "#111827" : "#FFFFFF"), ColorOf(tip.Background));
            Assert.Equal(Color.Parse(dark ? "#475569" : "#94A3B8"), ColorOf(tip.BorderBrush));
            Assert.Equal(Color.Parse(dark ? "#E2E8F0" : "#1E293B"), ColorOf(tip.Foreground));
            Assert.Equal(new CornerRadius(square ? 6 : 8), tip.CornerRadius);
            Assert.Equal(new Thickness(8, 4), tip.Padding);
            Assert.Equal(new Thickness(1), tip.BorderThickness);
            Assert.Equal(ColorOf(tip.Foreground), ColorOf(text.Foreground));
            Assert.Equal(ColorOf(tip.Foreground), ColorOf(selectable.Foreground));
            ContentPresenter presenter = Assert.Single(tip.GetVisualDescendants().OfType<ContentPresenter>());
            Assert.Equal(ColorOf(tip.Foreground), ColorOf(presenter.Foreground));
            Assert.Equal(new CornerRadius(8), Resource<CornerRadius>(tip, "Nvt.ToolTip.CornerRadius"));
            Assert.Equal(320d, Resource<double>(tip, "Nvt.ToolTip.MaxWidth"));
            Assert.Same(Resource<IBrush>(tip, "Nvt.ToolTip.BackgroundBrush"), tip.Background);
            Assert.Same(Resource<IBrush>(tip, "Nvt.ToolTip.BorderBrush"), tip.BorderBrush);
            Assert.Same(Resource<IBrush>(tip, "Nvt.ToolTip.ForegroundBrush"), tip.Foreground);
            double contrast = Contrast(ColorOf(tip.Foreground), ColorOf(tip.Background));
            Assert.True(contrast >= 4.5, $"Tooltip contrast: {contrast:F3}:1");
            Assert.Equal(expectedContrast, contrast, 3);
        }
        finally { window.Close(); }
    }

    /// <summary>The same tooltip follows theme, shape, and geometry-token changes without rebuilding its template.</summary>
    [AvaloniaFact]
    public void StylesFollowRuntimeThemeShapeAndGeometryTokenChanges()
    {
        var tip = new ToolTip { Content = "Live tooltip" };
        var window = Create(tip);
        try
        {
            window.Show();
            Flush(window);
            object? template = tip.Template;
            foreach (bool dark in new[] { true, false })
            foreach (ThemeShape shape in new[] { ThemeShape.Square, ThemeShape.Pill })
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                ThemeShapes.SetShape(window.Resources, shape);
                Flush(window);
                Assert.Equal(new CornerRadius(shape == ThemeShape.Square ? 6 : 8), tip.CornerRadius);
                Assert.Equal(Color.Parse(dark ? "#111827" : "#FFFFFF"), ColorOf(tip.Background));
                Assert.Equal(Color.Parse(dark ? "#475569" : "#94A3B8"), ColorOf(tip.BorderBrush));
                Assert.Same(template, tip.Template);
            }
            window.Resources["Nvt.ToolTip.CornerRadius"] = new CornerRadius(4, 3, 2, 1);
            window.Resources["Nvt.ToolTip.Padding"] = new Thickness(12, 6);
            window.Resources["Nvt.ToolTip.BorderThickness"] = new Thickness(2);
            Flush(window);
            Assert.Equal(new CornerRadius(4, 3, 2, 1), tip.CornerRadius);
            Assert.Equal(new Thickness(12, 6), tip.Padding);
            Assert.Equal(new Thickness(2), tip.BorderThickness);
        }
        finally { window.Close(); }
    }

    /// <summary>Style rules use resources for brushes and corners, with no duplicate or aliased foreground setters.</summary>
    [Fact]
    public void StyleRulesUseTokensAndHaveNoDuplicateSetters()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        const string relative = "src/Nvt.Core.Avalonia/Focus/ToolTipStyles.axaml";
        while (root is not null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        Assert.NotNull(root);
        XElement source = XDocument.Load(Path.Combine(root.FullName, relative)).Root!;
        XNamespace ns = source.Name.Namespace;
        XElement[] styles = [.. source.Elements(ns + "Style")];
        Assert.Equal(ExpectedSelectors, styles.Select(style => style.Attribute("Selector")!.Value));
        foreach (XElement style in styles)
        {
            XElement[] setters = [.. style.Elements(ns + "Setter")];
            string[] properties = [.. setters.Select(setter => setter.Attribute("Property")!.Value.Split('.').Last())];
            Assert.Equal(properties.Length, properties.Distinct(StringComparer.Ordinal).Count());
            foreach (XElement setter in setters)
            {
                string property = setter.Attribute("Property")!.Value;
                if (property == "CornerRadius")
                {
                    XElement binding = Assert.Single(setter.Elements(ns + "MultiBinding"));
                    Assert.Equal(RadiusKeys,
                        binding.Elements(ns + "DynamicResource").Select(resource => resource.Attribute("ResourceKey")!.Value));
                }
                else Assert.StartsWith("{DynamicResource Nvt.ToolTip.", setter.Attribute("Value")!.Value, StringComparison.Ordinal);
                Assert.DoesNotContain(setter.Descendants(), element => element.Name.LocalName is "SolidColorBrush" or "Color" or "CornerRadius");
            }
        }
        Assert.DoesNotContain('#', source.ToString());
    }

    private static double Contrast(Color first, Color second)
    {
        Assert.Equal(byte.MaxValue, first.A);
        Assert.Equal(byte.MaxValue, second.A);
        static double Linear(byte component)
        {
            double channel = component / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
