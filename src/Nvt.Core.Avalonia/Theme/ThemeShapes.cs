// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;

namespace Nvt.Core.Avalonia.Theme;

/// <summary>Changes shared shape resources at an application, window, or subtree resource root.</summary>
public static class ThemeShapes
{
    private const string Prefix = "avares://Nvt.Core.Avalonia/Theme/Shape";

    /// <summary>Replaces the root's shape dictionary and updates attached controls through dynamic resources.</summary>
    /// <param name="resources">The application, window, or subtree resources to update on the UI thread.</param>
    /// <param name="shape">The shared shape to apply independently of the requested theme variant.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shape"/> is undefined.</exception>
    /// <exception cref="InvalidOperationException">The caller is outside the UI thread.</exception>
    public static void SetShape(IResourceDictionary resources, ThemeShape shape)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (!Enum.IsDefined(shape)) throw new ArgumentOutOfRangeException(nameof(shape));
        Dispatcher.UIThread.VerifyAccess();

        var uri = new Uri($"{Prefix}{shape}.axaml");
        var next = new ResourceInclude(uri) { Source = uri };
        var dictionaries = resources.MergedDictionaries;
        var previous = dictionaries.OfType<ResourceInclude>().FirstOrDefault(include =>
            include.Source?.OriginalString is Prefix + "Pill.axaml" or Prefix + "Square.axaml");
        if (previous is null) dictionaries.Add(next);
        else dictionaries[dictionaries.IndexOf(previous)] = next;
    }
}
