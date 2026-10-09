// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Focus;

internal static class ToolTipTestHost
{
    internal static Window Create(Control content, bool dark = false, bool styles = true,
        double width = 600, double height = 300)
    {
        var window = new Window
        {
            Content = content, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"), FontSize = 13,
        };
        window.Styles.Add(new FluentTheme());
        if (styles)
        {
            var uri = new Uri("avares://Nvt.Core.Avalonia/Focus/ToolTipStyles.axaml");
            window.Styles.Add(new StyleInclude(uri) { Source = uri });
        }
        return window;
    }

    internal static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    internal static ToolTip Open(Window window, Control target, TextBlock text)
    {
        window.Show();
        Flush(window);
        ToolTip.SetIsOpen(target, true);
        Flush(window);
        Assert.True(ToolTip.GetIsOpen(target));
        return Assert.Single(text.GetVisualAncestors().OfType<ToolTip>());
    }

    internal static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    internal static T Resource<T>(Control owner, string key)
    {
        Assert.True(owner.TryFindResource(key, owner.ActualThemeVariant, out object? resource), key);
        return Assert.IsAssignableFrom<T>(resource);
    }
}
