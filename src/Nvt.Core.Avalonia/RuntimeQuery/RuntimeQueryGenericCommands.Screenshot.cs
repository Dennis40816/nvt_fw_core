// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Avalonia.RuntimeQuery;

public static partial class RuntimeQueryGenericCommands
{
    private static async Task<RuntimeQueryResponseEnvelope> ScreenshotAsync(
        RuntimeQueryGenericCommandOptions options, IReadOnlyDictionary<string, string>? args)
    {
        if (args is null || !args.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return InvalidScreenshotPath();
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return InvalidScreenshotPath();
        }

        if (File.Exists(path))
        {
            return ScreenshotFileExists();
        }

        RuntimeQueryScreenshotResult result;
        if (options.CaptureScreenshot is { } capture)
        {
            result = await capture(path, CancellationToken.None);
        }
        else
        {
            var window = options.GetMainWindow();
            if (window is null)
            {
                return NoMainWindow();
            }

            result = CaptureWindow(window, path);
        }

        return result.Error is { } error
            ? new RuntimeQueryResponseEnvelope(false, null, error)
            : RuntimeQueryResponseEnvelope.Success(new
            {
                path, pixelWidth = result.PixelWidth, pixelHeight = result.PixelHeight, fileSize = result.FileSize
            });
    }

    private static RuntimeQueryScreenshotResult CaptureWindow(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = new Vector(96 * window.RenderScaling, 96 * window.RenderScaling);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSizeWithDpi(window.Bounds.Size, dpi), dpi);
        bitmap.Render(window);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".runtime-query-{Guid.NewGuid():N}.tmp");
        var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (stream)
            {
                bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            }

            var fileSize = new FileInfo(temporaryPath).Length;
            try
            {
                File.Move(temporaryPath, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                return RuntimeQueryScreenshotResult.Failure(
                    RuntimeQueryGenericFailureCodes.FileExists, "The screenshot file already exists.");
            }

            return RuntimeQueryScreenshotResult.Success(bitmap.PixelSize.Width, bitmap.PixelSize.Height, fileSize);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static RuntimeQueryResponseEnvelope InvalidScreenshotPath() => RuntimeQueryResponseEnvelope.Failure(
        RuntimeQueryGenericFailureCodes.InvalidArguments, "Argument '--path' must be an absolute path ending in '.png'.");

    private static RuntimeQueryResponseEnvelope ScreenshotFileExists() => RuntimeQueryResponseEnvelope.Failure(
        RuntimeQueryGenericFailureCodes.FileExists, "The screenshot file already exists.");
}
