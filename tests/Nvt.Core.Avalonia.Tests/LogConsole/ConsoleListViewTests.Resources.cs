// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

#pragma warning disable CA1707 // Owner-mandated Method_Scenario_Expected test names.

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed partial class ConsoleListViewTests
{
    /// <summary>Exactly one new message resolves the host singular string.</summary>
    [Fact]
    public Task JumpButton_SingleNewMessage_UsesHostSingularResource() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i => Row(i)), newCount: 1);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            view.Resources["Nvt.Console.List.JumpToLatestOne"] = "單則 {0}";
            view.Resources["Nvt.Console.List.JumpToLatestMany"] = "多則 {0}";
            Flush(window);
            var jump = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Jump");
            Assert.Equal("單則 1", jump.Content);
        }
        finally { window.Close(); }
    });

    /// <summary>Plural text uses the host format and explicit time culture.</summary>
    [Fact]
    public Task JumpButton_MultipleNewMessages_FormatsHostResourceWithTimeCulture() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i => Row(i)), newCount: 1234);
        var culture = new CultureInfo("en-US");
        culture.NumberFormat.NumberGroupSeparator = "_";
        var view = new ConsoleListView { Projection = projection, TimeOptions = new() { Culture = culture },
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            view.Resources["Nvt.Console.List.JumpToLatestOne"] = "單則 {0}";
            view.Resources["Nvt.Console.List.JumpToLatestMany"] = "多則 {0:N0}";
            Flush(window);
            var jump = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Jump");
            Assert.Equal("多則 1_234", jump.Content);
        }
        finally { window.Close(); }
    });

    /// <summary>Retention text uses the host format and explicit time culture.</summary>
    [Fact]
    public Task RetentionNotice_HostResourceOverride_FormatsWithTimeCulture() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i => Row(i)), evicted: 1234);
        var culture = new CultureInfo("en-US");
        culture.NumberFormat.NumberGroupSeparator = "_";
        var view = new ConsoleListView { Projection = projection, TimeOptions = new() { Culture = culture },
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            view.Resources["Nvt.Console.List.RetentionFormat"] = "已移除 {0:N0} 則";
            Flush(window);
            var notice = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Retention");
            Assert.Equal("已移除 1_234 則", notice.Text);
        }
        finally { window.Close(); }
    });

    /// <summary>Each level resolves its host text after a resource change.</summary>
    [Theory]
    [InlineData(LogLevel.Trace, "追蹤")]
    [InlineData(LogLevel.Debug, "偵錯")]
    [InlineData(LogLevel.Info, "資訊")]
    [InlineData(LogLevel.Warn, "警告")]
    [InlineData(LogLevel.Error, "錯誤")]
    [InlineData(LogLevel.Fatal, "嚴重")]
    public Task Configure_LevelResourceOverride_DisplaysHostText(LogLevel level, string text) => Run(() =>
    {
        using var projection = Project([Row(1, level: level)]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            view.Resources[$"Nvt.Console.List.Level.{level}"] = text;
            Flush(window);
            Assert.Equal(text, ((TextBlock)Member(Container(view, 1), "_level")!).Text);
        }
        finally { window.Close(); }
    });

    /// <summary>A font change invalidates measured heights of offscreen expanded rows.</summary>
    [Fact]
    public Task ResourcesChanged_FontSizeWithUnrealizedExpandedRow_InvalidatesMeasuredHeight() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i =>
            Row(i, i == 1 ? new string('W', 4000) : "Message")));
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view);
        try
        {
            var heights = (IDictionary)Member(Host(view), "_heights")!;
            Assert.True(heights.Contains(new ConsoleRowId(1)));
            Assert.DoesNotContain(Host(view).Children, row => Id(row).Value == 1);
            var extent = Scroll(view).Extent.Height;
            view.Resources["Nvt.Font.Body.Size"] = 24d;
            Flush(window);
            Assert.False(heights.Contains(new ConsoleRowId(1)));
            Assert.True(Scroll(view).Extent.Height > extent);
        }
        finally { window.Close(); }
    });

    /// <summary>A theme change invalidates measured heights of offscreen expanded rows.</summary>
    [Fact]
    public Task ActualThemeVariantChanged_UnrealizedExpandedRow_InvalidatesMeasuredHeight() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i =>
            Row(i, i == 1 ? new string('W', 4000) : "Message")));
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view);
        try
        {
            var heights = (IDictionary)Member(Host(view), "_heights")!;
            Assert.True(heights.Contains(new ConsoleRowId(1)));
            Assert.DoesNotContain(Host(view).Children, row => Id(row).Value == 1);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Flush(window);
            Assert.False(heights.Contains(new ConsoleRowId(1)));
        }
        finally { window.Close(); }
    });

    /// <summary>A font resource change preserves the paused row and its pixel inset.</summary>
    [Fact]
    public Task ResourcesChanged_PausedWithUnrealizedExpandedRow_PreservesReadingRowPosition() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1, "first\nsecond\nthird")));
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view);
        try
        {
            Assert.Equal(2040, Scroll(view).Extent.Height);
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 645);
            Flush(window);
            Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
            Assert.DoesNotContain(Host(view).Children, row => Id(row).Value == 1);
            var before = Container(view, 31).Bounds.Y;
            Assert.Equal(-5, before);
            view.Resources["Nvt.Font.Body.Size"] = 24d;
            Flush(window);
            Assert.Equal(before, Container(view, 31).Bounds.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>A theme change preserves the paused row and its pixel inset.</summary>
    [Fact]
    public Task ActualThemeVariantChanged_PausedWithUnrealizedExpandedRow_PreservesReadingRowPosition() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1, "first\nsecond\nthird")));
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view);
        try
        {
            Assert.Equal(2040, Scroll(view).Extent.Height);
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            Scroll(view).Offset = new(0, 645);
            Flush(window);
            Assert.IsType<ConsoleFollow.Paused>(view.ViewState.Follow);
            Assert.DoesNotContain(Host(view).Children, row => Id(row).Value == 1);
            var before = Container(view, 31).Bounds.Y;
            Assert.Equal(-5, before);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Flush(window);
            Assert.Equal(before, Container(view, 31).Bounds.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Resource invalidation keeps Following at the bottom of the rebuilt extent.</summary>
    [Fact]
    public Task ResourcesChanged_FollowingWithUnrealizedExpandedRow_RemainsAtBottom() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(2, 99).Select(i => Row(i))
            .Prepend(Row(1, "first\nsecond\nthird")));
        var view = new ConsoleListView { Projection = projection, ViewState = new() { ExpandedIds = [new(1)] } };
        var window = Window(view);
        try
        {
            Assert.Equal(2040, Scroll(view).Extent.Height);
            Assert.DoesNotContain(Host(view).Children, row => Id(row).Value == 1);
            view.ViewStateRequested += (_, state) => view.ViewState = state;
            view.Resources["Nvt.Font.Body.Size"] = 24d;
            Flush(window);
            Assert.IsType<ConsoleFollow.Following>(view.ViewState.Follow);
            Assert.Equal(Scroll(view).Extent.Height - Scroll(view).Viewport.Height, Scroll(view).Offset.Y);
        }
        finally { window.Close(); }
    });

    /// <summary>Both decorative row glyphs consume the shared accessibility convention.</summary>
    [Theory]
    [InlineData("_icon")]
    [InlineData("_arrow")]
    public Task RowGlyph_SharedIconStyle_IsRawForAccessibility(string member) => Run(() =>
    {
        using var projection = Project([Row(1, "first\nsecond")]);
        var view = new ConsoleListView { Projection = projection };
        var window = Window(view);
        try
        {
            var glyph = (TextBlock)Member(Container(view, 1), member)!;
            Assert.Contains("nvtIcon", glyph.Classes);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(glyph));
        }
        finally { window.Close(); }
    });
}
