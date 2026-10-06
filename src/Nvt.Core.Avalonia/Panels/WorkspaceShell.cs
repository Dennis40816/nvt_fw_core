// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Nvt.Core.Avalonia.Panels;

/// <summary>Arranges a workspace header, summary, toolbar, two main columns and footer.</summary>
public class WorkspaceShell : TemplatedControl
{
    /// <summary>Defines the <see cref="Title"/> property.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<WorkspaceShell, string>(nameof(Title), string.Empty);

    /// <summary>Defines the <see cref="Subtitle"/> property.</summary>
    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<WorkspaceShell, string>(nameof(Subtitle), string.Empty);

    /// <summary>Defines the <see cref="HeaderRight"/> property.</summary>
    public static readonly StyledProperty<object?> HeaderRightProperty =
        AvaloniaProperty.Register<WorkspaceShell, object?>(nameof(HeaderRight));

    /// <summary>Defines the <see cref="SummaryContent"/> property.</summary>
    public static readonly StyledProperty<object?> SummaryContentProperty =
        AvaloniaProperty.Register<WorkspaceShell, object?>(nameof(SummaryContent));

    /// <summary>Defines the <see cref="ToolbarContent"/> property.</summary>
    public static readonly StyledProperty<object?> ToolbarContentProperty =
        AvaloniaProperty.Register<WorkspaceShell, object?>(nameof(ToolbarContent));

    /// <summary>Defines the <see cref="LeftContent"/> property.</summary>
    public static readonly StyledProperty<object?> LeftContentProperty =
        AvaloniaProperty.Register<WorkspaceShell, object?>(nameof(LeftContent));

    /// <summary>Defines the <see cref="RightContent"/> property.</summary>
    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<WorkspaceShell, object?>(nameof(RightContent));

    /// <summary>Defines the <see cref="FooterContent"/> property.</summary>
    public static readonly StyledProperty<object?> FooterContentProperty =
        AvaloniaProperty.Register<WorkspaceShell, object?>(nameof(FooterContent));

    /// <summary>Defines the <see cref="LeftColumnWidth"/> property.</summary>
    public static readonly StyledProperty<GridLength> LeftColumnWidthProperty =
        AvaloniaProperty.Register<WorkspaceShell, GridLength>(nameof(LeftColumnWidth), new GridLength(2.2, GridUnitType.Star));

    /// <summary>Defines the <see cref="RightColumnWidth"/> property.</summary>
    public static readonly StyledProperty<GridLength> RightColumnWidthProperty =
        AvaloniaProperty.Register<WorkspaceShell, GridLength>(nameof(RightColumnWidth), new GridLength(1, GridUnitType.Star));

    /// <summary>Gets or sets the main title. Defaults to an empty string.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the wrapping subtitle. Defaults to an empty string.</summary>
    public string Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>Gets or sets optional content at the top right of the header. Defaults to null.</summary>
    public object? HeaderRight
    {
        get => GetValue(HeaderRightProperty);
        set => SetValue(HeaderRightProperty, value);
    }

    /// <summary>Gets or sets the summary region below the header. Defaults to null.</summary>
    public object? SummaryContent
    {
        get => GetValue(SummaryContentProperty);
        set => SetValue(SummaryContentProperty, value);
    }

    /// <summary>Gets or sets the toolbar region below the summary. Defaults to null.</summary>
    public object? ToolbarContent
    {
        get => GetValue(ToolbarContentProperty);
        set => SetValue(ToolbarContentProperty, value);
    }

    /// <summary>Gets or sets the left main column content. Defaults to null.</summary>
    public object? LeftContent
    {
        get => GetValue(LeftContentProperty);
        set => SetValue(LeftContentProperty, value);
    }

    /// <summary>Gets or sets the right main column content. Defaults to null.</summary>
    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    /// <summary>Gets or sets the footer below the main columns. Defaults to null.</summary>
    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    /// <summary>Gets or sets the left main column width. Defaults to <c>2.2*</c>.</summary>
    public GridLength LeftColumnWidth
    {
        get => GetValue(LeftColumnWidthProperty);
        set => SetValue(LeftColumnWidthProperty, value);
    }

    /// <summary>Gets or sets the right main column width. Defaults to <c>*</c>.</summary>
    public GridLength RightColumnWidth
    {
        get => GetValue(RightColumnWidthProperty);
        set => SetValue(RightColumnWidthProperty, value);
    }
}
