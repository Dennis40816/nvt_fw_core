// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Panels;

internal static class PanelsTestHost
{
    internal static readonly Uri StylesUri = new("avares://Nvt.Core.Avalonia/Panels/PanelsStyles.axaml");

    internal static Window Create(Control control, Action<Window>? configure = null)
    {
        var host = new Window { Width = 960, Height = 640, Content = control };
        var buttons = new Uri("avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml");
        host.Styles.Add(new StyleInclude(buttons) { Source = buttons });
        host.Classes.Add("reducedMotion");
        host.Styles.Add(new StyleInclude(StylesUri) { Source = StylesUri });
        configure?.Invoke(host);
        host.Show();
        host.UpdateLayout();
        return host;
    }

    internal static T Find<T>(Control control, string className) where T : Control =>
        Assert.Single(control.GetVisualDescendants().OfType<T>(), candidate => candidate.Classes.Contains(className));
}
