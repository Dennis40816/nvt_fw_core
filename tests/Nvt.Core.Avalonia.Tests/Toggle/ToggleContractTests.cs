// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.Theme;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Toggle.ToggleTestHost;

namespace Nvt.Core.Avalonia.Tests.Toggle;

/// <summary>Guards token ownership, runtime replacement, motion, and role isolation.</summary>
public sealed class ToggleContractTests
{
    /// <summary>Rejects literal colors and corners in setters, templates, and nested style values.</summary>
    [Fact]
    public void StyleColorsAndCornersUseResourcesOrTemplateBindings()
    {
        XElement source = ThemeContractTests.ReadExtracted("ToggleStyles").Root!;
        string[] properties = ["Background", "Foreground", "BorderBrush", "Fill", "Stroke", "Color", "CornerRadius", "BoxShadow"];
        foreach (XElement element in source.Descendants())
        {
            Assert.False(element.Name.LocalName is "SolidColorBrush" or "Color" or "CornerRadius" or "BoxShadows", element.ToString());
            foreach (XAttribute attribute in element.Attributes().Where(attribute => properties.Contains(attribute.Name.LocalName.Split('.').Last())))
                AssertResource(attribute.Value);
            if (element.Name.LocalName == "Setter" && properties.Contains(element.Attribute("Property")?.Value.Split('.').Last()))
                AssertResource(element.Attribute("Value")?.Value ?? element.Value);
        }
        foreach (XElement style in source.Descendants(ThemeContractTests.Presentation + "Style"))
        foreach (string selector in style.Attribute("Selector")!.Value.Split(','))
            Assert.Matches(@"^\s*(?:ToggleButton\.(?:toggleSegment|toggleTab|toggleIcon|toggleSwitch)|ToggleSwitch|Border\.toggleSegmentGroup)(?:[\s:.>]|$)", selector);

        static void AssertResource(string value) => Assert.True(
            value.StartsWith("{DynamicResource ", StringComparison.Ordinal) ||
            value.StartsWith("{TemplateBinding ", StringComparison.Ordinal), value);
    }

    /// <summary>Resolves the entire toggle resource contract and pins 150 ms brush and knob motion.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourcesResolveAndMotionUsesTransitions(bool dark)
    {
        foreach (string role in Roles)
        {
            ToggleButton button = Sample(role, new ToggleState("Rest"));
            Window host = Create(button, dark, snapshot: false);
            try
            {
                Show(host);
                string source = ThemeContractTests.ReadExtracted("ToggleStyles").ToString();
                foreach (Match match in Regex.Matches(source, @"\{DynamicResource ([^}]+)\}", RegexOptions.CultureInvariant))
                    Assert.True(button.TryFindResource(match.Groups[1].Value, button.ActualThemeVariant, out _), match.Value);
                Assert.NotNull(button.Transitions);
                Assert.Contains(button.Transitions, transition => transition is BrushTransition brush && brush.Property == TemplatedControl.BackgroundProperty);
                Assert.Contains(button.Transitions, transition => transition is BrushTransition brush && brush.Property == TemplatedControl.ForegroundProperty);
                Assert.All(button.Transitions, transition => Assert.Equal(TimeSpan.FromMilliseconds(150), ((TransitionBase)transition).Duration));
                if (!ToggleStylesTests.IsSwitch(role)) continue;
                Grid moving = Assert.Single(button.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "PART_MovingKnobs");
                Assert.NotNull(moving.Transitions);
                DoubleTransition motion = Assert.IsType<DoubleTransition>(Assert.Single(moving.Transitions));
                Assert.Equal(Canvas.LeftProperty, motion.Property);
                Assert.Equal(TimeSpan.FromMilliseconds(150), motion.Duration);
                if (button is ToggleSwitch native)
                    Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<DoubleTransition>(Assert.Single(native.KnobTransitions!)).Duration);
            }
            finally { host.Close(); }
        }
    }

    /// <summary>Replaces one dictionary to update all selected fills, labels, knobs, and focus corners.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DictionaryReplacementUpdatesAttachedRoles(bool dark)
    {
        ToggleButton[] buttons = [.. Roles.Select(role => Sample(role, new ToggleState("Checked", Checked: true, Focus: true)))];
        var row = new StackPanel();
        foreach (ToggleButton button in buttons) row.Children.Add(button);
        Window host = Create(row, dark, height: 400);
        try
        {
            Show(host);
            Restore(row);
            ResourceDictionary first = Palette(Colors.Purple, Colors.Red, Colors.White, 8);
            host.Resources.MergedDictionaries.Add(first);
            Verify(Colors.Purple, Colors.Red, Colors.White, 8);
            host.Resources.MergedDictionaries[0] = Palette(Colors.Navy, Colors.Maroon, Colors.Yellow, 12);
            Verify(Colors.Navy, Colors.Maroon, Colors.Yellow, 12);
            host.Resources.MergedDictionaries.Clear();
            foreach (bool nextDark in new[] { !dark, dark })
            {
                host.RequestedThemeVariant = nextDark ? ThemeVariant.Dark : ThemeVariant.Light;
                Flush(host);
                foreach (ToggleButton button in buttons)
                {
                    button.Classes.Remove("danger");
                    SetState(button, new ToggleState("Checked", Checked: true));
                    Flush(host);
                    Color expected = Color.Parse(ToggleStylesTests.IsSwitch(button is ToggleSwitch ? "nativeSwitch" : button.Classes.First())
                        ? "#2563EB" : nextDark ? "#1148BE" : "#1557E9");
                    Assert.Equal(expected, ColorOf(button.Background));
                }
            }
        }
        finally { host.Close(); }

        void Verify(Color accent, Color danger, Color label, double radius)
        {
            foreach (ToggleButton button in buttons)
            foreach (bool isDanger in new[] { false, true })
            foreach (ToggleState state in new[]
            {
                new ToggleState("Checked", Checked: true, Focus: true),
                new ToggleState("Checked hover", Checked: true, Hover: true),
                new ToggleState("Checked pressed", Checked: true, Pressed: true),
            })
            {
                button.Classes.Set("danger", isDanger);
                SetState(button, state);
                Flush(host);
                Assert.Equal(isDanger ? danger : accent, ColorOf(Part(button, "ToggleBody").Background));
                bool isSwitch = button is ToggleSwitch || button.Classes.Contains("toggleSwitch");
                if (isSwitch)
                    Assert.Equal(label, ColorOf(Assert.Single(button.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>()).Fill));
                else
                {
                    Assert.Equal(label, ColorOf(button.Foreground));
                    Assert.Equal(new CornerRadius(radius), button.CornerRadius);
                    Assert.Equal(new CornerRadius(radius + 4), Part(button, "ToggleFocusRing").CornerRadius);
                }
            }
        }

        static ResourceDictionary Palette(Color accent, Color danger, Color label, double radius)
        {
            var resources = new ResourceDictionary();
            foreach (string key in new[] { "SelectedBrush", "SelectedPointerOverBrush", "SelectedPressedBrush", "SwitchOnBrush", "SwitchPointerOverBrush", "SwitchPressedBrush" })
                resources["Nvt.Toggle." + key] = new SolidColorBrush(accent);
            foreach (string key in new[] { "DangerFillBrush", "DangerFillPointerOverBrush", "DangerFillPressedBrush" })
                resources["Nvt.Toggle." + key] = new SolidColorBrush(danger);
            resources["Nvt.Toggle.SelectedLabelBrush"] = new SolidColorBrush(label);
            resources["Nvt.Toggle.KnobBrush"] = new SolidColorBrush(label);
            resources["Nvt.Shape.ControlCornerRadius"] = new CornerRadius(radius);
            resources["Nvt.Shape.GroupCornerRadius"] = new CornerRadius(radius);
            resources["Nvt.Shape.FocusCornerRadius"] = new CornerRadius(radius + 4);
            return resources;
        }
    }

    /// <summary>Leaves plain toggles and existing button roles unchanged when toggle styles load.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedControlsKeepTheirTemplatesAndStates(bool dark)
    {
        var plain = Button("unrelated");
        var action = Button("actionPrimary");
        var command = new global::Avalonia.Controls.Button { Content = "Apply", Classes = { "actionPrimary" } };
        Window host = Create(new StackPanel { Children = { plain, action, command } }, dark, styles: false, snapshot: false);
        try
        {
            Show(host);
            plain.Transitions = action.Transitions = command.Transitions = null;
            var templates = new[] { plain.Template, action.Template, command.Template };
            var commandState = (command.Background, command.Foreground, command.CornerRadius, command.FocusAdorner);
            var snapshots = States.Select(state =>
            {
                SetState(plain, state);
                SetState(action, state);
                return (plain.Background, plain.Foreground, plain.CornerRadius, plain.FocusAdorner,
                    action.Background, action.Foreground, action.CornerRadius, action.FocusAdorner);
            }).ToArray();
            Include(host, "avares://Nvt.Core.Avalonia/Theme/ToggleStyles.axaml");
            Flush(host);
            for (int index = 0; index < States.Length; index++)
            {
                SetState(plain, States[index]);
                SetState(action, States[index]);
                Flush(host);
                Assert.Equal(snapshots[index], (plain.Background, plain.Foreground, plain.CornerRadius, plain.FocusAdorner,
                    action.Background, action.Foreground, action.CornerRadius, action.FocusAdorner));
                Assert.Equal(commandState, (command.Background, command.Foreground, command.CornerRadius, command.FocusAdorner));
                Assert.Same(templates[0], plain.Template);
                Assert.Same(templates[1], action.Template);
                Assert.Same(templates[2], command.Template);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Supports application resources, preserves unrelated dictionaries, and rejects undefined shapes.</summary>
    [AvaloniaFact]
    public void ShapeSettingSupportsApplicationResourcesAndValidatesArguments()
    {
        Application app = Application.Current!;
        var previous = app.Resources.MergedDictionaries.ToArray();
        var button = Button("toggleIcon");
        Window host = Create(button, false);
        try
        {
            Show(host);
            foreach (ThemeShape shape in new[] { ThemeShape.Square, ThemeShape.Pill, ThemeShape.Square })
            {
                ThemeShapes.SetShape(app.Resources, shape);
                Flush(host);
                Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 999 : 6), button.CornerRadius);
                Assert.Equal(previous.Length + 1, app.Resources.MergedDictionaries.Count);
                Assert.Same(previous[0], app.Resources.MergedDictionaries[0]);
            }
            Assert.Throws<ArgumentNullException>(() => ThemeShapes.SetShape(null!, ThemeShape.Pill));
            Assert.Throws<ArgumentOutOfRangeException>(() => ThemeShapes.SetShape(app.Resources, (ThemeShape)42));
        }
        finally
        {
            host.Close();
            app.Resources.MergedDictionaries.Clear();
            foreach (var dictionary in previous) app.Resources.MergedDictionaries.Add(dictionary);
        }
    }
}
