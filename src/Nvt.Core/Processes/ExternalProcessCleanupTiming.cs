// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;

namespace Nvt.Core.Processes;

/// <summary>
/// Host cleanup timing. <see cref="Deadline"/> is the one total bound that counts
/// from the terminal signal and covers termination confirmation, output drain and reader stop. It is not a manifest
/// or profile timeout.
/// </summary>
/// <param name="Deadline">Total host wait after the terminal signal.</param>
/// <param name="HeldOutputGrace">After a natural exit, how long the streams may stay open before they count as held.</param>
/// <param name="ReaderStopReserve">The final part of the deadline reserved for stopped readers to return.</param>
internal readonly record struct ExternalProcessCleanupTiming(
    TimeSpan Deadline,
    TimeSpan HeldOutputGrace,
    TimeSpan ReaderStopReserve)
{
    /// <summary>Production timing: 5 s in total, of which up to 2 s is exit grace and the last 1 s is reader stop.</summary>
    internal static ExternalProcessCleanupTiming Default { get; } = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(1));

    /// <summary>Absolute Stopwatch timestamps for the reader-stop point and the single deadline.</summary>
    /// <remarks>
    /// The reader stop lies inside the deadline (deadline minus reserve); the reserve is not added after the
    /// deadline, so every wait shares the one deadline.
    /// </remarks>
    internal CleanupSchedule Schedule(long signaledAt)
    {
        return Schedule(signaledAt, Stopwatch.Frequency);
    }

    /// <summary>The same schedule in the timestamp unit of an injected <see cref="TimeProvider"/>.</summary>
    internal CleanupSchedule Schedule(long signaledAt, long timestampFrequency)
    {
        long deadlineTicks = Ticks(Deadline, timestampFrequency);
        long reserveTicks = Ticks(ReaderStopReserve, timestampFrequency);
        return new CleanupSchedule(
            signaledAt + Ticks(HeldOutputGrace, timestampFrequency),
            signaledAt + deadlineTicks - reserveTicks,
            signaledAt + deadlineTicks);
    }

    internal static long Ticks(TimeSpan span)
    {
        return Ticks(span, Stopwatch.Frequency);
    }

    internal static long Ticks(TimeSpan span, long timestampFrequency)
    {
        return (long)(span.TotalSeconds * timestampFrequency);
    }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Deadline, TimeSpan.Zero, nameof(Deadline));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(HeldOutputGrace, TimeSpan.Zero, nameof(HeldOutputGrace));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReaderStopReserve, TimeSpan.Zero, nameof(ReaderStopReserve));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            HeldOutputGrace + ReaderStopReserve,
            Deadline,
            nameof(HeldOutputGrace));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            Deadline,
            TimeSpan.FromMilliseconds(int.MaxValue),
            nameof(Deadline));
    }
}
