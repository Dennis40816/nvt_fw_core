// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Nvt.Core.Processes;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>Characterizes frozen runner timing, admission, launch inputs, and native output boundaries.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class SystemExternalProcessRunnerBoundaryTests
{
    private static readonly string[] LaunchArguments = ["", "two words", "quote\"inside", "trail\\", "工具"];
    private static readonly string[] PhaseNames =
    [
        "Started", "ExitSignaled", "TimeoutSignaled", "CancellationSignaled", "OutputHeldAfterExit",
        "TerminationStarted", "ReaderStopRequested", "Returning", "Detached", "ResourcesReleased", "ResourcesReleaseFailed",
    ];
    // Watchdog only. The production cleanup deadline is also 5 s, so a 5 s wait raced the runner's own release.
    private static readonly TimeSpan _releaseBound = TimeSpan.FromSeconds(30);

    /// <summary>The fixed production mechanism retains eight slots, the three timing values, and a null observer.</summary>
    [Fact]
    public void ProductionDefaultsRetainFrozenMechanismBounds()
    {
        Assert.Equal(8, ExternalProcessCapacity.DefaultLimit);
        Assert.Equal(8, ExternalProcessRunnerSeams.Production.Capacity.Limit);
        Assert.Equal(TimeSpan.FromSeconds(5), ExternalProcessCleanupTiming.Default.Deadline);
        Assert.Equal(TimeSpan.FromSeconds(2), ExternalProcessCleanupTiming.Default.HeldOutputGrace);
        Assert.Equal(TimeSpan.FromSeconds(1), ExternalProcessCleanupTiming.Default.ReaderStopReserve);
        Assert.Equal(ExternalProcessCleanupTiming.Default, ExternalProcessRunnerSeams.Production.Timing);
        Assert.Null(ExternalProcessRunnerSeams.Production.Observe);
        Assert.Same(TimeProvider.System, ExternalProcessRunnerSeams.Production.Time);
        ExternalProcessCleanupTiming.Default.Validate();
        Assert.Equal(PhaseNames, Enum.GetNames<ExternalProcessRunnerPhase>());
        Assert.Equal(Enumerable.Range(0, PhaseNames.Length), Enum.GetValues<ExternalProcessRunnerPhase>().Select(phase => (int)phase));
    }

    /// <summary>Schedule(1000) uses the frozen absolute Stopwatch tick calculations and one total deadline.</summary>
    [Fact]
    public void DefaultScheduleUsesExactAbsoluteTicks()
    {
        CleanupSchedule schedule = ExternalProcessCleanupTiming.Default.Schedule(1000);
        Assert.Equal(1000 + 2 * Stopwatch.Frequency, schedule.HeldOutputGraceAt);
        Assert.Equal(1000 + 4 * Stopwatch.Frequency, schedule.ReaderStopAt);
        Assert.Equal(1000 + 5 * Stopwatch.Frequency, schedule.DeadlineAt);
        Assert.Equal(Stopwatch.Frequency, schedule.DeadlineAt - schedule.ReaderStopAt);
    }

    /// <summary>Fractional spans truncate each timestamp conversion before subtracting the stop reserve.</summary>
    [Theory]
    [InlineData(2L, 1L, 1L)]
    [InlineData(3L, 1L, 1L)]
    [InlineData(12345678L, 2345678L, 3456789L)]
    [InlineData(21474836470000L, 1L, 1L)]
    public void SchedulePreservesFractionalTickCalculations(long deadline, long grace, long reserve)
    {
        var timing = new ExternalProcessCleanupTiming(TimeSpan.FromTicks(deadline), TimeSpan.FromTicks(grace), TimeSpan.FromTicks(reserve));
        timing.Validate();
        long expectedDeadline = (long)(timing.Deadline.TotalSeconds * Stopwatch.Frequency);
        long expectedGrace = (long)(timing.HeldOutputGrace.TotalSeconds * Stopwatch.Frequency);
        long expectedReserve = (long)(timing.ReaderStopReserve.TotalSeconds * Stopwatch.Frequency);
        Assert.Equal(expectedDeadline, ExternalProcessCleanupTiming.Ticks(timing.Deadline));
        Assert.Equal(expectedGrace, ExternalProcessCleanupTiming.Ticks(timing.HeldOutputGrace));
        Assert.Equal(expectedReserve, ExternalProcessCleanupTiming.Ticks(timing.ReaderStopReserve));
        Assert.Equal(new CleanupSchedule(1000 + expectedGrace, 1000 + expectedDeadline - expectedReserve, 1000 + expectedDeadline),
            timing.Schedule(1000));
    }

    /// <summary>Each strictly positive timing field rejects zero and negative values with its exact parameter name.</summary>
    [Theory]
    [InlineData("Deadline", 0L)]
    [InlineData("Deadline", -1L)]
    [InlineData("Deadline", long.MinValue)]
    [InlineData("HeldOutputGrace", 0L)]
    [InlineData("HeldOutputGrace", -1L)]
    [InlineData("HeldOutputGrace", long.MinValue)]
    [InlineData("ReaderStopReserve", 0L)]
    [InlineData("ReaderStopReserve", -1L)]
    [InlineData("ReaderStopReserve", long.MinValue)]
    public void TimingRejectsEveryNonpositiveField(string parameter, long ticks)
    {
        TimeSpan value = TimeSpan.FromTicks(ticks);
        ExternalProcessCleanupTiming timing = parameter switch
        {
            "Deadline" => ExternalProcessCleanupTiming.Default with { Deadline = value },
            "HeldOutputGrace" => ExternalProcessCleanupTiming.Default with { HeldOutputGrace = value },
            _ => ExternalProcessCleanupTiming.Default with { ReaderStopReserve = value },
        };
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(timing.Validate);
        Assert.Equal(parameter, failure.ParamName);
        Assert.Equal(value, failure.ActualValue);
    }

    /// <summary>Validation checks deadline, grace, reserve, their sum, then the timer maximum in that order.</summary>
    [Fact]
    public void TimingValidationPreservesExceptionOrder()
    {
        Assert.Equal("Deadline", Assert.Throws<ArgumentOutOfRangeException>(() => default(ExternalProcessCleanupTiming).Validate()).ParamName);
        Assert.Equal("HeldOutputGrace", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExternalProcessCleanupTiming(TimeSpan.FromTicks(1), TimeSpan.Zero, TimeSpan.Zero).Validate()).ParamName);
        Assert.Equal("ReaderStopReserve", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExternalProcessCleanupTiming(TimeSpan.FromTicks(1), TimeSpan.FromTicks(1), TimeSpan.Zero).Validate()).ParamName);
        Assert.Equal("HeldOutputGrace", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExternalProcessCleanupTiming(
                TimeSpan.FromMilliseconds(int.MaxValue) + TimeSpan.FromTicks(1),
                TimeSpan.FromMilliseconds(int.MaxValue) + TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)).Validate()).ParamName);
    }

    /// <summary>One tick is valid for grace and reserve; the minimum compatible deadline is their two-tick sum.</summary>
    [Fact]
    public void MinimumPositiveTimingValuesRemainAccepted()
    {
        var timing = new ExternalProcessCleanupTiming(TimeSpan.FromTicks(2), TimeSpan.FromTicks(1), TimeSpan.FromTicks(1));
        timing.Validate();
        Assert.Equal("HeldOutputGrace", Assert.Throws<ArgumentOutOfRangeException>(() =>
            (timing with { Deadline = TimeSpan.FromTicks(1) }).Validate()).ParamName);
        (timing with { Deadline = TimeSpan.FromTicks(3) }).Validate();
    }

    /// <summary>Grace plus reserve permits the exact deadline and the tick below it, but rejects the tick above.</summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    public void GracePlusReservePreservesExactDeadlineBoundary(long offset)
    {
        var timing = new ExternalProcessCleanupTiming(TimeSpan.FromTicks(10), TimeSpan.FromTicks(6 + offset), TimeSpan.FromTicks(4));
        if (offset > 0)
        {
            ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(timing.Validate);
            Assert.Equal("HeldOutputGrace", failure.ParamName);
            Assert.Equal(TimeSpan.FromTicks(11), failure.ActualValue);
        }
        else
        {
            timing.Validate();
        }
    }

    /// <summary>The reserve participates in the same exact sum boundary as grace.</summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    public void ReaderReservePreservesExactDeadlineBoundary(long offset)
    {
        var timing = new ExternalProcessCleanupTiming(TimeSpan.FromTicks(10), TimeSpan.FromTicks(6), TimeSpan.FromTicks(4 + offset));
        if (offset > 0)
        {
            Assert.Equal("HeldOutputGrace", Assert.Throws<ArgumentOutOfRangeException>(timing.Validate).ParamName);
        }
        else
        {
            timing.Validate();
        }
    }

    /// <summary>The maximum deadline is int.MaxValue milliseconds, characterized one tick below and above.</summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    public void DeadlinePreservesTimerMaximumBoundary(long offset)
    {
        TimeSpan deadline = TimeSpan.FromMilliseconds(int.MaxValue) + TimeSpan.FromTicks(offset);
        ExternalProcessCleanupTiming timing = ExternalProcessCleanupTiming.Default with { Deadline = deadline };
        if (offset > 0)
        {
            ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(timing.Validate);
            Assert.Equal("Deadline", failure.ParamName);
            Assert.Equal(deadline, failure.ActualValue);
        }
        else
        {
            timing.Validate();
        }
    }

    /// <summary>Overflow while adding grace and reserve retains the source's overflow before deadline ceiling validation.</summary>
    [Fact]
    public void TimingSumOverflowPreservesSourceFailure()
    {
        _ = Assert.Throws<OverflowException>(() =>
            new ExternalProcessCleanupTiming(TimeSpan.MaxValue, TimeSpan.MaxValue, TimeSpan.FromTicks(1)).Validate());
    }

    /// <summary>Nonpositive private capacity limits preserve the frozen constructor failure.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void CapacityRejectsNonpositiveLimits(int limit)
    {
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() => new ExternalProcessCapacity(limit));
        Assert.Equal("limit", failure.ParamName);
        Assert.Null(failure.ActualValue);
    }

    /// <summary>Private capacities one and seven/eight/nine admit exactly the limit, refuse one more, and release every slot.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void CapacityReservesRefusesAndReleasesAtExactLimit(int limit)
    {
        var capacity = new ExternalProcessCapacity(limit);
        Assert.Equal(limit, capacity.Limit);
        Assert.Equal(0, capacity.InUse);
        for (int count = 1; count <= limit; count++)
        {
            Assert.True(capacity.TryReserve(out int observed));
            Assert.Equal(count, observed);
            Assert.Equal(count, capacity.InUse);
        }
        Assert.False(capacity.TryReserve(out int refused));
        Assert.Equal(limit, refused);
        Assert.Equal(limit, capacity.InUse);
        capacity.Release();
        Assert.Equal(limit - 1, capacity.InUse);
        Assert.True(capacity.TryReserve(out int readmitted));
        Assert.Equal(limit, readmitted);
        for (int remaining = limit - 1; remaining >= 0; remaining--)
        {
            capacity.Release();
            Assert.Equal(remaining, capacity.InUse);
        }
    }

    /// <summary>The largest positive private test capacity is retained without changing production's fixed eight slots.</summary>
    [Fact]
    public void CapacityAcceptsLargestPositivePrivateLimit()
    {
        var capacity = new ExternalProcessCapacity(int.MaxValue);
        Assert.True(capacity.TryReserve(out int observed));
        Assert.Equal(1, observed);
        capacity.Release();
        Assert.Equal(0, capacity.InUse);
        Assert.Equal(int.MaxValue, capacity.Limit);
    }

    /// <summary>The constructor rejects a null seam and validates only the frozen timing contract.</summary>
    [Fact]
    public void RunnerRejectsNullSeamsAndInvalidTiming()
    {
        Assert.Equal("seams", Assert.Throws<ArgumentNullException>(() => new SystemExternalProcessRunner(null!)).ParamName);
        Assert.Equal("Deadline", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with { Timing = default })).ParamName);
        ExternalProcessRunnerSeams seams = ExternalProcessRunnerSeams.Production with
        {
            Timing = new ExternalProcessCleanupTiming(TimeSpan.FromTicks(2), TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)),
        };
        Assert.NotNull(new SystemExternalProcessRunner(seams));
    }

    /// <summary>The runner constructor applies every timing rejection before it can start an invocation.</summary>
    [Theory]
    [InlineData(0, "Deadline")]
    [InlineData(1, "HeldOutputGrace")]
    [InlineData(2, "ReaderStopReserve")]
    [InlineData(3, "HeldOutputGrace")]
    [InlineData(4, "Deadline")]
    public void RunnerValidatesEveryTimingConstraint(int scenario, string parameter)
    {
        ExternalProcessCleanupTiming timing = scenario switch
        {
            0 => ExternalProcessCleanupTiming.Default with { Deadline = TimeSpan.Zero },
            1 => ExternalProcessCleanupTiming.Default with { HeldOutputGrace = TimeSpan.Zero },
            2 => ExternalProcessCleanupTiming.Default with { ReaderStopReserve = TimeSpan.Zero },
            3 => ExternalProcessCleanupTiming.Default with { HeldOutputGrace = TimeSpan.FromSeconds(5) },
            _ => ExternalProcessCleanupTiming.Default with
            {
                Deadline = TimeSpan.FromMilliseconds(int.MaxValue) + TimeSpan.FromTicks(1),
            },
        };
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with { Timing = timing })).ParamName);
    }

    /// <summary>Null input wins over already-canceled input and neither takes capacity.</summary>
    [Fact]
    public async Task NullStartInfoPrecedesCancellationWithoutReservation()
    {
        var capacity = new ExternalProcessCapacity(1);
        var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with { Capacity = capacity });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        ArgumentNullException failure = await Assert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(null!, cancellation.Token).AsTask());
        Assert.Equal("startInfo", failure.ParamName);
        Assert.Equal(0, capacity.InUse);
    }

    /// <summary>Already-canceled input precedes full-capacity refusal and any OS launch attempt.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlreadyCanceledInputReservesNoCapacity(bool full)
    {
        var capacity = new ExternalProcessCapacity(1);
        if (full)
        {
            Assert.True(capacity.TryReserve(out _));
        }
        try
        {
            var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with { Capacity = capacity });
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancellation.Cancel();
            OperationCanceledException failure = await Assert.ThrowsAsync<OperationCanceledException>(() =>
                runner.RunAsync(MissingExecutable(), cancellation.Token).AsTask());
            Assert.Equal(cancellation.Token, failure.CancellationToken);
            Assert.Equal(full ? 1 : 0, capacity.InUse);
        }
        finally
        {
            if (full)
            {
                capacity.Release();
            }
        }
    }

    /// <summary>A private one-slot refusal precedes starting even a missing executable and retains the observed count.</summary>
    [Fact]
    public async Task OneSlotRefusalStartsNoMissingExecutable()
    {
        var capacity = new ExternalProcessCapacity(1);
        Assert.True(capacity.TryReserve(out _));
        try
        {
            var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with { Capacity = capacity });
            ExternalProcessCleanupCapacityException failure = await Assert.ThrowsAsync<ExternalProcessCleanupCapacityException>(() =>
                runner.RunAsync(MissingExecutable(), TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(1, failure.Limit);
            Assert.Equal(1, failure.InUseInvocations);
            Assert.Equal(1, capacity.InUse);
        }
        finally
        {
            capacity.Release();
        }
        Assert.Equal(0, capacity.InUse);
    }

    /// <summary>ProcessStartInfo preserves argument order and content without constructing a command line.</summary>
    [Fact]
    public void LaunchInfoPreservesArgumentsAndWorkingDirectory()
    {
        var request = new ExternalProcessStartInfo(" relative tool ", " relative folder ", LaunchArguments, TimeSpan.FromTicks(1));
        ProcessStartInfo actual = SystemExternalProcessRunner.CreateProcessStartInfo(request);
        Assert.Equal(request.ExecutablePath, actual.FileName);
        Assert.Equal(request.WorkingDirectory, actual.WorkingDirectory);
        Assert.Equal(LaunchArguments, actual.ArgumentList);
        Assert.Empty(actual.Arguments);
        Assert.False(actual.UseShellExecute);
        Assert.True(actual.CreateNoWindow);
        Assert.True(actual.RedirectStandardOutput);
        Assert.True(actual.RedirectStandardError);
    }

    /// <summary>A natural exit retains code seven, full cleanup, and private capacity release within five seconds.</summary>
    [Fact]
    public async Task NaturalExitRetainsSevenAndReleasesCapacity()
    {
        RequireWindows();
        var capacity = new ExternalProcessCapacity(1);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var phases = new List<ExternalProcessRunnerPhase>();
        var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with
        {
            Capacity = capacity,
            Observe = phase =>
            {
                lock (phases)
                {
                    phases.Add(phase);
                }
                if (phase == ExternalProcessRunnerPhase.ResourcesReleased)
                {
                    _ = released.TrySetResult();
                }
            },
        });
        ExternalProcessResult result = await runner.RunAsync(
            Probe("exit", ["--exit-code", "7"]), TestContext.Current.CancellationToken);
        Assert.Equal(7, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal(ExternalProcessCleanup.Complete, result.Cleanup);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
        await released.Task.WaitAsync(_releaseBound, TestContext.Current.CancellationToken);
        Assert.Equal(0, capacity.InUse);
        lock (phases)
        {
            Assert.Equal(ExternalProcessRunnerPhase.Started, phases[0]);
            Assert.True(phases.IndexOf(ExternalProcessRunnerPhase.ExitSignaled) < phases.IndexOf(ExternalProcessRunnerPhase.Returning));
            Assert.Equal(ExternalProcessRunnerPhase.ResourcesReleased, phases[^1]);
            Assert.DoesNotContain(ExternalProcessRunnerPhase.TerminationStarted, phases);
            Assert.DoesNotContain(ExternalProcessRunnerPhase.ReaderStopRequested, phases);
        }
    }

    /// <summary>Both real streams preserve separate routing at zero and one output character.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task DualOutputRoutesEachStreamExactly(int count)
    {
        RequireWindows();
        string countText = count.ToString(CultureInfo.InvariantCulture);
        ExternalProcessResult result = await new SystemExternalProcessRunner().RunAsync(
            Probe("dual-output-exit", ["--out-count", countText, "--err-count", countText]), TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ExternalProcessCleanup.Complete, result.Cleanup);
        Assert.Equal(new string('A', count) + "OUT-END", result.StandardOutput);
        Assert.Equal(new string('B', count) + "ERR-END", result.StandardError);
    }

    /// <summary>The actual runner captures both streams one character below, at, and above the fixed output ceiling.</summary>
    [Theory]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    public async Task DualOutputPreservesCaptureLimitBoundaries(int totalLength)
    {
        RequireWindows();
        string countText = (totalLength - "OUT-END".Length).ToString(CultureInfo.InvariantCulture);
        ExternalProcessResult result = await new SystemExternalProcessRunner().RunAsync(
            Probe("dual-output-exit", ["--out-count", countText, "--err-count", countText]), TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal(ExternalProcessCleanup.Complete, result.Cleanup);
        Assert.Equal(Math.Min(totalLength, BoundedProcessOutputReader.MaximumCapturedCharacters), result.StandardOutput.Length);
        Assert.Equal(Math.Min(totalLength, BoundedProcessOutputReader.MaximumCapturedCharacters), result.StandardError.Length);
        if (totalLength <= BoundedProcessOutputReader.MaximumCapturedCharacters)
        {
            Assert.Equal(new string('A', totalLength - 7) + "OUT-END", result.StandardOutput);
            Assert.Equal(new string('B', totalLength - 7) + "ERR-END", result.StandardError);
        }
        else
        {
            Assert.Contains(BoundedProcessOutputReader.TruncationMarker, result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains(BoundedProcessOutputReader.TruncationMarker, result.StandardError, StringComparison.Ordinal);
            Assert.EndsWith("OUT-END", result.StandardOutput, StringComparison.Ordinal);
            Assert.EndsWith("ERR-END", result.StandardError, StringComparison.Ordinal);
        }
    }

    /// <summary>Shared OS termination failures use the existing cleanup outcome and never schedule a second kill.</summary>
    [Theory]
    [InlineData("aggregate")]
    [InlineData("win32")]
    [InlineData("invalid-operation")]
    [InlineData("not-supported")]
    public async Task TerminationFailurePreservesExternalCleanupOutcome(string failureKind)
    {
        RequireWindows();
        var capacity = new ExternalProcessCapacity(1);
        var identity = new TaskCompletionSource<ProcessIdentity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int terminationCalls = 0;
        var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with
        {
            Capacity = capacity,
            Timing = new ExternalProcessCleanupTiming(
                TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100)),
            ObserveExit = (process, token) =>
            {
                _ = identity.TrySetResult(new ProcessIdentity(process.Id, process.StartTime));
                return process.WaitForExitAsync(token);
            },
            TerminateTree = process =>
            {
                _ = Interlocked.Increment(ref terminationCalls);
                throw failureKind switch
                {
                    "aggregate" => new AggregateException(new Win32Exception(5)),
                    "win32" => new Win32Exception(5),
                    "invalid-operation" => new InvalidOperationException("Synthetic termination failure."),
                    _ => new NotSupportedException("Synthetic termination failure."),
                };
            },
            Observe = phase =>
            {
                if (phase == ExternalProcessRunnerPhase.ResourcesReleased)
                {
                    _ = released.TrySetResult();
                }
            },
        });
        try
        {
            var request = new ExternalProcessStartInfo(ProcessProbe.Executable, Environment.CurrentDirectory,
                ["--mode", "silent-wait"], TimeSpan.FromTicks(1));
            ExternalProcessResult result = await runner.RunAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(-1, result.ExitCode);
            Assert.True(result.TimedOut);
            Assert.Equal(ExternalProcessCleanup.TerminationUnconfirmed, result.Cleanup);
            await released.Task.WaitAsync(_releaseBound, TestContext.Current.CancellationToken);
            Assert.Equal(0, capacity.InUse);
            Assert.Equal(1, Volatile.Read(ref terminationCalls));
        }
        finally
        {
            if (identity.Task.IsCompletedSuccessfully)
            {
                ProcessIdentity expected = await identity.Task;
                try
                {
                    using var process = Process.GetProcessById(expected.Id);
                    if (!process.HasExited && process.StartTime == expected.StartTime)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                            .WaitAsync(_releaseBound, TestContext.Current.CancellationToken);
                    }
                }
                catch (ArgumentException)
                {
                    // The synthetic process already exited.
                }
            }
        }
    }

    private readonly record struct ProcessIdentity(int Id, DateTime StartTime);

    private static ExternalProcessStartInfo MissingExecutable()
    {
        return new ExternalProcessStartInfo(Path.Combine(Environment.CurrentDirectory, $"core-missing-{Guid.NewGuid():N}.exe"),
            Environment.CurrentDirectory, [], TimeSpan.FromSeconds(5));
    }

    private static ExternalProcessStartInfo Probe(string mode, string[] arguments)
    {
        return new ExternalProcessStartInfo(ProcessProbe.Executable, Environment.CurrentDirectory,
            ["--mode", mode, .. arguments], TimeSpan.FromSeconds(10));
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }
    }
}
