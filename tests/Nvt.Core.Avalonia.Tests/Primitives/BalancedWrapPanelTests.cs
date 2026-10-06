// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Primitives;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Primitives;

/// <summary>Pins rectangles derived by hand from the frozen NFH wrapping and balancing algorithm.</summary>
public sealed class BalancedWrapPanelTests
{
    /// <summary>Spacing defaults to zero and last-row balancing is enabled.</summary>
    [AvaloniaFact]
    public void PropertiesHaveFrozenDefaults()
    {
        var panel = new BalancedWrapPanel();
        Assert.Equal(0, panel.ItemSpacing);
        Assert.Equal(0, panel.LineSpacing);
        Assert.True(panel.BalanceLastRow);
    }

    /// <summary>One row uses desired widths plus spacing and the tallest child for its height.</summary>
    [AvaloniaFact]
    public void OneRowKeepsChildSizesAndHorizontalSpacing()
    {
        var panel = new BalancedWrapPanel
        {
            ItemSpacing = 5,
            LineSpacing = 7,
            Children = { new FixedControl(20, 10), new FixedControl(30, 20), new FixedControl(10, 15) },
        };

        Layout(panel, 100, 20);

        Assert.Equal(new Size(70, 20), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 0, 30, 20), child.Bounds),
            child => Assert.Equal(new Rect(60, 0, 10, 15), child.Bounds));
    }

    /// <summary>Rows that exactly fit are retained; each next row starts after the maximum height and spacing.</summary>
    [AvaloniaFact]
    public void SeveralRowsUseTheirOwnMaximumHeights()
    {
        var panel = new BalancedWrapPanel
        {
            ItemSpacing = 5,
            LineSpacing = 7,
            Children =
            {
                new FixedControl(30, 10), new FixedControl(30, 20),
                new FixedControl(30, 15), new FixedControl(30, 25),
                new FixedControl(30, 12), new FixedControl(30, 18),
            },
        };

        Layout(panel, 65, 77);

        Assert.Equal(new Size(65, 77), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 30, 10), child.Bounds),
            child => Assert.Equal(new Rect(35, 0, 30, 20), child.Bounds),
            child => Assert.Equal(new Rect(0, 27, 30, 15), child.Bounds),
            child => Assert.Equal(new Rect(35, 27, 30, 25), child.Bounds),
            child => Assert.Equal(new Rect(0, 59, 30, 12), child.Bounds),
            child => Assert.Equal(new Rect(35, 59, 30, 18), child.Bounds));
    }

    /// <summary>Moving the tallest child recalculates both row heights and inserts it before the last child.</summary>
    [AvaloniaFact]
    public void BalancedLastRowMovesPreviousLastChildAndRecalculatesHeight()
    {
        var panel = FourChildren();

        Layout(panel, 70, 49);

        // Greedy rows are [1,2,3] and [4]. Moving 3 gives heights 12 and 30, with a 7-pixel gap.
        Assert.Equal(new Size(45, 49), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 0, 20, 12), child.Bounds),
            child => Assert.Equal(new Rect(0, 19, 20, 30), child.Bounds),
            child => Assert.Equal(new Rect(25, 19, 20, 15), child.Bounds));
    }

    /// <summary>Disabling balancing preserves the greedy three-child row and single-child last row.</summary>
    [AvaloniaFact]
    public void DisabledBalancingKeepsUnbalancedLastRow()
    {
        var panel = FourChildren();
        panel.BalanceLastRow = false;

        Layout(panel, 70, 52);

        Assert.Equal(new Size(70, 52), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 0, 20, 12), child.Bounds),
            child => Assert.Equal(new Rect(50, 0, 20, 30), child.Bounds),
            child => Assert.Equal(new Rect(0, 37, 20, 15), child.Bounds));
    }

    /// <summary>Only the final pair of rows is considered for balancing.</summary>
    [AvaloniaFact]
    public void BalancingLeavesEarlierRowsUnchanged()
    {
        var panel = new BalancedWrapPanel
        {
            ItemSpacing = 5,
            LineSpacing = 7,
            Children =
            {
                new FixedControl(20, 10), new FixedControl(20, 10), new FixedControl(20, 10),
                new FixedControl(20, 10), new FixedControl(20, 10), new FixedControl(20, 10),
                new FixedControl(20, 10),
            },
        };

        Layout(panel, 70, 44);

        Assert.Equal(new Size(70, 44), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(50, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(0, 17, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 17, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(0, 34, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 34, 20, 10), child.Bounds));
    }

    /// <summary>A child is not moved when it would exceed the available width.</summary>
    [AvaloniaFact]
    public void BalancingDoesNotMoveAChildThatCannotFit()
    {
        var panel = new BalancedWrapPanel
        {
            ItemSpacing = 5,
            LineSpacing = 7,
            Children = { new FixedControl(20, 10), new FixedControl(30, 20), new FixedControl(60, 15) },
        };

        Layout(panel, 60, 42);

        Assert.Equal(new Size(60, 42), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 0, 30, 20), child.Bounds),
            child => Assert.Equal(new Rect(0, 27, 60, 15), child.Bounds));
    }

    /// <summary>A single-child preceding row is never emptied by balancing.</summary>
    [AvaloniaFact]
    public void BalancingKeepsSingleChildPreviousRow()
    {
        var panel = new BalancedWrapPanel
        {
            ItemSpacing = 5,
            LineSpacing = 3,
            Children = { new FixedControl(40, 10), new FixedControl(40, 20) },
        };

        Layout(panel, 50, 33);

        Assert.Equal(new Size(40, 33), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 40, 10), child.Bounds),
            child => Assert.Equal(new Rect(0, 13, 40, 20), child.Bounds));
    }

    /// <summary>Visible zero-size children consume spacing; collapsed children consume no row space.</summary>
    [AvaloniaFact]
    public void ZeroSizeAndCollapsedChildrenKeepFrozenSpacingRules()
    {
        var hidden = new FixedControl(100, 100) { IsVisible = false };
        var panel = new BalancedWrapPanel
        {
            ItemSpacing = 5,
            LineSpacing = 7,
            Children = { new FixedControl(20, 10), new FixedControl(0, 0), hidden, new FixedControl(30, 15) },
        };

        Layout(panel, 100, 15);

        Assert.Equal(new Size(60, 15), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 0, 0, 0), child.Bounds),
            child => Assert.Equal(default, child.Bounds),
            child => Assert.Equal(new Rect(30, 0, 30, 15), child.Bounds));
        Assert.Equal(default, hidden.DesiredSize);
        Assert.Null(hidden.MeasureConstraint);
    }

    /// <summary>Empty and entirely collapsed panels request no space even with nonzero spacing.</summary>
    [AvaloniaFact]
    public void EmptyAndCollapsedOnlyPanelsHaveZeroDesiredSize()
    {
        var panel = new BalancedWrapPanel { ItemSpacing = 5, LineSpacing = 7 };
        Layout(panel, 100, 20);
        Assert.Equal(default, panel.DesiredSize);

        panel.Children.Add(new FixedControl(100, 100) { IsVisible = false });
        Layout(panel, 100, 20);
        Assert.Equal(default, panel.DesiredSize);
        Assert.Equal(default, Assert.Single(panel.Children).Bounds);
    }

    /// <summary>Infinite available width measures children without constraints and keeps a single row.</summary>
    [AvaloniaFact]
    public void InfiniteAvailableWidthKeepsOneRow()
    {
        var panel = new BalancedWrapPanel
        {
            ItemSpacing = 5,
            LineSpacing = 7,
            Children =
            {
                new FixedControl(20, 10), new FixedControl(30, 20),
                new FixedControl(40, 5), new FixedControl(10, 15),
            },
        };

        panel.Measure(Size.Infinity);
        panel.Arrange(new Rect(0, 0, 200, 20));

        Assert.Equal(new Size(115, 20), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 0, 30, 20), child.Bounds),
            child => Assert.Equal(new Rect(60, 0, 40, 5), child.Bounds),
            child => Assert.Equal(new Rect(105, 0, 10, 15), child.Bounds));
        Assert.All(panel.Children, child =>
            Assert.Equal(Size.Infinity, Assert.IsType<FixedControl>(child).MeasureConstraint));
    }

    /// <summary>Arrange recomputes rows from the final width instead of retaining measured rows.</summary>
    [AvaloniaFact]
    public void ArrangeRebuildsRowsForFinalWidth()
    {
        var panel = new BalancedWrapPanel
        {
            ItemSpacing = 5,
            LineSpacing = 7,
            Children = { new FixedControl(30, 10), new FixedControl(30, 10), new FixedControl(30, 10) },
        };

        panel.Measure(new Size(100, double.PositiveInfinity));
        Assert.Equal(new Size(100, 10), panel.DesiredSize);
        panel.Arrange(new Rect(0, 0, 65, 27));

        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 30, 10), child.Bounds),
            child => Assert.Equal(new Rect(0, 17, 30, 10), child.Bounds),
            child => Assert.Equal(new Rect(35, 17, 30, 10), child.Bounds));
    }

    /// <summary>Ports NFH's portable style guard that BalanceLastRow is available to consumer styles.</summary>
    [AvaloniaFact]
    public void BalanceLastRowCanBeSetFromStyles()
    {
        var panel = FourChildren();
        panel.Styles.Add(new Style(selector => selector.OfType<BalancedWrapPanel>())
        {
            Setters = { new Setter(BalancedWrapPanel.BalanceLastRowProperty, false) },
        });

        Layout(panel, 70, 52);

        Assert.False(panel.BalanceLastRow);
        Assert.Equal(new Size(70, 52), panel.DesiredSize);
        Assert.Collection(panel.Children,
            child => Assert.Equal(new Rect(0, 0, 20, 10), child.Bounds),
            child => Assert.Equal(new Rect(25, 0, 20, 12), child.Bounds),
            child => Assert.Equal(new Rect(50, 0, 20, 30), child.Bounds),
            child => Assert.Equal(new Rect(0, 37, 20, 15), child.Bounds));
    }

    /// <summary>All three layout properties invalidate measurement when changed.</summary>
    [AvaloniaFact]
    public void LayoutPropertyChangesInvalidateMeasurement()
    {
        var panel = FourChildren();
        Layout(panel, 70, 49);
        Assert.True(panel.IsMeasureValid);
        panel.BalanceLastRow = false;
        Assert.False(panel.IsMeasureValid);
        Layout(panel, 70, 52);
        Assert.Equal(new Size(70, 52), panel.DesiredSize);

        panel.ItemSpacing = 6;
        Assert.False(panel.IsMeasureValid);
        Layout(panel, 70, 49);
        Assert.Equal(new Size(46, 49), panel.DesiredSize);

        panel.LineSpacing = 8;
        Assert.False(panel.IsMeasureValid);
        Layout(panel, 70, 50);
        Assert.Equal(new Size(46, 50), panel.DesiredSize);
    }

    private static BalancedWrapPanel FourChildren() => new()
    {
        ItemSpacing = 5,
        LineSpacing = 7,
        Children =
        {
            new FixedControl(20, 10), new FixedControl(20, 12),
            new FixedControl(20, 30), new FixedControl(20, 15),
        },
    };

    private static void Layout(BalancedWrapPanel panel, double width, double height)
    {
        panel.UseLayoutRounding = false;
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, height));
    }

    private sealed class FixedControl(double width, double height) : Control
    {
        internal Size? MeasureConstraint { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureConstraint = availableSize;
            return new Size(width, height);
        }
    }
}
