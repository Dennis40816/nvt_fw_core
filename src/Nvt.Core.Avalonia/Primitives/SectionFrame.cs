// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;

namespace Nvt.Core.Avalonia.Primitives;

/// <summary>Presents content beneath a selectable title flanked by divider lines.</summary>
public class SectionFrame : ContentControl
{
    /// <summary>Defines the section title, defaulting to an empty string.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SectionFrame, string>(nameof(Title), string.Empty);

    /// <summary>Gets or sets the section title.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
