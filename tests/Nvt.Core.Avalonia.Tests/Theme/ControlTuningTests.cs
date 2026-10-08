// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using Path = Avalonia.Controls.Shapes.Path;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Checks post-adoption geometry tuning on attached controls without replacing their templates.</summary>
public sealed class ControlTuningTests
{
    private static readonly string[] ToggleRoles = ["toggleSegment", "toggleTab", "toggleIcon", "toggleSoft", "toggleSwitch"];
    /// <summary>Updates exterior and inset focus margins together across all redesigned control families.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SharedFocusMarginsUpdateAttachedControls(bool dark, bool square)
    {
        TemplatedControl[] controls =
        [
            new CheckBox { Content = "Choice" }, new RadioButton { Content = "Choice" },
            new Expander { Header = "Details" }, new GridSplitter { Height = 80 },
            new ListBoxItem { Content = "Item" }, new ComboBoxItem { Content = "Item" },
            new MenuItem { Header = "Action" }, new ToggleSwitch(),
            .. ToggleRoles.Select(role => new ToggleButton { Content = "Option", Classes = { role } }),
        ];
        var root = new StackPanel();
        foreach (TemplatedControl control in controls) root.Children.Add(control);
        Window host = RedesignSheets.Create(root, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            ListMenuTestHost.Show(host);
            var templates = controls.Select(control => control.Template).ToArray();
            Border[] rings = root.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Name?.EndsWith("FocusRing", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(controls.Length, rings.Length);
            foreach (int margin in new[] { 6, 5 })
            {
                host.Resources["Nvt.Controls.FocusRingMargin"] = new Thickness(-margin);
                host.Resources["Nvt.Controls.InsetFocusRingMargin"] = new Thickness(margin - 2);
                ListMenuTestHost.Flush(host);
                foreach (Border ring in rings)
                    Assert.Equal(new Thickness(ring.Name is "ListFocusRing" or "MenuFocusRing" ? margin - 2 : -margin), ring.Margin);
                for (int index = 0; index < controls.Length; index++)
                    Assert.Same(templates[index], controls[index].Template);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Changes glyphs, padding, switch geometry and pressed scale through family tokens at runtime.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FamilyGeometryUpdatesWithoutReplacingTemplates(bool dark, bool square)
    {
        var choice = new CheckBox { Content = "Choice", IsChecked = true };
        var expander = new Expander { Header = "Details" };
        var list = new ListBoxItem { Content = "Item" };
        var menu = new MenuItem { Header = "Action", ItemsSource = new[] { "Child" } };
        var toggle = new ToggleButton { Classes = { "toggleSwitch", "reducedMotion" }, IsChecked = true };
        var nativeSwitch = new ToggleSwitch { Classes = { "reducedMotion" }, IsChecked = true };
        TemplatedControl[] controls = [choice, expander, list, menu, toggle, nativeSwitch];
        var root = new StackPanel();
        foreach (TemplatedControl control in controls) root.Children.Add(control);
        Window host = RedesignSheets.Create(root, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            ListMenuTestHost.Show(host);
            var templates = controls.Select(control => control.Template).ToArray();
            host.Resources["Nvt.CheckBox.CheckWidth"] = 14d;
            host.Resources["Nvt.Choice.IndicatorBorderThickness"] = new Thickness(3);
            host.Resources["Nvt.Expander.ChevronWidth"] = 14d;
            host.Resources["Nvt.List.RowPadding"] = new Thickness(12, 5);
            host.Resources["Nvt.Menu.ChevronHeight"] = 12d;
            host.Resources["Nvt.Toggle.SwitchTrackWidth"] = 56d;
            host.Resources["Nvt.Toggle.SwitchKnobSize"] = 20d;
            host.Resources["Nvt.Toggle.SwitchKnobTravel"] = 26d;
            host.Resources["Nvt.Toggle.PressedTransform"] = TransformOperations.Parse("scale(0.96)");
            ((IPseudoClasses)toggle.Classes).Set(":pressed", true);
            ListMenuTestHost.Flush(host);
            nativeSwitch.IsChecked = false;
            nativeSwitch.IsChecked = true;
            ListMenuTestHost.Flush(host);
            Assert.Equal(14, Part<Path>(choice, "ChoiceCheck").Width);
            Assert.Equal(new Thickness(3), Part<Border>(choice, "ChoiceIndicator").BorderThickness);
            Assert.Equal(14, Part<Path>(expander, "ExpanderChevron").Width);
            Assert.Equal(new Thickness(12, 5), list.Padding);
            Assert.Equal(12, Part<Path>(menu, "PART_ChevronPath").Height);
            foreach (TemplatedControl control in new TemplatedControl[] { toggle, nativeSwitch })
            {
                Assert.Equal(56, Part<Border>(control, "ToggleBody").Width);
                Assert.Equal(20, Part<Grid>(control, "PART_MovingKnobs").Width);
                Assert.Equal(26, Part<Canvas>(control, "PART_SwitchKnob").Width);
                Assert.Equal(26, Canvas.GetLeft(Part<Grid>(control, "PART_MovingKnobs")));
            }
            Assert.Equal(new Matrix(0.96, 0, 0, 0.96, 0, 0), toggle.RenderTransform!.Value);
            for (int index = 0; index < controls.Length; index++)
                Assert.Same(templates[index], controls[index].Template);
        }
        finally { host.Close(); }
    }

    /// <summary>The check mark and dash keep the sizes the styles had before they became tokens.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckBoxGlyphDefaultsKeepTheirFrozenSizes(bool dark)
    {
        var checkedBox = new CheckBox { Content = "Checked", IsChecked = true };
        var mixedBox = new CheckBox { Content = "Mixed", IsThreeState = true, IsChecked = null };
        var root = new StackPanel();
        root.Children.Add(checkedBox);
        root.Children.Add(mixedBox);
        Window host = RedesignSheets.Create(root, dark);
        try
        {
            ListMenuTestHost.Show(host);
            ListMenuTestHost.Flush(host);
            var check = Part<Path>(checkedBox, "ChoiceCheck");
            Assert.Equal(12, check.Width);
            Assert.Equal(10, check.Height);
            Assert.Equal(2, check.StrokeThickness);
            var dash = Part<Rectangle>(mixedBox, "ChoiceDash");
            Assert.Equal(10, dash.Width);
            Assert.Equal(2, dash.Height);
        }
        finally { host.Close(); }
    }

    private static T Part<T>(Control control, string name) where T : Control =>
        Assert.Single(control.GetVisualDescendants().OfType<T>(), part => part.Name == name);
}
