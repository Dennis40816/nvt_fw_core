// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Nvt.Core.Avalonia.Tests.Toggle;

namespace Nvt.Core.Avalonia.Tests.Theme;

// UI thread only. Restore the shared headless application's resources, styles and variant together.
internal sealed class AccentSelectionScope : IDisposable
{
    private readonly Application _app = Application.Current!;
    private readonly ApplicationState _saved;
    private readonly List<Window> _windows = [];

    private sealed record ApplicationState(IResourceDictionary Resources, IStyle[] Styles, ThemeVariant? Variant);

    internal AccentSelectionScope(string tool, bool dark, bool flat = false)
    {
        _saved = new(_app.Resources, [.. _app.Styles], _app.RequestedThemeVariant);
        try
        {
            _app.Styles.Clear();
            _app.Resources = new ResourceDictionary();
            _app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            if (flat)
                foreach (var entry in Palette(tool, dark)) _app.Resources[entry.Key] = new SolidColorBrush(entry.Value);
            else
            {
                _app.Resources.ThemeDictionaries[ThemeVariant.Light] = Dictionary(tool, false);
                _app.Resources.ThemeDictionaries[ThemeVariant.Dark] = Dictionary(tool, true);
            }

            // Both application override forms precede loading Core. No control tokens are overridden.
            var tokens = new Uri("avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml");
            _app.Resources.MergedDictionaries.Add(new ResourceInclude(tokens)
            {
                Source = tokens,
            });
            var fonts = new Uri("avares://Nvt.Core.Fonts/FontRoles.axaml");
            _app.Resources.MergedDictionaries.Add(new ResourceInclude(fonts)
            {
                Source = fonts,
            });
            _app.Styles.Add(new FluentTheme());
            foreach (string file in new[] { "ButtonStyles", "ToggleStyles", "ChoiceStyles", "ListStyles", "TabStyles", "FormStyles", "ProgressStyles", "DividerStyles", "TextStyles" })
            {
                var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{file}.axaml");
                _app.Styles.Add(new StyleInclude(uri)
                {
                    Source = uri,
                });
            }
        }
        catch
        {
            // A failed resource load must not leave the shared headless application modified.
            Dispose();
            throw;
        }
    }

    internal Window Show(Control content)
    {
        var host = new Window
        {
            Width = 600,
            Height = 900,
            Content = content,
            Tag = true,
        };
        _windows.Add(host);
        // No transition may be running while the assertions read the end colors.
        host.Classes.Add("reducedMotion");
        ToggleTestHost.Show(host);
        return host;
    }

    internal void SetVariant(bool dark) => _app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

    internal static Dictionary<string, Color> Palette(string tool, bool dark)
    {
        string[] colors = (tool, dark) switch
        {
            ("NFC", false) => ["#1557E9", "#1148BE", "#EFF3FD", "#F7F9FE"],
            ("NFC", true) => ["#5FA5FA", "#8FBFFB", "#1A2940", "#162034"],
            ("NFH", false) => ["#2967A9", "#225489", "#F0F4F9", "#F8FAFC"],
            ("NFH", true) => ["#53A6FF", "#86C0FF", "#192941", "#152134"],
            ("NFU", false) => ["#4A6F00", "#3D5B00", "#F2F5ED", "#F9FAF6"],
            ("NFU", true) => ["#82B11A", "#97CD1E", "#1F2A25", "#182126"],
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
        return new(StringComparer.Ordinal)
        {
            ["NfcAccentBrush"] = Color.Parse(colors[0]),
            ["NfcAccentStrongBrush"] = Color.Parse(colors[1]),
            ["NfcAccentSurfaceBrush"] = Color.Parse(colors[2]),
            ["NfcAccentSurfaceSubtleBrush"] = Color.Parse(colors[3]),
            ["NfcAccentBorderBrush"] = Color.Parse(colors[0]),
            ["NfcAccentBorderStrongBrush"] = Color.Parse(colors[1]),
            ["NfcAccentBorderLightBrush"] = Color.Parse(colors[0]),
        };
    }

    private static ResourceDictionary Dictionary(string tool, bool dark)
    {
        var result = new ResourceDictionary();
        foreach (var entry in Palette(tool, dark)) result[entry.Key] = new SolidColorBrush(entry.Value);
        return result;
    }

    public void Dispose()
    {
        foreach (Window window in _windows) window.Close();
        _app.Styles.Clear();
        _app.Resources = _saved.Resources;
        _app.RequestedThemeVariant = _saved.Variant;
        foreach (IStyle style in _saved.Styles) _app.Styles.Add(style);
    }
}
