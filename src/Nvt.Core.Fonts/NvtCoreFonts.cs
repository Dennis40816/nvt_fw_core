// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Media;

namespace Nvt.Core.Fonts;

/// <summary>Provides the embedded Chinese fallback for the font roles.</summary>
public static class NvtCoreFonts
{
    /// <summary>Gets the embedded Noto Sans TC fallback.</summary>
    public static FontFallback CjkFallback { get; } = new()
    {
        FontFamily = new FontFamily("avares://Nvt.Core.Fonts/Assets/NotoSansTC#Noto Sans TC"),
    };

    /// <summary>Uses Noto Sans TC before system fonts when a role family lacks a character.</summary>
    /// <remarks>
    /// This method replaces <see cref="FontManagerOptions"/>.
    /// A tool with its own options adds <see cref="CjkFallback"/> to its own fallback list instead.
    /// </remarks>
    /// <param name="builder">The application builder.</param>
    /// <returns>The application builder with the font fallback options.</returns>
    public static AppBuilder WithNvtCoreFonts(this AppBuilder builder) =>
        builder.With(new FontManagerOptions
        {
            FontFallbacks = [CjkFallback],
        });
}
