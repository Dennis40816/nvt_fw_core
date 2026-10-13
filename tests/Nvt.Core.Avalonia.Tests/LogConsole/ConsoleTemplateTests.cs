// Copyright (c) 2026 Dennis Liu. All rights reserved.


using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Checks template fallback and source presentation through live bindings.</summary>
public sealed class ConsoleTemplateTests
{
    private const string Summary = "No matching events · Trace, Debug, Info, Warn, Error, Fatal; Application; search ‘missing’ (matches only)";

    /// <summary>Syntax errors and missing arguments both retain readable built-in template text.</summary>
    [AvaloniaTheory]
    [InlineData("Count", "Info", "{", "Info · 1")]
    [InlineData("Count", "Info", "{99}", "Info · 1")]
    [InlineData("Dedupe.Count", "Dedupe", "{", "Dedupe ×0")]
    [InlineData("Dedupe.Count", "Dedupe", "{99}", "Dedupe ×0")]
    [InlineData("Zoom.Value", "Zoom", "{", "100%")]
    [InlineData("Zoom.Value", "Zoom", "{99}", "100%")]
    [InlineData("Sources.Selected", "Sources", "{", "Sources (1)")]
    [InlineData("Sources.Selected", "Sources", "{99}", "Sources (1)")]
    [InlineData("Empty.NoMatches", "Empty", "{", Summary)]
    [InlineData("Empty.NoMatches", "Empty", "{99}", Summary)]
    [InlineData("Filter.Search", "Empty", "{", Summary)]
    [InlineData("Filter.Search", "Empty", "{99}", Summary)]
    [InlineData("Filter.Summary", "Empty", "{", Summary)]
    [InlineData("Filter.Summary", "Empty", "{99}", Summary)]
    public void ResourcesMalformedTemplateShowsBuiltInDefault(string key, string label, string template, string expected)
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "visible");
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.SetSelectedSources(["app"]);
        controller.SetSearchText("missing");
        var surface = ConsoleTestView.Surface(controller);
        var window = ConsoleTestView.Create(surface, height: 300);
        try
        {
            using var log = new ConsoleBindingLogScope();
            window.Resources["Nvt.Console." + key] = template;
            ConsoleTestView.Pump(window);
            Assert.Equal(expected, ReadLabel(surface, label));
            Assert.Empty(log.Errors);
        }
        finally { window.Close(); }
    }

    /// <summary>Malformed level summary templates keep the built-in text on the label and in its accessible name.</summary>
    [AvaloniaTheory]
    [InlineData("Levels.AndAbove", "{", LogLevel.Trace, "Debug and above")]
    [InlineData("Levels.AndAbove", "{99}", LogLevel.Trace, "Debug and above")]
    [InlineData("Levels.Some", "{", LogLevel.Debug, "5 of 6 levels")]
    [InlineData("Levels.Some", "{99}", LogLevel.Debug, "5 of 6 levels")]
    public void LevelsMalformedSummaryTemplateShowsBuiltInDefault(string key, string template, LogLevel hidden, string expected)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        controller.ToggleLevelCommand.Execute(hidden);
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 40);
        try
        {
            using var log = new ConsoleBindingLogScope();
            window.Resources["Nvt.Console." + key] = template;
            ConsoleTestView.Pump(window);
            Assert.Equal(expected, toolbar.FindControl<TextBlock>("LevelsText")!.Text);
            Assert.Equal("Levels: " + expected, AutomationProperties.GetName(toolbar.FindControl<ConsoleLevelLabel>("LevelsLabel")!));
            Assert.Empty(log.Errors);
        }
        finally { window.Close(); }
    }

    /// <summary>Malformed popup count templates retain the declared source display name and raw count.</summary>
    [AvaloniaTheory]
    [InlineData("{")]
    [InlineData("{2}")]
    public void SourcesMalformedCountTemplateShowsBuiltInDefault(string template)
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "visible");
        fixture.Fence();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 300);
        try
        {
            using var log = new ConsoleBindingLogScope();
            var button = toolbar.FindControl<Button>("Sources")!;
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            menu.ShowAt(button);
            window.Resources["Nvt.Console.Count"] = template;
            ConsoleTestView.Pump(window);
            Assert.Equal("Application · 1", ((MenuItem)menu.Items[1]!).Header);
            Assert.Empty(log.Errors);
            menu.Hide();
        }
        finally { window.Close(); }
    }

    /// <summary>The empty summary resolves selected IDs through the current source catalog.</summary>
    [AvaloniaFact]
    public void EmptyStateSelectedDeclaredSourceShowsDisplayName()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "visible");
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.SetSelectedSources(["app"]);
        controller.SetSearchText("missing");
        var empty = new ConsoleEmptyState { Controller = controller };
        var window = ConsoleTestView.Create(empty, height: 100);
        try
        {
            Assert.Contains("; Application", empty.FindControl<TextBlock>("EmptyMessage")!.Text, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    private static string? ReadDedupe(ConsoleHeader header)
    {
        var display = header.FindControl<Button>("Display")!;
        var menu = Assert.IsType<MenuFlyout>(display.Flyout);
        menu.ShowAt(display);
        ConsoleTestView.Pump();
        var text = menu.Items.OfType<MenuItem>().Last().Header as string;
        menu.Hide();
        return text;
    }

    private static string? ReadLabel(StackPanel surface, string label)
    {
        var toolbar = (ConsoleToolbar)surface.Children[1];
        return label switch
        {
            "Info" => AutomationProperties.GetName(toolbar.FindControl<ToggleButton>("LevelInfo")!),
            "Dedupe" => ReadDedupe((ConsoleHeader)surface.Children[0]),
            "Levels" => toolbar.FindControl<TextBlock>("LevelsText")!.Text,
            "Zoom" => toolbar.FindControl<TextBlock>("ZoomText")!.Text,
            "Sources" => toolbar.FindControl<TextBlock>("SourceLabel")!.Text,
            "Empty" => ((ConsoleEmptyState)surface.Children[2]).FindControl<TextBlock>("EmptyMessage")!.Text,
            _ => throw new ArgumentOutOfRangeException(nameof(label)),
        };
    }
}
