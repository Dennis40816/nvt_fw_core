// Copyright (c) 2026 Dennis Liu. All rights reserved.


using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Nvt.Core.Avalonia.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Checks that derived search and focus dimensions track host resources.</summary>
public sealed class ConsoleDimensionResourceTests
{
    /// <summary>Each search inset recomputes when its dimension token changes at runtime.</summary>
    [AvaloniaTheory]
    [InlineData(1200, 0, "NfcControlHeight", 40, 12, 4, 44)]
    [InlineData(640, 1, "NfcControlHeight", 40, 12, 4, 44)]
    [InlineData(1200, 0, "NfcSpace12", 16, 16, 4, 36)]
    [InlineData(640, 1, "NfcSpace12", 16, 16, 4, 36)]
    [InlineData(1200, 0, "NfcSpace4", 6, 12, 6, 38)]
    [InlineData(640, 1, "NfcSpace4", 6, 12, 6, 38)]
    public void SearchPaddingHostChangesDimensionUpdatesInsets(int width, int row, string key,
        double value, double left, double vertical, double right)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var surface = ConsoleTestView.Surface(controller);
        var window = ConsoleTestView.Create(surface, width: width, height: 300);
        try
        {
            var search = ((UserControl)surface.Children[row]).FindControl<TextBox>("SearchBox")!;
            window.Resources[key] = value;
            ConsoleTestView.Pump(window);
            Assert.Equal(new Thickness(left, vertical, right, vertical), search.Padding);
        }
        finally { window.Close(); }
    }

    /// <summary>Both focus templates derive their outward margin from the current spacing token.</summary>
    [AvaloniaTheory]
    [InlineData("LevelInfo", "LevelFocus")]
    [InlineData("SearchBox", "SearchFocus")]
    public void FocusMarginHostChangesSpacingUpdatesInset(string controlName, string borderName)
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, width: 640, height: 300);
        try
        {
            var control = toolbar.FindControl<Control>(controlName)!;
            var focus = control.GetVisualDescendants().OfType<Border>().Single(border => border.Name == borderName);
            window.Resources["NfcSpace4"] = 6d;
            ConsoleTestView.Pump(window);
            Assert.Equal(new Thickness(-6), focus.Margin);
        }
        finally { window.Close(); }
    }
}
