// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Preserves fail-closed whole-tree termination, cleanup bounds, and exception order.</summary>
public sealed class ManagedProcessTerminationTests
{
    /// <summary>A failed whole-tree kill stays unconfirmed even when the root subsequently exits.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialTreeKillFailureIgnoresSubsequentRootExit(bool rootExitsDuringKill)
    {
        using var process = new Process();
        var operations = new AggregateKillFailureOperations(rootExitsDuringKill);
        ManagedProcessTerminationResult result = new ManagedProcessTermination(operations).ConfirmExited(process);
        Assert.False(result.IsExitConfirmed);
        Assert.Null(result.ExitCode);
        Assert.Equal(1, operations.HasExitedCalls);
    }

    /// <summary>A nonreturning wait uses the frozen bounded policy and remains unconfirmed.</summary>
    [Fact]
    public void NonreturningWaitExpiresAsUnconfirmedTermination()
    {
        using var process = new Process();
        var operations = new ObservedOperations();
        TimeSpan timeout = TimeSpan.FromMilliseconds(25);
        ManagedProcessTerminationResult result = new ManagedProcessTermination(operations, timeout).ConfirmExited(process);
        Assert.False(result.IsExitConfirmed);
        Assert.Equal(timeout, operations.ObservedTimeout);
    }

    /// <summary>Five seconds remains the normal cleanup wait.</summary>
    [Fact]
    public void DefaultTerminationWaitIsFiveSeconds()
    {
        using var process = new Process();
        var operations = new ObservedOperations();
        _ = new ManagedProcessTermination(operations).ConfirmExited(process);
        Assert.Equal(TimeSpan.FromSeconds(5), operations.ObservedTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), ManagedProcessTermination.DefaultWaitTimeout);
    }

    /// <summary>The positive native wait bound accepts the smallest tick and the exact maximum.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(21474836460000)]
    [InlineData(21474836470000)]
    public void NativeWaitAcceptsExactBoundary(long ticks)
    {
        using var process = new Process();
        var operations = new ObservedOperations();
        TimeSpan timeout = TimeSpan.FromTicks(ticks);
        _ = new ManagedProcessTermination(operations, timeout).ConfirmExited(process);
        Assert.Equal(timeout, operations.ObservedTimeout);
    }

    /// <summary>Zero, negative waits, and one tick beyond the native maximum fail.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(21474836470001)]
    public void NativeWaitRejectsOutsideBoundary(long ticks)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ManagedProcessTermination(new ObservedOperations(), TimeSpan.FromTicks(ticks)));
        Assert.Equal("_waitTimeout", exception.ParamName);
    }

    /// <summary>Each uncertain native operation fails closed and never proceeds to a later observation.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NativeFailurePreservesObservationOrder(int failAt)
    {
        using var process = new Process();
        var operations = new ObservedOperations(failAt);
        ManagedProcessTerminationResult result = new ManagedProcessTermination(operations).ConfirmExited(process);
        Assert.False(result.IsExitConfirmed);
        Assert.Null(result.ExitCode);
        Assert.Equal(failAt + 1, operations.Calls);
    }

    /// <summary>An already exited root returns its exit code without attempting a kill.</summary>
    [Fact]
    public void AlreadyExitedProcessIsConfirmed()
    {
        using var process = new Process();
        var operations = new ExitedOperations();
        Assert.Equal(new ManagedProcessTerminationResult(true, 7),
            new ManagedProcessTermination(operations).ConfirmExited(process));
    }

    private sealed class AggregateKillFailureOperations(bool rootExitsDuringKill)
        : IManagedProcessTerminationOperations
    {
        // Guard: the synchronous test thread owns this fake.
        private bool _rootExited;
        // Guard: the synchronous test thread owns this fake.
        public int HasExitedCalls { get; private set; }
        public bool HasExited(Process process)
        {
            HasExitedCalls++;
            return _rootExited;
        }
        public void Kill(Process process)
        {
            _rootExited = rootExitsDuringKill;
            throw new AggregateException("Injected partial tree-kill failure.");
        }
        public bool WaitForExit(Process process, TimeSpan timeout) =>
            throw new InvalidOperationException("Wait must not follow a failed tree kill.");
        public int GetExitCode(Process process) => 0;
    }

    private sealed class ObservedOperations(int? failAt = null) : IManagedProcessTerminationOperations
    {
        // Guard: the synchronous test thread owns this fake.
        public TimeSpan? ObservedTimeout { get; private set; }
        // Guard: the synchronous test thread owns this fake.
        public int Calls { get; private set; }
        private void Observe()
        {
            if (Calls++ == failAt)
            {
                throw new Win32Exception("Injected termination failure.");
            }
        }
        public bool HasExited(Process process) { Observe(); return false; }
        public void Kill(Process process) => Observe();
        public bool WaitForExit(Process process, TimeSpan timeout)
        {
            Observe();
            ObservedTimeout = timeout;
            return failAt is not null;
        }
        public int GetExitCode(Process process) => throw new InvalidOperationException("No confirmed exit.");
    }

    private sealed class ExitedOperations : IManagedProcessTerminationOperations
    {
        public bool HasExited(Process process) => true;
        public void Kill(Process process) => throw new InvalidOperationException("Exited root needs no kill.");
        public bool WaitForExit(Process process, TimeSpan timeout) =>
            throw new InvalidOperationException("Exited root needs no wait.");
        public int GetExitCode(Process process) => 7;
    }
}
