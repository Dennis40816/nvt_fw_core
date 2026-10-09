// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls;
using Avalonia.Data;
using CommunityToolkit.Mvvm.Input;

namespace Nvt.Core.Avalonia.LogConsole;

internal static class ConsoleMenuBuilder
{
    internal static void Clear(MenuFlyout menu)
    {
        foreach (var item in menu.Items.OfType<SourceMenuItem>()) item.Release();
        menu.Items.Clear();
    }

    internal static void Sources(MenuFlyout menu, ConsoleController? controller, StyledElement host)
    {
        Clear(menu);
        if (controller is null) return;
        menu.Items.Add(new SourceMenuItem(controller, null, host));
        foreach (var source in controller.Projection.Sources)
            menu.Items.Add(new SourceMenuItem(controller, source.SourceId, host));
    }

    internal static void RefreshSources(MenuFlyout menu, ConsoleController controller, StyledElement host)
    {
        // Compare the rendered membership directly with the projection; do not retain a second catalog.
        if (!menu.Items.OfType<SourceMenuItem>().Select(item => item.SourceId).SequenceEqual(
            controller.Projection.Sources.Select(source => (string?)source.SourceId).Prepend(null), StringComparer.Ordinal))
            Sources(menu, controller, host);
    }

    private sealed class SourceMenuItem : MenuItem
    {
        protected override Type StyleKeyOverride => typeof(MenuItem);
        private readonly string? _sourceId;
        internal string? SourceId => _sourceId;
        // UI-thread-only lifetime, released as one unit. Commands retain the item, never the controller.
        private SourceLifetime? _lifetime;
        // Binding subscriptions are mutable only on the UI thread and released with their controller.
        private sealed record SourceLifetime(ConsoleController Controller, List<IDisposable> Bindings);

        internal SourceMenuItem(ConsoleController controller, string? sourceId, StyledElement host)
        {
            _lifetime = new SourceLifetime(controller, []);
            _sourceId = sourceId;
            Classes.Add("consoleMenuItem");
            ToggleType = MenuItemToggleType.CheckBox;
            Command = new RelayCommand(ToggleSource, CanToggleSource);
            var parameter = sourceId is null ? "AllSources" : "Source:" + sourceId;
            var converter = new ConsoleValueConverter { ResourceHost = host };
            _lifetime.Bindings.Add(this.Bind(HeaderProperty, new MultiBinding
            {
                Bindings = { new Binding(nameof(ConsoleController.Projection)) { Source = controller },
                    new DynamicResourceExtension("Nvt.Console.Count"), new DynamicResourceExtension("Nvt.Console.Sources.All") },
                Converter = converter, ConverterParameter = sourceId is null ? "SourceText:All" : "SourceText:Id:" + sourceId,
            }));
            _lifetime.Bindings.Add(this.Bind(IsCheckedProperty, new MultiBinding
            {
                Bindings = { new Binding(nameof(ConsoleController.Filter)) { Source = controller },
                    new Binding(nameof(ConsoleController.Projection)) { Source = controller } },
                Converter = converter, ConverterParameter = parameter, Mode = BindingMode.OneWay,
            }));
            _lifetime.Bindings.Add(this.Bind(AutomationProperties.NameProperty, new Binding(nameof(Header)) { Source = this }));
            controller.PropertyChanged += StateChanged;
        }

        private static ImmutableHashSet<string> Selected(ConsoleController controller)
            => controller.Filter.SelectedSources.IsEmpty
                ? controller.Projection.Sources.Select(source => source.SourceId).ToImmutableHashSet(StringComparer.Ordinal)
                : controller.Filter.SelectedSources;

        private bool CanToggleSource()
        {
            if (_lifetime is not { Controller: var controller } || !controller.ResetFiltersCommand.CanExecute(null)) return false;
            if (_sourceId is null) return true;
            var selected = Selected(controller);
            return !selected.Contains(_sourceId) || selected.Count > 1;
        }

        private void ToggleSource()
        {
            if (!CanToggleSource() || _lifetime is not { Controller: var controller }) return;
            if (_sourceId is null) { controller.SetSelectedSources([]); return; }
            var selected = Selected(controller);
            var removed = selected.Remove(_sourceId);
            controller.SetSelectedSources(ReferenceEquals(removed, selected) ? selected.Add(_sourceId) : removed);
        }

        private void StateChanged(object? sender, PropertyChangedEventArgs args)
            => (Command as IRelayCommand)?.NotifyCanExecuteChanged();

        internal void Release()
        {
            if (_lifetime is not { } lifetime) return;
            _lifetime = null;
            lifetime.Controller.PropertyChanged -= StateChanged;
            foreach (var binding in lifetime.Bindings) binding.Dispose();
            ((IRelayCommand)Command!).NotifyCanExecuteChanged();
            Command = null;
            IsEnabled = false;
        }
    }
}
