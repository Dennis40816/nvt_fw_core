// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Headless;

namespace Nvt.Core.Avalonia.Testing;

/// <summary>Bootstraps a headless application with NVT FW Combiner's Inter and Skia rendering chain.</summary>
/// <remarks>Link this source into test projects that reference Avalonia.Headless.XUnit and Avalonia.Skia.</remarks>
public static class AvaloniaTestHost
{
    /// <summary>Builds an empty application for the Avalonia headless test runner.</summary>
    public static AppBuilder BuildAvaloniaApp() => Build<Application>();

    /// <summary>Builds the test assembly's application with the frozen rendering chain.</summary>
    /// <typeparam name="TApplication">The application that loads the test assembly's resources.</typeparam>
    public static AppBuilder Build<TApplication>() where TApplication : Application, new()
    {
        return AppBuilder.Configure<TApplication>()
            .WithInterFont()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
    }
}
