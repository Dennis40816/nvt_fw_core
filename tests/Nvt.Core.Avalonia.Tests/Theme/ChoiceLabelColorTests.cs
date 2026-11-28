// Copyright (c) 2026 Dennis Liu. All rights reserved.

using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media;
using global::Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Checks choice label colors when FluentTheme is loaded under the Core styles.</summary>
public sealed class ChoiceLabelColorTests
{
    /// <summary>The label of every choice state uses the Core token, not the host theme's state color.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LabelKeepsCoreColorUnderFluentTheme(bool dark)
    {
        using var scope = new AccentSelectionScope("NFH", dark);
        var panel = new StackPanel();
        foreach (bool enabled in new[] { true, false })
        {
            panel.Children.Add(new CheckBox { Content = "Off", IsEnabled = enabled });
            panel.Children.Add(new CheckBox { Content = "On", IsChecked = true, IsEnabled = enabled });
            panel.Children.Add(new CheckBox { Content = "Mixed", IsThreeState = true, IsChecked = null, IsEnabled = enabled });
            panel.Children.Add(new RadioButton { Content = "Off", IsEnabled = enabled });
            panel.Children.Add(new RadioButton { Content = "On", IsChecked = true, GroupName = enabled ? "a" : "b", IsEnabled = enabled });
        }

        Window host = scope.Show(panel);
        foreach (ToggleButton choice in panel.Children.OfType<ToggleButton>())
        {
            Assert.True(choice.TryFindResource(
                choice.IsEnabled ? "Nvt.Controls.ChoiceForegroundBrush" : "NfcTextDisabledBrush",
                host.ActualThemeVariant,
                out object? token));
            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(token).Color;
            foreach (string state in new[] { "", ":pointerover", ":pressed" })
            {
                if (state.Length > 0) ((global::Avalonia.Controls.IPseudoClasses)choice.Classes).Set(state, true);
                ListMenuTestHost.Flush(host);
                TextBlock label = Assert.Single(choice.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
                if (state.Length > 0) ((global::Avalonia.Controls.IPseudoClasses)choice.Classes).Set(state, false);
            }
        }
    }

    /// <summary>A color that the application sets on the control still reaches the label.</summary>
    [AvaloniaFact]
    public void ApplicationForegroundReachesLabel()
    {
        using var scope = new AccentSelectionScope("NFH", dark: false);
        var choice = new CheckBox { Content = "Custom", IsChecked = true, Foreground = Brushes.DarkRed };
        scope.Show(choice);
        TextBlock label = Assert.Single(choice.GetVisualDescendants().OfType<TextBlock>());
        Assert.Equal(Colors.DarkRed, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
    }
}
