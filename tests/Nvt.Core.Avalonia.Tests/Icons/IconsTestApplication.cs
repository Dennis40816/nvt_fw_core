// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Nvt.Core.Avalonia.Testing;
using Nvt.Core.Fonts;

namespace Nvt.Core.Avalonia.Tests.Icons;

/// <summary>Loads icon resources and font roles with the shared Skia test host.</summary>
public sealed class IconsTestApplication : Application
{
    /// <summary>Builds an isolated application for icon tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AvaloniaTestHost.Build<IconsTestApplication>().WithNvtCoreFonts();

    /// <inheritdoc />
    public override void Initialize()
    {
        foreach (string path in new[]
        {
            "avares://Nvt.Core.Fonts/FontRoles.axaml",
            "avares://Nvt.Core.Avalonia/Icons/IconResources.axaml",
        })
        {
            var uri = new Uri(path);
            Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });
        }

        Styles.Add(new FluentTheme());
    }
}
