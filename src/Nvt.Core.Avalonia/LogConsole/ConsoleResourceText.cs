// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace Nvt.Core.Avalonia.LogConsole;

// UI-thread-only dictionary access. Local views use these defaults only when host lookup has no value.
internal static class ConsoleResourceText
{
    private static readonly ResourceInclude _defaults = new(new Uri("avares://Nvt.Core.Avalonia/LogConsole/ConsoleResources.axaml"))
    { Source = new Uri("avares://Nvt.Core.Avalonia/LogConsole/ConsoleResources.axaml") };
    internal static object GetValue(string key, StyledElement? host = null)
    {
        var resourceKey = "Nvt.Console." + key;
        if (host?.TryFindResource(resourceKey, out var value) == true && value is not null) return value;
        if (Application.Current?.TryFindResource(resourceKey, out value) == true && value is not null) return value;
        return GetDefaultValue(key);
    }
    internal static string Get(string key, StyledElement? host = null) => (string)GetValue(key, host);
    internal static string GetDefault(string key) => (string)GetDefaultValue(key);
    private static object GetDefaultValue(string key)
    {
        var resourceKey = "Nvt.Console." + key;
        if (_defaults.TryGetResource(resourceKey, ThemeVariant.Default, out var value) && value is not null) return value;
        throw new KeyNotFoundException(resourceKey);
    }
}
