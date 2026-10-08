// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Avalonia.Theme;

/// <summary>Chooses shared control and group corners independently of the light or dark theme.</summary>
public enum ThemeShape
{
    /// <summary>Uses full pill corners. This is the default shape.</summary>
    Pill,

    /// <summary>Uses six-pixel corners while preserving round switch tracks.</summary>
    Square,
}
