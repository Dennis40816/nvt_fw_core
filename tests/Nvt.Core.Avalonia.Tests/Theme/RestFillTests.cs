// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Nvt.Core.Avalonia.Tests.Choice;
using Nvt.Core.Avalonia.Tests.Dividers;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Verifies rest-fill resources, scope, native input, runtime updates, and painted-surface contrast.</summary>
/// <param name="output">Receives measured contrast ratios.</param>
[Collection(nameof(RestFillIsolation))]
public sealed class RestFillTests(ITestOutputHelper output)
{
    private static readonly bool[] Both = [false, true];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] Surfaces = ["NfcSurfaceBrush", "NfcAppBackgroundBrush", "NfcSurfaceSubtleBrush",
        "NfcSelectionSurfaceBrush", "NfcSecondaryActionPressedBrush"];
    private static readonly ThemeRestFill[] Fills = [ThemeRestFill.Soft, ThemeRestFill.None, ThemeRestFill.Soft];

    /// <summary>Rejects null resources and undefined values before mutating resources.</summary>
    [AvaloniaFact]
    public void InvalidArgumentsLeaveResourcesUntouched()
    {
        var resources = new ResourceDictionary();
        Assert.Throws<ArgumentNullException>(() => ThemeRestFills.SetRestFill(null!, ThemeRestFill.Soft));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThemeRestFills.SetRestFill(resources, (ThemeRestFill)42));
        Assert.Empty(resources.MergedDictionaries);
        Assert.Equal(ThemeRestFill.Soft, default(ThemeRestFill));
    }

    /// <summary>Rejects a worker-thread call before adding a dictionary.</summary>
    [AvaloniaFact]
    public void WorkerThreadCannotChangeResources()
    {
        var resources = new ResourceDictionary();
        Exception? error = null;
        var worker = new Thread(() => error = Record.Exception(() => ThemeRestFills.SetRestFill(resources, ThemeRestFill.None)));
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        Assert.IsType<InvalidOperationException>(error);
        Assert.Empty(resources.MergedDictionaries);
    }

    /// <summary>Proves the later plain mode key beats the earlier default and Soft leaves the default in place.</summary>
    [AvaloniaFact]
    public void LaterMergedDictionaryOverridesTheModeToken()
    {
        var resources = new ResourceDictionary();
        var uri = new Uri("avares://Nvt.Core.Avalonia/Theme/ThemeTokens.axaml");
        var tokens = new ResourceInclude(uri) { Source = uri };
        resources.MergedDictionaries.Add(tokens);
        foreach (ThemeRestFill fill in Fills)
        {
            ThemeRestFills.SetRestFill(resources, fill);
            Assert.Equal(2, resources.MergedDictionaries.Count);
            Assert.Same(tokens, resources.MergedDictionaries[0]);
            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Assert.True(resources.TryGetResource("Nvt.Controls.RestFillMode", variant, out object? value));
                Assert.Equal(fill.ToString(), value);
            }
        }
        Assert.Empty(ThemeContractTests.ReadExtracted("RestFillSoft").Root!.Elements());
        Assert.Single(ThemeContractTests.ReadExtracted("RestFillNone").Root!.Elements());
    }

    /// <summary>Updates attached controls at application, window, and subtree roots while preserving unrelated dictionaries and siblings.</summary>
    [AvaloniaFact]
    public void ResourceRootsReplaceOneIncludeAndIsolateSubtrees()
    {
        Application app = Application.Current!;
        var saved = app.Resources.MergedDictionaries.ToArray();
        try
        {
            foreach (int scope in new[] { 0, 1, 2 })
            {
                var choice = Sample(false, new("Rest"));
                var radio = Sample(true, new("Rest", true));
                var expander = new Expander { Header = "Details" };
                var subtree = new StackPanel { Children = { choice, radio, expander } };
                var sibling = Sample(false, new("Rest"));
                Window host = RedesignSheets.Create(new StackPanel { Margin = new Thickness(24), Children = { subtree, sibling } });
                IResourceDictionary resources = scope == 0 ? app.Resources : scope == 1 ? host.Resources : subtree.Resources;
                var unrelated = new ResourceDictionary { ["Unrelated"] = 7 };
                resources.MergedDictionaries.Add(unrelated);
                ThemeShapes.SetShape(resources, ThemeShape.Square);
                int count = resources.MergedDictionaries.Count;
                var shape = resources.MergedDictionaries[^1];
                try
                {
                    ListMenuTestHost.Show(host);
                    foreach (ThemeRestFill fill in Fills)
                    {
                        ThemeRestFills.SetRestFill(resources, fill);
                        Flush(host);
                        Assert.Equal(count + 1, resources.MergedDictionaries.Count);
                        Assert.Contains(unrelated, resources.MergedDictionaries);
                        Assert.Contains(shape, resources.MergedDictionaries);
                        Assert.True((fill == ThemeRestFill.None) == (ColorOf(Part<Border>(choice, "ChoiceRow").Background).A == 0),
                            $"Scope {scope}, {fill}: choice {ColorOf(Part<Border>(choice, "ChoiceRow").Background)}, classes {string.Join(",", choice.Classes)}");
                        Assert.True((fill == ThemeRestFill.None) == (ColorOf(Part<Border>(radio, "ChoiceRow").Background).A == 0),
                            $"Scope {scope}, {fill}: radio {ColorOf(Part<Border>(radio, "ChoiceRow").Background)}, classes {string.Join(",", radio.Classes)}");
                        Assert.True((fill == ThemeRestFill.None) == (ColorOf(DividerTestHost.Header(expander).Background).A == 0),
                            $"Scope {scope}, {fill}: header {ColorOf(DividerTestHost.Header(expander).Background)}, classes {string.Join(",", DividerTestHost.Header(expander).Classes)}");
                        Assert.Equal(scope != 2 && fill == ThemeRestFill.None, ColorOf(Part<Border>(sibling, "ChoiceRow").Background).A == 0);
                    }
                }
                finally { host.Close(); }
                app.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in saved) app.Resources.MergedDictionaries.Add(dictionary);
            }
        }
        finally
        {
            app.Resources.MergedDictionaries.Clear();
            foreach (var dictionary in saved) app.Resources.MergedDictionaries.Add(dictionary);
        }
    }

    /// <summary>Pins default and explicit Soft rest and disabled fills for every selection, shape, and theme.</summary>
    /// <param name="radio">Whether the native choice is a radio button.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChoicesKeepTodaySoftAndDisabledBrushes(bool radio)
    {
        VisitChoice(radio, (host, control) =>
        {
            foreach (bool explicitSoft in Both)
            {
                if (explicitSoft) ThemeRestFills.SetRestFill(host.Resources, ThemeRestFill.Soft);
                foreach (bool? selected in Selections(radio))
                foreach (bool disabled in Both)
                {
                    SetState(control, new("Rest", selected, Disabled: disabled));
                    Flush(host);
                    Assert.Equal(ResourceColor(control, !disabled && selected is not false ? "Nvt.Controls.SelectedBrush" : "NfcSurfaceSubtleBrush"),
                        ColorOf(Part<Border>(control, "ChoiceRow").Background));
                }
            }
        });
    }

    /// <summary>Hides unchecked, checked, mixed, and disabled rows while retaining exact hover and pressed fills.</summary>
    /// <param name="radio">Whether the native choice is a radio button.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChoicesWithoutRestFillKeepInteractionFeedback(bool radio)
    {
        VisitChoice(radio, (host, control) =>
        {
            ThemeRestFills.SetRestFill(host.Resources, ThemeRestFill.None);
            foreach (bool? selected in Selections(radio))
            foreach (ChoiceState state in Interactions.Concat([new("Pressed outside", Pressed: true),
                new("Disabled interactions", Disabled: true, Hover: true, Pressed: true, Focus: true)]))
            {
                SetState(control, state with { Checked = selected });
                Flush(host);
                string? key = state.Disabled || !state.Hover && !state.Pressed ? null : selected is not false
                    ? state.Pressed ? "Nvt.Controls.SelectedPressedBrush" : "Nvt.Controls.SelectedPointerOverBrush"
                    : state.Pressed ? "NfcSecondaryActionPressedBrush" : "NfcSelectionSurfaceBrush";
                Color color = ColorOf(Part<Border>(control, "ChoiceRow").Background);
                if (key is null) Assert.Equal(0, color.A);
                else Assert.Equal(ResourceColor(control, key), color);
                Assert.Equal(state.Focus && !state.Disabled, Part<Border>(control, "ChoiceFocusRing").IsVisible);
            }
        });
    }

    /// <summary>Keeps Soft rows and headers following palette overrides that the attached window sets after first use.</summary>
    /// <param name="radio">Whether the native choice is a radio button.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoftFillsFollowPaletteOverridesAtTheWindow(bool radio)
    {
        Color subtle = Color.Parse("#FF336699"), selected = Color.Parse("#FF996633");
        VisitChoice(radio, (host, control) =>
        {
            ThemeRestFills.SetRestFill(host.Resources, ThemeRestFill.Soft);
            foreach (bool? isChecked in Selections(radio))
            foreach (bool disabled in Both)
            {
                SetState(control, new("Rest", isChecked, Disabled: disabled));
                Flush(host);
                Color original = ColorOf(Part<Border>(control, "ChoiceRow").Background);
                host.Resources["NfcSurfaceSubtleBrush"] = new SolidColorBrush(subtle);
                host.Resources["Nvt.Controls.SelectedBrush"] = new SolidColorBrush(selected);
                Flush(host);
                Assert.Equal(!disabled && isChecked is not false ? selected : subtle, ColorOf(Part<Border>(control, "ChoiceRow").Background));
                host.Resources.Remove("NfcSurfaceSubtleBrush");
                host.Resources.Remove("Nvt.Controls.SelectedBrush");
                Flush(host);
                Assert.Equal(original, ColorOf(Part<Border>(control, "ChoiceRow").Background));
            }
        });
        VisitExpander((host, expander, header) =>
        {
            ThemeRestFills.SetRestFill(host.Resources, ThemeRestFill.Soft);
            foreach (bool disabled in Both)
            {
                expander.IsEnabled = !disabled;
                DividerTestHost.SetState(header, new("Rest", Disabled: disabled));
                host.Resources["NfcSurfaceSubtleBrush"] = new SolidColorBrush(subtle);
                Flush(host);
                Assert.Equal(subtle, ColorOf(header.Background));
                host.Resources.Remove("NfcSurfaceSubtleBrush");
            }
        });
    }

    /// <summary>Pins header fills in both modes and retains the container, divider, and interactive fills.</summary>
    [AvaloniaFact]
    public void ExpanderRestDisabledAndInteractionsUseIndependentFills()
    {
        VisitExpander((host, expander, header) =>
        {
            Color container = ColorOf(expander.Background);
            Color border = ColorOf(expander.BorderBrush);
            Color divider = ColorOf(Part<Border>(expander, "ExpanderContent").BorderBrush);
            foreach (ThemeRestFill fill in Fills)
            foreach (DividerState state in DividerTestHost.States.Concat([new("Pressed outside", Pressed: true)]))
            {
                ThemeRestFills.SetRestFill(host.Resources, fill);
                expander.IsEnabled = !state.Disabled;
                DividerTestHost.SetState(header, state);
                Flush(host);
                string? key = !state.Disabled && state.Pressed ? "Nvt.Controls.ExpanderPressedBrush"
                    : !state.Disabled && state.Over ? "NfcSelectionSurfaceBrush" : fill == ThemeRestFill.None ? null : "NfcSurfaceSubtleBrush";
                if (key is null) Assert.Equal(0, ColorOf(header.Background).A);
                else Assert.Equal(ResourceColor(header, key), ColorOf(header.Background));
                Assert.Equal(container, ColorOf(expander.Background));
                Assert.Equal(border, ColorOf(expander.BorderBrush));
                Assert.Equal(divider, ColorOf(Part<Border>(expander, "ExpanderContent").BorderBrush));
                Assert.Equal(state.Focus && !state.Disabled, Part<Border>(header, "ExpanderFocusRing").IsVisible);
            }
        });
    }

    /// <summary>Switches attached choice rows in both directions without replacing templates, indicators, or geometry.</summary>
    /// <param name="radio">Whether the native choice is a radio button.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachedChoicesSwitchBothWaysWithoutReplacingTemplates(bool radio)
    {
        VisitChoice(radio, (host, control) =>
        {
            var template = control.Template;
            Border row = Part<Border>(control, "ChoiceRow");
            Border indicator = Part<Border>(control, "ChoiceIndicator");
            var size = control.Bounds.Size;
            foreach (bool? selected in Selections(radio))
            foreach (bool disabled in Both)
            {
                SetState(control, new("Rest", selected, Disabled: disabled));
                Flush(host);
                Color indicatorFill = ColorOf(indicator.Background), indicatorBorder = ColorOf(indicator.BorderBrush);
                foreach (ThemeRestFill fill in Fills)
                {
                    ThemeRestFills.SetRestFill(host.Resources, fill);
                    Flush(host);
                    Assert.Equal(fill == ThemeRestFill.None, ColorOf(row.Background).A == 0);
                    Assert.Same(template, control.Template);
                    Assert.Same(row, Part<Border>(control, "ChoiceRow"));
                    Assert.Same(indicator, Part<Border>(control, "ChoiceIndicator"));
                    Assert.Equal(size, control.Bounds.Size);
                    Assert.Equal(indicatorFill, ColorOf(indicator.Background));
                    Assert.Equal(indicatorBorder, ColorOf(indicator.BorderBrush));
                }
            }
            foreach (ThemeRestFill fill in Fills)
            foreach (bool dark in Both)
            {
                ThemeRestFills.SetRestFill(host.Resources, fill);
                host.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                Flush(host);
                Assert.Equal(fill == ThemeRestFill.None, ColorOf(row.Background).A == 0);
                Assert.Same(template, control.Template);
                Assert.Same(row, Part<Border>(control, "ChoiceRow"));
            }
        });
    }

    /// <summary>Switches attached headers in both directions while retaining templates, parts, expanded state, and geometry.</summary>
    [AvaloniaFact]
    public void AttachedExpanderSwitchesBothWaysWithoutReplacingTemplates()
    {
        VisitExpander((host, expander, header) =>
        {
            var outerTemplate = expander.Template;
            var headerTemplate = header.Template;
            Border body = Part<Border>(header, "ExpanderHeaderBody");
            var size = header.Bounds.Size;
            foreach (bool disabled in Both)
            foreach (bool expanded in Both)
            {
                expander.IsEnabled = !disabled;
                expander.IsExpanded = expanded;
                foreach (ThemeRestFill fill in Fills)
                {
                    ThemeRestFills.SetRestFill(host.Resources, fill);
                    Flush(host);
                    Assert.Equal(fill == ThemeRestFill.None, ColorOf(body.Background).A == 0);
                    Assert.Same(outerTemplate, expander.Template);
                    Assert.Same(headerTemplate, header.Template);
                    Assert.Same(header, DividerTestHost.Header(expander));
                    Assert.Same(body, Part<Border>(header, "ExpanderHeaderBody"));
                    Assert.Equal(size, header.Bounds.Size);
                    Assert.Equal(expanded, expander.IsExpanded);
                }
            }
            foreach (ThemeRestFill fill in Fills)
            foreach (bool dark in Both)
            {
                ThemeRestFills.SetRestFill(host.Resources, fill);
                host.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                Flush(host);
                Assert.Equal(fill == ThemeRestFill.None, ColorOf(body.Background).A == 0);
                Assert.Same(outerTemplate, expander.Template);
                Assert.Same(headerTemplate, header.Template);
                Assert.Same(body, Part<Border>(header, "ExpanderHeaderBody"));
            }
        });
    }

    /// <summary>Moves and clicks the label, gap, and outer padding of transparent choices in both shapes and themes.</summary>
    [AvaloniaFact]
    public void TransparentChoiceRowsRemainHoverableAndClickable()
    {
        foreach (bool radio in Both)
            VisitChoice(radio, (host, control) =>
            {
                ThemeRestFills.SetRestFill(host.Resources, ThemeRestFill.None);
                foreach (Point point in new[] { new Point(34, 16), new Point(90, 16), new Point(control.Bounds.Width - 2, 2) })
                {
                    host.MouseMove(new Point(host.Width - 1, host.Height - 1));
                    control.IsChecked = false;
                    Flush(host);
                    Assert.Equal(0, ColorOf(Part<Border>(control, "ChoiceRow").Background).A);
                    Point location = control.TranslatePoint(point, host)!.Value;
                    host.MouseMove(location);
                    Flush(host);
                    Assert.Contains(":pointerover", control.Classes);
                    Assert.Equal(ResourceColor(control, "NfcSelectionSurfaceBrush"), ColorOf(Part<Border>(control, "ChoiceRow").Background));
                    host.MouseDown(location, MouseButton.Left);
                    host.MouseUp(location, MouseButton.Left);
                    Flush(host);
                    Assert.True(control.IsChecked);
                    host.MouseMove(new Point(host.Width - 1, host.Height - 1));
                    Flush(host);
                    Assert.Equal(0, ColorOf(Part<Border>(control, "ChoiceRow").Background).A);
                }
            });
    }

    /// <summary>Moves and clicks the label, chevron, and outer padding of transparent headers in both shapes and themes.</summary>
    [AvaloniaFact]
    public void TransparentExpanderHeadersRemainHoverableAndClickable()
    {
        VisitExpander((host, expander, header) =>
        {
            ThemeRestFills.SetRestFill(host.Resources, ThemeRestFill.None);
            foreach (Point point in new[] { new Point(2, 2), new Point(90, 16), new Point(header.Bounds.Width - 12, 16) })
            {
                host.MouseMove(new Point(host.Width - 1, host.Height - 1));
                expander.IsExpanded = false;
                Flush(host);
                Assert.Equal(0, ColorOf(header.Background).A);
                Point location = header.TranslatePoint(point, host)!.Value;
                host.MouseMove(location);
                Flush(host);
                Assert.Contains(":pointerover", header.Classes);
                Assert.Equal(ResourceColor(header, "NfcSelectionSurfaceBrush"), ColorOf(header.Background));
                host.MouseDown(location, MouseButton.Left);
                host.MouseUp(location, MouseButton.Left);
                Flush(host);
                Assert.True(expander.IsExpanded);
            }
        });
    }

    /// <summary>Measures every affected painted pair in both shapes, themes, modes, selections, and interactions.</summary>
    [AvaloniaFact]
    public void PaintedSurfaceContrastPreservesDocumentedLevels()
    {
        var pairs = new List<object>();
        foreach (bool radio in Both)
            VisitChoice(radio, (host, control) =>
            {
                foreach (ThemeRestFill fill in new[] { ThemeRestFill.Soft, ThemeRestFill.None })
                foreach (bool? selected in Selections(radio))
                foreach (ChoiceState state in Interactions.Concat([new("Pressed outside", Pressed: true),
                    new("Disabled interactions", Disabled: true, Hover: true, Pressed: true, Focus: true)]))
                {
                    ThemeRestFills.SetRestFill(host.Resources, fill);
                    SetState(control, state with { Checked = selected });
                    Flush(host);
                    Border row = Part<Border>(control, "ChoiceRow"), indicator = Part<Border>(control, "ChoiceIndicator");
                    var presenter = Part<global::Avalonia.Controls.Presenters.ContentPresenter>(control, "PART_ContentPresenter");
                    TextBlock label = Assert.IsAssignableFrom<TextBlock>(presenter.Child);
                    Assert.Equal(ColorOf(control.Foreground), ColorOf(label.Foreground));
                    string selection = selected is true ? "Checked" : selected is null ? "Indeterminate" : "Unchecked";
                    foreach (string surfaceKey in Surfaces)
                    {
                        Color surface = ResourceColor(control, surfaceKey);
                        Color painted = Composite(ColorOf(row.Background), surface);
                        Measure(control, fill, selection + " / " + state.Name, "Label / row", ColorOf(label.Foreground), painted, state.Disabled ? 3 : 4.5, surfaceKey);
                        Measure(control, fill, selection + " / " + state.Name, "Indicator border / row", ColorOf(indicator.BorderBrush), painted, 3, surfaceKey);
                        Measure(control, fill, selection + " / " + state.Name, "Indicator border / indicator", ColorOf(indicator.BorderBrush), ColorOf(indicator.Background),
                            !state.Disabled && selected is false ? 3 : 0, surfaceKey);
                        Color ring = ColorOf(Part<Border>(control, "ChoiceFocusRing").BorderBrush);
                        Measure(control, fill, selection + " / " + state.Name, "Focus / row", ring, painted, 3, surfaceKey);
                        Measure(control, fill, selection + " / " + state.Name, "Focus / surrounding surface", ring, surface, 3, surfaceKey);
                        if (selected is not false)
                        {
                            IBrush? glyph = radio ? Part<Ellipse>(control, "ChoiceDot").Fill : selected is null
                                ? Part<Rectangle>(control, "ChoiceDash").Fill : Part<global::Avalonia.Controls.Shapes.Path>(control, "ChoiceCheck").Stroke;
                            Measure(control, fill, selection + " / " + state.Name, "Glyph / indicator", ColorOf(glyph), ColorOf(indicator.Background), state.Disabled ? 3 : 4.5, surfaceKey);
                        }
                    }
                }
            });
        VisitExpander((host, expander, header) =>
        {
            foreach (ThemeRestFill fill in new[] { ThemeRestFill.Soft, ThemeRestFill.None })
            foreach (DividerState state in DividerTestHost.States.Concat([new("Pressed outside", Pressed: true)]))
            {
                ThemeRestFills.SetRestFill(host.Resources, fill);
                expander.IsEnabled = !state.Disabled;
                DividerTestHost.SetState(header, state);
                Flush(host);
                Color surface = ColorOf(expander.Background);
                Color painted = Composite(ColorOf(header.Background), surface);
                var presenter = Part<global::Avalonia.Controls.Presenters.ContentPresenter>(header, "ExpanderHeaderPresenter");
                TextBlock label = Assert.IsAssignableFrom<TextBlock>(presenter.Child);
                Assert.Equal(ColorOf(header.Foreground), ColorOf(label.Foreground));
                Measure(expander, fill, state.Name, "Header text / header", ColorOf(label.Foreground), painted, state.Disabled ? 3 : 4.5, "NfcSurfaceBrush");
                Measure(expander, fill, state.Name, "Chevron / header", ColorOf(Part<global::Avalonia.Controls.Shapes.Path>(header, "ExpanderChevron").Stroke), painted, state.Disabled ? 3 : 4.5, "NfcSurfaceBrush");
                Color ring = ColorOf(Part<Border>(header, "ExpanderFocusRing").BorderBrush);
                Measure(expander, fill, state.Name, "Focus / header", ring, painted, 3, "NfcSurfaceBrush");
                Measure(expander, fill, state.Name, "Focus / container", ring, surface, 3, "NfcSurfaceBrush");
                Measure(expander, fill, state.Name, "Divider / container (decorative)", ColorOf(Part<Border>(expander, "ExpanderContent").BorderBrush), surface,
                    host.ActualThemeVariant == ThemeVariant.Dark ? 1.4135 : 1.2325, "NfcSurfaceBrush");
                Measure(expander, fill, state.Name, "Container border / container (decorative)", ColorOf(expander.BorderBrush), surface,
                    host.ActualThemeVariant == ThemeVariant.Dark ? 1.7125 : 1.4845, "NfcSurfaceBrush");
                Measure(expander, fill, state.Name, "Container border / page (decorative)", ColorOf(expander.BorderBrush), ResourceColor(expander, "NfcAppBackgroundBrush"), 1, "NfcAppBackgroundBrush");
            }
        });
        string? destination = Environment.GetEnvironmentVariable("NVT_RESTFILL_CONTRAST_DIR");
        if (!string.IsNullOrWhiteSpace(destination))
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(System.IO.Path.Combine(destination, "contrast.json"), JsonSerializer.Serialize(pairs, JsonOptions));
        }
        output.WriteLine($"Verified {pairs.Count} painted contrast pairs across both themes, shapes, and modes.");

        void Measure(Control owner, ThemeRestFill fill, string state, string pair, Color foreground, Color background, double minimum, string surface)
        {
            bool dark = owner.ActualThemeVariant == ThemeVariant.Dark;
            bool disabled = state.Contains("Disabled", StringComparison.Ordinal);
            double documented = owner is Expander ? pair switch
            {
                "Header text / header" or "Chevron / header" => disabled ? dark ? 5.997 : 4.794 : dark ? 11.866 : 12.525,
                _ => minimum,
            } : pair switch
            {
                "Label / row" => disabled ? dark ? 3.783 : 3.698 : dark ? 11.215 : 14.328,
                "Indicator border / row" => dark ? 3.256 : 3.257,
                "Glyph / indicator" => disabled ? dark ? 3.422 : 4.559 : dark ? 7.794 : 5.879,
                "Focus / row" or "Focus / surrounding surface" => dark ? 6.194 : 5.340,
                _ => minimum,
            };
            // Existing documents round their numeric minima to three decimal places.
            minimum = Math.Max(minimum, documented - 0.0005);
            double ratio = Contrast(foreground, background);
            Assert.True(ratio >= minimum, $"{owner.GetType().Name}/{fill}/{owner.ActualThemeVariant}/{state}/{pair}: {ratio:F6} < {minimum}");
            pairs.Add(new { Control = owner.GetType().Name, Shape = ResourceColorShape(owner), Theme = owner.ActualThemeVariant.ToString(),
                Fill = fill.ToString(), State = state, Pair = pair, Surface = surface, Foreground = foreground.ToString(), Background = background.ToString(), Ratio = ratio, Minimum = minimum });
        }
    }

    private static string ResourceColorShape(Control control) =>
        control.TryFindResource("Nvt.Shape.ControlCornerRadius", control.ActualThemeVariant, out object? radius)
            && radius is CornerRadius corners && corners.TopLeft == 6 ? "Square" : "Pill";

    private static Color Composite(Color brush, Color surface) => brush.A == 0 ? surface : brush;
    private static bool?[] Selections(bool radio) => radio ? [false, true] : [false, true, null];

    private static void VisitChoice(bool radio, Action<Window, ToggleButton> verify)
    {
        foreach (bool dark in Both)
        foreach (ThemeShape shape in new[] { ThemeShape.Pill, ThemeShape.Square })
        {
            ToggleButton control = Sample(radio, new("Rest"));
            Window host = Create(new Border { Padding = new Thickness(24), Child = control }, dark);
            try { ThemeShapes.SetShape(host.Resources, shape); Show(host); verify(host, control); }
            finally { host.Close(); }
        }
    }

    private static void VisitExpander(Action<Window, Expander, ToggleButton> verify)
    {
        foreach (bool dark in Both)
        foreach (ThemeShape shape in new[] { ThemeShape.Pill, ThemeShape.Square })
        {
            var expander = new Expander { Header = "Details", Content = "Synthetic content", Width = 240 };
            Window host = DividerTestHost.Create(new Border { Padding = new Thickness(24), Child = expander }, dark);
            try { ThemeShapes.SetShape(host.Resources, shape); DividerTestHost.Show(host); verify(host, expander, DividerTestHost.Header(expander)); }
            finally { host.Close(); }
        }
    }
}
