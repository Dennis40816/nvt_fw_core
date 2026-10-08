// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Freezes numeric exit encodings, typed receipt shapes, and monotonic wait-budget boundaries.</summary>
public sealed class ImmutableBootstrapContractsTests
{
    /// <summary>Every concrete failure keeps its exact numeric encoding and coarse admission category.</summary>
    [Theory]
    [InlineData(2, ImmutableBootstrapExitIssue.Busy, ImmutableBootstrapAdmissionOutcome.Busy)]
    [InlineData(10, ImmutableBootstrapExitIssue.InvalidState, ImmutableBootstrapAdmissionOutcome.RecoveryRequired)]
    [InlineData(11, ImmutableBootstrapExitIssue.ManagedRootMismatch, ImmutableBootstrapAdmissionOutcome.RecoveryRequired)]
    [InlineData(12, ImmutableBootstrapExitIssue.MutationPending, ImmutableBootstrapAdmissionOutcome.RecoveryRequired)]
    [InlineData(13, ImmutableBootstrapExitIssue.DamagedLauncher, ImmutableBootstrapAdmissionOutcome.RecoveryRequired)]
    [InlineData(14, ImmutableBootstrapExitIssue.ProtocolMismatch, ImmutableBootstrapAdmissionOutcome.RecoveryRequired)]
    [InlineData(15, ImmutableBootstrapExitIssue.StartFailed, ImmutableBootstrapAdmissionOutcome.LaunchFailed)]
    [InlineData(16, ImmutableBootstrapExitIssue.RollbackUnavailable, ImmutableBootstrapAdmissionOutcome.LaunchFailed)]
    [InlineData(17, ImmutableBootstrapExitIssue.StateChanged, ImmutableBootstrapAdmissionOutcome.LaunchFailed)]
    [InlineData(18, ImmutableBootstrapExitIssue.StateUnavailable, ImmutableBootstrapAdmissionOutcome.HealthUnavailable)]
    [InlineData(19, ImmutableBootstrapExitIssue.TerminationUnconfirmed, ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed)]
    [InlineData(20, ImmutableBootstrapExitIssue.InvalidArguments, ImmutableBootstrapAdmissionOutcome.HealthUnavailable)]
    [InlineData(21, ImmutableBootstrapExitIssue.InvariantViolation, ImmutableBootstrapAdmissionOutcome.HealthUnavailable)]
    [InlineData(22, ImmutableBootstrapExitIssue.InvalidInheritedContext, ImmutableBootstrapAdmissionOutcome.HealthUnavailable)]
    [InlineData(23, ImmutableBootstrapExitIssue.StartNotAuthorized, ImmutableBootstrapAdmissionOutcome.HealthUnavailable)]
    [InlineData(99, ImmutableBootstrapExitIssue.UndefinedFailure, ImmutableBootstrapAdmissionOutcome.HealthUnavailable)]
    public void EveryFailureEncodingAndReceiptShapeIsExact(int code, ImmutableBootstrapExitIssue issue, ImmutableBootstrapAdmissionOutcome outcome)
    {
        Assert.Equal(code, ImmutableBootstrapExitCodeCodec.EncodeFailure(issue));
        Assert.Equal(issue, ImmutableBootstrapExitCodeCodec.DecodeFailure(code));
        ImmutableBootstrapAdmissionResult admission = ImmutableBootstrapProcessLaunch.MapExitBeforeAdmission(code);
        Assert.Equal(outcome, admission.Outcome);
        Assert.Equal(code, admission.ExitCode);
        Assert.Equal(issue, admission.ExitIssue);
        Assert.True(admission.HasValidShape);
        var completion = new ImmutableBootstrapCompletionResult(ImmutableBootstrapExitCodeCodec.ClassifyCompletion(code), code, issue);
        Assert.Equal(issue == ImmutableBootstrapExitIssue.TerminationUnconfirmed
            ? ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed : ImmutableBootstrapCompletionOutcome.Failed, completion.Outcome);
        Assert.True(completion.HasValidShape);
        Assert.False((admission with { ExitIssue = ImmutableBootstrapExitIssue.None }).HasValidShape);
        Assert.False((completion with { ExitIssue = ImmutableBootstrapExitIssue.None }).HasValidShape);
    }

    /// <summary>Unknown adjacent wire values never become a READY or rollback completion.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(24)]
    [InlineData(98)]
    [InlineData(100)]
    public void UnknownExitCodeIsFailClosed(int code)
    {
        Assert.Equal(ImmutableBootstrapExitIssue.Unknown, ImmutableBootstrapExitCodeCodec.DecodeFailure(code));
        Assert.Equal(ImmutableBootstrapCompletionOutcome.Failed, ImmutableBootstrapExitCodeCodec.ClassifyCompletion(code));
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, ImmutableBootstrapProcessLaunch.MapExitBeforeAdmission(code).Outcome);
        Assert.True(ImmutableBootstrapProcessLaunch.MapExitBeforeAdmission(code).HasValidShape);
    }

    /// <summary>Successful completion encodings cannot be used as a pre-admission failure receipt.</summary>
    [Theory]
    [InlineData(0, ImmutableBootstrapCompletionOutcome.Ready)]
    [InlineData(1, ImmutableBootstrapCompletionOutcome.RolledBack)]
    public void SuccessCodesRequireAdmittedCompletion(int code, ImmutableBootstrapCompletionOutcome outcome)
    {
        Assert.Equal(outcome, ImmutableBootstrapExitCodeCodec.ClassifyCompletion(code));
        Assert.True(new ImmutableBootstrapCompletionResult(outcome, code).HasValidShape);
        Assert.False(ImmutableBootstrapProcessLaunch.MapExitBeforeAdmission(code).HasValidShape);
        Assert.False(new ImmutableBootstrapAdmissionResult(ImmutableBootstrapAdmissionOutcome.Admitted, code).HasValidShape);
    }

    /// <summary>Non-failure and undefined issues retain the frozen exception type and messages.</summary>
    [Theory]
    [InlineData(ImmutableBootstrapExitIssue.None, "Issue has no failure encoding.")]
    [InlineData(ImmutableBootstrapExitIssue.Unknown, "Issue has no failure encoding.")]
    [InlineData((ImmutableBootstrapExitIssue)(-1), "Issue is undefined.")]
    public void UnencodableIssueIsRejected(ImmutableBootstrapExitIssue issue, string message)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => ImmutableBootstrapExitCodeCodec.EncodeFailure(issue));
        Assert.Equal("issue", exception.ParamName);
        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Only the exact no-code receipts and typed termination uncertainty have valid shapes.</summary>
    [Fact]
    public void NoCodeAndMismatchedReceiptShapesRemainExact()
    {
        Assert.True(new ImmutableBootstrapAdmissionResult(ImmutableBootstrapAdmissionOutcome.Admitted).HasValidShape);
        Assert.True(new ImmutableBootstrapAdmissionResult(ImmutableBootstrapAdmissionOutcome.LaunchFailed, ExitIssue: ImmutableBootstrapExitIssue.StartFailed).HasValidShape);
        Assert.True(new ImmutableBootstrapAdmissionResult(ImmutableBootstrapAdmissionOutcome.HealthUnavailable).HasValidShape);
        Assert.True(new ImmutableBootstrapCompletionResult(ImmutableBootstrapCompletionOutcome.Unavailable).HasValidShape);
        Assert.True(new ImmutableBootstrapAdmissionResult(ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed, ExitIssue: ImmutableBootstrapExitIssue.TerminationUnconfirmed).HasValidShape);
        Assert.True(new ImmutableBootstrapCompletionResult(ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed, ExitIssue: ImmutableBootstrapExitIssue.TerminationUnconfirmed).HasValidShape);
        Assert.False(new ImmutableBootstrapAdmissionResult(ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed).HasValidShape);
        Assert.False(new ImmutableBootstrapCompletionResult(ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed).HasValidShape);
        Assert.False(new ImmutableBootstrapAdmissionResult((ImmutableBootstrapAdmissionOutcome)(-1)).HasValidShape);
        Assert.False(new ImmutableBootstrapCompletionResult((ImmutableBootstrapCompletionOutcome)(-1)).HasValidShape);
        Assert.True(new ImmutableBootstrapStartResult(null, ImmutableBootstrapStartIssue.Busy).HasValidShape);
        Assert.False(new ImmutableBootstrapStartResult(null, ImmutableBootstrapStartIssue.None).HasValidShape);
        Assert.False(new ImmutableBootstrapStartResult(null, (ImmutableBootstrapStartIssue)(-1)).HasValidShape);
    }

    /// <summary>Negative work and total budgets smaller than work are rejected in the original order.</summary>
    [Theory]
    [InlineData(-1, -2, "remainingOperation")]
    [InlineData(0, -1, "remainingTotal")]
    [InlineData(1, 0, "remainingTotal")]
    public void InvalidWaitBudgetIsRejected(long work, long total, string parameter)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ImmutableBootstrapWaitBudget(TimeSpan.FromTicks(work), TimeSpan.FromTicks(total)));
        Assert.Equal(parameter, exception.ParamName);
    }

    /// <summary>Zero, the smallest positive duration, and equal or larger total budgets remain accepted.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(long.MaxValue, long.MaxValue)]
    public void WaitBudgetBoundaryIsExact(long work, long total)
    {
        var budget = new ImmutableBootstrapWaitBudget(TimeSpan.FromTicks(work), TimeSpan.FromTicks(total));
        Assert.Equal(work, budget.RemainingOperation.Ticks);
        Assert.Equal(total, budget.RemainingTotal.Ticks);
    }
}
