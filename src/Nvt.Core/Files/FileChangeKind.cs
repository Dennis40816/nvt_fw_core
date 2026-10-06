// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Files;

/// <summary>Describes a stream change observed during a complete content read.</summary>
public enum FileChangeKind
{
    /// <summary>No specific change was supplied.</summary>
    Unspecified,

    /// <summary>The stream ended before the measured length was read.</summary>
    ShortRead,

    /// <summary>A trailing byte or a larger final length was observed.</summary>
    Growth,

    /// <summary>A smaller final length was observed.</summary>
    Shrinkage,

    /// <summary>The final stream position differed from the measured length.</summary>
    PositionChanged,
}
