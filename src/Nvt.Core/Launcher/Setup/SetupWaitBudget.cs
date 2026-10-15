// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Transport;

namespace Nvt.Core.Launcher.Setup;

internal static class SetupWaitBudget
{
    internal static TimeSpan Remaining(TimeSpan total, TimeSpan elapsed) =>
        elapsed >= total ? TimeSpan.Zero : total - elapsed;

    internal static ImmutableBootstrapWaitBudget FromElapsed(TimeSpan operationCutoff,
        TimeSpan elapsed, TimeSpan cleanupBudget)
    {
        TimeSpan operationRemaining = Remaining(operationCutoff, elapsed);
        TimeSpan totalCutoff = operationCutoff + cleanupBudget;
        return new(operationRemaining, Remaining(totalCutoff, elapsed));
    }
}
