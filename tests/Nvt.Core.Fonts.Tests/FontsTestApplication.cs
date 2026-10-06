// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Nvt.Core.Avalonia.Testing;

[assembly: AvaloniaTestApplication(typeof(Nvt.Core.Fonts.Tests.FontsTestApplication))]

namespace Nvt.Core.Fonts.Tests;

/// <summary>Loads the font roles with the shared Skia test host.</summary>
public sealed class FontsTestApplication : Application
{
    /// <summary>Builds the font test application.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AvaloniaTestHost.Build<FontsTestApplication>().WithNvtCoreFonts();

    /// <inheritdoc />
    public override void Initialize()
    {
        var uri = new Uri("avares://Nvt.Core.Fonts/FontRoles.axaml");
        Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });
    }
}
