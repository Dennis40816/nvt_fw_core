// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.Theme;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Choice.ChoiceTestHost;

namespace Nvt.Core.Avalonia.Tests.Choice;

/// <summary>Checks token ownership, live resource updates, reduced motion, and style isolation.</summary>
public sealed class ChoiceContractTests
{
    /// <summary>Rejects colors and corner literals throughout style setters and templates.</summary>
    [Fact]
    public void StylesContainOnlyTokenColorsAndCorners()
    {
        XElement source = ThemeContractTests.ReadExtracted("ChoiceStyles").Root!;
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
            Assert.Matches(@"^\s*(?:CheckBox|RadioButton|\.reducedMotion (?:CheckBox|RadioButton))(?:[\s:.]|$)", selector);
        using Stream stream = typeof(ChoiceContractTests).Assembly.GetManifestResourceStream("Nvt.Core.Avalonia.Tests.Theme.Source.ThemeTokens.xml")!;
        Assert.Single(XDocument.Load(stream).Descendants(), element => element.Attribute("Source")?.Value == "avares://Nvt.Core.Avalonia/Theme/ChoiceTokens.axaml");

        static void AssertResource(string value) => Assert.True(
            value.StartsWith("{DynamicResource ", StringComparison.Ordinal) || value.StartsWith("{TemplateBinding ", StringComparison.Ordinal), value);
    }

    /// <summary>Every resource resolves in both themes and each brush transition honors the shared reducedMotion class.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourcesResolveAndReducedMotionDisablesAllBrushTransitions(bool dark)
    {
        foreach (bool radio in new[] { false, true })
        {
            ToggleButton control = Sample(radio, new("Rest"));
            Window host = Create(control, dark, snapshot: false);
            try
            {
                Show(host);
                foreach (Match match in Regex.Matches(ThemeContractTests.ReadExtracted("ChoiceStyles").ToString(), @"\{DynamicResource ([^}]+)\}", RegexOptions.CultureInvariant))
                    Assert.True(control.TryFindResource(match.Groups[1].Value, control.ActualThemeVariant, out _), match.Value);
                AssertMotion();
                control.Classes.Add("reducedMotion");
                Assert.Null(control.Transitions);
                control.Classes.Remove("reducedMotion");
                AssertMotion();
                host.Classes.Add("reducedMotion");
                Assert.Null(control.Transitions);
                host.Classes.Remove("reducedMotion");
                AssertMotion();
            }
            finally { host.Close(); }

            void AssertMotion()
            {
                Assert.NotNull(control.Transitions);
                Assert.Equal(3, control.Transitions.Count);
                var properties = control.Transitions.Select(transition => Assert.IsType<BrushTransition>(transition).Property).ToArray();
                Assert.Contains(TemplatedControl.BackgroundProperty, properties);
                Assert.Contains(TemplatedControl.BorderBrushProperty, properties);
                Assert.Contains(TemplatedControl.ForegroundProperty, properties);
                Assert.All(control.Transitions, transition => Assert.Equal(TimeSpan.FromMilliseconds(150), ((TransitionBase)transition).Duration));
            }
        }
    }

    /// <summary>Replacing a palette updates all glyphs and selected fills while runtime shapes leave radios round.</summary>
    [AvaloniaFact]
    public void DictionaryAndShapeReplacementUpdateAttachedChoices()
    {
        ToggleButton[] controls = [Sample(false, new("Checked", true)), Sample(false, new("Mixed", null)), Sample(true, new("Checked", true))];
        var panel = new StackPanel { Spacing = 16 };
        foreach (ToggleButton control in controls) panel.Children.Add(control);
        Window host = Create(panel);
        try
        {
            Show(host);
            Assert.All(controls, control => Assert.Equal(new CornerRadius(control is RadioButton ? 999 : 6), control.CornerRadius));
            var templates = controls.Select(control => control.Template).ToArray();
            ResourceDictionary palette = Palette(Colors.Purple, Colors.Yellow);
            host.Resources.MergedDictionaries.Add(palette);
            Verify(Colors.Purple, Colors.Yellow);
            host.Resources.MergedDictionaries[host.Resources.MergedDictionaries.IndexOf(palette)] = Palette(Colors.Navy, Colors.White);
            Verify(Colors.Navy, Colors.White);
            foreach (bool dark in new[] { true, false })
            foreach (ThemeShape shape in new[] { ThemeShape.Square, ThemeShape.Pill, ThemeShape.Square })
            {
                host.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                ThemeShapes.SetShape(host.Resources, shape);
                Flush(host);
                for (int index = 0; index < controls.Length; index++)
                {
                    ToggleButton control = controls[index];
                    Assert.Same(templates[index], control.Template);
                    Assert.Equal(new CornerRadius(control is RadioButton ? 999 : 6), Part<Border>(control, "ChoiceIndicator").CornerRadius);
                    Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 999 : 10), Part<Border>(control, "ChoiceFocusRing").CornerRadius);
                }
            }
        }
        finally { host.Close(); }

        void Verify(Color fill, Color mark)
        {
            foreach (ToggleButton control in controls)
            foreach (ChoiceState state in Interactions.Where(state => !state.Disabled))
            {
                SetState(control, state with { Checked = control.Tag is ChoiceState original ? original.Checked : true });
                Flush(host);
                Assert.Equal(fill, ColorOf(Part<Border>(control, "ChoiceIndicator").Background));
                Assert.Equal(mark, ColorOf(Part<Ellipse>(control, "ChoiceDot").Fill));
                Assert.Equal(mark, ColorOf(Part<Rectangle>(control, "ChoiceDash").Fill));
                Assert.Equal(mark, ColorOf(Part<global::Avalonia.Controls.Shapes.Path>(control, "ChoiceCheck").Stroke));
                Assert.Equal(ResourceColor(control, "Nvt.Controls.ChoiceForegroundBrush"), ColorOf(control.Foreground));
            }
        }

        static ResourceDictionary Palette(Color fill, Color mark) => new()
        {
            ["Nvt.Toggle.SelectedBrush"] = new SolidColorBrush(fill),
            ["Nvt.Toggle.SelectedPointerOverBrush"] = new SolidColorBrush(fill),
            ["Nvt.Toggle.SelectedPressedBrush"] = new SolidColorBrush(fill),
            ["Nvt.Toggle.SelectedLabelBrush"] = new SolidColorBrush(mark),
        };
    }

    /// <summary>Loading choice styles preserves the templates and values of unrelated buttons and toggles.</summary>
    [AvaloniaFact]
    public void LoadingChoiceStylesLeavesOtherControlFamiliesUnchanged()
    {
        var button = new Button { Content = "Apply" };
        var toggle = new ToggleButton { Content = "Pin", IsChecked = true };
        var nativeSwitch = new ToggleSwitch { IsChecked = true };
        Window host = Create(new StackPanel { Children = { button, toggle, nativeSwitch } }, styles: false);
        try
        {
            Toggle.ToggleTestHost.Include(host, "avares://Nvt.Core.Avalonia/Theme/ToggleStyles.axaml");
            Show(host);
            Button[] controls = [button, toggle, nativeSwitch];
            var templates = controls.Select(control => control.Template).ToArray();
            var values = controls.Select(control => (control.Background, control.Foreground, control.CornerRadius, control.FocusAdorner)).ToArray();
            Include(host);
            Flush(host);
            for (int index = 0; index < controls.Length; index++)
            {
                Button control = controls[index];
                Assert.Same(templates[index], control.Template);
                Assert.Equal(values[index], (control.Background, control.Foreground, control.CornerRadius, control.FocusAdorner));
            }
        }
        finally { host.Close(); }
    }
}
