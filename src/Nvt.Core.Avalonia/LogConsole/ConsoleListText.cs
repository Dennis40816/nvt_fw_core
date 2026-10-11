// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nvt.Core.Avalonia.Theme;

namespace Nvt.Core.Avalonia.LogConsole;

// UI thread only. The independent built-in dictionary is the sole source of English defaults.
internal static class ConsoleListText
{
    private static readonly ResourceDictionary _defaults = (ResourceDictionary)AvaloniaXamlLoader.Load(
        new Uri("avares://Nvt.Core.Avalonia/LogConsole/ConsoleListGeometry.axaml"));

    internal static string Default(string key) => _defaults.TryGetValue(key, out var value) && value is string builtIn
        ? builtIn : key[(key.LastIndexOf('.') + 1)..];
    internal static string Template(ConsoleListView view, string key)
    {
        var text = UiResourceResolver.GetString(view, key);
        return string.IsNullOrWhiteSpace(text) ? Default(key) : text;
    }

    internal static string Format(ConsoleListView view, string key, params object[] arguments)
    {
        var fallback = Default(key);
        var text = Template(view, key);
        try { return string.Format(view.TimeOptions.Culture, text, arguments); }
        catch (FormatException) { return string.Format(view.TimeOptions.Culture, fallback, arguments); }
    }
}
