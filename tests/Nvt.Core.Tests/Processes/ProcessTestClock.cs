// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Tests.Processes;

/// <summary>Holds the common start instant for manual clocks in the process tests.</summary>
internal static class ProcessTestClock
{
    internal static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
