// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed partial class ConsoleListViewTests
{
    /// <summary>Unavailable keys replace previous translations with their built-in English text.</summary>
    [Theory]
    [InlineData("JumpToLatestOne", "Jump to latest (1 new message)", 1, LogLevel.Info)]
    [InlineData("JumpToLatestMany", "Jump to latest (2 new messages)", 2, LogLevel.Info)]
    [InlineData("RetentionOne", "Retention changed · 1 message evicted", 1, LogLevel.Info)]
    [InlineData("RetentionFormat", "Retention changed · 2 messages evicted", 2, LogLevel.Info)]
    [InlineData("Level.Trace", "Trace", 1, LogLevel.Trace)]
    [InlineData("Level.Debug", "Debug", 1, LogLevel.Debug)]
    [InlineData("Level.Info", "Info", 1, LogLevel.Info)]
    [InlineData("Level.Warn", "Warn", 1, LogLevel.Warn)]
    [InlineData("Level.Error", "Error", 1, LogLevel.Error)]
    [InlineData("Level.Fatal", "Fatal", 1, LogLevel.Fatal)]
    public Task MissingTextResourceUsesEnglishDefault(string suffix, string expected, int count, LogLevel level) => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i => Row(i, level: level)), newCount: count, evicted: count);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            var key = $"Nvt.Console.List.{suffix}";
            view.Resources[key] = "Translated";
            Flush(window);
            Assert.Equal("Translated", DisplayedListText(view, suffix));
            RemoveBuiltInText(window, key);
            view.Resources.Remove(key);
            Assert.False(view.TryFindResource(key, view.ActualThemeVariant, out _));
            Flush(window);
            Assert.Equal(expected, DisplayedListText(view, suffix));
        }
        finally { window.Close(); }
    });

    /// <summary>Malformed composite formats use the same fallback for labels and count text.</summary>
    [Theory]
    [InlineData("JumpToLatestOne", "Jump to latest (1 new message)", 1, LogLevel.Info)]
    [InlineData("JumpToLatestMany", "Jump to latest (2 new messages)", 2, LogLevel.Info)]
    [InlineData("RetentionOne", "Retention changed · 1 message evicted", 1, LogLevel.Info)]
    [InlineData("RetentionFormat", "Retention changed · 2 messages evicted", 2, LogLevel.Info)]
    [InlineData("Level.Trace", "Trace", 1, LogLevel.Trace)]
    [InlineData("Level.Debug", "Debug", 1, LogLevel.Debug)]
    [InlineData("Level.Info", "Info", 1, LogLevel.Info)]
    [InlineData("Level.Warn", "Warn", 1, LogLevel.Warn)]
    [InlineData("Level.Error", "Error", 1, LogLevel.Error)]
    [InlineData("Level.Fatal", "Fatal", 1, LogLevel.Fatal)]
    public Task MalformedTextResourceUsesEnglishDefault(string suffix, string expected, int count, LogLevel level) => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i => Row(i, level: level)), newCount: count, evicted: count);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            view.Resources[$"Nvt.Console.List.{suffix}"] = "Broken {";
            Flush(window);
            Assert.Equal(expected, DisplayedListText(view, suffix));
        }
        finally { window.Close(); }
    });

    /// <summary>Count text with an unavailable argument is also a malformed override.</summary>
    [Fact]
    public Task OutOfRangeTextFormatUsesEnglishDefault() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i => Row(i)), newCount: 2);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            view.Resources["Nvt.Console.List.JumpToLatestMany"] = "Jump {1}";
            Flush(window);
            Assert.Equal("Jump to latest (2 new messages)", DisplayedListText(view, "JumpToLatestMany"));
        }
        finally { window.Close(); }
    });

    /// <summary>A non-string override cannot make row measurement fail.</summary>
    [Fact]
    public Task WrongTypeTextResourceUsesEnglishDefault() => Run(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            view.Resources["Nvt.Console.List.Level.Info"] = 17;
            Flush(window);
            Assert.Equal("Info", DisplayedListText(view, "Level.Info"));
        }
        finally { window.Close(); }
    });

    /// <summary>Empty and whitespace overrides use the English label.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public Task BlankTextOverrideUsesEnglishDefault(string text) => Run(() =>
    {
        using var projection = Many(100);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            view.Resources["Nvt.Console.List.Level.Info"] = text;
            Flush(window);
            Assert.Equal("Info", DisplayedListText(view, "Level.Info"));
        }
        finally { window.Close(); }
    });

    /// <summary>An unknown level uses its key suffix without failing row measurement.</summary>
    [Fact]
    public Task UnknownLevelUsesKeySuffix() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i => Row(i, level: (LogLevel)17)));
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try { Assert.Equal("17", DisplayedListText(view, "Level.17")); }
        finally { window.Close(); }
    });

    /// <summary>One eviction selects the singular host resource instead of the plural format.</summary>
    [Fact]
    public Task SingleEvictionUsesHostSingularRetentionText() => Run(() =>
    {
        using var projection = Project(Enumerable.Range(1, 100).Select(i => Row(i)), evicted: 1);
        var view = new ConsoleListView { Projection = projection,
            ViewState = new ConsoleViewState().Pause(projection, new(21)) };
        var window = Window(view);
        try
        {
            view.Resources["Nvt.Console.List.RetentionOne"] = "One eviction: {0}";
            view.Resources["Nvt.Console.List.RetentionFormat"] = "Many evictions: {0}";
            Flush(window);
            Assert.Equal("One eviction: 1", DisplayedListText(view, "RetentionOne"));
        }
        finally { window.Close(); }
    });

    private static string? DisplayedListText(ConsoleListView view, string suffix) => suffix switch
    {
        "JumpToLatestOne" or "JumpToLatestMany" => (string?)view.GetVisualDescendants()
            .OfType<Button>().Single(button => button.Name == "PART_Jump").Content,
        "RetentionOne" or "RetentionFormat" => view.GetVisualDescendants()
            .OfType<TextBlock>().Single(text => text.Name == "PART_Retention").Text,
        _ => Container(view, 21).GetVisualChildren().OfType<TextBlock>().ElementAt(2).Text,
    };

    private static void RemoveBuiltInText(Window window, string key)
    {
        var styles = Assert.IsType<Styles>(window.Styles.OfType<StyleInclude>().Single().Loaded);
        var resources = Assert.IsType<ResourceDictionary>(styles.Resources);
        var defaults = Assert.IsType<ResourceDictionary>(resources.MergedDictionaries.Single());
        Assert.True(defaults.Remove(key));
    }
}
