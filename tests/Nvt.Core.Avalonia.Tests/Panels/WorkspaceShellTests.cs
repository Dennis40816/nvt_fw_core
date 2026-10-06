// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Panels;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Panels;

/// <summary>Characterizes NFH's workspace regions and preserved column proportions.</summary>
public sealed class WorkspaceShellTests
{
    /// <summary>Pins the frozen strings, nullable slots and product column width defaults.</summary>
    [AvaloniaFact]
    public void DefaultsMatchFrozenWorkspace()
    {
        var shell = new WorkspaceShell();
        Assert.Equal(string.Empty, shell.Title);
        Assert.Equal(string.Empty, shell.Subtitle);
        Assert.Null(shell.HeaderRight);
        Assert.Null(shell.SummaryContent);
        Assert.Null(shell.ToolbarContent);
        Assert.Null(shell.LeftContent);
        Assert.Null(shell.RightContent);
        Assert.Null(shell.FooterContent);
        Assert.Equal(new GridLength(2.2, GridUnitType.Star), shell.LeftColumnWidth);
        Assert.Equal(new GridLength(1, GridUnitType.Star), shell.RightColumnWidth);
    }

    /// <summary>Ports NFH's workspace layout smoke concern with synthetic content in every slot.</summary>
    [AvaloniaFact]
    public void EveryContentSlotAppearsInItsFrozenRegion()
    {
        var headerRight = new TextBlock { Text = "Header action" };
        var summary = new TextBlock { Text = "Summary" };
        var toolbar = new TextBlock { Text = "Toolbar" };
        var left = new TextBlock { Text = "Left" };
        var right = new TextBlock { Text = "Right" };
        var footer = new TextBlock { Text = "Footer" };
        var shell = new WorkspaceShell
        {
            Title = "Workspace title", Subtitle = "Workspace subtitle", HeaderRight = headerRight,
            SummaryContent = summary, ToolbarContent = toolbar, LeftContent = left,
            RightContent = right, FooterContent = footer,
        };
        Window host = PanelsTestHost.Create(shell);
        try
        {
            SelectableTextBlock title = Assert.Single(shell.GetVisualDescendants().OfType<SelectableTextBlock>(),
                text => text.Text == shell.Title);
            SelectableTextBlock subtitle = Assert.Single(shell.GetVisualDescendants().OfType<SelectableTextBlock>(),
                text => text.Text == shell.Subtitle);
            Assert.Equal(16, title.FontSize);
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            Assert.Equal(TextWrapping.Wrap, subtitle.TextWrapping);
            Assert.True(title.IsEffectivelyVisible);
            Assert.True(subtitle.IsEffectivelyVisible);
            ContentPresenter headerPresenter = Presenter(shell, headerRight);
            Grid headerGrid = Assert.IsType<Grid>(headerPresenter.GetVisualParent());
            Grid layout = Assert.IsType<Grid>(headerGrid.GetVisualParent());
            Assert.Equal(0, Grid.GetRow(headerGrid));
            Assert.Equal(1, Grid.GetColumn(headerPresenter));
            Assert.Equal(HorizontalAlignment.Right, headerPresenter.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, headerPresenter.VerticalAlignment);
            Assert.Equal(16, headerGrid.ColumnSpacing);
            Assert.Equal(14, layout.RowSpacing);
            Assert.Collection(layout.RowDefinitions,
                row => Assert.Equal(GridLength.Auto, row.Height),
                row => Assert.Equal(GridLength.Auto, row.Height),
                row => Assert.Equal(GridLength.Auto, row.Height),
                row => Assert.Equal(new GridLength(1, GridUnitType.Star), row.Height),
                row => Assert.Equal(GridLength.Auto, row.Height));
            Assert.Equal(new Thickness(14), Assert.IsType<Border>(layout.GetVisualParent()).Padding);

            AssertRegion(shell, summary, layout, 1);
            AssertRegion(shell, toolbar, layout, 2);
            ContentPresenter leftPresenter = Presenter(shell, left);
            ContentPresenter rightPresenter = Presenter(shell, right);
            Grid main = Assert.IsType<Grid>(leftPresenter.GetVisualParent());
            Assert.Same(main, rightPresenter.GetVisualParent());
            Assert.Equal(0, Grid.GetColumn(leftPresenter));
            Assert.Equal(1, Grid.GetColumn(rightPresenter));
            Assert.Equal(12, main.ColumnSpacing);
            Border mainRegion = Assert.IsType<Border>(main.GetVisualParent());
            Assert.Equal(3, Grid.GetRow(mainRegion));
            Assert.Same(layout, mainRegion.GetVisualParent());
            Assert.Equal(new Thickness(8), mainRegion.Padding);
            ContentPresenter footerPresenter = Presenter(shell, footer);
            Assert.Same(layout, footerPresenter.GetVisualParent());
            Assert.Equal(4, Grid.GetRow(footerPresenter));
            Assert.True(headerRight.IsEffectivelyVisible);
            Assert.True(summary.IsEffectivelyVisible);
            Assert.True(toolbar.IsEffectivelyVisible);
            Assert.True(left.IsEffectivelyVisible);
            Assert.True(right.IsEffectivelyVisible);
            Assert.True(footer.IsEffectivelyVisible);
            Assert.True(left.Bounds.Width > 0);
            Assert.True(right.Bounds.Width > 0);
            Assert.True(summary.TranslatePoint(default, shell)!.Value.Y < toolbar.TranslatePoint(default, shell)!.Value.Y);
            Assert.True(toolbar.TranslatePoint(default, shell)!.Value.Y < left.TranslatePoint(default, shell)!.Value.Y);
            Assert.True(left.TranslatePoint(default, shell)!.Value.Y < footer.TranslatePoint(default, shell)!.Value.Y);
            Assert.True(left.TranslatePoint(default, shell)!.Value.X < right.TranslatePoint(default, shell)!.Value.X);
            shell.Title = "Updated title";
            shell.Subtitle = "Updated subtitle";
            Assert.Equal(shell.Title, title.Text);
            Assert.Equal(shell.Subtitle, subtitle.Text);
        }
        finally { host.Close(); }
    }

    /// <summary>Default star widths reach the template grid and produce NFH's original proportions.</summary>
    [AvaloniaFact]
    public void DefaultColumnWidthsApplyToTheTemplateGrid()
    {
        var shell = new WorkspaceShell();
        Window host = PanelsTestHost.Create(shell);
        try
        {
            Grid main = MainGrid(shell);
            Assert.Collection(main.ColumnDefinitions,
                column => Assert.Equal(new GridLength(2.2, GridUnitType.Star), column.Width),
                column => Assert.Equal(new GridLength(1, GridUnitType.Star), column.Width));
            Assert.True(main.ColumnDefinitions[1].ActualWidth > 0);
            Assert.Equal(2.2, main.Children[0].Bounds.Width / main.Children[1].Bounds.Width, 0.01);
        }
        finally { host.Close(); }
    }

    /// <summary>Custom pixel and star widths apply both before templating and after a live update.</summary>
    [AvaloniaFact]
    public void CustomColumnWidthsApplyAndUpdateTheTemplateGrid()
    {
        var shell = new WorkspaceShell { LeftColumnWidth = new GridLength(180), RightColumnWidth = new GridLength(3, GridUnitType.Star) };
        Window host = PanelsTestHost.Create(shell);
        try
        {
            Grid main = MainGrid(shell);
            Assert.Equal(shell.LeftColumnWidth, main.ColumnDefinitions[0].Width);
            Assert.Equal(shell.RightColumnWidth, main.ColumnDefinitions[1].Width);
            Assert.Equal(180, main.Children[0].Bounds.Width);
            shell.LeftColumnWidth = new GridLength(1, GridUnitType.Star);
            shell.RightColumnWidth = new GridLength(2, GridUnitType.Star);
            host.UpdateLayout();
            Assert.Equal(shell.LeftColumnWidth, main.ColumnDefinitions[0].Width);
            Assert.Equal(shell.RightColumnWidth, main.ColumnDefinitions[1].Width);
            Assert.Equal(0.5, main.Children[0].Bounds.Width / main.Children[1].Bounds.Width, 0.01);
            shell.LeftColumnWidth = GridLength.Auto;
            Assert.Equal(GridLength.Auto, main.ColumnDefinitions[0].Width);
        }
        finally { host.Close(); }
    }

    private static ContentPresenter Presenter(WorkspaceShell shell, Control content) =>
        Assert.Single(shell.GetVisualDescendants().OfType<ContentPresenter>(), presenter => ReferenceEquals(presenter.Content, content));

    private static Grid MainGrid(WorkspaceShell shell) =>
        Assert.Single(shell.GetVisualDescendants().OfType<Grid>(), grid => grid.ColumnSpacing == 12);

    private static void AssertRegion(WorkspaceShell shell, Control content, Grid layout, int row)
    {
        Border region = Assert.IsType<Border>(Presenter(shell, content).GetVisualParent());
        Assert.Same(layout, region.GetVisualParent());
        Assert.Equal(row, Grid.GetRow(region));
        Assert.Contains("workspaceTopLayerBox", region.Classes);
        Assert.Equal(new Thickness(10, 8), region.Padding);
        Assert.Equal(new Thickness(1), region.BorderThickness);
        Assert.Equal(new CornerRadius(6), region.CornerRadius);
    }
}
