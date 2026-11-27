// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Regresses accent lookup at application scope, including the public brush tokens consumed by tools.</summary>
[Collection(nameof(RestFillIsolation))]
public sealed class AccentSelectionTests
{
    private static readonly string[] _tools = ["NFC", "NFH", "NFU"];
    private static readonly bool[] _themes = [false, true];
    private static readonly int[] _interactions = [0, 1, 2];

    public static IEnumerable<object[]> States() =>
        from tool in _tools
        from dark in _themes
        from state in _interactions
        from flat in _themes
        select new object[] { tool, dark, state, flat };

    public static IEnumerable<object[]> Variants() =>
        from tool in _tools
        from dark in _themes
        select new object[] { tool, dark };

    /// <summary>Pins rounded NFC defaults independently of the expected palette calculations.</summary>
    [AvaloniaTheory]
    [InlineData(false, "#D5E1FB", "#E3EBFC", "#DCE6FC", "#113D9D", "#113EA0")]
    [InlineData(true, "#223755", "#20334F", "#243B5C", "#BEDAFB", "#A4CBFB")]
    public void DefaultBlendedColorsArePinned(bool dark, string selected, string hover, string pressed, string text, string filledPressed)
    {
        using var scope = new AccentSelectionScope("NFC", dark);
        var consumer = new Border();
        scope.Show(consumer);
        foreach ((string key, string color) in new[]
        {
            ("Nvt.Controls.SelectedBrush", selected),
            ("Nvt.Controls.SelectedPointerOverBrush", hover),
            ("Nvt.Controls.SelectedPressedBrush", pressed),
            ("Nvt.Controls.SelectedForegroundBrush", text),
            ("Nvt.Toggle.SelectedPressedBrush", filledPressed),
            ("Nvt.Toggle.SwitchPressedBrush", filledPressed),
        })
            Assert.Equal(Color.Parse(color), ResourceColor(consumer, key));
        Assert.NotEqual(ResourceColor(consumer, "Nvt.Toggle.SelectedPointerOverBrush"), ResourceColor(consumer, "Nvt.Toggle.SelectedPressedBrush"));
        Assert.NotEqual(ResourceColor(consumer, "Nvt.Toggle.SwitchPointerOverBrush"), ResourceColor(consumer, "Nvt.Toggle.SwitchPressedBrush"));
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        Channel(from.A, to.A, amount), Channel(from.R, to.R, amount),
        Channel(from.G, to.G, amount), Channel(from.B, to.B, amount));

    private static byte Channel(byte from, byte to, double amount) => (byte)Math.Round(from + amount * (to - from));

    /// <summary>Only seven application keys control public tokens and global Core styles in every selected interaction.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(States))]
    public void ApplicationAccentReachesTokensAndRenderedSelection(string tool, bool dark, int state, bool flat)
    {
        using var scope = new AccentSelectionScope(tool, dark, flat);
        var scene = new SelectionScene();
        Window host = scope.Show(scene.Root);

        scene.Verify(host, tool, dark, state);
    }

    /// <summary>A global variant change reuses templates and selects the other application accent dictionary.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void ThemeChangeKeepsFollowingApplicationAccent(string tool, bool dark)
    {
        using var scope = new AccentSelectionScope(tool, dark);
        var scene = new SelectionScene();
        Window host = scope.Show(scene.Root);
        scene.Verify(host, tool, dark, 0);
        object? template = scene.List.Template;

        scope.SetVariant(!dark);

        scene.Verify(host, tool, !dark, 1);
        Assert.Same(template, scene.List.Template);
        scope.SetVariant(dark);
        scene.Verify(host, tool, dark, 2);
        Assert.Same(template, scene.List.Template);
    }

    /// <summary>Replacing the seven application brushes updates already materialized token brushes and tool visuals.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void ReplacingApplicationAccentUpdatesExistingTokens(string tool, bool dark)
    {
        using var scope = new AccentSelectionScope(tool, dark);
        var scene = new SelectionScene();
        Window host = scope.Show(scene.Root);
        scene.Verify(host, tool, dark, 0);
        string next = tool == "NFU" ? "NFH" : "NFU";

        ReplaceAccent(next, dark);

        scene.Verify(host, next, dark, 2);
    }

    /// <summary>Text without a role class keeps the color of its own container inside a filled selection.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void FilledSelectionKeepsUnclassedTextColor(string tool, bool dark)
    {
        using var scope = new AccentSelectionScope(tool, dark);
        var badge = new TextBlock
        {
            Text = "3",
        };
        var role = new TextBlock
        {
            Text = "Items",
            Classes = { "body" },
        };
        var container = new Border
        {
            Child = badge,
        };
        container.SetValue(TextElement.ForegroundProperty, Brushes.DarkRed);
        var toggle = new ToggleButton
        {
            IsChecked = true,
            Content = new StackPanel
            {
                Children = { container, role },
            },
        };
        toggle.Classes.Add("toggleSegment");

        scope.Show(toggle);

        Assert.Equal(Colors.DarkRed, ColorOf(badge.Foreground));
        Assert.Equal(ColorOf(toggle.Foreground), ColorOf(role.Foreground));
    }

    private static void ReplaceAccent(string tool, bool dark)
    {
        foreach (var entry in AccentSelectionScope.Palette(tool, dark))
            Application.Current!.Resources[entry.Key] = new SolidColorBrush(entry.Value);
    }

    private sealed class SelectionScene
    {
        internal ListBoxItem List { get; } = new()
        {
            Content = "Selected row",
            IsSelected = true,
        };
        private readonly ComboBoxItem _combo = new()
        {
            Content = "Selected option",
            IsSelected = true,
        };
        private readonly TabItem _tab = new()
        {
            Header = "Selected tab",
            IsSelected = true,
        };
        private readonly ToggleButton[] _filled = [.. new[] { "toggleSegment", "toggleTab", "toggleIcon" }
            .Select(role => Sample(role, new("Selected", Checked: true), "Filled"))];
        private readonly ToggleButton[] _switches = [Sample("toggleSwitch", new("On", Checked: true)), Sample("nativeSwitch", new("On", Checked: true))];
        private readonly ToggleButton _soft = Sample("toggleSoft", new("Selected", Checked: true), "Soft");
        private readonly CheckBox _check = new()
        {
            Content = "Checked",
            IsChecked = true,
        };
        private readonly CheckBox _mixed = new()
        {
            Content = "Mixed",
            IsThreeState = true,
            IsChecked = null,
        };
        private readonly RadioButton _radio = new()
        {
            Content = "Radio",
            IsChecked = true,
        };
        private readonly ProgressBar _progress = new()
        {
            Width = 200,
            Value = 50,
        };
        private readonly GridSplitter _splitter = new()
        {
            Height = 48,
        };
        private Border? _preview;
        private readonly Border _consumer = new();
        internal StackPanel Root { get; } = new();

        internal SelectionScene()
        {
            _consumer.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Nvt.List.SelectedLabelBrush"));
            foreach (Control control in (Control[])[List, _combo, _tab, _soft, _check, _mixed, _radio, _progress, _splitter, _consumer, .. _filled, .. _switches])
            {
                if (control is ContentControl { Content: string text } content)
                    content.Content = SelectedText(text);
                Root.Children.Add(control);
            }
            _tab.Header = SelectedText("Selected tab");
        }

        private static TextBlock SelectedText(string text) => new()
        {
            Text = text,
            Classes = { "body", "muted" },
        };

        internal void Verify(Window host, string tool, bool dark, int state)
        {
            if (_preview is null)
            {
                _preview = Assert.IsType<Border>(_splitter.PreviewContent!.Build());
                Root.Children.Add(_preview);
            }
            foreach (Control control in Root.Children)
            {
                var pseudo = (IPseudoClasses)control.Classes;
                pseudo.Set(":pointerover", state == 1);
                pseudo.Set(":pressed", state == 2);
            }
            Flush(host);
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, List.ActualThemeVariant);
            var expected = AccentSelectionScope.Palette(tool, dark);
            Color surface = Color.Parse(dark ? "#111827" : "#FFFFFF");
            Color text = Color.Parse(dark ? "#F8FAFC" : "#0F172A");
            expected["Nvt.Controls.SelectedBrush"] = Blend(surface, expected["NfcAccentBrush"], dark ? 0.22 : 0.18);
            expected["Nvt.Controls.SelectedPointerOverBrush"] = Blend(surface, expected["NfcAccentBrush"], dark ? 0.19 : 0.12);
            expected["Nvt.Controls.SelectedPressedBrush"] = Blend(surface, expected["NfcAccentBrush"], dark ? 0.25 : 0.15);
            expected["Nvt.Controls.SelectedForegroundBrush"] = Blend(expected["NfcAccentStrongBrush"], text, dark ? 0.45 : 0.22);
            expected["Nvt.Toggle.SelectedPressedBrush"] = Blend(expected["NfcAccentStrongBrush"], text, 0.2);
            expected["Nvt.Button.PrimaryLabelBrush"] = Color.Parse(dark ? "#0B1220" : "#FFFFFF");
            VerifyTokens(expected);
            Color strong = expected["Nvt.Controls.SelectedForegroundBrush"];
            Color muted = ResourceColor(List, "NfcTextMutedBrush");
            Color label = expected["Nvt.Button.PrimaryLabelBrush"];
            Color tint = expected[state == 2 ? "Nvt.Controls.SelectedPressedBrush" : state == 1 ? "Nvt.Controls.SelectedPointerOverBrush" : "Nvt.Controls.SelectedBrush"];
            Color fill = expected[state == 2 ? "Nvt.Toggle.SelectedPressedBrush" : state == 1 ? "NfcAccentStrongBrush" : "NfcAccentBrush"];
            foreach (TemplatedControl control in new TemplatedControl[] { List, _combo, _tab, _soft })
            {
                Assert.Equal(tint, ColorOf(control.Background));
                Assert.Equal(strong, ColorOf(control.Foreground));
                VerifyText(control, muted);
                CheckContrast(strong, tint, 4.5, "selected text");
                CheckContrast(muted, tint, 4.5, "muted selected text");
            }
            Assert.Equal(tint, ColorOf(Part<Border>(_tab, "TabBody").Background));
            Assert.Equal(tint, ColorOf(Part<global::Avalonia.Controls.Presenters.ContentPresenter>(List, "PART_ContentPresenter").Background));
            Assert.Equal(strong, ColorOf(Part<Border>(List, "ListSelectionIndicator").Background));
            Assert.Equal(strong, ColorOf(Part<Border>(_tab, "TabIndicator").Background));
            Assert.Equal(strong, ColorOf(_consumer.Background));
            foreach (ToggleButton control in new ToggleButton[] { _check, _mixed, _radio })
            {
                Assert.Equal(tint, ColorOf(Part<Border>(control, "ChoiceRow").Background));
                // Under FluentTheme the label color comes from Fluent's state setters. Core does not change it here.
                Color choice = ColorOf(control.Foreground);
                VerifyText(control, muted);
                Assert.Equal(fill, ColorOf(Part<Border>(control, "ChoiceIndicator").Background));
                CheckContrast(choice, tint, 4.5, "choice label");
            }
            Assert.Equal(label, ColorOf(Part<global::Avalonia.Controls.Shapes.Path>(_check, "ChoiceCheck").Stroke));
            Assert.Equal(label, ColorOf(Part<Rectangle>(_mixed, "ChoiceDash").Fill));
            Assert.Equal(label, ColorOf(Part<Ellipse>(_radio, "ChoiceDot").Fill));
            foreach (ToggleButton control in _filled.Concat(_switches))
            {
                Assert.Equal(fill, ColorOf(Part<Border>(control, "ToggleBody").Background));
                Color content = _switches.Contains(control) ? ColorOf(Part<Ellipse>(control, "ToggleKnob").Fill) : ColorOf(control.Foreground);
                Assert.Equal(label, content);
                if (!_switches.Contains(control)) VerifyText(control, label);
                CheckContrast(content, fill, 4.5, "filled label or knob");
            }
            Assert.Equal(expected["NfcAccentBrush"], ColorOf(_progress.Foreground));
            Assert.Equal(expected["NfcAccentBrush"], ColorOf(Part<Border>(_progress, "PART_Indicator").Background));
            Assert.Equal(expected["NfcAccentBrush"], ColorOf(_preview.Background));
            Assert.Equal(0.5, _preview.Opacity);
            foreach (string page in new[] { "NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSurfaceSubtleBrush" })
            {
                CheckContrast(fill, ResourceColor(List, page), 3, "filled track against page");
                CheckContrast(strong, ResourceColor(List, page), 3, "selection indicator against page");
                CheckContrast(ColorOf(_progress.Foreground), ResourceColor(List, page), 3, "progress against page");
            }
        }

        private void VerifyTokens(Dictionary<string, Color> expected)
        {
            foreach ((string source, string[] keys) in new (string, string[])[]
            {
                ("Nvt.Controls.SelectedBrush", ["Nvt.Controls.SelectedBrush", "Nvt.List.SelectedBrush", "Nvt.Tab.SelectedFillBrush", "Nvt.Toggle.SoftCheckedBrush", "Nvt.Form.SelectionBrush"]),
                ("Nvt.Controls.SelectedPointerOverBrush", ["Nvt.Controls.SelectedPointerOverBrush", "Nvt.List.SelectedPointerOverBrush", "Nvt.Tab.SelectedHoverFillBrush", "Nvt.Toggle.SoftCheckedPointerOverBrush"]),
                ("Nvt.Controls.SelectedPressedBrush", ["Nvt.Controls.SelectedPressedBrush", "Nvt.List.SelectedPressedBrush", "Nvt.Tab.SelectedPressedFillBrush", "Nvt.Toggle.SoftCheckedPressedBrush"]),
                ("Nvt.Controls.SelectedForegroundBrush", ["Nvt.Controls.SelectedForegroundBrush", "Nvt.List.SelectedLabelBrush", "Nvt.Tab.SelectedTextBrush", "Nvt.Tab.IndicatorBrush", "Nvt.Toggle.SoftCheckedForegroundBrush", "Nvt.Form.SelectionTextBrush"]),
                ("NfcAccentStrongBrush", ["Nvt.Toggle.SelectedPointerOverBrush", "Nvt.Toggle.SwitchPointerOverBrush"]),
                ("Nvt.Toggle.SelectedPressedBrush", ["Nvt.Toggle.SelectedPressedBrush", "Nvt.Toggle.SwitchPressedBrush"]),
                ("NfcAccentBrush", ["Nvt.Toggle.SelectedBrush", "Nvt.Toggle.SwitchOnBrush", "Nvt.Progress.IndicatorBrush", "Nvt.GridSplitter.PreviewBrush"]),
                ("Nvt.Button.PrimaryLabelBrush", ["Nvt.Toggle.SelectedLabelBrush"]),
            })
                foreach (string key in keys)
                {
                    Assert.True(List.TryFindResource(key, List.ActualThemeVariant, out object? value), key);
                    var brush = Assert.IsType<SolidColorBrush>(value);
                    Assert.Equal(expected[source], brush.Color);
                    Assert.Equal(1, brush.Opacity);
                    if (source.StartsWith("Nvt.Controls.", StringComparison.Ordinal))
                    {
                        Assert.True(List.TryFindResource(source, List.ActualThemeVariant, out object? shared), source);
                        Assert.Same(shared, brush);
                    }
                }
            Color hover = expected["Nvt.Controls.SelectedPointerOverBrush"];
            Assert.Equal(Color.FromArgb(0, hover.R, hover.G, hover.B), ResourceColor(List, "Nvt.Controls.SelectedRestBrush"));
            Assert.Equal(Colors.White, ResourceColor(List, "Nvt.Toggle.KnobBrush"));
        }

        private static T Part<T>(Control owner, string name) where T : Control =>
            Assert.Single(owner.GetVisualDescendants().OfType<T>(), item => item.Name == name);

        private static void VerifyText(Control owner, Color expected) =>
            Assert.All(owner.GetVisualDescendants().OfType<TextBlock>(), text => Assert.Equal(expected, ColorOf(text.Foreground)));

        private static void CheckContrast(Color foreground, Color background, double floor, string pair)
        {
            double ratio = ShownColor.Contrast(foreground, background);
            Assert.True(ratio >= floor, $"{pair}: {foreground}/{background} = {ratio:F6}:1 < {floor}:1");
        }
    }
}
