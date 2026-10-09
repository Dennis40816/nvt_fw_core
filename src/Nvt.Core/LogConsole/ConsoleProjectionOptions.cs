// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Globalization;

namespace Nvt.Core.LogConsole;

/// <summary>App-declared source metadata. Identity and filtering always use SourceId.</summary>
/// <param name="SourceId">The stable, ordinal source ID.</param>
/// <param name="DisplayName">The app's display name.</param>
/// <param name="DisplayOrder">Ascending display order; ties preserve registry order.</param>
public sealed record ConsoleSource(string SourceId, string DisplayName, int DisplayOrder = 0);

/// <summary>App-supplied presentation inputs, separate from event filters and reading state.</summary>
/// <remarks>The app replaces these inputs when its registry, resources, culture, or time zone changes.
/// Culture must remain unchanged during projection. Core performs no resource or local-zone lookup.</remarks>
public sealed record ConsoleProjectionOptions
{
    /// <summary>Gets the declared sources, including sources with no retained events. IDs must be unique.</summary>
    public ImmutableArray<ConsoleSource> SourceRegistry { get; init; } = [];
    /// <summary>Gets the Console.Timestamp.Ago composite template. Placeholder 0 receives formatted seconds.</summary>
    public string RelativeTimeTemplate { get; init; } = "{0} s ago";
    /// <summary>Gets the explicit culture for relative seconds. Core never reads thread culture.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
    /// <summary>Gets the explicit absolute display time zone. UTC preserves the default output.</summary>
    public TimeZoneInfo AbsoluteTimeZone { get; init; } = TimeZoneInfo.Utc;
}
