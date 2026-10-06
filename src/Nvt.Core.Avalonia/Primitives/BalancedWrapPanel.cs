// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;

namespace Nvt.Core.Avalonia.Primitives;

/// <summary>Wraps children at their desired sizes and optionally balances a single-child last row.</summary>
public sealed class BalancedWrapPanel : Panel
{
    /// <summary>Defines the horizontal spacing between children, defaulting to zero.</summary>
    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<BalancedWrapPanel, double>(nameof(ItemSpacing));

    /// <summary>Defines the vertical spacing between rows, defaulting to zero.</summary>
    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<BalancedWrapPanel, double>(nameof(LineSpacing));

    /// <summary>Defines whether a single-child last row is balanced, defaulting to true.</summary>
    public static readonly StyledProperty<bool> BalanceLastRowProperty =
        AvaloniaProperty.Register<BalancedWrapPanel, bool>(nameof(BalanceLastRow), true);

    static BalancedWrapPanel()
    {
        AffectsMeasure<BalancedWrapPanel>(ItemSpacingProperty, LineSpacingProperty, BalanceLastRowProperty);
    }

    /// <summary>Gets or sets the horizontal spacing between children.</summary>
    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    /// <summary>Gets or sets the vertical spacing between rows.</summary>
    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    /// <summary>Gets or sets whether the preceding row's last child can join a single-child last row.</summary>
    public bool BalanceLastRow
    {
        get => GetValue(BalanceLastRowProperty);
        set => SetValue(BalanceLastRowProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        var rows = BuildRows(GetVisibleChildren(), availableSize.Width);
        return BuildDesiredSize(rows);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = BuildRows(GetVisibleChildren(), finalSize.Width);
        var y = 0d;
        foreach (var row in rows)
        {
            var x = 0d;
            foreach (var child in row.Children)
            {
                var desired = child.DesiredSize;
                child.Arrange(new Rect(x, y, desired.Width, desired.Height));
                x += desired.Width + ItemSpacing;
            }

            y += row.Height + LineSpacing;
        }

        return finalSize;
    }

    private List<Control> GetVisibleChildren()
    {
        return Children
            .Where(static child => child.IsVisible)
            .ToList();
    }

    private List<Row> BuildRows(IReadOnlyList<Control> children, double availableWidth)
    {
        var maxWidth = double.IsInfinity(availableWidth) || double.IsNaN(availableWidth)
            ? double.PositiveInfinity
            : Math.Max(0, availableWidth);
        var rows = new List<Row>();
        var current = new Row();

        foreach (var child in children)
        {
            var desired = child.DesiredSize;
            var nextWidth = current.Children.Count == 0
                ? desired.Width
                : current.Width + ItemSpacing + desired.Width;
            if (current.Children.Count > 0 && nextWidth > maxWidth)
            {
                rows.Add(current);
                current = new Row();
            }

            current.Add(child, ItemSpacing);
        }

        if (current.Children.Count > 0)
        {
            rows.Add(current);
        }

        BalanceRows(rows, maxWidth);
        return rows;
    }

    private void BalanceRows(List<Row> rows, double maxWidth)
    {
        if (!BalanceLastRow || rows.Count < 2 || double.IsInfinity(maxWidth))
        {
            return;
        }

        var last = rows[^1];
        var previous = rows[^2];
        if (last.Children.Count != 1 || previous.Children.Count <= 1)
        {
            return;
        }

        var moved = previous.Children[^1];
        var balancedLastWidth = last.Width + ItemSpacing + moved.DesiredSize.Width;
        if (balancedLastWidth > maxWidth)
        {
            return;
        }

        previous.RemoveLast(ItemSpacing);
        last.InsertFirst(moved, ItemSpacing);
    }

    private Size BuildDesiredSize(IReadOnlyList<Row> rows)
    {
        if (rows.Count == 0)
        {
            return default;
        }

        var width = rows.Max(static row => row.Width);
        var height = rows.Sum(static row => row.Height) + (rows.Count - 1) * LineSpacing;
        return new Size(width, height);
    }

    private sealed class Row
    {
        public List<Control> Children { get; } = new();

        public double Width { get; private set; }

        public double Height { get; private set; }

        public void Add(Control child, double itemSpacing)
        {
            Width += Children.Count == 0 ? child.DesiredSize.Width : itemSpacing + child.DesiredSize.Width;
            Height = Math.Max(Height, child.DesiredSize.Height);
            Children.Add(child);
        }

        public void InsertFirst(Control child, double itemSpacing)
        {
            Width += Children.Count == 0 ? child.DesiredSize.Width : itemSpacing + child.DesiredSize.Width;
            Height = Math.Max(Height, child.DesiredSize.Height);
            Children.Insert(0, child);
        }

        public void RemoveLast(double itemSpacing)
        {
            if (Children.Count == 0)
            {
                return;
            }

            var child = Children[^1];
            Children.RemoveAt(Children.Count - 1);
            Width -= Children.Count == 0 ? child.DesiredSize.Width : itemSpacing + child.DesiredSize.Width;
            Height = Children.Count == 0
                ? 0
                : Children.Max(static item => item.DesiredSize.Height);
        }
    }
}
