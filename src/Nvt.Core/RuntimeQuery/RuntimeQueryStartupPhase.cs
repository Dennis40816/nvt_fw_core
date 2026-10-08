// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.RuntimeQuery;

/// <summary>Identifies when the tool runs a command at startup.</summary>
public enum RuntimeQueryStartupPhase
{
    /// <summary>The command has no startup option.</summary>
    None,
    /// <summary>The command runs only at startup, before the main window shows.</summary>
    BeforeFirstFrame,
    /// <summary>The command runs after the tool's startup flow or through RuntimeQuery.</summary>
    AfterStartup,
    /// <summary>The command runs before the main window shows at startup and also through RuntimeQuery at runtime.</summary>
    BeforeFirstFrameAndRuntime
}
