// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.ReportList;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.ReportList;

/// <summary>Checks pager geometry under the Core Theme with fixed window, font and DPI inputs.</summary>
public sealed class ReportPagerCoreThemeGeometryTests
{
    private const double WindowWidth = 960;
    private const double WindowHeight = 180;
    private const double Scaling = 1;
    private static readonly FontFamily SharedFamily = new("fonts:Inter#Inter, Microsoft JhengHei UI, Noto Sans CJK TC, Noto Sans TC, Segoe UI");

    // Bundled Inter covers both label sets; geometry never falls back to installed CJK fonts.
    // The existing binding/accessibility tests retain the English and Traditional Chinese labels.
    private static readonly ReportListLabels LongLabels = new(
        "There are no report items to display", "Show earlier report items", "Show later report items", "All report items have been loaded",
        static (first, last, total) => $"Displaying report items {first} through {last} from a total of {total} available items",
        static (visible, total) => $"Displaying {visible} from {total} available report items",
        static (next, remaining) => $"Load {next} more items ({remaining} left)");

    /// <summary>Checks resolved Core resources and frozen geometry through navigation and both disabled endpoints.</summary>
    /// <param name="windowed">Whether to use the fixed-window pager.</param>
    /// <param name="width">The pager width inside the fixed-size window.</param>
    /// <param name="dark">Whether to use the dark Core palette.</param>
    /// <param name="longLabels">Whether to use the longer bundled-font label set.</param>
    [AvaloniaTheory]
    [InlineData(false, 240, false, false)]
    [InlineData(false, 240, false, true)]
    [InlineData(false, 240, true, false)]
    [InlineData(false, 240, true, true)]
    [InlineData(false, 960, false, false)]
    [InlineData(false, 960, false, true)]
    [InlineData(false, 960, true, false)]
    [InlineData(false, 960, true, true)]
    [InlineData(true, 240, false, false)]
    [InlineData(true, 240, false, true)]
    [InlineData(true, 240, true, false)]
    [InlineData(true, 240, true, true)]
    [InlineData(true, 960, false, false)]
    [InlineData(true, 960, false, true)]
    [InlineData(true, 960, true, false)]
    [InlineData(true, 960, true, true)]
    public void CoreThemeGeometryUsesFixedInputsThroughNavigation(bool windowed, double width, bool dark, bool longLabels)
    {
        ReportListLabels labels = longLabels ? LongLabels : ReportListTestData.English;
        using PagerTemplateTestHost frozen = Create(CreateModel(9, 4, windowed, labels), windowed, true, width, dark);
        using PagerTemplateTestHost core = Create(CreateModel(9, 4, windowed, labels), windowed, false, width, dark);
        AssertGeometry(frozen, core, windowed, width, dark);
        ReportPagerGeometryTests.Advance(frozen, windowed);
        ReportPagerGeometryTests.Advance(core, windowed);
        AssertGeometry(frozen, core, windowed, width, dark);
        ReportPagerGeometryTests.Advance(frozen, windowed);
        ReportPagerGeometryTests.Advance(core, windowed);
        AssertGeometry(frozen, core, windowed, width, dark);
        if (windowed)
        {
            PagerTemplateTestHost.Execute(Assert.IsType<Button>(Assert.IsType<Grid>(frozen.Root.Children[1]).Children[0]));
            PagerTemplateTestHost.Execute(Assert.IsType<Button>(Assert.IsType<Grid>(core.Root.Children[1]).Children[0]));
            AssertGeometry(frozen, core, windowed, width, dark);
        }
    }

    /// <summary>Checks empty, single, exact, adjacent and maximum-batch inputs under the same Core Theme environment.</summary>
    /// <param name="windowed">Whether to use the fixed-window pager.</param>
    /// <param name="count">The synthetic row count.</param>
    /// <param name="pageSize">The batch or window size.</param>
    [AvaloniaTheory]
    [InlineData(false, 0, 1)]
    [InlineData(true, 0, 1)]
    [InlineData(false, 1, 1)]
    [InlineData(true, 1, 1)]
    [InlineData(false, 63, 64)]
    [InlineData(true, 63, 64)]
    [InlineData(false, 64, 64)]
    [InlineData(true, 64, 64)]
    [InlineData(false, 65, 64)]
    [InlineData(true, 65, 64)]
    [InlineData(false, 2, int.MaxValue - 1)]
    [InlineData(true, 2, int.MaxValue - 1)]
    [InlineData(false, 2, int.MaxValue)]
    [InlineData(true, 2, int.MaxValue)]
    public void CoreThemeEdgeGeometryUsesFixedInputs(bool windowed, int count, int pageSize)
    {
        using PagerTemplateTestHost frozen = Create(CreateModel(count, pageSize, windowed, ReportListTestData.English), windowed, true, 336, false);
        using PagerTemplateTestHost core = Create(CreateModel(count, pageSize, windowed, ReportListTestData.English), windowed, false, 336, false);
        AssertGeometry(frozen, core, windowed, 336, false);
    }

    private static object CreateModel(int count, int pageSize, bool windowed, ReportListLabels labels) => windowed
        ? ReportWindowedListViewModel.Create(PagerTemplateTestHost.Rows(count), pageSize, labels)
        : ReportPagedListViewModel.Create(PagerTemplateTestHost.Rows(count), pageSize, labels);

    private static PagerTemplateTestHost Create(object model, bool windowed, bool frozen, double width, bool dark)
    {
        PagerTemplateTestHost host = PagerTemplateTestHost.Create(model, windowed, frozen, WindowWidth, dark);
        try
        {
            var tokens = new Uri("avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml");
            host.Window.Resources.MergedDictionaries.Add(new ResourceInclude(tokens) { Source = tokens });
            var buttons = new Uri("avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml");
            var scroll = new Uri("avares://Nvt.Core.Avalonia/Theme/ScrollStyles.axaml");
            host.Window.Styles.Add(new StyleInclude(buttons) { Source = buttons });
            host.Window.Styles.Add(new StyleInclude(scroll) { Source = scroll });
            host.Window.Classes.Add("reducedMotion");
            host.Window.SizeToContent = SizeToContent.Manual;
            host.Window.WindowState = WindowState.Normal;
            host.Window.UseLayoutRounding = true;
            host.Window.FontFamily = Assert.IsType<FontFamily>(host.Window.FindResource("NfcUiFontFamily"));
            host.Window.FontSize = Assert.IsType<double>(host.Window.FindResource("NfcFontSize13"));
            host.Window.Resources["Nvt.ReportList.WindowedSpacing"] = Assert.IsType<double>(host.Window.FindResource("NfcSpace8"));
            host.Window.SetRenderScaling(Scaling);
            host.Content.Width = width;
            host.Content.HorizontalAlignment = HorizontalAlignment.Left;
            PagerTemplateTestHost.Render(host.Window);
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private static void AssertGeometry(PagerTemplateTestHost frozen, PagerTemplateTestHost core, bool windowed, double width, bool dark)
    {
        AssertEnvironment(frozen, windowed, width, dark);
        AssertEnvironment(core, windowed, width, dark);
        ReportPagerGeometryTests.AssertTreesEqual(frozen.Root, core.Root);
    }

    private static void AssertEnvironment(PagerTemplateTestHost host, bool windowed, double width, bool dark)
    {
        Assert.Equal(new Size(WindowWidth, WindowHeight), host.Window.ClientSize);
        Assert.Equal(Scaling, host.Window.RenderScaling);
        Assert.Equal(width, host.Content.Bounds.Width);
        Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, host.Window.ActualThemeVariant);
        Assert.True(host.Window.UseLayoutRounding);
        Assert.Equal(SharedFamily, Assert.IsType<FontFamily>(host.Window.FindResource("Nvt.Font.NfcLegacy.Ui.Family")));
        Assert.Equal(SharedFamily, host.Window.FontFamily);
        Assert.Equal(13d, host.Window.FontSize);
        Assert.Equal(13d, Assert.IsType<double>(host.Window.FindResource("Nvt.Font.NfcLegacy.Size13")));
        Assert.Equal(8d, Assert.IsType<double>(host.Window.FindResource("NfcSpace8")));
        Assert.Equal(8d, Assert.IsType<double>(host.Window.FindResource("Nvt.ReportList.WindowedSpacing")));
        TextBlock status = Assert.IsType<TextBlock>(host.Root.Children[0]);
        Assert.Equal("captionText", Assert.Single(status.Classes));
        if (windowed)
        {
            Assert.Equal(8d, host.Root.RowSpacing);
            Grid actions = Assert.IsType<Grid>(host.Root.Children[1]);
            Assert.Equal(8d, actions.ColumnSpacing);
            Assert.All(actions.ColumnDefinitions, column => Assert.Equal(new GridLength(1, GridUnitType.Star), column.Width));
            Assert.Equal(actions.ColumnDefinitions[0].ActualWidth, actions.ColumnDefinitions[1].ActualWidth);
        }
        else
        {
            Assert.Equal(new Thickness(0, 8, 0, 0), host.Root.Margin);
            Assert.Equal(10d, host.Root.ColumnSpacing);
        }

        foreach (Button button in host.Root.GetVisualDescendants().OfType<Button>())
        {
            Assert.Equal("semanticAction secondary", string.Join(' ', button.Classes.Where(name => !name.StartsWith(':'))));
            Assert.Equal(SharedFamily, button.FontFamily);
            Assert.Equal(13d, button.FontSize);
            Assert.Equal(new Thickness(8, 5, 8, 6), button.Padding);
            Assert.Equal(new Thickness(1), button.BorderThickness);
            Assert.Equal(new CornerRadius(3), button.CornerRadius);
            Assert.Equal(0d, button.MinHeight);
        }

        var typefaces = new HashSet<GlyphTypeface>();
        foreach (TextBlock text in host.Root.GetVisualDescendants().OfType<TextBlock>())
        {
            Assert.Equal(SharedFamily, text.FontFamily);
            Assert.Equal(13d, text.FontSize);
            Assert.Equal(FontWeight.Normal, text.FontWeight);
            Assert.Equal(FontStyle.Normal, text.FontStyle);
            foreach (ShapedTextRun run in text.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>())
            {
                Assert.Equal("Inter", run.GlyphRun.GlyphTypeface.FamilyName);
                Assert.Equal(FontSimulations.None, run.GlyphRun.GlyphTypeface.FontSimulations);
                Assert.All(run.GlyphRun.GlyphInfos, glyph => Assert.NotEqual((ushort)0, glyph.GlyphIndex));
                if (typefaces.Add(run.GlyphRun.GlyphTypeface))
                {
                    Assert.True(run.GlyphRun.GlyphTypeface.PlatformTypeface.TryGetStream(out Stream? stream));
                    using (stream)
                    using (Stream asset = AssetLoader.Open(new Uri("avares://Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf")))
                    {
                        Assert.Equal(SHA256.HashData(asset), SHA256.HashData(stream));
                    }
                }
            }
        }

        Assert.NotEmpty(typefaces);
    }
}
