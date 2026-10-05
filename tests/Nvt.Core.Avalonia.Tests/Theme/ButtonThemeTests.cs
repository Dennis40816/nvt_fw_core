// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Characterizes compiled resources and representative button states in both NFC themes.</summary>
public sealed class ButtonThemeTests
{
    /// <summary>Checks compiled brushes, sizes, fonts and glyphs against every frozen token value.</summary>
    [AvaloniaFact]
    public void CompiledTokensMatchEveryFrozenValueInBothThemes()
    {
        XElement root = ThemeContractTests.ReadBaseline("ThemeTokens").Root!;
        XElement[] common = [.. root.Elements().Where(element => element.Attribute(ThemeContractTests.Xaml + "Key") != null)];
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            XElement theme = Assert.Single(root.Descendants(ThemeContractTests.Presentation + "ResourceDictionary"),
                element => element.Attribute(ThemeContractTests.Xaml + "Key")?.Value == variant.Key.ToString());
            foreach (XElement token in common.Concat(theme.Elements()))
            {
                string key = token.Attribute(ThemeContractTests.Xaml + "Key")!.Value;
                Assert.True(Application.Current!.TryGetResource(key, variant, out object? actual), key);
                string value = token.Attribute("Color")?.Value ?? token.Value;
                switch (token.Name.LocalName)
                {
                    case "SolidColorBrush":
                        Assert.Equal(Color.Parse(value), Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
                        break;
                    case "Double":
                        Assert.Equal(double.Parse(value, CultureInfo.InvariantCulture), Assert.IsType<double>(actual));
                        break;
                    case "CornerRadius":
                        Assert.Equal(CornerRadius.Parse(value), Assert.IsType<CornerRadius>(actual));
                        break;
                    case "FontFamily":
                        Assert.Equal(new FontFamily(value), Assert.IsType<FontFamily>(actual));
                        break;
                    case "StreamGeometry":
                        Assert.Equal(StreamGeometry.Parse(value).Bounds, Assert.IsType<StreamGeometry>(actual).Bounds);
                        break;
                    default:
                        Assert.Fail($"Uncharacterized token type: {token.Name.LocalName}");
                        break;
                }
            }
        }
    }

    /// <summary>Ports NFC's ordinary action template, border and pill checks.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryActionUsesOwnedSemanticTemplate(bool dark)
    {
        var button = new StateButton { Content = "Browse" };
        button.Classes.Add("semanticAction");
        button.Classes.Add("secondary");
        Window host = CreateHost(button, dark);
        ContentPresenter presenter = Presenter(button);
        Assert.NotNull(button.Theme);
        Assert.Null(button.FocusAdorner);
        Assert.Equal(34, button.MinHeight);
        Assert.Equal(new Thickness(14, 8), button.Padding);
        Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
        Assert.Equal(new Thickness(2), button.BorderThickness);
        Assert.Equal(new Thickness(2), presenter.BorderThickness);
        Assert.Equal(new CornerRadius(999), presenter.CornerRadius);
        AssertBrush("NfcSurfaceBrush", presenter.Background, dark);
        AssertBrush("NfcBorderBrush", presenter.BorderBrush, dark);
        host.Close();
    }

    /// <summary>Freezes normal, hover, pressed and disabled colors for generic button roles.</summary>
    [AvaloniaTheory]
    [InlineData("primary", "", "NfcAccentSurfaceBrush", "NfcAccentBorderLightBrush", "NfcAccentStrongBrush")]
    [InlineData("primary", ":pointerover", "NfcAccentBrush", "NfcAccentBrush", "NfcSurfaceBrush")]
    [InlineData("primary", ":pressed", "NfcAccentSurfaceBrush", "NfcAccentBorderLightBrush", "NfcAccentStrongBrush")]
    [InlineData("primary", ":disabled", "NfcAccentSurfaceBrush", "NfcAccentBorderLightBrush", "NfcAccentStrongBrush")]
    [InlineData("secondary", "", "NfcSurfaceBrush", "NfcBorderBrush", "NfcTextBrush")]
    [InlineData("secondary", ":pointerover", "NfcAccentSurfaceBrush", "NfcAccentBorderBrush", "NfcAccentStrongBrush")]
    [InlineData("secondary", ":pressed", "NfcSecondaryActionPressedBrush", "NfcAccentBorderStrongBrush", "NfcAccentStrongBrush")]
    [InlineData("secondary", ":disabled", "NfcSurfaceSubtleBrush", "NfcBorderMutedBrush", "NfcTextDisabledBrush")]
    [InlineData("danger", "", "NfcDangerSurfaceBrush", "NfcDangerBorderBrush", "NfcDangerTextBrush")]
    [InlineData("danger", ":pointerover", "NfcDangerSurfaceMutedBrush", "NfcDangerBorderStrongBrush", "NfcDangerTextStrongBrush")]
    [InlineData("danger", ":pressed", "NfcCriticalSurfaceBrush", "NfcCriticalBorderBrush", "NfcDangerTextStrongBrush")]
    [InlineData("danger", ":disabled", "NfcSurfaceSubtleBrush", "NfcBorderMutedBrush", "NfcTextDisabledBrush")]
    [InlineData("action", ":pointerover", "NfcAccentBrush", "NfcAccentBrush", "NfcSurfaceBrush")]
    [InlineData("action", ":pressed", "NfcAccentStrongBrush", "NfcAccentStrongBrush", "NfcSurfaceBrush")]
    [InlineData("action", ":disabled", "NfcSurfaceSubtleBrush", "NfcBorderMutedBrush", "NfcTextDisabledBrush")]
    public void ButtonStatesKeepFrozenPalette(string role, string state, string background, string border, string foreground)
    {
        foreach (bool dark in new[] { false, true })
        {
            var text = new TextBlock { Text = "Action" };
            var button = new StateButton { Content = text };
            button.Classes.Add("semanticAction");
            button.Classes.Add(role);
            Window host = CreateHost(button, dark);
            if (state == ":disabled")
            {
                button.IsEnabled = false;
            }
            else if (state.Length != 0)
            {
                button.SetState(state, true);
            }

            ContentPresenter presenter = Presenter(button);
            AssertBrush(background, presenter.Background, dark);
            AssertBrush(border, presenter.BorderBrush, dark);
            AssertBrush(foreground, TextElement.GetForeground(presenter), dark);
            // NFC's later base presenter setters override primary pressed/disabled setters.
            // Its descendant TextBlock still receives the state-specific foreground.
            string textForeground = role == "primary" && state == ":pressed" ? "NfcSurfaceBrush" :
                role == "primary" && state == ":disabled" ? "NfcTextDisabledBrush" : foreground;
            AssertBrush(textForeground, text.Foreground, dark);
            Assert.Equal(new CornerRadius(999), presenter.CornerRadius);
            Assert.Equal(1, button.Opacity);
            host.Close();
        }
    }

    /// <summary>Checks real keyboard focus separately from pointer focus, as in NFC's focus contract.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyboardFocusShowsTheFrozenBorder(bool dark)
    {
        var button = new StateButton { Content = "Action" };
        button.Classes.Add("semanticAction");
        button.Classes.Add("secondary");
        Window host = CreateHost(button, dark);
        host.Show();
        Assert.True(button.Focus(NavigationMethod.Pointer));
        AssertBrush("NfcBorderBrush", Presenter(button).BorderBrush, dark);
        host.FocusManager!.Focus(null);
        Assert.True(button.Focus(NavigationMethod.Tab));
        AssertBrush("NfcAccentBorderStrongBrush", Presenter(button).BorderBrush, dark);
        Assert.Equal(new Thickness(2), Presenter(button).BorderThickness);
        Assert.Null(button.FocusAdorner);
        host.Close();
    }

    /// <summary>Ports NFC's expandable rail geometry, transition durations and reduced-motion contract.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RailActionPreservesExpansionAndReducedMotion(bool dark)
    {
        var label = new TextBlock { Text = "Action" };
        label.Classes.Add("railActionLabel");
        var button = new StateButton { Content = label };
        button.Classes.Add("railAction");
        Window host = CreateHost(button, dark);
        ContentPresenter presenter = Presenter(button);
        Assert.Equal(44, button.Width);
        Assert.Equal(44, button.Height);
        Assert.True(button.ClipToBounds);
        Assert.Equal(HorizontalAlignment.Right, button.HorizontalAlignment);
        Assert.Equal(new CornerRadius(999), presenter.CornerRadius);
        Assert.Equal(TimeSpan.FromMilliseconds(160), Assert.IsType<DoubleTransition>(Assert.Single(button.Transitions!)).Duration);
        Assert.Equal(2, presenter.Transitions!.Count);
        Assert.All(presenter.Transitions, transition =>
            Assert.Equal(TimeSpan.FromMilliseconds(120), Assert.IsType<BrushTransition>(transition).Duration));
        Assert.Equal(TimeSpan.FromMilliseconds(120), Assert.IsType<DoubleTransition>(Assert.Single(label.Transitions!)).Duration);
        Assert.Equal(0, label.Opacity);

        button.Classes.Add("reducedMotion");
        Assert.Null(button.Transitions);
        Assert.Null(presenter.Transitions);
        Assert.Null(label.Transitions);
        foreach (string state in new[] { ":pointerover", ":focus-visible" })
        {
            button.SetState(state, true);
            Assert.Equal(136, button.Width);
            Assert.Equal(1, label.Opacity);
            button.SetState(state, false);
            Assert.Equal(44, button.Width);
            Assert.Equal(0, label.Opacity);
        }

        button.IsEnabled = false;
        AssertBrush("NfcSurfaceSubtleBrush", presenter.Background, dark);
        AssertBrush("NfcBorderMutedBrush", presenter.BorderBrush, dark);
        host.Close();
    }

    private static Window CreateHost(Button button, bool dark)
    {
        var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/ButtonStyles.axaml");
        var host = new Window
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = button,
        };
        host.Styles.Add(new StyleInclude(uri) { Source = uri });
        host.Measure(new Size(188, 100));
        host.Arrange(new Rect(0, 0, 188, 100));
        return host;
    }

    private static ContentPresenter Presenter(Button button) => Assert.Single(
        button.GetVisualDescendants().OfType<ContentPresenter>(), candidate => candidate.Name == "PART_ContentPresenter");

    private static void AssertBrush(string key, IBrush? actual, bool dark)
    {
        XElement root = ThemeContractTests.ReadBaseline("ThemeTokens").Root!;
        XElement theme = Assert.Single(root.Descendants(ThemeContractTests.Presentation + "ResourceDictionary"),
            element => element.Attribute(ThemeContractTests.Xaml + "Key")?.Value == (dark ? "Dark" : "Light"));
        XElement brush = Assert.Single(theme.Elements(), element => element.Attribute(ThemeContractTests.Xaml + "Key")?.Value == key);
        Assert.Equal(Color.Parse(brush.Attribute("Color")!.Value), Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    }

    private sealed class StateButton : Button
    {
        protected override Type StyleKeyOverride => typeof(Button);

        internal void SetState(string state, bool enabled) => PseudoClasses.Set(state, enabled);
    }
}
