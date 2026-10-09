// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Theme;
using Xunit;
using static Nvt.Core.Avalonia.Tests.ListMenu.ListMenuTestHost;

namespace Nvt.Core.Avalonia.Tests.ListMenu;

/// <summary>Checks list and menu state colors, accessibility, geometry, and native interaction.</summary>
public sealed class ListMenuStylesTests(ITestOutputHelper output)
{
    /// <summary>Measures list and dropdown contrast for every state in both themes and shapes.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ListAndDropdownStatesUseTokensAndMeetContrast(bool dark, bool square)
    {
        double textMinimum = double.MaxValue, disabledMinimum = double.MaxValue, ringMinimum = double.MaxValue;
        foreach (bool dropdown in new[] { false, true })
        {
            ListBoxItem item = dropdown ? new ComboBoxItem() : new ListBoxItem();
            var label = new TextBlock { Text = "Sample item" };
            item.Content = label;
            item.HorizontalAlignment = HorizontalAlignment.Stretch;
            item.VerticalAlignment = VerticalAlignment.Top;
            Window host = Create(item, dark);
            try
            {
                ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
                Show(host);
                Assert.Equal(32, item.Bounds.Height);
                Assert.Equal(new Thickness(10, 5), item.Padding);
                Assert.Equal(new CornerRadius(square ? 6 : 999), item.CornerRadius);
                Assert.Null(item.FocusAdorner);
                Assert.Equal(1, item.Opacity);
                foreach (ItemState state in States.Concat([
                    new ItemState("Disabled interactions", Hover: true, Pressed: true, Disabled: true, Focus: true),
                    new ItemState("Disabled selected interactions", Selected: true, Hover: true, Pressed: true, Disabled: true, Focus: true)]))
                {
                    SetState(item, state);
                    Flush(host);
                    (string fill, string text) = Expected(state);
                    Assert.Equal(ResourceColor(item, fill), ColorOf(item.Background));
                    Assert.Equal(ResourceColor(item, text), ColorOf(label.Foreground));
                    var presenter = Assert.Single(item.GetVisualDescendants().OfType<ContentPresenter>(), child => child.Name == "PART_ContentPresenter");
                    Assert.Equal(ColorOf(item.Background), ColorOf(presenter.Background));
                    Color background = ColorOf(item.Background);
                    if (background.A == 0) background = ResourceColor(item, "NfcSurfaceBrush");
                    double contrast = Contrast(ColorOf(label.Foreground), background);
                    Assert.True(contrast >= (state.Disabled ? 3 : 4.5), $"{dark}/{square}/{dropdown}/{state.Name}: {contrast:F3}");
                    if (state.Disabled) disabledMinimum = Math.Min(disabledMinimum, contrast);
                    else textMinimum = Math.Min(textMinimum, contrast);
                    if (state.Selected)
                    {
                        Color selectedFill = ColorOf(item.Background);
                        SetState(item, state with { Selected = false });
                        Assert.NotEqual(selectedFill, ColorOf(item.Background));
                        SetState(item, state);
                        Flush(host);
                    }
                    Border indicator = Part(item, "ListSelectionIndicator");
                    Assert.Equal(state.Selected, indicator.IsVisible);
                    Assert.Equal(2, indicator.Width);
                    Assert.Equal(12, indicator.Height);
                    Assert.Equal(new Thickness(4, 0, 0, 0), indicator.Margin);
                    if (state.Selected) Assert.True(Contrast(ColorOf(indicator.Background), background) >= 3);
                    Border ring = Part(item, "ListFocusRing");
                    Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                    Assert.Equal(new Thickness(2), ring.Margin);
                    Assert.Equal(new Thickness(2), ring.BorderThickness);
                    if (ring.IsVisible)
                    {
                        Assert.Equal(item.Bounds.Width - 4, ring.Bounds.Width);
                        Assert.Equal(item.Bounds.Height - 4, ring.Bounds.Height);
                    }
                    double ringContrast = Contrast(ColorOf(ring.BorderBrush), background);
                    Assert.True(ringContrast >= 3);
                    ringMinimum = Math.Min(ringMinimum, ringContrast);
                }
                SetState(item, new ItemState("Rest"));
                Color rest = ColorOf(item.Background);
                SetState(item, new ItemState("Selected", Selected: true));
                Assert.NotEqual(rest, ColorOf(item.Background));
                item.Classes.Add("compact");
                Flush(host);
                Assert.Equal(24, item.Bounds.Height);
                Assert.Equal(new Thickness(10, 0), item.Padding);
            }
            finally { host.Close(); }
        }
        output.WriteLine($"{(dark ? "Dark" : "Light")}/{(square ? "Square" : "Pill")}: text {textMinimum:F3}:1, disabled {disabledMinimum:F3}:1, ring {ringMinimum:F3}:1");
    }

    /// <summary>Checks menu states, checked marks, gesture contrast, icons, separators, and popup tokens.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MenuStatesAndPopupGeometryUseTokens(bool dark, bool square)
    {
        var item = new MenuItem { Header = "_Open sample", Icon = new TextBlock { Text = "+" }, InputGesture = new KeyGesture(global::Avalonia.Input.Key.O, KeyModifiers.Control) };
        var separator = new Separator();
        var legacySeparator = new MenuItem { Header = "-" };
        var parent = new MenuItem { Header = "More", Items = { new MenuItem { Header = "Details" } } };
        var menu = new ContextMenu { Items = { item, separator, legacySeparator, parent }, VerticalAlignment = VerticalAlignment.Top };
        Window host = Create(menu, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            Show(host);
            Border surface = Part(menu, "MenuSurface");
            Assert.Equal(ResourceColor(menu, "NfcSurfaceBrush"), ColorOf(surface.Background));
            Assert.Equal(ResourceColor(menu, "NfcBorderBrush"), ColorOf(surface.BorderBrush));
            Assert.Equal(new Thickness(1), surface.BorderThickness);
            Assert.Equal(new Thickness(4), surface.Padding);
            Assert.Equal(new CornerRadius(square ? 6 : 8), surface.CornerRadius);
            Assert.True(menu.TryFindResource("Nvt.Menu.PopupShadow", menu.ActualThemeVariant, out object? shadow));
            Assert.Equal(Assert.IsType<BoxShadows>(shadow), surface.BoxShadow);
            Assert.Equal(32, item.Bounds.Height);
            Assert.Equal(new Thickness(10, 0), item.Padding);
            Assert.Equal(new CornerRadius(square ? 6 : 999), item.CornerRadius);
            var icon = Assert.Single(item.GetVisualDescendants().OfType<ContentPresenter>(), child => child.Name == "PART_IconPresenter");
            Assert.True(icon.IsVisible);
            Assert.Equal(20, icon.Bounds.Width);
            var gesture = Assert.Single(item.GetVisualDescendants().OfType<TextBlock>(), child => child.Name == "PART_InputGestureText");
            Assert.Contains("O", gesture.Text, StringComparison.Ordinal);
            foreach (ItemState state in States)
            {
                SetState(item, state);
                Flush(host);
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = state.Selected;
                Flush(host);
                Border body = Part(item, "PART_LayoutRoot");
                Assert.Equal(32, body.Bounds.Height);
                string fill = state.Disabled ? "Nvt.List.TransparentBrush"
                    : state.Selected ? state.Pressed ? "Nvt.Controls.SelectedPressedBrush" : state.Hover || state.Focus ? "Nvt.Controls.SelectedPointerOverBrush" : "Nvt.Controls.SelectedBrush"
                    : state.Pressed ? "NfcSecondaryActionPressedBrush"
                    : state.Hover || state.Focus ? "NfcSelectionSurfaceBrush" : "Nvt.List.TransparentBrush";
                Assert.Equal(ResourceColor(item, fill), ColorOf(body.Background));
                Color background = ColorOf(body.Background);
                if (background.A == 0) background = ResourceColor(item, "NfcSurfaceBrush");
                Assert.Equal(ResourceColor(item, state.Disabled ? "NfcTextDisabledBrush" : state.Selected ? "Nvt.Controls.SelectedForegroundBrush" : "NfcTextBrush"), ColorOf(item.Foreground));
                Assert.True(Contrast(ColorOf(item.Foreground), background) >= (state.Disabled ? 3 : 4.5));
                Assert.Equal(ResourceColor(item, state.Disabled ? "NfcTextDisabledBrush" : state.Selected ? "Nvt.Controls.SelectedForegroundBrush" : "NfcTextMutedBrush"), ColorOf(gesture.Foreground));
                Assert.True(Contrast(ColorOf(gesture.Foreground), background) >= (state.Disabled ? 3 : 4.5));
                Border ring = Part(item, "MenuFocusRing");
                Assert.Equal(state.Focus && !state.Disabled, ring.IsVisible);
                if (ring.IsVisible)
                {
                    Assert.Equal(body.Bounds.Width - 4, ring.Bounds.Width);
                    Assert.Equal(body.Bounds.Height - 4, ring.Bounds.Height);
                }
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.True(Contrast(ColorOf(ring.BorderBrush), background) >= 3);
                Assert.Null(item.FocusAdorner);
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = state.Selected;
                Flush(host);
                var check = Assert.Single(item.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>(), child => child.Name == "MenuCheck");
                Assert.Equal(state.Selected, check.IsVisible);
                Assert.Equal(ColorOf(item.Foreground), ColorOf(check.Stroke));
            }
            foreach (Control control in new Control[] { separator, legacySeparator })
            {
                Assert.Equal(1, control.Bounds.Height);
                Assert.Equal(ResourceColor(menu, "NfcDividerBrush"), ColorOf(Part(control, "MenuSeparator").Background));
            }
            parent.IsSubMenuOpen = true;
            Flush(host);
            Assert.True(parent.IsSubMenuOpen);
            var popup = Assert.Single(parent.GetVisualDescendants().OfType<Popup>());
            Border submenu = Assert.IsType<Border>(popup.Child);
            Assert.Equal(new CornerRadius(square ? 6 : 8), submenu.CornerRadius);
            parent.IsSubMenuOpen = false;
        }
        finally { host.Close(); }
    }

    /// <summary>Menu-bar items that toggle keep their check mark, including when disabled. Plain menu-bar items hide the slot.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MenuBarToggleItemsKeepTheirCheckMark(bool dark, bool square)
    {
        var plain = new MenuItem { Header = "Plain" };
        var checkedItem = new MenuItem { Header = "Checked", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        var disabledChecked = new MenuItem { Header = "Disabled checked", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true, IsEnabled = false };
        var bar = new Menu { Items = { plain, checkedItem, disabledChecked } };
        Window host = Create(bar, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            Show(host);
            Assert.False(Part(plain, "MenuCheckSlot").IsVisible);
            foreach (MenuItem item in new[] { checkedItem, disabledChecked })
            {
                Assert.True(Part(item, "MenuCheckSlot").IsVisible);
                var check = Assert.Single(item.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>(), child => child.Name == "MenuCheck");
                Assert.True(check.IsEffectivelyVisible);
            }
        }
        finally { host.Close(); }
    }

    /// <summary>Preserves arrows, Home, End, multi-selection, native pointer press, and inset keyboard focus.</summary>
    [AvaloniaFact]
    public void ListKeyboardSelectionAndPointerFocusRemainNative()
    {
        var before = new Button { Content = "Before" };
        var list = new ListBox { ItemsSource = new[] { "Alpha", "Beta", "Gamma" }, Height = 96 };
        var panel = new StackPanel { Margin = new Thickness(16), Children = { before, list } };
        Window host = Create(panel);
        try
        {
            Show(host);
            Assert.NotNull(list.Template);
            Assert.Equal(0, list.BorderThickness.Left);
            Assert.Equal(0, ColorOf(list.Background).A);
            var first = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            var last = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(2));
            Assert.True(before.Focus());
            Key(host, global::Avalonia.Input.Key.Tab);
            Assert.Same(first, host.FocusManager!.GetFocusedElement());
            Assert.True(Part(first, "ListFocusRing").IsVisible);
            Key(host, global::Avalonia.Input.Key.Down);
            Assert.Equal(1, list.SelectedIndex);
            Key(host, global::Avalonia.Input.Key.End);
            Assert.Equal(2, list.SelectedIndex);
            Key(host, global::Avalonia.Input.Key.Home);
            Assert.Equal(0, list.SelectedIndex);
            Assert.True(first.Focus(NavigationMethod.Pointer));
            Assert.False(Part(first, "ListFocusRing").IsVisible);
            Point center = last.TranslatePoint(new Point(last.Bounds.Width / 2, last.Bounds.Height / 2), host)!.Value;
            host.MouseMove(center);
            host.MouseDown(center, MouseButton.Left);
            Flush(host);
            Assert.Contains(":pressed", last.Classes);
            Assert.Equal(ResourceColor(last, "Nvt.List.SelectedPressedBrush"), ColorOf(last.Background));
            host.MouseUp(center, MouseButton.Left);
            Assert.Equal(2, list.SelectedIndex);
            list.SelectionMode = SelectionMode.Multiple;
            list.Selection.Select(0);
            Flush(host);
            Assert.True(first.IsSelected);
            Assert.True(last.IsSelected);
            host.MouseMove(new Point(1, 1));
            Flush(host);
            Assert.Equal(ColorOf(first.Background), ColorOf(last.Background));
            list.Classes.Add("compact");
            Flush(host);
            Assert.All(list.GetVisualDescendants().OfType<ListBoxItem>(), row => Assert.Equal(24, row.Bounds.Height));
            AutomationProperties.SetName(first, "First sample");
            Assert.Equal("First sample", AutomationProperties.GetName(first));
        }
        finally { host.Close(); }
    }

    /// <summary>Opens a real context menu by keyboard and navigates disabled items and nested commands.</summary>
    [AvaloniaFact]
    public void ContextMenuOpensAndItemsAreReachableByKeyboard()
    {
        bool invoked = false;
        var first = new MenuItem { Header = "_Open" };
        var disabled = new MenuItem { Header = "Unavailable", IsEnabled = false };
        var nested = new MenuItem { Header = "_Details" };
        nested.Click += (_, _) => invoked = true;
        var parent = new MenuItem { Header = "_More", Items = { nested } };
        var menu = new ContextMenu { Items = { first, disabled, new Separator(), parent } };
        var target = new Button { Content = "Sample target", ContextMenu = menu };
        Window host = Create(target);
        try
        {
            Show(host);
            target.Focus(NavigationMethod.Tab);
            menu.Open(target);
            Flush(host);
            Assert.True(menu.IsOpen);
            Key(host, global::Avalonia.Input.Key.Down);
            Assert.True(first.IsFocused);
            Assert.True(menu.Focus());
            Assert.True(first.Focus(NavigationMethod.Tab));
            Flush(host);
            Assert.True(Part(first, "MenuFocusRing").IsVisible, $"Tab menu classes: {string.Join(",", first.Classes)}");
            Assert.True(menu.Focus());
            Assert.True(first.Focus(NavigationMethod.Pointer));
            Assert.False(Part(first, "MenuFocusRing").IsVisible);
            Key(host, global::Avalonia.Input.Key.Down);
            Assert.True(parent.IsFocused);
            Key(host, global::Avalonia.Input.Key.Right);
            Assert.True(parent.IsSubMenuOpen);
            Assert.True(nested.IsFocused);
            Key(host, global::Avalonia.Input.Key.Enter);
            Assert.True(invoked);
            Assert.False(menu.IsOpen);
            menu.Open(target);
            Key(host, global::Avalonia.Input.Key.Escape);
            Assert.False(menu.IsOpen);
        }
        finally { menu.Close(); host.Close(); }
    }

    /// <summary>Leaves the ComboBox box intact while applying the list theme to real popup containers.</summary>
    [AvaloniaFact]
    public void ComboBoxPopupMatchesListAndPreservesBoxTemplate()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Alpha", "Beta", "Gamma" }, SelectedIndex = 0, Width = 220 };
        Window host = Create(combo, core: false);
        try
        {
            Show(host);
            var template = combo.Template;
            var appearance = (combo.Background, combo.BorderBrush, combo.BorderThickness, combo.CornerRadius, combo.Padding);
            Include(host, "ListStyles");
            Flush(host);
            Assert.Same(template, combo.Template);
            Assert.Equal(appearance, (combo.Background, combo.BorderBrush, combo.BorderThickness, combo.CornerRadius, combo.Padding));
            combo.Focus(NavigationMethod.Tab);
            Key(host, global::Avalonia.Input.Key.Down, RawInputModifiers.Alt);
            Assert.True(combo.IsDropDownOpen);
            var item = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(0));
            Freeze(item);
            SetState(item, new ItemState("Selected", Selected: true));
            Flush(host);
            Assert.Equal(32, item.Bounds.Height);
            Assert.Equal(ResourceColor(item, "Nvt.List.SelectedBrush"), ColorOf(item.Background));
            Assert.Equal(ResourceColor(item, "Nvt.List.SelectedLabelBrush"), ColorOf(item.Foreground));
            Key(host, global::Avalonia.Input.Key.Down);
            Key(host, global::Avalonia.Input.Key.Enter);
            Assert.False(combo.IsDropDownOpen);
            Assert.Equal(1, combo.SelectedIndex);
            Assert.Equal("Beta", combo.SelectedItem);
        }
        finally { host.Close(); }
    }

    /// <summary>Updates attached rows, menu surfaces, and keyboard rings when shape and palette roots change.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeShapeAndDictionaryReplacementUpdateAttachedControls(bool dark)
    {
        var list = new ListBoxItem { Content = "Selected", IsSelected = true };
        var combo = new ComboBoxItem { Content = "Selected", IsSelected = true };
        var item = new MenuItem { Header = "Menu command" };
        var menu = new ContextMenu { Items = { item } };
        Window host = Create(new StackPanel { Children = { list, combo, menu } }, dark);
        try
        {
            Show(host);
            foreach (ThemeShape shape in new[] { ThemeShape.Square, ThemeShape.Pill, ThemeShape.Square })
            {
                ThemeShapes.SetShape(host.Resources, shape);
                Flush(host);
                Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 999 : 6), list.CornerRadius);
                Assert.Equal(list.CornerRadius, combo.CornerRadius);
                Assert.Equal(list.CornerRadius, item.CornerRadius);
                Assert.Equal(new CornerRadius(shape == ThemeShape.Pill ? 8 : 6), Part(menu, "MenuSurface").CornerRadius);
            }
            var palette = new ResourceDictionary();
            palette["Nvt.List.SelectedBrush"] = new SolidColorBrush(Colors.Navy);
            palette["Nvt.List.SelectedLabelBrush"] = new SolidColorBrush(Colors.White);
            host.Resources.MergedDictionaries.Add(palette);
            SetState(list, new ItemState("Selected", Selected: true));
            SetState(combo, new ItemState("Selected", Selected: true));
            Flush(host);
            Assert.Equal(Colors.Navy, ColorOf(list.Background));
            Assert.Equal(Colors.White, ColorOf(combo.Foreground));
            var next = new ResourceDictionary();
            next["Nvt.List.SelectedBrush"] = new SolidColorBrush(Colors.Maroon);
            next["Nvt.List.SelectedLabelBrush"] = new SolidColorBrush(Colors.Yellow);
            next["Nvt.Menu.PopupMaximumCornerRadius"] = new CornerRadius(4);
            host.Resources.MergedDictionaries[1] = next;
            Flush(host);
            Assert.Equal(Colors.Maroon, ColorOf(combo.Background));
            Assert.Equal(Colors.Yellow, ColorOf(list.Foreground));
            Assert.Equal(new CornerRadius(4), Part(menu, "MenuSurface").CornerRadius);
            host.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
            Flush(host);
            Assert.Equal(ResourceColor(menu, "NfcSurfaceBrush"), ColorOf(Part(menu, "MenuSurface").Background));
        }
        finally { host.Close(); }
    }

    /// <summary>Checks menu bar states, popup placement, access keys, and native arrow navigation in every theme and shape.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MenuBarStatesAndAccessKeysRemainNative(bool dark, bool square)
    {
        var file = new MenuItem { Header = "_File", Items = { new MenuItem { Header = "_Open" } } };
        var edit = new MenuItem { Header = "_Edit", Items = { new MenuItem { Header = "_Copy" } } };
        var bar = new Menu { Items = { file, edit }, VerticalAlignment = VerticalAlignment.Top };
        Window host = Create(bar, dark);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            Show(host);
            Assert.Equal(32, bar.Bounds.Height);
            Assert.Equal(32, file.Bounds.Height);
            foreach (ItemState state in States)
            {
                SetState(file, state);
                Flush(host);
                string fill = state.Disabled ? "Nvt.List.TransparentBrush" : state.Pressed ? "NfcSecondaryActionPressedBrush"
                    : state.Hover || state.Focus ? "NfcSelectionSurfaceBrush" : "Nvt.List.TransparentBrush";
                Assert.Equal(ResourceColor(file, fill), ColorOf(Part(file, "PART_LayoutRoot").Background));
                Assert.Equal(state.Focus && !state.Disabled, Part(file, "MenuFocusRing").IsVisible);
            }
            SetState(file, new ItemState("Rest"));
            Assert.True(file.Focus(NavigationMethod.Tab));
            host.KeyPress(global::Avalonia.Input.Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            host.KeyPress(global::Avalonia.Input.Key.F, RawInputModifiers.Alt, PhysicalKey.F, "f");
            host.KeyRelease(global::Avalonia.Input.Key.F, RawInputModifiers.Alt, PhysicalKey.F, "f");
            host.KeyRelease(global::Avalonia.Input.Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            Flush(host);
            Assert.True(file.IsSubMenuOpen);
            var popup = Assert.Single(file.GetVisualDescendants().OfType<Popup>());
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
            Key(host, global::Avalonia.Input.Key.Escape);
            Assert.False(file.IsSubMenuOpen);
            Assert.True(file.Focus(NavigationMethod.Tab));
            Key(host, global::Avalonia.Input.Key.Right);
            Assert.True(edit.IsFocused);
            Key(host, global::Avalonia.Input.Key.Down);
            Assert.True(edit.IsSubMenuOpen);
            Key(host, global::Avalonia.Input.Key.Escape);
            Assert.False(edit.IsSubMenuOpen);
        }
        finally { host.Close(); }
    }

    /// <summary>Keeps the keyboard ring inside virtualized rows after scrolling, resizing, and changing render scale.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FocusGeometrySurvivesScrollingResizingAndDpi(bool dark, bool square)
    {
        var list = new ListBox { ItemsSource = Enumerable.Range(1, 30).Select(index => $"Sample {index}").ToArray(), Height = 96,
            VerticalAlignment = VerticalAlignment.Top };
        var before = new Button { Content = "Before" };
        Window host = Create(new StackPanel { Children = { before, list } }, dark, height: 180);
        try
        {
            ThemeShapes.SetShape(host.Resources, square ? ThemeShape.Square : ThemeShape.Pill);
            Show(host);
            foreach (double width in new[] { 320d, 720d })
            foreach (double scaling in new[] { 1d, 1.25, 2 })
            {
                host.Width = width;
                host.SetRenderScaling(scaling);
                Flush(host);
                var first = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
                Assert.True(first.Focus(NavigationMethod.Tab));
                Key(host, global::Avalonia.Input.Key.End);
                Assert.Equal(29, list.SelectedIndex);
                var last = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(29));
                Assert.True(before.Focus());
                Assert.True(last.Focus(NavigationMethod.Tab));
                Flush(host);
                Border ring = Part(last, "ListFocusRing");
                Assert.True(ring.IsVisible);
                Assert.Equal(32, last.Bounds.Height);
                Assert.Equal(last.Bounds.Width - 4, ring.Bounds.Width);
                Assert.Equal(28, ring.Bounds.Height);
                Point point = ring.TranslatePoint(default, list)!.Value;
                Assert.True(point.Y >= 0 && point.Y + ring.Bounds.Height <= list.Bounds.Height);
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal((int)(width * scaling), frame.PixelSize.Width);
                Key(host, global::Avalonia.Input.Key.Home);
                Assert.Equal(0, list.SelectedIndex);
            }
        }
        finally { host.Close(); }
    }

}
