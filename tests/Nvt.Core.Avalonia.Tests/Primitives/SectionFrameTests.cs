// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Primitives;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Primitives;

/// <summary>Characterizes the generic section title, content bindings and compiled default template.</summary>
public sealed class SectionFrameTests
{
    /// <summary>The title is empty and content is unset by default.</summary>
    [AvaloniaFact]
    public void PropertiesHaveFrozenDefaults()
    {
        var frame = new SectionFrame();
        Assert.Equal(string.Empty, frame.Title);
        Assert.Null(frame.Content);
    }

    /// <summary>The selectable title and stretched content presenter track property changes in both themes.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultTemplateBindsTitleAndContent(bool dark)
    {
        var firstContent = new Border { Width = 60, Height = 25 };
        var frame = new SectionFrame { Title = "First section", Content = firstContent };
        var host = CreateHost(frame, dark);
        try
        {
            var title = Assert.Single(frame.GetVisualDescendants().OfType<SelectableTextBlock>());
            var presenter = Assert.Single(frame.GetVisualDescendants().OfType<ContentPresenter>());
            var stack = Assert.Single(frame.GetVisualDescendants().OfType<StackPanel>());
            var grid = Assert.Single(frame.GetVisualDescendants().OfType<Grid>());
            Assert.Equal("First section", title.Text);
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            Assert.Equal(new Thickness(8, 4), title.Padding);
            Assert.Equal(HorizontalAlignment.Stretch, frame.HorizontalAlignment);
            Assert.Equal(HorizontalAlignment.Stretch, presenter.HorizontalAlignment);
            Assert.Same(firstContent, presenter.Content);
            Assert.Contains(firstContent, frame.GetVisualDescendants());
            Assert.Equal(8, stack.Spacing);
            Assert.Equal(8, grid.ColumnSpacing);
            Assert.Collection(grid.ColumnDefinitions,
                column => Assert.Equal(GridLength.Star, column.Width),
                column => Assert.Equal(GridLength.Auto, column.Width),
                column => Assert.Equal(GridLength.Star, column.Width));
            Assert.Equal(1, Grid.GetColumn(title));
            AssertResource(host, "NfcTextStrongBrush", title.Foreground);
            AssertResource(host, "NfcSurfaceBrush", title.Background);
            Assert.Collection(grid.Children.OfType<Border>(),
                divider => AssertDivider(host, divider, 0),
                divider => AssertDivider(host, divider, 2));

            var secondContent = new Border { Width = 80, Height = 30 };
            frame.Title = "Second section";
            frame.Content = secondContent;
            Layout(host);
            Assert.Equal("Second section", title.Text);
            Assert.Same(secondContent, presenter.Content);
            Assert.Contains(secondContent, frame.GetVisualDescendants());
            Assert.DoesNotContain(firstContent, frame.GetVisualDescendants());

            frame.Content = "Plain content";
            Layout(host);
            Assert.Equal("Plain content", presenter.Content);
            frame.Content = null;
            frame.Title = string.Empty;
            Layout(host);
            Assert.Null(presenter.Content);
            Assert.Equal(string.Empty, title.Text);
        }
        finally { host.Close(); }
    }

    /// <summary>Spacing and brushes use live Nfc resources without requiring a template rebuild.</summary>
    [AvaloniaFact]
    public void DefaultTemplateTracksDynamicResourceChanges()
    {
        var frame = new SectionFrame { Title = "Section" };
        var host = CreateHost(frame, false);
        try
        {
            var title = Assert.Single(frame.GetVisualDescendants().OfType<SelectableTextBlock>());
            var stack = Assert.Single(frame.GetVisualDescendants().OfType<StackPanel>());
            var grid = Assert.Single(frame.GetVisualDescendants().OfType<Grid>());
            var foreground = new SolidColorBrush(Colors.Red);
            var background = new SolidColorBrush(Colors.Blue);
            host.Resources["NfcSpace8"] = 12d;
            host.Resources["NfcTextStrongBrush"] = foreground;
            host.Resources["NfcSurfaceBrush"] = background;
            Layout(host);
            Assert.Equal(12, stack.Spacing);
            Assert.Equal(12, grid.ColumnSpacing);
            Assert.Same(foreground, title.Foreground);
            Assert.Same(background, title.Background);
            Assert.All(grid.Children.OfType<Border>(), divider => Assert.Same(background, divider.Background));
        }
        finally { host.Close(); }
    }

    private static Window CreateHost(SectionFrame frame, bool dark)
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia/Primitives/PrimitivesStyles.axaml");
        var host = new Window
        {
            Content = frame,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
        Layout(host);
        return host;
    }

    private static void Layout(Window host)
    {
        host.Measure(new Size(320, 200));
        host.Arrange(new Rect(0, 0, 320, 200));
    }

    private static void AssertDivider(Window host, Border divider, int column)
    {
        Assert.Equal(column, Grid.GetColumn(divider));
        Assert.Equal(1, divider.Height);
        Assert.Equal(VerticalAlignment.Center, divider.VerticalAlignment);
        AssertResource(host, "NfcSurfaceBrush", divider.Background);
    }

    private static void AssertResource(Window host, string key, IBrush? brush)
    {
        Assert.True(Application.Current!.TryGetResource(key, host.ActualThemeVariant, out object? expected));
        Assert.Same(expected, brush);
    }
}
