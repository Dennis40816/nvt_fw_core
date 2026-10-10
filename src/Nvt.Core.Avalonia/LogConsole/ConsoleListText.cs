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

    internal static string Format(ConsoleListView view, string key, params object[] arguments)
    {
        var fallback = _defaults.TryGetValue(key, out var value) && value is string builtIn
            ? builtIn : key[(key.LastIndexOf('.') + 1)..];
        var text = UiResourceResolver.GetString(view, key);
        if (string.IsNullOrWhiteSpace(text)) text = fallback;
        try { return string.Format(view.TimeOptions.Culture, text, arguments); }
        catch (FormatException) { return string.Format(view.TimeOptions.Culture, fallback, arguments); }
    }
}
