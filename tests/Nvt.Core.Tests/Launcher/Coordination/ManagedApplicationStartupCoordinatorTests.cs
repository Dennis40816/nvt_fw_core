// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Coordination;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

/// <summary>Guards managed-child startup qualification and state reload behavior.</summary>
public sealed class ManagedApplicationStartupCoordinatorTests
{
    /// <summary>Only an accepted exact inherited READY write selects the bounded startup lease wait.</summary>
    [Theory]
    [InlineData(ApplicationReadySignalOutcome.NotInherited)]
    [InlineData(ApplicationReadySignalOutcome.InvalidInheritedContext)]
    [InlineData(ApplicationReadySignalOutcome.WriteFailed)]
    [InlineData(ApplicationReadySignalOutcome.Reported)]
    public async Task OnlyReportedReadyUsesBoundedManagedStartupInitialization(
        ApplicationReadySignalOutcome outcome)
    {
        ManagedAppVersion version = ManagedAppVersion.Parse("0.10.6");
        var signal = new FixedReadySignal(outcome);
        var experience = new RecordingExperience(Snapshot("durable"));
        var coordinator = new ManagedApplicationStartupCoordinator(version, signal, experience);

        ManagedApplicationStartupResult result = await coordinator.CompleteStartupAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(version, signal.ReportedVersion);
        Assert.Equal(outcome, result.ReadySignalOutcome);
        Assert.Equal("durable", result.Snapshot.State!.UpdateSource);
        Assert.Equal(0, experience.ReadOnlyInitializations);
        if (outcome == ApplicationReadySignalOutcome.Reported)
        {
            Assert.Equal(0, experience.ImmediateInitializations);
            Assert.True(experience.ManagedReadyInitialization);
        }
        else
        {
            Assert.Equal(1, experience.ImmediateInitializations);
            Assert.False(experience.ManagedReadyInitialization);
        }
    }

    /// <summary>Capture reports READY for every outcome, then reads without selecting either writer initialization.</summary>
    [Theory]
    [InlineData(ApplicationReadySignalOutcome.NotInherited)]
    [InlineData(ApplicationReadySignalOutcome.InvalidInheritedContext)]
    [InlineData(ApplicationReadySignalOutcome.WriteFailed)]
    [InlineData(ApplicationReadySignalOutcome.Reported)]
    public async Task ReadOnlyStartupPreservesReadySignalWithoutWriterInitialization(ApplicationReadySignalOutcome outcome)
    {
        ManagedAppVersion version = ManagedAppVersion.Parse("0.10.6");
        ManagedMutationSnapshot snapshot = Snapshot("durable");
        var signal = new FixedReadySignal(outcome);
        var experience = new RecordingExperience(snapshot);
        var coordinator = new ManagedApplicationStartupCoordinator(version, signal, experience);

        ManagedApplicationStartupResult result = await coordinator.CompleteStartupAsync(
            TestContext.Current.CancellationToken, isReadOnly: true);

        Assert.Equal(version, signal.ReportedVersion);
        Assert.Equal(1, signal.ReportCount);
        Assert.Equal(new ManagedApplicationStartupResult(outcome, snapshot), result);
        Assert.Equal(1, experience.ReadOnlyInitializations);
        Assert.Equal(0, experience.ImmediateInitializations);
        Assert.False(experience.ManagedReadyInitialization);
    }

    private static ManagedMutationSnapshot Snapshot(string updateSource)
    {
        return new(State(updateSource), ManagedVersionInventory.Create([]), VersionManagerStateLoadIssue.None);
    }

    private static VersionManagerState State(string updateSource)
    {
        return VersionManagerState.Create(
            updateSource,
            activeVersion: null,
            lastKnownGoodVersion: null,
            admissions: [],
            pendingActivation: null,
            failedActivationVersion: null,
            retentionReviewDue: false,
            managedRootIdentity: "managed");
    }

    private sealed class FixedReadySignal(ApplicationReadySignalOutcome outcome)
        : IApplicationReadySignal
    {
        internal ManagedAppVersion? ReportedVersion { get; private set; }
        internal int ReportCount { get; private set; }

        public ValueTask<ApplicationReadySignalOutcome> ReportReadyAsync(
            ManagedAppVersion version,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportedVersion = version;
            ReportCount++;
            return ValueTask.FromResult(outcome);
        }
    }

    private sealed class RecordingExperience(ManagedMutationSnapshot snapshot)
        : IManagedApplicationInitialization
    {
        internal int ImmediateInitializations { get; private set; }
        internal int ReadOnlyInitializations { get; private set; }

        internal bool ManagedReadyInitialization { get; private set; }

        public ValueTask<ManagedMutationSnapshot> InitializeAsync(CancellationToken cancellationToken, bool isReadOnly = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isReadOnly) { ReadOnlyInitializations++; }
            else { ImmediateInitializations++; }
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask<ManagedMutationSnapshot> InitializeAfterManagedReadyAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedReadyInitialization = true;
            return ValueTask.FromResult(snapshot);
        }

    }
}
