// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Nvt.Core.Avalonia.Tests.Choice;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Checks opt-in marked selection through resolved controls and their existing visual parts.</summary>
public sealed class MarkedSelectionTests
{
    private static readonly string[] _kinds =
        ["Button", "ToggleButton", "ListBox", "ListBoxItem", "ComboBoxItem", "TabControl", "TabItem"];

    private static readonly string[] _surfaces =
        ["NfcAppBackgroundBrush", "NfcSurfaceBrush", "NfcSurfaceSubtleBrush"];

    private static readonly ItemState[] _states =
    [
        .. ListMenuTestHost.States.Where(state => !state.Focus),
        new("Pressed without hover", Pressed: true),
        new("Selected + press without hover", Selected: true, Pressed: true),
        new("Disabled + hover", Hover: true, Disabled: true),
        new("Disabled + press", Hover: true, Pressed: true, Disabled: true),
        new("Disabled + selected + hover", Selected: true, Hover: true, Disabled: true),
        new("Disabled + selected + press", Selected: true, Hover: true, Pressed: true, Disabled: true),
    ];

    /// <summary>Names every state independently while sharing the seven selector entry points.</summary>
    public static IEnumerable<object[]> StateCases()
    {
        foreach (bool dark in new[] { false, true })
            foreach (ItemState state in _states)
                yield return [dark, state.Name];
    }

    /// <summary>Pins brushes, indicator visibility, reserved thickness, and stable button layout.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(StateCases))]
    public void MarkedStateTableResolves(bool dark, string stateName)
    {
        using var fixture = new Fixture(dark);
        ItemState state = _states.First(candidate => candidate.Name == stateName);
        var layouts = fixture.Samples.ToDictionary(sample => sample.Kind,
            sample => (sample.Item.Bounds.Size, sample.Item.DesiredSize, sample.Label.Bounds));

        fixture.SetState(state);

        Assert.All(fixture.Samples, sample => AssertState(sample, state, layouts[sample.Kind]));
    }

    /// <summary>Measures the resolved mark against each surface after composing the neutral state layer.</summary>
    [AvaloniaTheory]
    [InlineData("NFC", false, "#1557E9", "#1148BE", "#EFF3FD", "#F7F9FE")]
    [InlineData("NFC", true, "#5FA5FA", "#8FBFFB", "#1A2940", "#162034")]
    [InlineData("NFH", false, "#2967A9", "#225489", "#F0F4F9", "#F8FAFC")]
    [InlineData("NFH", true, "#53A6FF", "#86C0FF", "#192941", "#152134")]
    [InlineData("NFU", false, "#4A6F00", "#3D5B00", "#F2F5ED", "#F9FAF6")]
    [InlineData("NFU", true, "#82B11A", "#97CD1E", "#1F2A25", "#182126")]
    public void ToolAccentMarksMeetContrast(string tool, bool dark, string accent,
        string strong, string surface, string subtle)
    {
        using var fixture = new Fixture(dark);
        using var overrides = new AccentOverrides(accent, strong, surface, subtle);

        Assert.All(_states.Where(state => state.Selected), state =>
        {
            fixture.SetState(state);
            Assert.All(fixture.Samples, sample =>
            {
                AssertSelectionColors(sample, Color.Parse(accent), Color.Parse(strong), state.Disabled);
                Assert.All(_surfaces, key =>
                {
                    Color adjacent = ShownColor.Over(sample.Item.Background, ResourceColor(sample.Item, key));
                    double ratio = ShownColor.Contrast(ShownColor.Over(sample.MarkBrush, adjacent), adjacent);
                    Assert.True(ratio >= 3,
                        $"{tool}/{(dark ? "Dark" : "Light")}/{sample.Kind}/{state.Name}/{key}: {ratio:F6}:1");
                });
            });
        });
    }

    /// <summary>Replaces all seven application keys after templates exist; aliases have distinct colors.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationAccentOverridesUpdateExistingMarksAndText(bool dark)
    {
        using var fixture = new Fixture(dark);
        fixture.SetState(new ItemState("Selected", Selected: true));
        using var overrides = new AccentOverrides("#8C3CBD", "#346F28", "#F1E8F5", "#F8F3FA",
            border: "#B47F23", borderStrong: "#7F5423", borderLight: "#D1A251");

        Assert.All(_states.Where(state => state.Selected && !state.Disabled), state =>
        {
            fixture.SetState(state);
            Assert.All(fixture.Samples, sample =>
                AssertSelectionColors(sample, Color.Parse("#8C3CBD"), Color.Parse("#346F28")));
        });
    }

    /// <summary>The selection frame and the existing exterior keyboard ring remain separate.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarkedButtonsKeepTheirExteriorKeyboardRing(bool dark)
    {
        using var fixture = new Fixture(dark);

        Assert.All(_states.Where(state => !state.Disabled), state =>
        {
            fixture.SetState(state);
            Assert.All(fixture.Samples.Where(sample => sample.Item is Button), sample =>
            {
                var button = (Button)sample.Item;
                var colors = (ColorOf(button.Background), ColorOf(button.BorderBrush), ColorOf(button.Foreground));
                Assert.True(fixture.Before.Focus(NavigationMethod.Pointer));
                Assert.True(button.Focus(NavigationMethod.Tab));
                Flush(fixture.Host);
                AdornerLayer layer = AdornerLayer.GetAdornerLayer(button)!;
                Border ring = Assert.Single(layer.Children.OfType<Border>(),
                    border => AdornerLayer.GetAdornedElement(border) == button);
                Assert.Equal(ResourceColor(button, "Nvt.Focus.RingBrush"), ColorOf(ring.BorderBrush));
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.Equal(new Thickness(-4), ring.Margin);
                Assert.Equal(new CornerRadius(4), ring.CornerRadius);
                Assert.False(ring.IsHitTestVisible);
                Assert.False(AdornerLayer.GetIsClipEnabled(ring));
                Assert.Equal(colors, (ColorOf(button.Background), ColorOf(button.BorderBrush), ColorOf(button.Foreground)));
            });
        });
    }

    /// <summary>A marked button with an open flyout keeps one visible fill, and keeps its mark when selected.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarkedButtonsShowAnOpenFlyout(bool dark)
    {
        using var fixture = new Fixture(dark);

        Assert.All(_states.Where(state => state.Name is "Rest" or "Selected"), state =>
        {
            fixture.SetState(state);
            var button = (Button)fixture.Samples.Single(sample => sample.Kind == "Button").Item;
            var pseudo = (IPseudoClasses)button.Classes;
            pseudo.Set(":flyout-open", true);
            Flush(fixture.Host);

            Assert.Equal(ResourceColor(button, "Nvt.Interaction.HoverBrush"), ColorOf(button.Background));
            Assert.Equal(state.Selected ? ResourceColor(button, "NfcAccentBrush") : Colors.Transparent, ColorOf(button.BorderBrush));
            pseudo.Set(":flyout-open", false);
        });
    }

    /// <summary>A ToggleButton selected through the active class shows the same mark as a checked one.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarkedToggleButtonsAcceptTheActiveClass(bool dark)
    {
        using var fixture = new Fixture(dark);
        fixture.SetState(_states.First(state => state.Name == "Rest"));
        var toggle = (ToggleButton)fixture.Samples.Single(sample => sample.Kind == "ToggleButton").Item;

        toggle.Classes.Add("active");
        Flush(fixture.Host);

        Assert.Equal(ResourceColor(toggle, "NfcAccentBrush"), ColorOf(toggle.BorderBrush));
        Assert.Equal(ResourceColor(toggle, "NfcAccentStrongBrush"), ColorOf(toggle.Foreground));
    }

    /// <summary>A marked list or tab control styles its own items only, not the items of a nested control.</summary>
    [AvaloniaFact]
    public void MarkedContainersLeaveNestedControlsAlone()
    {
        var innerRow = new ListBoxItem
        {
            Content = "Inner row",
            IsSelected = true,
        };
        var innerTab = new TabItem
        {
            Header = "Inner tab",
        };
        var innerList = PlainList(innerRow);
        var innerTabs = new TabControl
        {
            ItemsSource = new[] { innerTab },
            SelectedIndex = 0,
        };
        var outerRow = new ListBoxItem
        {
            Content = innerList,
        };
        var outerList = PlainList(outerRow);
        outerList.Classes.Add("marked");
        var outerTabs = new TabControl
        {
            Classes = { "marked" },
            ItemsSource = new[]
            {
                new TabItem
                {
                    Header = "Outer tab",
                    Content = innerTabs,
                },
            },
            SelectedIndex = 0,
        };
        Window host = ChoiceTestHost.Create(new StackPanel
        {
            Children = { outerList, outerTabs },
        }, dark: false, styles: false, height: 500, snapshot: false);
        try
        {
            host.Resources.MergedDictionaries.Clear();
            host.Classes.Add("reducedMotion");
            foreach (string name in new[] { "ListStyles", "TabStyles" })
            {
                var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{name}.axaml");
                host.Styles.Add(new StyleInclude(uri)
                {
                    Source = uri,
                });
            }
            Show(host);
            Flush(host);

            Assert.Equal(ResourceColor(innerRow, "Nvt.List.SelectedBrush"), ColorOf(innerRow.Background));
            Assert.Equal(ResourceColor(innerTab, "Nvt.Tab.SelectedFillBrush"), ColorOf(innerTab.Background));
        }
        finally
        {
            host.Close();
        }
    }

    // The test application has no control theme; this is the smallest template that realizes the item.
    private static ListBox PlainList(ListBoxItem item) => new()
    {
        ItemsSource = new[] { item },
        Template = new FuncControlTemplate<ListBox>((_, scope) => new ItemsPresenter
        {
            Name = "PART_ItemsPresenter",
        }.RegisterInNameScope(scope)),
    };

    private static void AssertState(Sample sample, ItemState state, (Size Size, Size Desired, Rect Label) layout)
    {
        TemplatedControl item = sample.Item;
        string fill = state.Disabled ? sample.Item is Button ? "Transparent" : "Nvt.List.TransparentBrush"
            : state.Pressed ? "Nvt.Interaction.PressedBrush"
            : state.Hover ? "Nvt.Interaction.HoverBrush" : "Nvt.Interaction.RestBrush";
        string text = state.Disabled ? "NfcTextDisabledBrush" : state.Selected ? "NfcAccentStrongBrush"
            : item is TabItem ? state.Pressed ? "Nvt.Tab.PressedTextBrush"
                : state.Hover ? "Nvt.Tab.HoverTextBrush" : "Nvt.Tab.TextBrush"
            : item is Button ? state.Hover || state.Pressed ? "NfcTextBrush" : "NfcTextSecondaryBrush"
            : "NfcTextBrush";
        Assert.Equal(fill == "Transparent" ? Colors.Transparent : ResourceColor(item, fill), ColorOf(item.Background));
        Assert.Equal(ResourceColor(item, text), ColorOf(item.Foreground));
        Assert.Equal(ColorOf(item.Foreground), ColorOf(sample.Label.Foreground));
        Assert.Equal(1, item.Opacity);
        Assert.Equal(new Thickness(item is Button ? 2 : 0), item.BorderThickness);
        if (item is Button button)
        {
            string border = state.Disabled ? "NfcTextDisabledBrush" : "NfcAccentBrush";
            Assert.Equal(state.Selected ? ResourceColor(item, border) : Colors.Transparent, ColorOf(item.BorderBrush));
            Border frame = Part<Border>(button, "RoleBorder");
            Assert.Equal(ColorOf(item.Background), ColorOf(frame.Background));
            Assert.Equal(ColorOf(item.BorderBrush), ColorOf(frame.BorderBrush));
            Assert.Equal(new Thickness(2), frame.BorderThickness);
            Assert.Equal(new CornerRadius(0), frame.CornerRadius);
            Assert.Equal(layout, (item.Bounds.Size, item.DesiredSize, sample.Label.Bounds));
        }
        else
        {
            Border indicator = sample.Mark;
            Assert.Equal(state.Selected, indicator.IsVisible);
            Assert.Equal(2, item is TabItem ? indicator.Height : indicator.Width);
            if (state.Selected)
            {
                string mark = state.Disabled ? "NfcTextDisabledBrush"
                    : item is TabItem ? "NfcAccentBrush" : "NfcAccentStrongBrush";
                Assert.Equal(ResourceColor(item, mark), ColorOf(indicator.Background));
            }
            if (item is ListBoxItem)
                Assert.Equal(ColorOf(item.Background), ColorOf(Part<ContentPresenter>(item, "PART_ContentPresenter").Background));
            else
                Assert.Equal(ColorOf(item.Background), ColorOf(Part<Border>(item, "TabBody").Background));
        }
    }

    private static void AssertSelectionColors(Sample sample, Color accent, Color strong, bool disabled = false)
    {
        Color text = disabled ? ResourceColor(sample.Item, "NfcTextDisabledBrush") : strong;
        Color mark = disabled || sample.Item is ListBoxItem ? text : accent;
        Assert.Equal(mark, ColorOf(sample.MarkBrush));
        Assert.Equal(text, ColorOf(sample.Item.Foreground));
        Assert.Equal(text, ColorOf(sample.Label.Foreground));
    }

    private sealed record Sample(string Kind, TemplatedControl Item, TextBlock Label, Control Root)
    {
        internal Border Mark => Part<Border>(Item, Item is Button ? "RoleBorder"
            : Item is TabItem ? "TabIndicator" : "ListSelectionIndicator");

        internal IBrush? MarkBrush => Item is Button ? Mark.BorderBrush : Mark.Background;
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture(bool dark)
        {
            Samples = [.. _kinds.Select(CreateSample)];
            var panel = new StackPanel
            {
                Spacing = 12,
                Children = { Before },
            };
            foreach (Sample sample in Samples) panel.Children.Add(sample.Root);
            Host = ChoiceTestHost.Create(new VisualLayerManager
            {
                EnableAdornerLayer = true,
                Child = new Border
                {
                    Padding = new Thickness(12),
                    Child = panel,
                },
            }, dark, styles: false, height: 650, snapshot: false);
            // Application overrides must win; the shared application already owns ThemeTokens.
            Host.Resources.MergedDictionaries.Clear();
            Host.Classes.Add("reducedMotion");
            foreach (string name in new[] { "ButtonStyles", "ListStyles", "TabStyles" })
            {
                var uri = new Uri($"avares://Nvt.Core.Avalonia/Theme/{name}.axaml");
                Host.Styles.Add(new StyleInclude(uri)
                {
                    Source = uri,
                });
            }
            Show(Host);
        }

        internal Window Host { get; }
        internal Sample[] Samples { get; }
        internal Grid Before { get; } = new()
        {
            Focusable = true,
            Height = 20,
        };

        internal void SetState(ItemState state)
        {
            foreach (Sample sample in Samples)
            {
                TemplatedControl item = sample.Item;
                item.IsEnabled = !state.Disabled;
                if (sample.Root is TabControl tabs) tabs.SelectedIndex = state.Selected ? 0 : 1;
                if (sample.Root is ListBox list) list.SelectedIndex = state.Selected ? 0 : -1;
                switch (item)
                {
                    case ToggleButton toggle:
                        toggle.IsChecked = state.Selected;
                        break;
                    case Button button:
                        button.Classes.Set("active", state.Selected);
                        break;
                    case TabItem tab:
                        tab.IsSelected = state.Selected;
                        break;
                    case ListBoxItem row:
                        row.IsSelected = state.Selected;
                        break;
                }
                var pseudo = (IPseudoClasses)item.Classes;
                pseudo.Set(":pointerover", state.Hover);
                pseudo.Set(":pressed", state.Pressed);
            }
            Flush(Host);
        }

        public void Dispose() => Host.Close();

        private static Sample CreateSample(string kind)
        {
            var label = new TextBlock
            {
                Text = "Sample choice",
            };
            TemplatedControl item = kind switch
            {
                "Button" => new Button(),
                "ToggleButton" => new ToggleButton(),
                "TabItem" or "TabControl" => new TabItem(),
                "ComboBoxItem" => new ComboBoxItem(),
                _ => new ListBoxItem(),
            };
            item.HorizontalAlignment = HorizontalAlignment.Left;
            if (item is TabItem tab) tab.Header = label;
            else ((ContentControl)item).Content = label;
            if (item is Button)
            {
                item.Classes.Add("actionGhost");
                item.Classes.Add("flat");
            }
            Control root = kind switch
            {
                // The test application has no control theme; this is the smallest template that realizes the item.
                "ListBox" => new ListBox
                {
                    Classes = { "marked" },
                    ItemsSource = new[] { item },
                    Template = new FuncControlTemplate<ListBox>((_, scope) => new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                    }.RegisterInNameScope(scope)),
                },
                "TabControl" => new TabControl
                {
                    Classes = { "marked" },
                    ItemsSource = new[]
                    {
                        item,
                        new TabItem
                        {
                            Header = "Other choice",
                        },
                    },
                    SelectedIndex = 1,
                },
                _ => item,
            };
            if (ReferenceEquals(root, item)) item.Classes.Add("marked");
            return new Sample(kind, item, label, root);
        }
    }

    private sealed class AccentOverrides : IDisposable
    {
        // UI thread only; the shared headless session serializes application resource changes.
        private readonly IResourceDictionary _resources = Application.Current!.Resources;
        private readonly Dictionary<string, object?> _previous;
        private readonly Dictionary<string, Color> _colors;

        internal AccentOverrides(string accent, string strong, string surface, string subtle,
            string? border = null, string? borderStrong = null, string? borderLight = null)
        {
            _colors = new Dictionary<string, Color>(StringComparer.Ordinal)
            {
                ["NfcAccentBrush"] = Color.Parse(accent),
                ["NfcAccentStrongBrush"] = Color.Parse(strong),
                ["NfcAccentSurfaceBrush"] = Color.Parse(surface),
                ["NfcAccentSurfaceSubtleBrush"] = Color.Parse(subtle),
                ["NfcAccentBorderBrush"] = Color.Parse(border ?? accent),
                ["NfcAccentBorderStrongBrush"] = Color.Parse(borderStrong ?? strong),
                ["NfcAccentBorderLightBrush"] = Color.Parse(borderLight ?? accent),
            };
            _previous = _colors.Keys.Where(_resources.ContainsKey)
                .ToDictionary(key => key, key => _resources[key], StringComparer.Ordinal);
            foreach ((string key, Color color) in _colors) _resources[key] = new SolidColorBrush(color);
        }

        public void Dispose()
        {
            foreach (string key in _colors.Keys)
            {
                if (_previous.TryGetValue(key, out object? value)) _resources[key] = value;
                else _resources.Remove(key);
            }
        }
    }
}
