// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Tests.Forms;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.TextStyles;

/// <summary>Exports one English font-role sheet per theme.</summary>
public sealed class TextStylesRenderer(ITestOutputHelper output)
{
    /// <summary>Renders the ten opt-in role classes and the separate muted foreground class.</summary>
    [AvaloniaFact]
    public void RenderTextStyles() => FormSheets.Render("textstyles", "Inter and Cascadia Mono / existing font tokens / natural line height",
        FormSheets.TextSection, output, textOnly: true);
}
