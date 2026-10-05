// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;

namespace Nvt.Core.Avalonia.Panels;

/// <summary>Displays a flat toggle header and an optionally collapsible content area.</summary>
public class CollapsiblePanel : ContentControl
{
    /// <summary>Defines the <see cref="Title"/> property.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<CollapsiblePanel, string>(nameof(Title), string.Empty);

    /// <summary>Defines the <see cref="IsExpanded"/> property.</summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<CollapsiblePanel, bool>(nameof(IsExpanded), true);

    /// <summary>Defines the <see cref="DefaultExpanded"/> property.</summary>
    public static readonly StyledProperty<bool> DefaultExpandedProperty =
        AvaloniaProperty.Register<CollapsiblePanel, bool>(nameof(DefaultExpanded), true);

    /// <summary>Defines the <see cref="IsCollapsible"/> property.</summary>
    public static readonly StyledProperty<bool> IsCollapsibleProperty =
        AvaloniaProperty.Register<CollapsiblePanel, bool>(nameof(IsCollapsible), true);

    /// <summary>Defines the <see cref="HeaderRight"/> property.</summary>
    public static readonly StyledProperty<object?> HeaderRightProperty =
        AvaloniaProperty.Register<CollapsiblePanel, object?>(nameof(HeaderRight));

    /// <summary>Gets or sets the title displayed in the header. Defaults to an empty string.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets whether the body is visible. Defaults to <see langword="true"/>.</summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets the initial expanded state used when <see cref="IsExpanded"/> is unset.
    /// Defaults to <see langword="true"/> and is applied only at initialization.
    /// </summary>
    public bool DefaultExpanded
    {
        get => GetValue(DefaultExpandedProperty);
        set => SetValue(DefaultExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the header can collapse the panel. Defaults to <see langword="true"/>.
    /// Setting this to <see langword="false"/> forces and keeps the panel expanded.
    /// </summary>
    public bool IsCollapsible
    {
        get => GetValue(IsCollapsibleProperty);
        set => SetValue(IsCollapsibleProperty, value);
    }

    /// <summary>Gets or sets optional content on the right of the header. Defaults to null.</summary>
    public object? HeaderRight
    {
        get => GetValue(HeaderRightProperty);
        set => SetValue(HeaderRightProperty, value);
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (!IsSet(IsExpandedProperty))
        {
            IsExpanded = DefaultExpanded;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCollapsibleProperty && change.NewValue is bool isCollapsible && !isCollapsible)
        {
            IsExpanded = true;
        }

        if (change.Property == IsExpandedProperty && !IsCollapsible && change.NewValue is bool isExpanded && !isExpanded)
        {
            IsExpanded = true;
        }
    }
}
