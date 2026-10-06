// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>Identifies the effect of a runtime query command.</summary>
public enum RuntimeQueryCommandRisk
{
    /// <summary>The command only reads state.</summary>
    ReadOnly,
    /// <summary>The command changes UI state without writing files or changing data.</summary>
    ChangesState,
    /// <summary>The command writes files or changes the tool's data.</summary>
    WritesData
}
