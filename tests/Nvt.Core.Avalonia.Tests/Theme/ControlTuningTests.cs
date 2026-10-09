// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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
        var toggle = new ToggleButton { Classes = { "toggleSwitch" }, IsChecked = true };
        var nativeSwitch = new ToggleSwitch { IsChecked = true };
        TemplatedControl[] controls = [choice, expander, list, menu, toggle, nativeSwitch];
        var root = new StackPanel();
        foreach (TemplatedControl control in controls) root.Children.Add(control);
        Window host = RedesignSheets.Create(root, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            ListMenuTestHost.Show(host);
            var templates = controls.Select(control => control.Template).ToArray();
            foreach (TemplatedControl control in new TemplatedControl[] { toggle, nativeSwitch })
                Part<Grid>(control, "PART_MovingKnobs").Transitions = null;
            nativeSwitch.KnobTransitions = null!;
            Assert.Equal(24, Canvas.GetLeft(Part<Grid>(nativeSwitch, "PART_MovingKnobs")));
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
            Assert.Equal(26, Part<Canvas>(nativeSwitch, "PART_SwitchKnob").Width);
            Assert.Equal(24, Canvas.GetLeft(Part<Grid>(nativeSwitch, "PART_MovingKnobs")));
            nativeSwitch.IsChecked = false;
            ListMenuTestHost.Flush(host);
            Assert.Equal(0, Canvas.GetLeft(Part<Grid>(nativeSwitch, "PART_MovingKnobs")));
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

    /// <summary>Pins every consolidated token to the literal from the frozen parent in both themes and shapes.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ConsolidatedGeometryDefaultsKeepTheirFrozenValues(bool dark, bool square)
    {
        // Replaced style literals in Dennis40816/nvt_fw_core at c6c50c1c26b428f1c81d2397515feab3ff0e28fa.
        (string Key, object Literal)[] defaults =
        [
            ("Nvt.Controls.FocusRingMargin", new Thickness(-4)),
            ("Nvt.Controls.InsetFocusRingMargin", new Thickness(2)),
            ("Nvt.Choice.IndicatorBorderThickness", new Thickness(2)),
            ("Nvt.CheckBox.CheckWidth", 12d),
            ("Nvt.CheckBox.CheckHeight", 10d),
            ("Nvt.CheckBox.CheckStrokeThickness", 2d),
            ("Nvt.CheckBox.DashWidth", 10d),
            ("Nvt.CheckBox.DashHeight", 2d),
            ("Nvt.Choice.LabelMargin", new Thickness(8, 0, 0, 0)),
            ("Nvt.Choice.LabelMinHeight", 20d),
            ("Nvt.Expander.HeaderSpacing", 10d),
            ("Nvt.Expander.ChevronSlotSize", 20d),
            ("Nvt.Expander.ChevronGeometry", StreamGeometry.Parse("M0 0 L6 6 L12 0")),
            ("Nvt.Expander.ChevronWidth", 12d),
            ("Nvt.Expander.ChevronHeight", 6d),
            ("Nvt.Expander.ChevronStrokeThickness", 1.5d),
            ("Nvt.List.RowPadding", new Thickness(10, 5)),
            ("Nvt.List.SelectionIndicatorWidth", 2d),
            ("Nvt.List.SelectionIndicatorHeight", 12d),
            ("Nvt.List.SelectionIndicatorMargin", new Thickness(4, 0, 0, 0)),
            ("Nvt.List.CompactPadding", new Thickness(10, 0)),
            ("Nvt.Menu.PopupOffset", -16d),
            ("Nvt.Menu.PopupBorderThickness", new Thickness(1)),
            ("Nvt.Menu.PopupPadding", new Thickness(4)),
            ("Nvt.Menu.ItemPadding", new Thickness(10, 0)),
            ("Nvt.Menu.CheckStrokeThickness", 2d),
            ("Nvt.Menu.GestureMargin", new Thickness(24, 0, 0, 0)),
            ("Nvt.Menu.ChevronWidth", 6d),
            ("Nvt.Menu.ChevronHeight", 10d),
            ("Nvt.Menu.ChevronMargin", new Thickness(16, 0, 0, 0)),
            ("Nvt.Menu.ChevronStrokeThickness", 1.5d),
            ("Nvt.Menu.SubMenuHorizontalOffset", -20d),
            ("Nvt.Menu.SeparatorThickness", 1d),
            ("Nvt.Menu.SeparatorMargin", new Thickness(10, 4)),
            ("Nvt.Toggle.SegmentGroupPadding", new Thickness(4)),
            ("Nvt.Toggle.SegmentSpacing", 2d),
            ("Nvt.Toggle.Height", 40d),
            ("Nvt.Toggle.Padding", new Thickness(20, 0)),
            ("Nvt.Toggle.IconSize", 40d),
            ("Nvt.Toggle.SwitchWidth", 58d),
            ("Nvt.Toggle.SwitchTrackWidth", 52d),
            ("Nvt.Toggle.SwitchTrackHeight", 28d),
            ("Nvt.Toggle.SwitchTrackBorderThickness", new Thickness(2)),
            ("Nvt.Toggle.SwitchKnobTravel", 24d),
            ("Nvt.Toggle.SwitchKnobMargin", new Thickness(6, 0, 0, 0)),
            ("Nvt.Toggle.SwitchKnobSize", 22d),
            ("Nvt.Toggle.SwitchKnobTopOffset", 3d),
            ("Nvt.Toggle.SwitchFocusWidth", 60d),
            ("Nvt.Toggle.SwitchFocusHeight", 36d),
            ("Nvt.Toggle.PressedTransform", TransformOperations.Parse("scale(0.98)")),
            ("Nvt.Toggle.SoftPadding", new Thickness(12, 0)),
        ];
        Window host = RedesignSheets.Create(new Border(), dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            ListMenuTestHost.Show(host);
            foreach ((string key, object literal) in defaults)
            {
                Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out object? actual), key);
                if (literal is StreamGeometry geometry)
                {
                    var resolved = Assert.IsType<StreamGeometry>(actual);
                    Assert.Equal(geometry.Bounds, resolved.Bounds);
                    Assert.Equal(geometry.ContourLength, resolved.ContourLength);
                    foreach (double fraction in new[] { 0d, 0.25, 0.5, 0.75 })
                    {
                        double distance = geometry.ContourLength * fraction;
                        Assert.True(geometry.TryGetPointAtDistance(distance, out Point expected));
                        Assert.True(resolved.TryGetPointAtDistance(distance, out Point point));
                        Assert.Equal(expected, point);
                    }
                }
                else if (literal is TransformOperations transform)
                    Assert.Equal(transform.Value, Assert.IsType<TransformOperations>(actual).Value);
                else
                    Assert.Equal(literal, actual);
            }
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
