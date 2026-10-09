// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.Avalonia.Theme;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Checks resolved Core roles on Console actions and popup items.</summary>
public sealed class ConsoleFontRoleTests
{
    /// <summary>Actions and every Console menu resolve Body resources in both themes and shapes, including Chinese source text.</summary>
    [AvaloniaTheory]
    [InlineData(false, ThemeShape.Pill)]
    [InlineData(true, ThemeShape.Pill)]
    [InlineData(false, ThemeShape.Square)]
    [InlineData(true, ThemeShape.Square)]
    public void ConsoleActionsAndMenuItemsResolveBodyRolesIncludingChineseSources(bool dark, ThemeShape shape)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        fixture.Store.Add(LogLevel.Info, "測試來源", "event");
        fixture.Fence();
        ConsoleTestView.Pump();
        var surface = ConsoleTestView.Surface(controller);
        var header = (ConsoleHeader)surface.Children[0];
        var toolbar = (ConsoleToolbar)surface.Children[1];
        var window = ConsoleTestView.Create(surface, dark, shape, height: 400);
        try
        {
            var actions = surface.GetVisualDescendants().OfType<Button>().Where(button => button is not ToggleButton).ToArray();
            Assert.NotEmpty(actions);
            Assert.All(actions, AssertBodyRole);
            foreach (var name in new[] { "Time", "Export", "Display", "More" })
            {
                window.Width = name is "Display" or "More" ? 640 : 1200;
                ConsoleTestView.Pump(window);
                var button = header.FindControl<Button>(name)!;
                ConsoleTestView.Click(window, button);
                var menu = Assert.IsType<MenuFlyout>(button.Flyout);
                Assert.True(menu.IsOpen);
                Assert.All(menu.Items.OfType<MenuItem>(), AssertBodyRole);
                menu.Hide();
            }
            var sources = toolbar.FindControl<Button>("Sources")!;
            ConsoleTestView.Click(window, sources);
            var sourceMenu = Assert.IsType<MenuFlyout>(sources.Flyout);
            Assert.True(sourceMenu.IsOpen);
            Assert.All(sourceMenu.Items.Cast<MenuItem>(), AssertBodyRole);
            var chinese = sourceMenu.Items.Cast<MenuItem>().Single(item => Assert.IsType<string>(item.Header).Contains("測試來源", StringComparison.Ordinal));
            AssertBodyRole(chinese);
            fixture.Store.Add(LogLevel.Info, "新增來源", "event");
            fixture.Fence();
            ConsoleTestView.Pump(window);
            Assert.True(sourceMenu.IsOpen);
            Assert.All(sourceMenu.Items.Cast<MenuItem>(), AssertBodyRole);
            Assert.Contains(sourceMenu.Items.Cast<MenuItem>(), item => Assert.IsType<string>(item.Header).Contains("新增來源", StringComparison.Ordinal));
            sourceMenu.Hide();
        }
        finally { window.Close(); }
    }

    private static void AssertBodyRole(TemplatedControl control)
    {
        Assert.Equal(Assert.IsType<FontFamily>(control.FindResource("Nvt.Font.Body.Family")), control.FontFamily);
        Assert.Equal(Assert.IsType<double>(control.FindResource("Nvt.Font.Body.Size")), control.FontSize);
        Assert.Equal(Assert.IsType<FontWeight>(control.FindResource("Nvt.Font.Body.Weight")), control.FontWeight);
    }
}
