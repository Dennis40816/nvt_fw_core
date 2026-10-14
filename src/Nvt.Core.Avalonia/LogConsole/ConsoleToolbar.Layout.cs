// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.Icons;
using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// Search, zoom and jump handlers, icon centering and the one visibility rule.
public sealed partial class ConsoleToolbar
{
    private void SearchChanged(object? sender, TextChangedEventArgs args)
    {
        if (sender is TextBox search && Controller is { } controller && controller.IsActive
            && search.Text != controller.Filter.SearchText)
            controller.SetSearchText(search.Text);
    }
    // The button hides when the text is gone, so the focus goes back to the field it cleared.
    private void ClearSearchClick(object? sender, RoutedEventArgs args) => this.FindControl<TextBox>("SearchBox")!.Focus();
    private void ZoomOutClick(object? sender, RoutedEventArgs args) => List?.ZoomOut();
    private void ZoomInClick(object? sender, RoutedEventArgs args) => List?.ZoomIn();
    private void ZoomResetClick(object? sender, RoutedEventArgs args) => List?.ResetZoom();
    private void JumpClick(object? sender, RoutedEventArgs args) => List?.JumpToLatest();
    private void ScalingChanged(object? sender, EventArgs args) => CenterIcons();
    private void CenterIcons()
    {
        foreach (var level in Enum.GetValues<LogLevel>())
        {
            var icon = this.FindControl<TextBlock>("Level" + level + "Icon")!;
            TextOptions.SetBaselinePixelAlignment(icon, BaselinePixelAlignment.Unaligned);
            icon.RenderTransform = new TranslateTransform(0, ConsoleGlyphs.VerticalOffset(level, _iconHost?.RenderScaling ?? 1));
        }
    }

    private void ShowLevelParts(bool names, bool counts)
    {
        foreach (var level in Enum.GetValues<LogLevel>())
        {
            this.FindControl<TextBlock>("Level" + level + "Name")!.IsVisible = names;
            this.FindControl<TextBlock>("Level" + level + "Count")!.IsVisible = counts;
        }
    }

    // A visibility change marks only the control itself, so a plain Measure would answer from the cache of the previous state.
    private static double MeasuredWidth(Layoutable root)
    {
        foreach (var item in root.GetVisualDescendants().OfType<Layoutable>().Prepend(root)) item.InvalidateMeasure();
        root.Measure(Size.Infinity);
        return root.DesiredSize.Width;
    }

    // Measures the level filter and Jump to latest with the given parts shown, so the answer does not depend on the current state.
    private bool LevelPartsFit(bool names, bool counts)
    {
        ShowLevelParts(names, counts);
        var filter = this.FindControl<StackPanel>("LevelFilter")!;
        var right = this.FindControl<StackPanel>("Right")!;
        return MeasuredWidth(filter) + MeasuredWidth(right) <= this.FindControl<Grid>("Row")!.Bounds.Width;
    }

    private void Show(ConsoleToolbarParts part, Control control, bool widthAllows)
        => control.IsVisible = widthAllows && (_hostParts & part) != 0;

    private void ApplyLayout()
    {
        if (!_ready || _levelRow is null) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        var search = this.FindControl<Border>("SearchHost")!;
        var width = Bounds.Width;
        var compact = width < ConsoleDimensions.GetNarrowBreakpoint(this);
        var wide = width >= ConsoleDimensions.GetWideBreakpoint(this);
        var open = _levelRow.IsOpen;
        // An open row that does not fit next to the other controls takes over the row; only Jump to latest stays.
        var takeover = open && !wide;
        var sourcesAsIcon = compact || open && wide;

        Show(ConsoleToolbarParts.Levels, this.FindControl<StackPanel>("LevelFilter")!, true);
        Show(ConsoleToolbarParts.Search, search, !takeover);
        var sources = this.FindControl<Button>("Sources")!;
        Show(ConsoleToolbarParts.Sources, sources, !takeover);
        this.FindControl<StackPanel>("SourceText")!.IsVisible = !sourcesAsIcon;
        this.FindControl<TextBlock>("SourceIcon")!.IsVisible = sourcesAsIcon;
        sources.Classes.Set("actionIconButton", sourcesAsIcon);
        var copySelected = this.FindControl<Button>("CopySelected")!;
        var copyVisible = this.FindControl<Button>("CopyVisible")!;
        var clear = this.FindControl<Button>("ClearConsole")!;
        Show(ConsoleToolbarParts.CopySelected, copySelected, !takeover);
        // Copy visible is also in the header's More menu, so the narrow tier drops the button.
        Show(ConsoleToolbarParts.CopyVisible, copyVisible, !takeover && !compact);
        Show(ConsoleToolbarParts.Clear, clear, !takeover);
        var zoom = this.FindControl<Border>("ZoomGroup")!;
        Show(ConsoleToolbarParts.Zoom, zoom, !takeover);
        this.FindControl<Button>("ZoomOut")!.IsVisible = !compact;
        this.FindControl<Button>("ZoomIn")!.IsVisible = !compact;
        Show(ConsoleToolbarParts.Jump, this.FindControl<Button>("Jump")!, true);
        var anyCopy = copySelected.IsVisible || copyVisible.IsVisible || clear.IsVisible;
        this.FindControl<Border>("DividerSources")!.IsVisible = sources.IsVisible && anyCopy;
        this.FindControl<Border>("DividerZoom")!.IsVisible = zoom.IsVisible && (anyCopy || sources.IsVisible);
        this.FindControl<StackPanel>("Actions")!.IsVisible = sources.IsVisible || anyCopy || zoom.IsVisible;

        this.FindControl<StackPanel>("LevelItems")!.IsVisible = open;
        this.FindControl<Border>("ErrorHintHost")!.IsVisible = !open;
        var label = this.FindControl<ConsoleLevelLabel>("LevelsLabel")!;
        label.IsOpen = open;
        this.FindControl<TextBlock>("LevelsChevron")!.Text = open ? NvtIcons.ChevronLeft : NvtIcons.ChevronRight;
        // An open row that does not fit drops the names first and the counts next. The tooltip and the accessible name keep both.
        var names = !compact && (!open || LevelPartsFit(true, true));
        ShowLevelParts(names, !open || names || LevelPartsFit(false, true));
        // A control that hides while it has focus hands the focus to the level label, so keyboard users are not dropped.
        if (focused is { IsEffectivelyVisible: false } && this.IsVisualAncestorOf(focused)) label.Focus(NavigationMethod.Directional);
    }
}

// The parts of the toolbar that a host can close. The host choice is not public yet; the rule below already combines it.
[Flags]
internal enum ConsoleToolbarParts
{
    Levels = 1,
    Search = 2,
    Sources = 4,
    CopySelected = 8,
    CopyVisible = 16,
    Clear = 32,
    Zoom = 64,
    Jump = 128,
    All = 255,
}
