// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nvt.Core.Avalonia.RuntimeQuery;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

public sealed partial class RuntimeQueryGenericCommandsTests
{
    /// <summary>Invalid paths fail before the capture delegate or main-window lookup runs.</summary>
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("capture.png")]
    [InlineData("wrong-extension")]
    [InlineData("root-relative")]
    public async Task ScreenshotRejectsInvalidPathBeforeCapture(string? value)
    {
        if (value == "root-relative" && !OperatingSystem.IsWindows())
        {
            Assert.Skip("Root-relative paths are invalid only on Windows.");
        }

        using var workspace = new ScreenshotWorkspace();
        var path = value switch
        {
            "wrong-extension" => workspace.PathFor("capture.jpg"),
            "root-relative" => Path.DirectorySeparatorChar + "capture.png",
            _ => value
        };
        var options = Options() with
        {
            GetMainWindow = () => throw new InvalidOperationException("Path checks must precede window lookup."),
            CaptureScreenshot = (_, _) => throw new InvalidOperationException("Path checks must precede capture.")
        };
        AssertFailure(await Router(options).RouteAsync("screenshot", path is null ? null : Arg("path", path)),
            "INVALID_ARGUMENTS", "Argument '--path' must be an absolute path ending in '.png'.");
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
    }

    /// <summary>An existing destination remains unchanged and prevents either capture path.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScreenshotDoesNotReplaceExistingFile(bool customCapture)
    {
        using var workspace = new ScreenshotWorkspace();
        var path = workspace.PathFor("capture.png");
        byte[] original = [1, 2, 3, 4];
        File.WriteAllBytes(path, original);
        var options = Options() with
        {
            GetMainWindow = () => throw new InvalidOperationException("Existing files must precede window lookup."),
            CaptureScreenshot = customCapture
                ? (_, _) => throw new InvalidOperationException("Existing files must precede capture.")
                : null
        };
        AssertFailure(await Router(options).RouteAsync("screenshot", Arg("path", path)),
            "FILE_EXISTS", "The screenshot file already exists.");
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(workspace.DirectoryPath));
    }

    /// <summary>Default capture lays out fixed content and saves a PNG at the current size and scaling.</summary>
    [AvaloniaTheory]
    [InlineData(1.0, 160, 96)]
    [InlineData(1.5, 240, 144)]
    public async Task ScreenshotWritesLaidOutPngWithoutTemporaryFiles(double scaling, int width, int height)
    {
        using var workspace = new ScreenshotWorkspace();
        var path = workspace.PathFor("capture.PNG");
        var window = CaptureWindow();
        try
        {
            window.Show();
            window.SetRenderScaling(scaling);
            var content = new Border { Background = Brushes.Red };
            window.Content = content;
            Assert.False(content.IsArrangeValid);
            var response = await Router(Options(window)).RouteAsync("screenshot", Arg("path", path));
            Assert.True(content.IsArrangeValid);
            Assert.Equal(new Size(160, 96), content.Bounds.Size);
            using var image = new Bitmap(path);
            Assert.Equal(new PixelSize(width, height), image.PixelSize);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8));
            AssertSuccess(response, JsonSerializer.Serialize(new
            {
                path, pixelWidth = width, pixelHeight = height, fileSize = (long)bytes.Length
            }, RuntimeQueryProtocol.CompactJsonOptions));
            Assert.Equal(new[] { path }, Directory.GetFiles(workspace.DirectoryPath));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A failed final move deletes the written temporary PNG.</summary>
    [AvaloniaFact]
    public async Task ScreenshotMoveFailureLeavesNoTemporaryFile()
    {
        using var workspace = new ScreenshotWorkspace();
        var path = workspace.PathFor("destination.png");
        Directory.CreateDirectory(path);
        var window = CaptureWindow();
        try
        {
            window.Show();
            await Assert.ThrowsAnyAsync<IOException>(() => Router(Options(window)).RouteAsync("screenshot", Arg("path", path)));
            Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
            Assert.True(Directory.Exists(path));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A destination created during render survives the default capture's no-replace move.</summary>
    [AvaloniaFact]
    public async Task ScreenshotHandlesFileAppearingAfterInitialCheck()
    {
        using var workspace = new ScreenshotWorkspace();
        var path = workspace.PathFor("capture.png");
        var window = CaptureWindow();
        try
        {
            window.Show();
            window.Content = new RacingCaptureContent(path);
            AssertFailure(await Router(Options(window)).RouteAsync("screenshot", Arg("path", path)),
                "FILE_EXISTS", "The screenshot file already exists.");
            Assert.Equal("racing file", File.ReadAllText(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(workspace.DirectoryPath));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Default capture reports the exact missing-window failure after valid path checks.</summary>
    [AvaloniaFact]
    public async Task ScreenshotWithoutMainWindowReturnsFailure()
    {
        using var workspace = new ScreenshotWorkspace();
        AssertFailure(await Router(Options()).RouteAsync("screenshot", Arg("path", workspace.PathFor("capture.png"))),
            "NO_MAIN_WINDOW", "The main window is not available.");
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
    }

    /// <summary>A replacement gets the absolute path and None token before any Core layout or window lookup.</summary>
    [AvaloniaFact]
    public async Task ScreenshotDelegateOwnsLayoutAndReturnsExactData()
    {
        using var workspace = new ScreenshotWorkspace();
        var path = workspace.PathFor("capture.PNG");
        var suppliedPath = Path.Combine(workspace.DirectoryPath, ".", "capture.PNG");
        var window = CaptureWindow();
        try
        {
            window.Show();
            var content = new Border { Background = Brushes.Blue };
            window.Content = content;
            var calls = 0;
            var options = Options() with
            {
                GetMainWindow = () => throw new InvalidOperationException("Custom capture owns window lookup."),
                CaptureScreenshot = async (receivedPath, token) =>
                {
                    calls++;
                    Assert.Equal(path, receivedPath);
                    Assert.Equal(CancellationToken.None, token);
                    Assert.False(content.IsArrangeValid);
                    await Task.Yield();
                    return RuntimeQueryScreenshotResult.Success(17, 19, 23);
                }
            };
            var response = await Router(options).RouteAsync("screenshot", Arg("path", suppliedPath));
            AssertSuccess(response, JsonSerializer.Serialize(new
            {
                path, pixelWidth = 17, pixelHeight = 19, fileSize = 23L
            }, RuntimeQueryProtocol.CompactJsonOptions));
            Assert.Equal(1, calls);
            Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Tool capture failures keep their code, message and error instance unchanged.</summary>
    [AvaloniaTheory]
    [InlineData("PROTECTED_PATH", "  This destination is protected.\nChoose another folder.  ")]
    [InlineData("FILE_EXISTS", "The destination appeared during capture.")]
    public async Task ScreenshotReturnsToolFailureUnchanged(string code, string message)
    {
        using var workspace = new ScreenshotWorkspace();
        var result = RuntimeQueryScreenshotResult.Failure(code, message);
        var options = Options() with { CaptureScreenshot = (_, _) => Task.FromResult(result) };
        var response = await Router(options).RouteAsync("screenshot", Arg("path", workspace.PathFor("capture.png")));
        AssertFailure(response, code, message);
        Assert.Same(result.Error, response.Error);
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
    }

    /// <summary>Synchronous and asynchronous delegate exceptions escape unchanged.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScreenshotDelegateExceptionsEscape(bool asynchronous)
    {
        using var workspace = new ScreenshotWorkspace();
        var expected = new InvalidOperationException("Synthetic capture failure.");
        var options = Options() with
        {
            CaptureScreenshot = (_, _) => asynchronous ? Task.FromException<RuntimeQueryScreenshotResult>(expected) : throw expected
        };
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Router(options).RouteAsync("screenshot", Arg("path", workspace.PathFor("capture.png"))));
        Assert.Same(expected, actual);
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
    }

    private static Window CaptureWindow() => new()
    {
        Width = 160, Height = 96, Background = Brushes.White,
        Content = new Border { Background = Brushes.Red }
    };

    private sealed class RacingCaptureContent(string path) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            File.WriteAllText(path, "racing file");
        }
    }

    private sealed class ScreenshotWorkspace : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"nvt-core-query-test-{Guid.NewGuid():N}");
        public ScreenshotWorkspace() => Directory.CreateDirectory(DirectoryPath);
        public string PathFor(string name) => Path.Combine(DirectoryPath, name);
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
