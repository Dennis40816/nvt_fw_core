// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
using Nvt.Core.Time;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

public sealed partial class ManagedLauncherEntryCoordinatorTests
{
    /// <summary>Each source-supported duration override must be positive, in source check order.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ConstructorRejectsEveryNonpositiveDurationOverride(long ticks)
    {
        string root = Root("duration-guards");
        var state = new EntryStateStore(BoundState(root));
        var probe = new RecordingRootProbe(ManagedInstallationRootStatus.Present);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        TimeSpan invalid = TimeSpan.FromTicks(ticks);
        Assert.Equal("admissionOperationCutoff", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(root, state, probe, handoff, admissionDeadline: invalid)).ParamName);
        Assert.Equal("completionOperationCutoff", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(root, state, probe, handoff, completionDeadline: invalid)).ParamName);
        Assert.Equal("healthObservationDeadline", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(root, state, probe, handoff, healthObservationDeadline: invalid)).ParamName);
    }

    /// <summary>The smallest representable positive durations remain explicit valid configuration.</summary>
    [Fact]
    public void ConstructorAcceptsOneTickDurationOverrides()
    {
        string root = Root("one-tick");
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        Assert.NotNull(Create(root, new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present),
            handoff,
            TimeSpan.FromTicks(1), TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)));
    }

    /// <summary>Every configurable timer accepts the exact BCL maximum.</summary>
    [Fact]
    public async Task ConstructorAcceptsEveryMaximumTimerDuration()
    {
        string root = Root("maximum-duration");
        TimeSpan maximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        var state = new EntryStateStore(BoundState(root));
        var probe = new RecordingRootProbe(ManagedInstallationRootStatus.Present);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        Assert.NotNull(Create(root, state, probe, handoff, admissionDeadline: maximum));
        Assert.NotNull(Create(root, state, probe, handoff, completionDeadline: maximum));
        Assert.NotNull(Create(root, state, probe, handoff, healthObservationDeadline: maximum));
        ManagedLauncherEntryCoordinator coordinator = Create(root, state, probe, handoff,
            maximum, maximum, maximum);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.LaunchInstalled, result.Outcome);
    }

    /// <summary>Every oversized timer fails during construction, naming its public parameter.</summary>
    [Fact]
    public void ConstructorRejectsEveryDurationOneMillisecondAboveTimerMaximum()
    {
        string root = Root("oversized-duration");
        TimeSpan invalid = TimeSpan.FromMilliseconds(uint.MaxValue);
        var state = new EntryStateStore(BoundState(root));
        var probe = new RecordingRootProbe(ManagedInstallationRootStatus.Present);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        Assert.Equal("admissionOperationCutoff", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(root, state, probe, handoff, admissionDeadline: invalid)).ParamName);
        Assert.Equal("completionOperationCutoff", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(root, state, probe, handoff, completionDeadline: invalid)).ParamName);
        Assert.Equal("healthObservationDeadline", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(root, state, probe, handoff, healthObservationDeadline: invalid)).ParamName);
    }

    /// <summary>Cancellation at the largest admitted operation cannot overflow the cleanup timer.</summary>
    [Fact]
    public async Task MaximumAdmissionCutoffStillAllowsCancellationCleanup()
    {
        string root = Root("maximum-admission-cleanup");
        using var cancellation = new CancellationTokenSource();
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed)
        {
            AdmissionAction = () => Cancel(cancellation),
        };
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present), handoff,
            admissionDeadline: TimeSpan.FromMilliseconds(uint.MaxValue - 1));

        ManagedLauncherEntryResult result = await coordinator.RunAsync(cancellation.Token);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.Equal(1, handoff.CompletionWaitCount);
    }

    /// <summary>Completion at the inclusive timer maximum remains termination-unconfirmed.</summary>
    [Fact]
    public async Task MaximumCompletionCutoffTimesOutAtExactBoundary()
    {
        string root = Root("maximum-completion-cutoff");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        TimeSpan maximum = TimeSpan.FromMilliseconds(4_294_967_294);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready)
        {
            CompletionAction = () => time.Advance(maximum),
        };
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present), handoff,
            completionDeadline: maximum, timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.TerminationUnconfirmed, result.Outcome);
        Assert.Equal(1, handoff.CompletionWaitCount);
        Assert.Equal(1, handoff.StartCount);
    }

    /// <summary>The cancellation decision and remaining budget share one clock sample.</summary>
    [Fact]
    public async Task PreAdmissionDecisionSamplesClockOnce()
    {
        string root = Root("single-admission-sample");
        var manual = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        // Guard: the awaited handoff callbacks and timestamp delegate serialize these observations.
        int? admissionReads = null;
        int? readsAtAdmission = null;
        var time = new ManualTimerDelegateProvider(manual, () =>
        {
            if (admissionReads is { } count)
            {
                admissionReads = count + 1;
            }
            return manual.GetTimestamp();
        });
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready)
        {
            StartAction = () => admissionReads = 0,
            AdmissionAction = () => { readsAtAdmission = admissionReads; admissionReads = null; },
        };
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present), handoff, timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.LaunchInstalled, result.Outcome);
        Assert.Equal(1, readsAtAdmission);
    }

    /// <summary>The counting provider's timers fire only when the manual clock reaches their due time.</summary>
    [Theory]
    [InlineData(249, 0)]
    [InlineData(250, 1)]
    public void CountingProviderTimersFollowTheManualClock(int advanceMilliseconds, int expectedFired)
    {
        var manual = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var time = new ManualTimerDelegateProvider(manual, manual.GetTimestamp);
        int fired = 0;
        using ITimer timer = time.CreateTimer(_ => { _ = Interlocked.Increment(ref fired); }, null,
            ManagedLauncherEntryCoordinator.DefaultHealthObservationDeadline, Timeout.InfiniteTimeSpan);

        manual.Advance(TimeSpan.FromMilliseconds(advanceMilliseconds));

        Assert.Equal(expectedFired, Volatile.Read(ref fired));
    }

    /// <summary>Exact health expiry stops before handoff, including an uncooperative returned observation.</summary>
    [Theory]
    [InlineData(249, ManagedLauncherEntryOutcome.LaunchInstalled)]
    [InlineData(250, ManagedLauncherEntryOutcome.HealthUnavailable)]
    [InlineData(251, ManagedLauncherEntryOutcome.HealthUnavailable)]
    public async Task HealthObservationHonorsExactBoundary(int milliseconds, ManagedLauncherEntryOutcome expected)
    {
        string root = Root("health-boundary");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(BoundState(root)),
            new AdvancingRootProbe(time, TimeSpan.FromMilliseconds(milliseconds)), handoff, timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expected == ManagedLauncherEntryOutcome.LaunchInstalled ? 1 : 0, handoff.StartCount);
    }

    /// <summary>Admission remains distinct from completion on both sides of five seconds.</summary>
    [Theory]
    [InlineData(4_999, ManagedLauncherEntryOutcome.LaunchInstalled)]
    [InlineData(5_000, ManagedLauncherEntryOutcome.HealthUnavailable)]
    [InlineData(5_001, ManagedLauncherEntryOutcome.HealthUnavailable)]
    public async Task AdmissionUsesExactFiveSecondBoundary(int milliseconds, ManagedLauncherEntryOutcome expected)
    {
        string root = Root("admission-boundary");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready)
        {
            AdmissionAction = () => time.Advance(TimeSpan.FromMilliseconds(milliseconds)),
        };
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present), handoff, timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        TimeSpan totalDuration = result.TotalElapsed;
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), totalDuration);
        Assert.Equal(1, handoff.CompletionWaitCount);
    }

    /// <summary>A zero remaining cleanup budget cannot authorize another observation.</summary>
    [Theory]
    [InlineData(5_499, ManagedLauncherEntryOutcome.HealthUnavailable, 1)]
    [InlineData(5_500, ManagedLauncherEntryOutcome.TerminationUnconfirmed, 0)]
    [InlineData(5_501, ManagedLauncherEntryOutcome.TerminationUnconfirmed, 0)]
    public async Task CleanupUsesExactFiveHundredMillisecondReserve(
        int milliseconds, ManagedLauncherEntryOutcome expected, int completionWaits)
    {
        string root = Root("cleanup-boundary");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Unavailable)
        {
            AdmissionAction = () => time.Advance(TimeSpan.FromMilliseconds(milliseconds)),
        };
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present), handoff, timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(completionWaits, handoff.CompletionWaitCount);
    }

    /// <summary>READY must precede the exact 44,500-millisecond operation cutoff.</summary>
    [Theory]
    [InlineData(44_499, ManagedLauncherEntryOutcome.LaunchInstalled)]
    [InlineData(44_500, ManagedLauncherEntryOutcome.TerminationUnconfirmed)]
    [InlineData(44_501, ManagedLauncherEntryOutcome.TerminationUnconfirmed)]
    public async Task CompletionUsesExactDefaultBoundary(int milliseconds, ManagedLauncherEntryOutcome expected)
    {
        string root = Root("completion-boundary");
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready)
        {
            CompletionAction = () => time.Advance(TimeSpan.FromMilliseconds(milliseconds)),
        };
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(BoundState(root)),
            new RecordingRootProbe(ManagedInstallationRootStatus.Present), handoff, timeProvider: time);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        TimeSpan totalDuration = result.TotalElapsed;
        TimeSpan admissionDuration = result.AdmissionElapsed;
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), totalDuration);
        Assert.Equal(TimeSpan.Zero, admissionDuration);
    }

    /// <summary>Frozen entry normalization accepts relative defaults; plan admission has a separate absolute-root guard.</summary>
    [Theory]
    [InlineData(".")]
    [InlineData("relative-default")]
    public async Task RelativeDefaultRootIsNormalizedBeforeObservation(string root)
    {
        var roots = new RecordingRootProbe(ManagedInstallationRootStatus.Absent);
        using var handoff = new RecordingBootstrapHandoff(ImmutableBootstrapCompletionOutcome.Ready);
        ManagedLauncherEntryCoordinator coordinator = Create(root, new EntryStateStore(state: null), roots, handoff);

        ManagedLauncherEntryResult result = await coordinator.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherEntryOutcome.SetupRequired, result.Outcome);
        Assert.Equal(ManagedRootPathIdentity.Normalize(root), result.ManagedRoot);
        Assert.True(Path.IsPathFullyQualified(Assert.IsType<string>(result.ManagedRoot)));
        Assert.Equal(1, roots.ObserveCount);
        Assert.Equal(0, handoff.StartCount);
    }

    // Counts timestamp reads only: clock reads and every timer stay with the shared manual provider.
    private sealed class ManualTimerDelegateProvider(ManualTimeProvider manual, Func<long> timestamp)
        : DelegateTimeProvider(manual.GetUtcNow, timestamp, manual.TimestampFrequency)
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            manual.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class AdvancingRootProbe(ManualTimeProvider time, TimeSpan elapsed) : IManagedInstallationRootProbe
    {
        public ValueTask<ManagedInstallationRootObservation> ObserveAsync(string managedRoot,
            CancellationToken cancellationToken)
        {
            time.Advance(elapsed);
            return ValueTask.FromResult(new ManagedInstallationRootObservation(ManagedInstallationRootStatus.Present));
        }
    }
}
