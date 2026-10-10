// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Avalonia.Theme;

/// <summary>Chooses the resting row fill for choices and Expander headers independently of theme and shape.</summary>
public enum ThemeRestFill
{
    /// <summary>Uses the existing soft row and header fills. This is the default.</summary>
    Soft,

    /// <summary>Uses transparent rest and disabled fills while preserving hover, pressed, indicator, and focus feedback.</summary>
    None,
}
