// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Setup;

/// <summary>Caller-admitted selection role captured in a source publication identity.</summary>
public enum FreshInstallationSourceStatus
{
    /// <summary>The preferred automatic source.</summary>
    Latest,
    /// <summary>An ordered automatic fallback.</summary>
    Available,
    /// <summary>A historical source excluded from automatic selection by the caller.</summary>
    Deprecated,
}
