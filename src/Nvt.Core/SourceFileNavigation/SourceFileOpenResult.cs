// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.SourceFileNavigation;

/// <summary>
/// Describes the outcome of an attempt to open a source file.
/// </summary>
/// <param name="Opened">Whether the open attempt completed successfully.</param>
/// <param name="ExactLine">Whether the application was directed to the requested source line.</param>
/// <param name="Application">The application used for the open attempt.</param>
/// <param name="Error">The failure message, or <see langword="null"/> when no error was reported.</param>
public sealed record SourceFileOpenResult(
    bool Opened,
    bool ExactLine,
    string Application,
    string? Error = null);
