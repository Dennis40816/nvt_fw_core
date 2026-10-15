// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Checks the complete planning, recheck, progress and lease trace.</summary>
public sealed class FirstInstallationTraceTests : FirstInstallationTestSupport
{
    /// <summary>Every exact recheck occurs under the expected lease and progress comes from the same attempt.</summary>
    [Fact]
    public async Task CompleteInstallationPreservesTheEntireOrderedTrace()
    {
        var trace = new List<string>();
        string root = Root("trace");
        FreshInstallationCandidate candidate = Candidate();
        using var state = new SequencedStateStore(MissingState(), MissingState(), BoundState(root, candidate)) { Observe = trace.Add };
        var roots = new SequencedRootProbe(ManagedInstallationRootStatus.Absent, ManagedInstallationRootStatus.Absent) { Observe = trace.Add };
        using var payload = new RecordingPayloadSource(PayloadIdentity()) { Observe = trace.Add };
        var source = new RecordingCandidateSource(candidate) { Observe = trace.Add };
        var materializer = new RecordingRootMaterializer(state) { Observe = trace.Add };
        using var handoff = new SetupBootstrapHandoff(state);
        ManagedFirstInstallationExperience experience = Create(state, roots, payload, source, materializer, handoff);
        ManagedFirstInstallationPlanResult prepared = await experience.PrepareAsync(root, TestContext.Current.CancellationToken);

        ManagedFirstInstallationResult result = await experience.InstallAndLaunchAsync(
            Assert.IsType<ManagedFirstInstallationPlan>(prepared.Plan), new TraceProgress(trace), TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.Completed, result.Outcome);
        Assert.False(materializer.Transaction.ContractViolated);
        Assert.Equal(
            ["state-load", "root-observe", "destination-admit", "payload-inspect", "candidate-inspect",
                "writer-acquire:1", "state-load", "root-observe", "destination-admit", "payload-capture",
                "progress:RevalidatingSource", "candidate-reverify", "materialize", "progress:FinalizingInstallation",
                "record-bootstrap-launch", "acquire-bootstrap-lease", "payload-dispose", "writer-release:1", "progress:StartingApplication",
                "handoff-owned-lease", "admitted", "ready", "writer-acquire:2", "state-load", "complete", "writer-release:2"], trace);
    }

    /// <summary>Payload custody outlives a failing operation and ends before the writer is released.</summary>
    [Theory]
    [InlineData("record-bootstrap-launch", "record-bootstrap-launch")]
    [InlineData("acquire-bootstrap-lease", "record-bootstrap-launch,acquire-bootstrap-lease")]
    public async Task PostPromotionExceptionDisposesPayloadAfterFailingOperation(
        string failingOperation,
        string operationTrace)
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var trace = new List<string>();
        var failure = new IOException("Injected post-promotion failure.");
        harness.State.Observe = operation =>
        {
            trace.Add(operation);
            if (string.Equals(operation, failingOperation, StringComparison.Ordinal))
            {
                trace.Add("injected-failure");
                throw failure;
            }
        };
        harness.Payload.Observe = trace.Add;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.False(harness.Materializer.Transaction.ContractViolated);
        Assert.True(result.IsRecoveryOwned);
        Assert.Null(result.LaunchFailure);
        Assert.Equal(
            $"writer-acquire:1,state-load,payload-capture,{operationTrace},injected-failure,payload-dispose,writer-release:1",
            string.Join(',', trace));
        Assert.True(harness.Payload.LastCapture!.Disposed);
        Assert.False(harness.State.LeaseActive);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    /// <summary>Undefined transaction issues cannot escape after the irreversible promotion.</summary>
    [Fact]
    public async Task PostPromotionMapperViolationReturnsRecoveryWithoutLaunchFailure()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        harness.Materializer.Transaction.RecordLaunchIssue = (ManagedFirstInstallationTransactionIssue)int.MaxValue;

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.RecoveryRequired, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.False(harness.Materializer.Transaction.ContractViolated);
        Assert.True(result.IsRecoveryOwned);
        Assert.Null(result.LaunchFailure);
        Assert.Equal(0, harness.Materializer.Transaction.CompleteCount);
        Assert.Equal(1, harness.Materializer.Transaction.DisposeCount);
        Assert.True(harness.Payload.LastCapture!.Disposed);
        Assert.False(harness.State.LeaseActive);
        Assert.Equal(0, harness.Handoff.StartCount);
    }

    /// <summary>Pre-promotion adapter exceptions retain their existing propagation.</summary>
    [Fact]
    public async Task PrePromotionExceptionStillEscapes()
    {
        using InstallHarness harness = await CreateInstallHarnessAsync();
        var failure = new IOException("Pre-promotion adapter failure.");
        harness.CandidateSource.Observe = _ => throw failure;

        IOException actual = await Assert.ThrowsAsync<IOException>(() =>
            harness.Experience.InstallAndLaunchAsync(harness.Plan, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, actual);
        Assert.Equal(0, harness.Materializer.MaterializeCount);
        Assert.Equal(0, harness.Materializer.Transaction.DisposeCount);
        Assert.True(harness.Payload.LastCapture!.Disposed);
        Assert.False(harness.State.LeaseActive);
    }

    private sealed class TraceProgress(List<string> trace) : IProgress<ManagedFirstInstallationProgress>
    {
        public void Report(ManagedFirstInstallationProgress value) => trace.Add($"progress:{value.Stage}");
    }
}
