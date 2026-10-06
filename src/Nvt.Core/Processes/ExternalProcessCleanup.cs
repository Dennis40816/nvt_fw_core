// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Processes;

/// <summary>
/// Cleanup outcome observed for one external process invocation. The runner observes only the direct child's
/// exit and the end of its two redirected streams. No value proves that every descendant has stopped,
/// because a descendant that holds neither stream is not observable.
/// </summary>
public enum ExternalProcessCleanup
{
    /// <summary>The direct child's exit was observed and both redirected streams reported their end.</summary>
    Complete,

    /// <summary>The termination request failed or did not finish, or the direct child's exit was not observed.</summary>
    TerminationUnconfirmed,

    /// <summary>A redirected output stream did not reach its end within the allowed drain period.</summary>
    OutputStreamHeldOpen,

    /// <summary>Reading a redirected stream failed, so its end was not observed.</summary>
    OutputReadFailed,
}
