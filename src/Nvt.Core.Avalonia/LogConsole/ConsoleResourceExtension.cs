// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Nvt.Core.Avalonia.LogConsole;

// Dynamic resource lookup remains host-overridable; only a missing resource uses the shared dictionary default.
internal sealed class ConsoleResourceExtension(string key) : MarkupExtension, IMultiValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Converter = this,
        Bindings = { new DynamicResourceExtension("Nvt.Console." + key) },
    };
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values.Count != 0 && values[0] is { } value && !ReferenceEquals(value, AvaloniaProperty.UnsetValue)
            ? value : ConsoleResourceText.GetValue(key);
}
