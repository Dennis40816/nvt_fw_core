// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;

namespace Nvt.Core.Avalonia.LogConsole;

// The level label and the hidden-error count are derived from immutable filter and projection state only.
internal static class ConsoleLevelSummary
{
    internal enum Kind { All, None, AndAbove, FatalOnly, Some }

    internal readonly record struct Summary(Kind Kind, LogLevel Floor, int Enabled);

    private static readonly LogLevel[] _levels = Enum.GetValues<LogLevel>().Order().ToArray();

    internal static Summary Describe(ConsoleFilter filter)
    {
        var enabled = _levels.Count(filter.EnabledLevels.Contains);
        if (enabled == _levels.Length) return new Summary(Kind.All, _levels[0], enabled);
        if (enabled == 0) return new Summary(Kind.None, _levels[0], 0);
        var floor = _levels.First(filter.EnabledLevels.Contains);
        // A contiguous run that reaches Fatal reads as a threshold; any other set is a custom selection.
        var threshold = _levels.SkipWhile(level => level != floor).All(filter.EnabledLevels.Contains);
        if (!threshold) return new Summary(Kind.Some, floor, enabled);
        return new Summary(floor == _levels[^1] ? Kind.FatalOnly : Kind.AndAbove, floor, enabled);
    }

    // Entries that the level filter hides and that the list therefore cannot show.
    internal static int HiddenErrors(ConsoleFilter filter, ConsoleProjection projection)
    {
        var hidden = 0;
        foreach (var level in new[] { LogLevel.Error, LogLevel.Fatal })
            if (!filter.EnabledLevels.Contains(level)) hidden += projection.LevelCounts.GetValueOrDefault(level);
        return hidden;
    }
}
