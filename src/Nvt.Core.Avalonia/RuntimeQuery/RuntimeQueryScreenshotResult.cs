// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>A completed PNG capture or a tool-owned failure.</summary>
public sealed record RuntimeQueryScreenshotResult
{
    private RuntimeQueryScreenshotResult(int pixelWidth, int pixelHeight, long fileSize, RuntimeQueryError? error)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        FileSize = fileSize;
        Error = error;
    }

    /// <summary>The captured width in pixels, or zero on failure.</summary>
    public int PixelWidth { get; }
    /// <summary>The captured height in pixels, or zero on failure.</summary>
    public int PixelHeight { get; }
    /// <summary>The PNG file size in bytes, or zero on failure.</summary>
    public long FileSize { get; }
    /// <summary>The unchanged tool error, or null on success.</summary>
    public RuntimeQueryError? Error { get; }

    /// <summary>Creates a successful capture result.</summary>
    public static RuntimeQueryScreenshotResult Success(int pixelWidth, int pixelHeight, long fileSize) =>
        new(pixelWidth, pixelHeight, fileSize, null);

    /// <summary>Creates a failed capture result with the tool's code and message.</summary>
    public static RuntimeQueryScreenshotResult Failure(string code, string message) =>
        new(0, 0, 0, new RuntimeQueryError(code, message));
}
