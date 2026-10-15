// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.TestSupport;
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
