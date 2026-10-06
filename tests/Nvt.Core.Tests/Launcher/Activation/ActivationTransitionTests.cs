// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using static Nvt.Core.Tests.Launcher.Activation.ActivationFixture;

namespace Nvt.Core.Tests.Launcher.Activation;

/// <summary>Characterizes every application and launcher transition phase and exact failure message.</summary>
public sealed class ActivationTransitionTests
{
    /// <summary>Application transitions preserve the source phase matrix, including ordinary active launches.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ApplicationTransitionsRequireTheirExactPhase(int phase)
    {
        VersionManagerState state = InPhase(phase);
        ManagedAppVersion candidate = phase == 3 ? Active : Candidate;
        AssertTransition(phase == -1, () => VersionActivationPolicy.BeginActivation(state, Candidate),
            "Another managed-version activation is already pending.");
        AssertTransition(phase == -1, () => VersionActivationPolicy.RecordActiveLaunch(state),
            "Another managed-version transaction is already pending.");
        AssertTransition(phase == 0, () => VersionActivationPolicy.RecordCandidateLaunch(state),
            "Candidate launch is not in the requested phase.");
        AssertTransition(phase == 0, () => VersionActivationPolicy.CancelRequestedActivation(state),
            "Only an unlaunched activation request can be cancelled.");
        AssertTransition(phase == 1, () => VersionActivationPolicy.CommitReady(state, candidate),
            "Ready signal does not match a durably recorded candidate launch.");
        AssertTransition(phase == 2, () => VersionActivationPolicy.CommitRollback(state, Older),
            "Rollback is not durably recorded.");
        AssertTransition(phase == 3, () => VersionActivationPolicy.ClearActiveLaunch(state, Active),
            "Active launch guard does not match the confirmed process.");

        ActivationRecoveryDecision failure = VersionActivationPolicy.FailActivation(state, candidate);
        ActivationRecoveryDecision rollback = VersionActivationPolicy.RecordRollbackLaunch(state, candidate);
        if (phase < 0)
        {
            Assert.Same(state, failure.State);
            Assert.Same(state, rollback.State);
            Assert.Null(failure.RollbackVersion);
            Assert.Null(rollback.RollbackVersion);
        }
        else
        {
            Assert.Equal(Older, failure.State.ActiveVersion);
            Assert.Equal(candidate, failure.State.FailedActivationVersion);
            Assert.Null(failure.State.PendingActivation);
            Assert.Equal(Older, failure.RollbackVersion);
            if (phase == 2)
            {
                Assert.Same(state, rollback.State);
                Assert.Null(rollback.RollbackVersion);
            }
            else
            {
                Assert.Equal(VersionActivationPhase.RollbackLaunchRecorded, rollback.State.PendingActivation?.Phase);
                Assert.Equal(Older, rollback.RollbackVersion);
                Assert.Equal(candidate, rollback.State.FailedActivationVersion);
            }
        }
    }

    /// <summary>Launcher transitions retain their stricter candidate-to-rollback phase order.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LauncherTransitionsRequireTheirExactPhase(int phase)
    {
        LauncherBootstrapState state = LauncherState(phase);
        AssertTransition(phase == -1, () => state.Begin(CreateLauncher(Candidate)),
            "Launcher candidate cannot begin from current state.");
        AssertTransition(phase == -1, state.RecordActiveLaunch,
            "Active launcher attempt cannot begin from current state.");
        AssertTransition(phase == 0, state.RecordCandidateLaunch,
            "Launcher candidate is not requested.");
        AssertTransition(phase == 1, state.RecordRollbackLaunch,
            "Launcher candidate launch is not recorded.");
        AssertTransition(phase == 1, state.CommitReady,
            "Launcher candidate readiness is not committable.");
        AssertTransition(phase == 2, state.CommitRollback,
            "Launcher rollback is not committable.");
        AssertTransition(phase == 3, () => state.ClearActiveLaunch(CreateLauncher(Active)),
            "Active launcher guard does not match confirmed process.");
        AssertTransition(phase >= 0, state.FailCandidate, "Launcher candidate is absent.");
    }

    /// <summary>Explicit activation can select an admitted older version and clears the previous failure.</summary>
    [Fact]
    public void HealthyAdmittedOlderVersionCanBeSelectedExplicitly()
    {
        ManagedVersionInventory inventory = ManagedVersionInventory.Create([Row(Older), Row(Active, active: true)]);
        Assert.Equal(ManagedVersionIntegrity.Healthy, inventory.Find(Older)?.Integrity);
        VersionManagerState initial = State(failed: Candidate);
        VersionManagerState requested = VersionActivationPolicy.BeginActivation(initial, Older);
        Assert.Null(requested.FailedActivationVersion);
        Assert.Equal(Older, requested.PendingActivation?.CandidateVersion);
        VersionManagerState committed = VersionActivationPolicy.CommitReady(
            VersionActivationPolicy.RecordCandidateLaunch(requested), Older);
        Assert.Equal(Older, committed.ActiveVersion);
        Assert.Equal(Older, committed.LastKnownGoodVersion);
        Assert.Equal(Active, initial.ActiveVersion);
        Assert.Equal(Candidate, initial.FailedActivationVersion);
    }

    /// <summary>Fallback is exactly the recorded admission even when a newer directory is admitted.</summary>
    [Fact]
    public void RollbackUsesRecordedAdmissionAndCannotOscillate()
    {
        VersionManagerState candidate = VersionActivationPolicy.RecordCandidateLaunch(
            VersionActivationPolicy.BeginActivation(State(), Candidate));
        ActivationRecoveryDecision recorded = VersionActivationPolicy.RecordRollbackLaunch(candidate, Candidate);
        Assert.Equal(Older, recorded.RollbackVersion);
        Assert.Equal(Active, recorded.State.ActiveVersion);
        Assert.Null(VersionActivationPolicy.RecordRollbackLaunch(recorded.State, Candidate).RollbackVersion);
        VersionManagerState committed = VersionActivationPolicy.CommitRollback(recorded.State, Older);
        Assert.Equal(Older, committed.ActiveVersion);
        Assert.Equal(Older, committed.LastKnownGoodVersion);
        Assert.Equal(Candidate, committed.FailedActivationVersion);
        Assert.Null(committed.PendingActivation);
        Assert.Null(VersionActivationPolicy.FailActivation(committed, Candidate).RollbackVersion);
    }

    /// <summary>Wrong ready and failure identities cannot close or replace an unrelated transaction.</summary>
    [Fact]
    public void WrongVersionCannotCommitOrRecoverAnotherCandidate()
    {
        VersionManagerState state = InPhase(1);
        Assert.Equal("Ready signal does not match a durably recorded candidate launch.",
            Assert.Throws<InvalidOperationException>(() => VersionActivationPolicy.CommitReady(state, Older)).Message);
        Assert.Same(state, VersionActivationPolicy.FailActivation(state, Older).State);
        Assert.Same(state, VersionActivationPolicy.RecordRollbackLaunch(state, Older).State);
        Assert.Equal("Ready fallback differs from the recorded rollback target.",
            Assert.Throws<InvalidOperationException>(() =>
                VersionActivationPolicy.CommitRollback(InPhase(2), Active)).Message);
        Assert.Throws<InvalidOperationException>(() =>
            VersionActivationPolicy.ClearActiveLaunch(InPhase(3), Older));
        Assert.Throws<InvalidOperationException>(() =>
            LauncherState(3).ClearActiveLaunch(CreateLauncher(Older)));
    }

    /// <summary>Missing or self-referential fallback never selects the failed candidate for another launch.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrFailedFallbackIsNotLaunched(bool selfFallback)
    {
        VersionManagerState state = VersionManagerState.Create(null, Active,
            selfFallback ? Candidate : null, Admissions,
            new(Candidate, Admission(Candidate).AdmissionIdentity, Active,
                selfFallback ? Candidate : null, VersionActivationPhase.CandidateLaunchRecorded),
            null, false);
        ActivationRecoveryDecision failure = VersionActivationPolicy.FailActivation(state, Candidate);
        Assert.Null(failure.RollbackVersion);
        Assert.Equal(Active, failure.State.ActiveVersion);
        Assert.Equal(selfFallback ? Candidate : (ManagedAppVersion?)null, failure.State.LastKnownGoodVersion);
        Assert.Null(VersionActivationPolicy.RecordRollbackLaunch(state, Candidate).RollbackVersion);
    }

    /// <summary>A requested cancellation preserves committed authority and the source failure field.</summary>
    [Fact]
    public void CancelRequestedActivationPreservesCommittedFields()
    {
        VersionManagerState initial = State(new(Candidate, Admission(Candidate).AdmissionIdentity,
            Active, Older), failed: Older);
        VersionManagerState cancelled = VersionActivationPolicy.CancelRequestedActivation(initial);
        Assert.Null(cancelled.PendingActivation);
        Assert.Equal(Older, cancelled.FailedActivationVersion);
        Assert.Equal(Active, cancelled.ActiveVersion);
        Assert.Equal(Older, cancelled.LastKnownGoodVersion);
        Assert.Equal(initial.ManagedRootIdentity, cancelled.ManagedRootIdentity);
        Assert.Equal(initial.UpdateSource, cancelled.UpdateSource);
        Assert.Equal(initial.Admissions, cancelled.Admissions);
    }

    /// <summary>Filesystem mutation guards reject both new activation and ordinary active launch before selection.</summary>
    [Fact]
    public void MutationGuardsPrecedeCandidateAndActiveSelection()
    {
        VersionManagerState state = State(mutation: new(ManagedVersionMutationKind.Delete, Admission(Older)));
        Assert.Equal("A managed-version filesystem mutation is still pending.",
            Assert.Throws<InvalidOperationException>(() =>
                VersionActivationPolicy.BeginActivation(state, ManagedAppVersion.Parse("9.0.0"))).Message);
        Assert.Equal("Another managed-version transaction is already pending.",
            Assert.Throws<InvalidOperationException>(() => VersionActivationPolicy.RecordActiveLaunch(state)).Message);
        Assert.Equal("Activation candidate is not installed and admitted.",
            Assert.Throws<InvalidOperationException>(() =>
                VersionActivationPolicy.BeginActivation(State(), ManagedAppVersion.Parse("9.0.0"))).Message);
        VersionManagerState empty = VersionManagerState.Create(null, null, null, [], null, null, false);
        Assert.Equal("No admitted active version is available.",
            Assert.Throws<InvalidOperationException>(() => VersionActivationPolicy.RecordActiveLaunch(empty)).Message);
    }

    /// <summary>Application entry points all reject missing state before inspecting transition identities.</summary>
    [Fact]
    public void ApplicationEntryPointsRejectNullState()
    {
        Assert.Throws<ArgumentNullException>(() => VersionActivationPolicy.BeginActivation(null!, Candidate));
        Assert.Throws<ArgumentNullException>(() => VersionActivationPolicy.RecordCandidateLaunch(null!));
        Assert.Throws<ArgumentNullException>(() => VersionActivationPolicy.CancelRequestedActivation(null!));
        Assert.Throws<ArgumentNullException>(() => VersionActivationPolicy.CommitReady(null!, Candidate));
        Assert.Throws<ArgumentNullException>(() => VersionActivationPolicy.FailActivation(null!, Candidate));
        Assert.Throws<ArgumentNullException>(() => VersionActivationPolicy.RecordRollbackLaunch(null!, Candidate));
        Assert.Throws<ArgumentNullException>(() => VersionActivationPolicy.CommitRollback(null!, Older));
        Assert.Throws<ArgumentNullException>(() => VersionActivationPolicy.RecordActiveLaunch(null!));
        Assert.Throws<ArgumentNullException>(() => VersionActivationPolicy.ClearActiveLaunch(null!, Active));
    }

    /// <summary>Launcher commit and failure transitions preserve complete exact identities.</summary>
    [Fact]
    public void LauncherCommitsAndFailureRetainExactAuthority()
    {
        LauncherBootstrapState initial = LauncherState();
        LauncherBootstrapState recorded = initial.Begin(CreateLauncher(Candidate)).RecordCandidateLaunch();
        LauncherBootstrapState ready = recorded.CommitReady();
        Assert.Equal(CreateLauncher(Candidate), ready.Active);
        Assert.Equal(ready.Active, ready.LastKnownGood);
        Assert.Null(ready.Pending);
        Assert.Null(ready.Failed);
        LauncherBootstrapState rollback = recorded.RecordRollbackLaunch().CommitRollback();
        Assert.Equal(CreateLauncher(Older), rollback.Active);
        Assert.Equal(rollback.Active, rollback.LastKnownGood);
        Assert.Equal(CreateLauncher(Candidate), rollback.Failed);
        Assert.Null(rollback.Pending);
        LauncherBootstrapState failure = recorded.FailCandidate();
        Assert.Equal(initial.Active, failure.Active);
        Assert.Equal(initial.LastKnownGood, failure.LastKnownGood);
        Assert.Equal(CreateLauncher(Candidate), failure.Failed);
        Assert.Null(failure.Pending);
        LauncherBootstrapState cleared = initial.RecordActiveLaunch().ClearActiveLaunch(CreateLauncher(Active));
        Assert.True(initial.CreateDurableSnapshotToken().Matches(cleared.CreateDurableSnapshotToken()));
        Assert.Throws<InvalidOperationException>(() => initial.Begin(CreateLauncher(Active)));
        LauncherBootstrapState empty = LauncherBootstrapState.Create(Root, null, null, null, null);
        Assert.Throws<InvalidOperationException>(empty.RecordActiveLaunch);
        Assert.Equal("Launcher rollback target is absent.",
            Assert.Throws<InvalidOperationException>(() =>
                empty.Begin(CreateLauncher(Candidate)).RecordCandidateLaunch().RecordRollbackLaunch().CommitRollback()).Message);
    }

    /// <summary>Ordinary application launch and clear preserve all original durable fields.</summary>
    [Fact]
    public void ActiveApplicationLaunchRoundTripPreservesDurableSnapshot()
    {
        VersionManagerState initial = State(failed: Candidate).WithRetentionReviewDue(true)
            .WithUpdateSource(Source, new(1, Digest, true));
        VersionManagerState recorded = VersionActivationPolicy.RecordActiveLaunch(initial);
        Assert.Equal(Active, recorded.PendingActivation?.CandidateVersion);
        Assert.Equal(Admission(Active).AdmissionIdentity, recorded.PendingActivation?.CandidateAdmissionIdentity);
        Assert.Equal(Active, recorded.PendingActivation?.PreviousActiveVersion);
        Assert.Equal(Older, recorded.PendingActivation?.PreviousLastKnownGoodVersion);
        Assert.Equal(VersionActivationPhase.ActiveLaunchRecorded, recorded.PendingActivation?.Phase);
        VersionManagerState cleared = VersionActivationPolicy.ClearActiveLaunch(recorded, Active);
        Assert.True(initial.CreateDurableSnapshotToken().Matches(cleared.CreateDurableSnapshotToken()));
    }

    private static void AssertTransition<T>(bool allowed, Func<T> transition, string message)
    {
        if (allowed)
        {
            Assert.NotNull(transition());
        }
        else
        {
            Assert.Equal(message, Assert.Throws<InvalidOperationException>(() => { _ = transition(); }).Message);
        }
    }
}
