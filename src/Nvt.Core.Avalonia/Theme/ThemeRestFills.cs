// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;

namespace Nvt.Core.Avalonia.Theme;

/// <summary>Changes shared rest-fill resources at an application, window, or subtree resource root.</summary>
public static class ThemeRestFills
{
    private const string Prefix = "avares://Nvt.Core.Avalonia/Theme/RestFill";

    /// <summary>Replaces the root's rest-fill dictionary and updates attached controls through dynamic resources.</summary>
    /// <param name="resources">The application, window, or subtree resources to update on the UI thread.</param>
    /// <param name="fill">The rest fill to apply independently of the requested theme variant and shape.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fill"/> is undefined.</exception>
    /// <exception cref="InvalidOperationException">The caller is outside the UI thread.</exception>
    public static void SetRestFill(IResourceDictionary resources, ThemeRestFill fill)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (!Enum.IsDefined(fill)) throw new ArgumentOutOfRangeException(nameof(fill));
        Dispatcher.UIThread.VerifyAccess();

        var uri = new Uri($"{Prefix}{fill}.axaml");
        var next = new ResourceInclude(uri) { Source = uri };
        var dictionaries = resources.MergedDictionaries;
        var previous = dictionaries.OfType<ResourceInclude>().FirstOrDefault(include =>
            include.Source?.OriginalString is Prefix + "Soft.axaml" or Prefix + "None.axaml");
        if (previous is null) dictionaries.Add(next);
        else dictionaries[dictionaries.IndexOf(previous)] = next;
    }
}
