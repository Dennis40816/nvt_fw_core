// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Tests.Dividers;
using Nvt.Core.Avalonia.Tests.ListMenu;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.Dividers.DividerTestHost;

namespace Nvt.Core.Avalonia.Tests.Theme;

/// <summary>Prevents shape and contrast regressions in the redesigned control families.</summary>
public sealed class ControlRedesignTests
{
    /// <summary>Changes the attached row while retaining a square checkbox indicator.</summary>
    [AvaloniaFact]
    public void CheckBoxShapeChangesAtRuntime() => VerifyShape(new CheckBox { Content = "Sample", IsChecked = true }, "ChoiceRow", 999, 6);

    /// <summary>Changes the attached row while retaining a round radio indicator.</summary>
    [AvaloniaFact]
    public void RadioButtonShapeChangesAtRuntime() => VerifyShape(new RadioButton { Content = "Sample", IsChecked = true }, "ChoiceRow", 999, 6);

    /// <summary>Changes both the container and the clickable expander header.</summary>
    [AvaloniaFact]
    public void ExpanderShapeChangesAtRuntime() => VerifyShape(new Expander { Header = "Sample", IsExpanded = true, Content = "Details" }, "ExpanderHeaderBody", 999, 6);

    /// <summary>Changes the rendered progress track ends without replacing the control.</summary>
    [AvaloniaFact]
    public void ProgressBarShapeChangesAtRuntime() => VerifyShape(new ProgressBar { Value = 50, Width = 240 }, "ProgressBarRoot", 3, 1);

    /// <summary>Changes the native switch track while the knob stays circular.</summary>
    [AvaloniaFact]
    public void SwitchShapeChangesAtRuntime() => VerifyShape(new ToggleSwitch { IsChecked = true }, "ToggleBody", 999, 6);

    /// <summary>Changes the visible splitter grip on the existing splitter.</summary>
    [AvaloniaFact]
    public void GridSplitterShapeChangesAtRuntime() => VerifyShape(new GridSplitter { Height = 80 }, "SplitterGrip", 2, 1);

    /// <summary>Changes the attached list row and its inset focus corner.</summary>
    [AvaloniaFact]
    public void ListItemShapeChangesAtRuntime() => VerifyShape(new ListBoxItem { Content = "Sample", IsSelected = true }, "PART_ContentPresenter", 999, 6);

    /// <summary>Changes the attached menu row and its inset focus corner.</summary>
    [AvaloniaFact]
    public void MenuItemShapeChangesAtRuntime() => VerifyShape(new MenuItem { Header = "Sample", IsChecked = true, ToggleType = MenuItemToggleType.CheckBox }, "PART_LayoutRoot", 999, 6);

    /// <summary>Changes dropdown rows through the same shared shape resources.</summary>
    [AvaloniaFact]
    public void ComboBoxItemShapeChangesAtRuntime() => VerifyShape(new ComboBoxItem { Content = "Sample", IsSelected = true }, "PART_ContentPresenter", 999, 6);

    /// <summary>Retains runtime shape updates across all existing toggle roles.</summary>
    [AvaloniaTheory]
    [InlineData("toggleSegment")]
    [InlineData("toggleTab")]
    [InlineData("toggleIcon")]
    [InlineData("toggleSoft")]
    [InlineData("toggleSwitch")]
    public void ToggleRoleShapeChangesAtRuntime(string role) => VerifyShape(new ToggleButton { Content = "Sample", IsChecked = true, Classes = { role } }, "ToggleBody", 999, 6);

    private static void VerifyShape(TemplatedControl control, string partName, double pill, double square)
    {
        var root = new StackPanel { Spacing = 20, Margin = new Thickness(24), Children = { control } };
        Window host = RedesignSheets.Create(root);
        try
        {
            ListMenuTestHost.Show(host);
            var template = control.Template;
            foreach (bool dark in new[] { false, true })
            foreach (ThemeShape shape in new[] { ThemeShape.Pill, ThemeShape.Square, ThemeShape.Pill })
            {
                host.RequestedThemeVariant = dark ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light;
                ThemeShapes.SetShape(host.Resources, shape);
                ListMenuTestHost.Flush(host);
                Control part = Assert.Single(control.GetVisualDescendants().OfType<Control>(), value => value.Name == partName);
                CornerRadius radius = part is Border border ? border.CornerRadius : Assert.IsType<ContentPresenter>(part).CornerRadius;
                Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? pill : square), radius);
                Assert.Same(template, control.Template);
                if (control is CheckBox or RadioButton)
                {
                    Border indicator = Part<Border>(control, "ChoiceIndicator");
                    Assert.Equal(new CornerRadius(control is RadioButton ? 999 : 6), indicator.CornerRadius);
                    Assert.Equal(new Size(20, 20), indicator.Bounds.Size);
                }
                if (control is Expander expander)
                {
                    Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 8 : 6), Part<Border>(control, "ExpanderContainer").CornerRadius);
                    foreach (ExpandDirection direction in new[] { ExpandDirection.Down, ExpandDirection.Up })
                    {
                        expander.ExpandDirection = direction;
                        ListMenuTestHost.Flush(host);
                        ToggleButton header = Part<ToggleButton>(control, "ExpanderHeader");
                        Border content = Part<Border>(control, "ExpanderContent");
                        double gap = direction == ExpandDirection.Down
                            ? content.Bounds.Top - header.Bounds.Bottom : header.Bounds.Top - content.Bounds.Bottom;
                        Assert.Equal(6, gap);
                    }
                }
                if (control is ListBoxItem or MenuItem)
                    Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 999 : 4), Part<Border>(control, control is MenuItem ? "MenuFocusRing" : "ListFocusRing").CornerRadius);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Measures the actual selected row, mark and ring against the previous documented minimum in each theme.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewTonalStatesPreserveDocumentedContrast(bool dark)
    {
        var list = new ListBoxItem { Content = "Sample", IsSelected = true };
        var choice = new CheckBox { Content = "Sample", IsChecked = true };
        var menu = new MenuItem { Header = "Sample", IsChecked = true, ToggleType = MenuItemToggleType.CheckBox };
        Window host = RedesignSheets.Create(new StackPanel { Children = { list, choice, menu } }, dark);
        try
        {
            ListMenuTestHost.Show(host);
            foreach (string state in new[] { "", ":pointerover", ":pressed" })
            {
                foreach (Control control in new Control[] { list, choice, menu })
                {
                    var pseudo = (IPseudoClasses)control.Classes;
                    pseudo.Set(":pointerover", state == ":pointerover");
                    pseudo.Set(":pressed", state == ":pressed");
                }
                ListMenuTestHost.Flush(host);
                foreach (TemplatedControl control in new TemplatedControl[] { list, menu })
                {
                    Color fill = ColorOf(control.Background);
                    Assert.True(Contrast(ColorOf(control.Foreground), fill) >= (dark ? 7.674 : 7.018));
                    Assert.True(Contrast(ResourceColor(control, "Nvt.Controls.FocusBrush"), fill) >= (dark ? 4.930 : 4.006));
                }
                Color row = ColorOf(Part<Border>(choice, "ChoiceRow").Background);
                Assert.True(Contrast(ColorOf(choice.Foreground), row) >= (dark ? 10.501 : 11.866));
                Assert.True(Contrast(ColorOf(Part<Border>(choice, "ChoiceIndicator").BorderBrush), row) >= (dark ? 3.256 : 3.257));
            }
            var bar = new ProgressBar();
            ((StackPanel)host.Content!).Children.Add(bar);
            ListMenuTestHost.Flush(host);
            Assert.True(Contrast(ColorOf(bar.Foreground), ColorOf(bar.Background)) >= (dark ? 3.974 : 3.794));
        }
        finally { host.Close(); }
    }
}
