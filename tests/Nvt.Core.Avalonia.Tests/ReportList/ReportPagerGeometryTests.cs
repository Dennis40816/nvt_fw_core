// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.ReportList;

/// <summary>Compares actual compiled trees with the independent frozen fragments under identical host resources.</summary>
public sealed class ReportPagerGeometryTests
{
    /// <summary>Checks the entire visual tree and exact measurements in both viewports, themes and label sets.</summary>
    /// <param name="windowed">Whether to compare the fixed-window tree.</param>
    /// <param name="width">The host viewport width in DIP.</param>
    /// <param name="dark">Whether the host uses the dark theme.</param>
    /// <param name="chinese">Whether the host supplies Traditional Chinese labels.</param>
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
    public void CompiledTreesMatchFrozenGeometryBeforeAndAfterNavigation(bool windowed, double width, bool dark, bool chinese)
    {
        object frozenModel = PagerTemplateTestHost.CreateModel(9, 4, windowed, chinese);
        object coreModel = PagerTemplateTestHost.CreateModel(9, 4, windowed, chinese);
        using PagerTemplateTestHost frozen = PagerTemplateTestHost.Create(frozenModel, windowed, true, width, dark);
        using PagerTemplateTestHost core = PagerTemplateTestHost.Create(coreModel, windowed, false, width, dark);
        AssertTreesEqual(frozen.Root, core.Root);
        Advance(frozen, windowed);
        Advance(core, windowed);
        AssertTreesEqual(frozen.Root, core.Root);
        Advance(frozen, windowed);
        Advance(core, windowed);
        AssertTreesEqual(frozen.Root, core.Root);
        if (windowed)
        {
            PagerTemplateTestHost.Execute(Assert.IsType<Button>(Assert.IsType<Grid>(frozen.Root.Children[1]).Children[0]));
            PagerTemplateTestHost.Execute(Assert.IsType<Button>(Assert.IsType<Grid>(core.Root.Children[1]).Children[0]));
            AssertTreesEqual(frozen.Root, core.Root);
        }
    }

    /// <summary>Empty, single-row, exact and adjacent pages retain the frozen tree with a positive maximum-sized batch.</summary>
    /// <param name="windowed">Whether to compare the fixed-window tree.</param>
    /// <param name="count">The synthetic row count.</param>
    /// <param name="pageSize">The host batch size, including the largest valid integer and its neighbour.</param>
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
    public void EdgeInputsKeepFrozenGeometry(bool windowed, int count, int pageSize)
    {
        using PagerTemplateTestHost frozen = PagerTemplateTestHost.Create(
            PagerTemplateTestHost.CreateModel(count, pageSize, windowed, false), windowed, true);
        using PagerTemplateTestHost core = PagerTemplateTestHost.Create(
            PagerTemplateTestHost.CreateModel(count, pageSize, windowed, false), windowed);
        AssertTreesEqual(frozen.Root, core.Root);
    }

    internal static void Advance(PagerTemplateTestHost host, bool windowed)
    {
        Button action = windowed
            ? Assert.IsType<Button>(Assert.IsType<Grid>(host.Root.Children[1]).Children[1])
            : Assert.IsType<Button>(host.Root.Children[1]);
        PagerTemplateTestHost.Execute(action);
    }

    internal static void AssertTreesEqual(Control expected, Control actual) =>
        Assert.Equal(Snapshot(expected).ToString(SaveOptions.DisableFormatting), Snapshot(actual).ToString(SaveOptions.DisableFormatting));

    private static XElement Snapshot(Control control)
    {
        var node = new XElement(control.GetType().Name,
            new XAttribute("Bounds", FormattableString.Invariant($"{control.Bounds}")),
            new XAttribute("DesiredSize", FormattableString.Invariant($"{control.DesiredSize}")),
            new XAttribute("Margin", control.Margin),
            new XAttribute("HorizontalAlignment", control.HorizontalAlignment),
            new XAttribute("VerticalAlignment", control.VerticalAlignment),
            new XAttribute("Visible", control.IsVisible),
            new XAttribute("EffectivelyEnabled", control.IsEffectivelyEnabled),
            new XAttribute("Classes", string.Join(' ', control.Classes)),
            new XAttribute("Name", AutomationProperties.GetName(control) ?? string.Empty),
            new XAttribute("LiveSetting", AutomationProperties.GetLiveSetting(control)),
            new XAttribute("Tooltip", ToolTip.GetTip(control)?.ToString() ?? string.Empty));
        if (control is Grid grid)
        {
            node.Add(new XAttribute("Rows", string.Join(',', grid.RowDefinitions.Select(row => row.Height))),
                new XAttribute("Columns", string.Join(',', grid.ColumnDefinitions.Select(column => column.Width))),
                new XAttribute("RowSpacing", grid.RowSpacing), new XAttribute("ColumnSpacing", grid.ColumnSpacing));
        }

        if (control is TextBlock text)
        {
            node.Add(new XAttribute("Text", text.Text ?? string.Empty),
                new XAttribute("FontFamily", text.FontFamily), new XAttribute("FontSize", text.FontSize),
                new XAttribute("FontWeight", text.FontWeight), new XAttribute("FontStyle", text.FontStyle),
                new XAttribute("Wrapping", text.TextWrapping), new XAttribute("Trimming", text.TextTrimming),
                new XAttribute("Foreground", text.Foreground?.ToString() ?? string.Empty));
        }

        if (control is Button button)
        {
            node.Add(new XAttribute("Content", button.Content?.ToString() ?? string.Empty),
                new XAttribute("CanExecute", button.Command?.CanExecute(button.CommandParameter) ?? false),
                new XAttribute("FontFamily", button.FontFamily), new XAttribute("FontSize", button.FontSize),
                new XAttribute("Padding", button.Padding), new XAttribute("BorderThickness", button.BorderThickness),
                new XAttribute("CornerRadius", button.CornerRadius));
        }

        node.Add(control.GetVisualChildren().OfType<Control>().Select(Snapshot));
        return node;
    }
}
