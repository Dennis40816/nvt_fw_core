// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.Icons;

// Console-only glyph and optical corrections. Level icons come from the shared NvtIcons catalog.
internal static class ConsoleGlyphs
{
    public const string Terminal = "\uEB8E";

    // Shared optical correction from the approved 16 DIP glyph/scale table.
    // Pixel tests extend its 100/150 percent entries to 125/200 percent.
    internal static double VerticalOffset(LogLevel level, double scale) => level switch
    {
        LogLevel.Debug => Math.Abs(scale - 1.25) < 0.01 ? 1.5 : 1,
        LogLevel.Warn => scale >= 1.5 ? 0.5 : 1,
        _ => 0,
    };
}
