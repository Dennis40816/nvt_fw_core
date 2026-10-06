// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Nvt.Core.Avalonia.Testing;

[assembly: AvaloniaTestApplication(typeof(Nvt.Core.Avalonia.Tests.Theme.ThemeTestApplication))]

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Initializes only the extracted theme for headless contract tests.</summary>
public sealed class ThemeTestApplication : Application
{
    /// <summary>Builds the isolated headless application.</summary>
    public static AppBuilder BuildAvaloniaApp() => AvaloniaTestHost.Build<ThemeTestApplication>();

    /// <inheritdoc />
    public override void Initialize()
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml");
        Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });
    }
}
