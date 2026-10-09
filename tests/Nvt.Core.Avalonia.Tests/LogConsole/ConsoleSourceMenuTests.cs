// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Checks live catalog changes and tolerant bindings without wall-clock waits.</summary>
public sealed class ConsoleSourceMenuTests
{
    /// <summary>Clear removes undeclared sources from an open menu while keeping declared zero-count sources.</summary>
    [AvaloniaFact]
    public void OpenSourceMenuRefreshesCatalogAfterClearWithoutBindingErrors()
        => AssertCatalogChange(fixture => fixture.Store.Clear(), maxEntries: 100, "transient");

    /// <summary>Capacity eviction removes an undeclared source without dismissing the menu.</summary>
    [AvaloniaFact]
    public void OpenSourceMenuRefreshesCatalogAfterEvictionWithoutBindingErrors()
        => AssertCatalogChange(fixture => fixture.Store.Add(LogLevel.Info, "app", "replacement"), maxEntries: 2, "transient");

    /// <summary>A newly observed source appears in the same open menu.</summary>
    [AvaloniaFact]
    public void OpenSourceMenuRefreshesCatalogAfterNewSourceWithoutBindingErrors()
        => AssertCatalogChange(fixture => fixture.Store.Add(LogLevel.Info, "new-source", "new event"), maxEntries: 100, null);

    /// <summary>Bindings retained independently of the menu resolve a missing ID as unchecked with zero events.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingSourceBindingsResolveUncheckedAndZeroWithoutBindingErrors(bool explicitSelection)
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "transient", "event");
        fixture.Fence();
        using var controller = fixture.Controller();
        controller.SetSelectedSources(explicitSelection ? ["transient"] : []);
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 400);
        try
        {
            using var log = new ConsoleBindingLogScope();
            var converter = Assert.IsAssignableFrom<IValueConverter>(toolbar.Resources["ConsoleValue"]);
            var checkedConverter = Assert.IsAssignableFrom<IMultiValueConverter>(converter);
            var retained = new MenuItem();
            using var headerBinding = retained.Bind(MenuItem.HeaderProperty, new Binding(nameof(ConsoleController.Projection))
                { Source = controller, Converter = converter, ConverterParameter = "Source:transient" });
            using var checkedBinding = retained.Bind(MenuItem.IsCheckedProperty, new MultiBinding
            {
                Bindings = { new Binding(nameof(ConsoleController.Filter)) { Source = controller },
                    new Binding(nameof(ConsoleController.Projection)) { Source = controller } },
                Converter = checkedConverter, ConverterParameter = "Source:transient", Mode = BindingMode.OneWay,
            });
            ConsoleTestView.Pump(window);
            Assert.True(retained.IsChecked);
            Assert.Equal("transient · 1", retained.Header);
            fixture.Store.Clear();
            fixture.Fence();
            ConsoleTestView.Pump(window);
            Assert.False(retained.IsChecked);
            Assert.Equal("transient · 0", retained.Header);
            Assert.Equal(false, checkedConverter.Convert([controller.Filter, controller.Projection], typeof(bool), "Source:transient", CultureInfo.InvariantCulture));
            Assert.True(!log.Errors.Any(), string.Join(Environment.NewLine, log.Errors));
        }
        finally { window.Close(); }
    }

    /// <summary>Count and filter changes preserve menu items when source membership stays the same.</summary>
    [AvaloniaFact]
    public void OpenSourceMenuKeepsItemsForUnchangedCatalogAndUpdatesBindings()
    {
        using var fixture = new ConsoleTestStore();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 400);
        try
        {
            using var log = new ConsoleBindingLogScope();
            var menu = OpenSources(toolbar, window);
            var items = menu.Items.Cast<MenuItem>().ToArray();
            controller.SetSelectedSources(["app"]);
            fixture.Store.Add(LogLevel.Info, "app", "event");
            fixture.Fence();
            ConsoleTestView.Pump(window);
            Assert.True(menu.IsOpen);
            Assert.Equal(items, menu.Items.Cast<MenuItem>());
            Assert.Equal("Application · 1", items[1].Header);
            Assert.True(items[1].IsChecked);
            Assert.False(items[2].IsChecked);
            Assert.True(!log.Errors.Any(), string.Join(Environment.NewLine, log.Errors));
            menu.Hide();
        }
        finally { window.Close(); }
    }

    private static void AssertCatalogChange(Action<ConsoleTestStore> change, int maxEntries, string? removedId)
    {
        using var fixture = new ConsoleTestStore(maxEntries);
        fixture.Store.Add(LogLevel.Info, "transient", "first event");
        fixture.Store.Add(LogLevel.Info, "app", "retained event");
        fixture.Fence();
        using var controller = fixture.Controller();
        var toolbar = new ConsoleToolbar { Controller = controller };
        var window = ConsoleTestView.Create(toolbar, height: 400);
        try
        {
            using var log = new ConsoleBindingLogScope();
            var menu = OpenSources(toolbar, window);
            AssertCatalog(menu, controller);
            var previous = menu.Items.Cast<MenuItem>().ToArray();
            var commands = previous.Select(item => item.Command!).ToArray();
            fixture.UtcNow += TimeSpan.FromSeconds(1);
            change(fixture);
            fixture.Fence();
            ConsoleTestView.Pump(window);
            Assert.True(menu.IsOpen);
            Assert.Same(menu, toolbar.FindControl<Button>("Sources")!.Flyout);
            AssertCatalog(menu, controller);
            if (removedId is not null) Assert.DoesNotContain(controller.Projection.Sources, source => source.SourceId == removedId);
            else Assert.Contains(controller.Projection.Sources, source => source.SourceId == "new-source");
            Assert.All(previous, item => { Assert.Null(item.Command); Assert.False(item.IsEnabled); });
            Assert.All(commands, command => Assert.False(command.CanExecute(null)));
            Assert.True(!log.Errors.Any(), string.Join(Environment.NewLine, log.Errors));
            menu.Hide();
        }
        finally { window.Close(); }
    }

    private static MenuFlyout OpenSources(ConsoleToolbar toolbar, Window window)
    {
        var button = toolbar.FindControl<Button>("Sources")!;
        ConsoleTestView.Click(window, button);
        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        Assert.True(menu.IsOpen);
        return menu;
    }

    private static void AssertCatalog(MenuFlyout menu, ConsoleController controller)
    {
        var expected = controller.Projection.Sources.Select(source => $"{source.DisplayName} · {controller.Projection.SourceCounts.GetValueOrDefault(source.SourceId)}")
            .Prepend($"All sources · {controller.Projection.SourceCounts.Values.Sum()}");
        Assert.Equal(expected, menu.Items.Cast<MenuItem>().Select(item => Assert.IsType<string>(item.Header)));
        Assert.All(menu.Items.Cast<MenuItem>(), item => Assert.True(item.IsChecked));
    }
}
