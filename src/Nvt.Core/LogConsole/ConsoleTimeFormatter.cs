// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;

namespace Nvt.Core.LogConsole;

/// <summary>Pure time presentation using only explicit app inputs.</summary>
public static class ConsoleTimeFormatter
{
    /// <summary>Formats one timestamp; relative seconds use one decimal and the supplied resource template.</summary>
    /// <param name="timestamp">The occurrence instant.</param>
    /// <param name="timeBase">The snapshot or frozen pause instant.</param>
    /// <param name="mode">The display mode.</param>
    /// <param name="relativeTimeTemplate">The Console.Timestamp.Ago composite template.</param>
    /// <param name="culture">The app's explicit culture for relative seconds.</param>
    /// <param name="absoluteTimeZone">The app's absolute display zone, or null for UTC.</param>
    public static string Format(DateTimeOffset timestamp, DateTimeOffset timeBase, ConsoleTimeMode mode,
        string relativeTimeTemplate, CultureInfo culture, TimeZoneInfo? absoluteTimeZone = null)
    {
        ArgumentNullException.ThrowIfNull(relativeTimeTemplate);
        ArgumentNullException.ThrowIfNull(culture);
        return mode switch
        {
            ConsoleTimeMode.Absolute => TimeZoneInfo.ConvertTime(timestamp, absoluteTimeZone ?? TimeZoneInfo.Utc)
                .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            ConsoleTimeMode.Relative => string.Format(culture, relativeTimeTemplate,
                (timeBase - timestamp).TotalSeconds.ToString("0.0", culture)),
            ConsoleTimeMode.Hidden => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }
}
