// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>Identifies whether a command runs during startup or at runtime.</summary>
public enum RuntimeQueryInvocation
{
    /// <summary>The startup runner invokes the command.</summary>
    Startup,
    /// <summary>The runtime router invokes the command.</summary>
    Runtime
}
