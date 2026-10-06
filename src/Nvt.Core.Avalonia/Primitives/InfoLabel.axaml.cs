// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Nvt.Core.Avalonia.Primitives;

/// <summary>Displays selectable wrapped text with a tooltip and optional ancestor tooltip propagation.</summary>
public sealed partial class InfoLabel : UserControl
{
    /// <summary>Defines the label text, defaulting to an empty string.</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<InfoLabel, string>(nameof(Text), string.Empty);

    /// <summary>Defines the tooltip text, defaulting to an empty string.</summary>
    public static readonly StyledProperty<string> TipProperty =
        AvaloniaProperty.Register<InfoLabel, string>(nameof(Tip), string.Empty);

    /// <summary>Defines the optional ancestor class that receives the tooltip, defaulting to null.</summary>
    public static readonly StyledProperty<string?> TipTargetClassProperty =
        AvaloniaProperty.Register<InfoLabel, string?>(nameof(TipTargetClass));

    /// <summary>Initializes the selectable label and applies its tooltip on visual-tree attachment.</summary>
    public InfoLabel()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ApplyTipToAncestor();
    }

    /// <summary>Gets or sets the label text.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the tooltip text. Empty or whitespace tips leave applied tooltips unchanged.</summary>
    public string Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    /// <summary>Gets or sets the ancestor class to search. Null or empty limits the tooltip to this label.</summary>
    public string? TipTargetClass
    {
        get => GetValue(TipTargetClassProperty);
        set => SetValue(TipTargetClassProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TipProperty)
        {
            ApplyTipToAncestor();
        }
    }

    private void ApplyTipToAncestor()
    {
        var tip = Tip;
        if (string.IsNullOrWhiteSpace(tip))
        {
            return;
        }

        ToolTip.SetTip(this, tip);

        var targetClass = TipTargetClass;
        if (string.IsNullOrEmpty(targetClass))
        {
            return;
        }

        var parent = this.GetVisualParent();
        while (parent is not null)
        {
            if (parent is Control control && control.Classes.Contains(targetClass))
            {
                ToolTip.SetTip(control, tip);
                return;
            }

            parent = parent.GetVisualParent();
        }
    }
}
