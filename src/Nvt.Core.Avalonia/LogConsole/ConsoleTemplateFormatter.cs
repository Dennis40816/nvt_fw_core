// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Text;

namespace Nvt.Core.Avalonia.LogConsole;

internal sealed class ConsoleTemplateFormatter
{
    // UI-thread-only converter cache: one current template per built-in resource key.
    private readonly Dictionary<string, (string Template, CompositeFormat Format, CompositeFormat Fallback)> _formats = new(StringComparer.Ordinal);

    internal string Format(string key, string template, CultureInfo culture, params object?[] arguments)
    {
        var cached = _formats.TryGetValue(key, out var entry);
        if (!cached || !string.Equals(entry.Template, template, StringComparison.Ordinal))
        {
            var fallback = cached ? entry.Fallback : CompositeFormat.Parse(ConsoleResourceText.GetDefault(key));
            CompositeFormat format;
            try { format = CompositeFormat.Parse(template); }
            catch (FormatException) { format = fallback; } // Malformed host translations use the built-in template.
            entry = (template, format, fallback);
            _formats[key] = entry;
        }
        try { return string.Format(culture, entry.Format, arguments); }
        catch (FormatException)
        {
            // Valid syntax can still reference a missing argument or use an invalid argument format.
            _formats[key] = (template, entry.Fallback, entry.Fallback);
            return string.Format(culture, entry.Fallback, arguments);
        }
    }
}
