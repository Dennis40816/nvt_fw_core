// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.TestSupport;

/// <summary>Measures a hot path against this machine, never against a wall-clock limit.</summary>
/// <remarks>
/// A performance test needs a threshold that holds on a slow CI runner and on a fast workstation. Use one of these:
/// a calibration unit (<see cref="CalibrationUnit"/> and <see cref="InUnits"/>), a scale ratio (<see cref="ScaleRatio"/>),
/// or a machine-independent count (<see cref="MedianAllocatedBytes"/>). Prefer a count when one fits.
/// Every measurement warms up first, takes at least <see cref="MinimumSamples"/> samples and reports the median.
/// A failed threshold is never retried. This class is the only place in a test project that reads a clock to measure
/// speed. The default clock is <see cref="TimeProvider.System"/>; a test of the helper itself passes a
/// <see cref="ManualTimeProvider"/>.
/// </remarks>
public static class RelativePerf
{
    /// <summary>The smallest sample count that a measurement accepts.</summary>
    public const int MinimumSamples = 7;

    /// <summary>The default number of warm-up runs whose time is discarded.</summary>
    public const int DefaultWarmups = 1;

    private const int ReferenceLength = 32 * 1024;

    /// <summary>Gets the median time of the work.</summary>
    /// <param name="work">The work to time. It runs <paramref name="warmups"/> + <paramref name="samples"/> times.</param>
    /// <param name="samples">The sample count, at least <see cref="MinimumSamples"/>.</param>
    /// <param name="warmups">Runs that are discarded before sampling, at least 0.</param>
    /// <param name="clock">The clock. <see langword="null"/> means <see cref="TimeProvider.System"/>.</param>
    /// <returns>The median elapsed time. For an even sample count it is the upper of the two middle samples.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample count is below the minimum or the warm-up count is negative.</exception>
    public static TimeSpan MedianTime(Action work, int samples = MinimumSamples, int warmups = DefaultWarmups, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidateCounts(samples, warmups);
        TimeProvider timeProvider = clock ?? TimeProvider.System;
        for (int run = 0; run < warmups; run++)
        {
            work();
        }
        var elapsed = new TimeSpan[samples];
        for (int run = 0; run < samples; run++)
        {
            long started = timeProvider.GetTimestamp();
            work();
            elapsed[run] = timeProvider.GetElapsedTime(started);
        }
        Array.Sort(elapsed);
        return elapsed[samples / 2];
    }

    /// <summary>Gets the time of a fixed reference workload on this machine. One calibration unit equals this time.</summary>
    /// <remarks>
    /// The workload fills and sorts a fixed integer array. It is deterministic and does not depend on the repository.
    /// Run it in the same process as the measured work, close to it, so both see the same machine load.
    /// </remarks>
    /// <param name="samples">The sample count, at least <see cref="MinimumSamples"/>.</param>
    /// <param name="clock">The clock. <see langword="null"/> means <see cref="TimeProvider.System"/>.</param>
    /// <returns>The median time of the workload.</returns>
    /// <exception cref="InvalidOperationException">The clock measured no time, so no unit exists.</exception>
    public static TimeSpan CalibrationUnit(int samples = MinimumSamples, TimeProvider? clock = null)
    {
        TimeSpan unit = MedianTime(ReferenceWorkload, samples, DefaultWarmups, clock);
        return unit > TimeSpan.Zero ? unit : throw new InvalidOperationException("The reference workload measured zero time, so there is no calibration unit.");
    }

    /// <summary>Expresses a time as a multiple of a calibration unit.</summary>
    /// <param name="elapsed">The measured time.</param>
    /// <param name="unit">The calibration unit from <see cref="CalibrationUnit"/>. It must be positive.</param>
    /// <returns>How many units the time spans.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The unit is not positive.</exception>
    public static double InUnits(TimeSpan elapsed, TimeSpan unit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(unit, TimeSpan.Zero);
        return elapsed / unit;
    }

    /// <summary>Gets how the time grows from a smaller input to a larger one.</summary>
    /// <remarks>
    /// Doubling the input of a linear algorithm gives a ratio near 2. A quadratic one gives near 4. The ratio does not
    /// depend on the machine speed, so a threshold such as "20,000 rows cost at most 2.5 times 10,000 rows" is portable.
    /// </remarks>
    /// <param name="smaller">The work on the smaller input.</param>
    /// <param name="larger">The work on the larger input.</param>
    /// <param name="samples">The sample count of each side, at least <see cref="MinimumSamples"/>.</param>
    /// <param name="warmups">Warm-up runs of each side.</param>
    /// <param name="clock">The clock. <see langword="null"/> means <see cref="TimeProvider.System"/>.</param>
    /// <returns>The median time of the larger work divided by the median time of the smaller work.</returns>
    /// <exception cref="InvalidOperationException">The smaller work measured zero time.</exception>
    public static double ScaleRatio(Action smaller, Action larger, int samples = MinimumSamples, int warmups = DefaultWarmups, TimeProvider? clock = null)
    {
        TimeSpan small = MedianTime(smaller, samples, warmups, clock);
        TimeSpan large = MedianTime(larger, samples, warmups, clock);
        return small > TimeSpan.Zero
            ? large / small
            : throw new InvalidOperationException("The smaller work measured zero time, so there is no scale ratio.");
    }

    /// <summary>Gets the median number of bytes that the work allocates on the calling thread.</summary>
    /// <remarks>This count does not depend on the machine speed. The work must run synchronously on the calling thread.</remarks>
    /// <param name="work">The work to count.</param>
    /// <param name="samples">The sample count, at least <see cref="MinimumSamples"/>.</param>
    /// <param name="warmups">Runs that are discarded before sampling, at least 0.</param>
    /// <returns>The median allocated bytes of one run.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample count is below the minimum or the warm-up count is negative.</exception>
    public static long MedianAllocatedBytes(Action work, int samples = MinimumSamples, int warmups = DefaultWarmups)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidateCounts(samples, warmups);
        for (int run = 0; run < warmups; run++)
        {
            work();
        }
        var allocated = new long[samples];
        for (int run = 0; run < samples; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            work();
            allocated[run] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(allocated);
        return allocated[samples / 2];
    }

    private static void ValidateCounts(int samples, int warmups)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, MinimumSamples);
        ArgumentOutOfRangeException.ThrowIfNegative(warmups);
    }

    private static void ReferenceWorkload()
    {
        var values = new int[ReferenceLength];
        uint state = 2463534242;
        for (int index = 0; index < values.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            values[index] = (int)state;
        }
        Array.Sort(values);
        GC.KeepAlive(values);
    }
}
