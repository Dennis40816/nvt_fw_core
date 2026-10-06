// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Characterizes the shared scroll styles taken from NFC, plus NFH's viewport-bound classes.</summary>
public sealed class ScrollStylesTests
{
    private static readonly string[] ViewportBoundSelectors = ["ScrollViewer.viewportBoundScroll", "Border.viewportBoundContent"];
    private static readonly string[] ScrollResourceKeys = ["NfcPillCornerRadius", "NfcTextDisabledBrush", "NfcTextMutedBrush"];
    private static readonly double[] ContentHeights = [500, 100, 500, 100];

    /// <summary>Every resource the scroll styles use exists in both shared themes.</summary>
    [AvaloniaFact]
    public void ScrollStylesResolveEveryResourceReference()
    {
        XElement tokens = ThemeContractTests.ReadExtracted("ThemeTokens").Root!;
        string[] common = Keys(tokens.Elements());
        XElement[] themes = [.. tokens.Element(ThemeContractTests.Presentation + "ResourceDictionary.ThemeDictionaries")!.Elements()];
        string styles = ThemeContractTests.ReadExtracted("ScrollStyles").ToString();
        string[] references = [.. Regex.Matches(styles, @"\{DynamicResource (?<key>[^}]+)\}", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["key"].Value).Distinct(StringComparer.Ordinal)];

        Assert.Equal(ScrollResourceKeys, references.Order(StringComparer.Ordinal));
        Assert.Equal(2, themes.Length);
        foreach (XElement theme in themes)
        {
            Assert.Empty(references.Except(common.Concat(Keys(theme.Elements())), StringComparer.Ordinal));
        }
    }

    /// <summary>The NFH classes come last and only apply to elements that opt in.</summary>
    [AvaloniaFact]
    public void ViewportBoundClassesFollowTheNfcBlock()
    {
        string[] selectors = [.. ThemeContractTests.ReadExtracted("ScrollStyles").Root!
            .Elements(ThemeContractTests.Presentation + "Style")
            .Select(style => style.Attribute("Selector")!.Value)];

        Assert.Equal(ViewportBoundSelectors, selectors[^2..]);
        Assert.All(selectors[..^2], selector => Assert.DoesNotContain("viewportBound", selector, StringComparison.Ordinal));
    }

    /// <summary>The bar's own setters apply without a control template: 14 px thick, transparent, never hidden.</summary>
    [AvaloniaTheory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public void ScrollBarSettersApplyToBothOrientations(Orientation orientation)
    {
        var bar = new ScrollBar { Orientation = orientation };
        Window host = CreateHost(bar);
        try
        {
            host.Show();
            if (orientation == Orientation.Vertical)
            {
                Assert.Equal((14, 14, 14), (bar.Width, bar.MinWidth, bar.MaxWidth));
                Assert.True(double.IsNaN(bar.Height));
            }
            else
            {
                Assert.Equal((14, 14, 14), (bar.Height, bar.MinHeight, bar.MaxHeight));
                Assert.True(double.IsNaN(bar.Width));
            }

            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(bar.Background).Color);
            Assert.False(bar.AllowAutoHide);
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>Auto-hide is off for scroll viewers and, through the attached property, for templated controls.</summary>
    [AvaloniaFact]
    public void AutoHideIsOffForScrollViewersAndTemplatedControls()
    {
        var viewer = new ScrollViewer();
        var list = new ListBox { Height = 100, ItemsSource = new[] { "a", "b" } };
        var panel = new StackPanel { Children = { viewer, list } };
        Window host = CreateHost(panel, fluent: true, dark: false);
        try
        {
            host.Show();
            ScrollViewer inner = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.False(viewer.AllowAutoHide);
            Assert.False(ScrollViewer.GetAllowAutoHide(list));
            Assert.False(inner.AllowAutoHide);
            Assert.True(ScrollViewer.GetAllowAutoHide(panel));
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>
    /// Under the Fluent template: a 14 px lane, a centred 6 px thumb, a hidden track and hidden line buttons
    /// that still take space, and the hover brush while the pointer is over the thumb.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(Orientation.Vertical, false)]
    [InlineData(Orientation.Vertical, true)]
    [InlineData(Orientation.Horizontal, false)]
    [InlineData(Orientation.Horizontal, true)]
    public void FluentScrollBarPartsFollowTheFrozenStyles(Orientation orientation, bool dark)
    {
        bool vertical = orientation == Orientation.Vertical;
        var bar = new ScrollBar
        {
            Orientation = orientation,
            Maximum = 100,
            ViewportSize = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (vertical)
        {
            bar.Height = 160;
        }
        else
        {
            bar.Width = 160;
        }

        Window host = CreateHost(bar, fluent: true, dark);
        try
        {
            host.Show();
            Thumb thumb = Assert.Single(bar.GetVisualDescendants().OfType<Thumb>());
            Rectangle track = Assert.Single(bar.GetVisualDescendants().OfType<Rectangle>(), shape => shape.Name == "TrackRect");
            RepeatButton[] repeatButtons = [.. bar.GetVisualDescendants().OfType<RepeatButton>()];
            RepeatButton[] lineButtons = [.. repeatButtons.Where(button => button.Name is "PART_LineUpButton" or "PART_LineDownButton")];

            Assert.Equal(14, vertical ? bar.Bounds.Width : bar.Bounds.Height);
            Assert.Equal(6, vertical ? thumb.Bounds.Width : thumb.Bounds.Height);
            Point thumbOrigin = thumb.TranslatePoint(default, bar)!.Value;
            Assert.Equal(4, vertical ? thumbOrigin.X : thumbOrigin.Y);
            Assert.Equal(0, track.Opacity);
            Assert.NotEmpty(repeatButtons);
            Assert.All(repeatButtons, button => Assert.Equal(0, button.Opacity));
            Assert.Equal(2, lineButtons.Length);
            Assert.All(lineButtons, button => Assert.True((vertical ? button.Bounds.Height : button.Bounds.Width) > 0));
            AssertBrush("NfcTextDisabledBrush", thumb.Background, dark);

            Point thumbCenter = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), host)!.Value;
            host.MouseMove(thumbCenter);
            AssertBrush("NfcTextMutedBrush", thumb.Background, dark);
            Border thumbBorder = Assert.IsType<Border>(Assert.Single(thumb.GetVisualChildren()));
            AssertBrush("NfcTextMutedBrush", thumbBorder.Background, dark);

            Point dragEnd = thumbCenter + (vertical ? new Vector(0, 40) : new Vector(40, 0));
            host.MouseDown(thumbCenter, MouseButton.Left);
            host.MouseMove(dragEnd, RawInputModifiers.LeftMouseButton);
            host.MouseUp(dragEnd, MouseButton.Left);
            host.UpdateLayout();
            double afterDrag = bar.Value;
            Assert.InRange(afterDrag, double.Epsilon, bar.Maximum);
            Assert.Equal(6, vertical ? thumb.Bounds.Width : thumb.Bounds.Height);

            // A click on the hidden track after the thumb still pages forward.
            Rect thumbBounds = new(thumb.TranslatePoint(default, host)!.Value, thumb.Bounds.Size);
            Point page = vertical
                ? new Point(thumbBounds.Center.X, thumbBounds.Bottom + 8)
                : new Point(thumbBounds.Right + 8, thumbBounds.Center.Y);
            host.MouseDown(page, MouseButton.Left);
            host.MouseUp(page, MouseButton.Left);
            host.UpdateLayout();
            Assert.True(bar.Value > afterDrag);

            host.MouseMove(new Point(250, 150));
            AssertBrush("NfcTextDisabledBrush", thumb.Background, dark);
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>Viewport-bound content takes the viewport width when the vertical bar is shown.</summary>
    [AvaloniaFact]
    public void ViewportBoundContentTakesTheViewportWidth()
    {
        (ScrollViewer viewer, MeasureCountingBorder content, Border inner) = CreateViewportBoundViewer();
        Window host = CreateHost(viewer, fluent: true, dark: false);
        try
        {
            host.Show();
            inner.Height = 500;
            host.UpdateLayout();
            ScrollBar verticalBar = Assert.Single(viewer.GetVisualDescendants().OfType<ScrollBar>(), bar => bar.Name == "PART_VerticalScrollBar");

            Assert.True(verticalBar.IsVisible);
            Assert.Equal(viewer.Viewport.Width, content.Bounds.Width);
            Assert.True(content.Bounds.Width < viewer.Bounds.Width);
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>The bound width follows the vertical bar as it appears and disappears, and settles at once.</summary>
    [AvaloniaFact]
    public void ViewportBoundWidthSettlesWhenTheVerticalBarComesAndGoes()
    {
        (ScrollViewer viewer, MeasureCountingBorder content, Border inner) = CreateViewportBoundViewer();
        Window host = CreateHost(viewer, fluent: true, dark: false);
        try
        {
            host.Show();
            var widths = new List<double>();
            foreach (double height in ContentHeights)
            {
                inner.Height = height;
                content.MeasureCount = 0;
                host.UpdateLayout();
                double settled = content.Bounds.Width;

                // Avalonia 12.1.1 settles each change in three measure passes of the content.
                Assert.Equal(3, content.MeasureCount);
                Assert.Equal(viewer.Viewport.Width, settled);

                content.MeasureCount = 0;
                host.UpdateLayout();
                host.UpdateLayout();
                Assert.Equal(0, content.MeasureCount);
                Assert.Equal(settled, content.Bounds.Width);
                widths.Add(settled);
            }

            Assert.Equal(widths[0], widths[2]);
            Assert.Equal(widths[1], widths[3]);
            Assert.True(widths[0] < widths[1]);
            Assert.Equal(viewer.Bounds.Width, widths[1]);
        }
        finally
        {
            host.Close();
        }
    }

    private static (ScrollViewer Viewer, MeasureCountingBorder Content, Border Inner) CreateViewportBoundViewer()
    {
        var inner = new Border { Height = 100 };
        var content = new MeasureCountingBorder { Child = inner };
        content.Classes.Add("viewportBoundContent");
        var viewer = new ScrollViewer
        {
            Width = 300,
            Height = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };
        viewer.Classes.Add("viewportBoundScroll");
        return (viewer, content, inner);
    }

    private static Window CreateHost(Control content, bool fluent = false, bool dark = false)
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml");
        var host = new Window
        {
            Width = 300,
            Height = 200,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        if (fluent)
        {
            host.Styles.Add(new FluentTheme());
        }

        host.Styles.Add(new StyleInclude(uri) { Source = uri });

        // Set the content after the styles: a control caches a missing implicit theme when it is attached.
        host.Content = content;
        return host;
    }

    private static void AssertBrush(string key, IBrush? actual, bool dark)
    {
        XElement root = ThemeContractTests.ReadBaseline("ThemeTokens").Root!;
        XElement theme = Assert.Single(root.Descendants(ThemeContractTests.Presentation + "ResourceDictionary"),
            element => element.Attribute(ThemeContractTests.Xaml + "Key")?.Value == (dark ? "Dark" : "Light"));
        XElement brush = Assert.Single(theme.Elements(), element => element.Attribute(ThemeContractTests.Xaml + "Key")?.Value == key);
        Assert.Equal(Color.Parse(brush.Attribute("Color")!.Value), Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    }

    private static string[] Keys(IEnumerable<XElement> elements) =>
        [.. elements.Select(element => element.Attribute(ThemeContractTests.Xaml + "Key")?.Value)
            .OfType<string>()];

    /// <summary>A border that counts its measure passes and keeps the <see cref="Border"/> style key.</summary>
    private sealed class MeasureCountingBorder : Border
    {
        public int MeasureCount { get; set; }

        protected override Type StyleKeyOverride => typeof(Border);

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            return base.MeasureOverride(availableSize);
        }
    }
}
