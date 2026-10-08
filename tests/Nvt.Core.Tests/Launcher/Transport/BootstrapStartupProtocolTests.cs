// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Pipes;
using System.Text;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Verifies one-use START and ADMITTED byte protocols using captured Windows pipe handles.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class BootstrapStartupProtocolTests
{
    /// <summary>Absent START context preserves the source's direct-start behavior.</summary>
    [Fact]
    public async Task BootstrapStartGateWithoutInheritanceAllowsDirectStart()
    {
        using var environment = StartEnvironment(null, null);
        using BootstrapStartGate gate = BootstrapStartGate.Capture(TransportFixture.Names);
        Assert.Equal(BootstrapStartGateInheritanceOutcome.NotInherited, gate.Outcome);
        Assert.True(await gate.WaitForStartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Partial and malformed inheritance is cleared and fails closed.</summary>
    [Theory]
    [InlineData("v1", null)]
    [InlineData("v1", "invalid")]
    [InlineData("v1", "0")]
    [InlineData("v1", "-1")]
    [InlineData(null, "invalid")]
    public async Task BootstrapStartGatePartialContextFailsClosed(string? context, string? handle)
    {
        using var environment = StartEnvironment(context, handle);
        using BootstrapStartGate gate = BootstrapStartGate.Capture(TransportFixture.Names);
        Assert.Equal(BootstrapStartGateInheritanceOutcome.Invalid, gate.Outcome);
        Assert.False(await gate.WaitForStartAsync(TestContext.Current.CancellationToken));
        AssertStartEnvironmentCleared();
    }

    /// <summary>One exact START signal consumes its captured handle and clears inheritance before reading.</summary>
    [Fact]
    public async Task BootstrapStartGateAcceptsExactSignalAndClearsInheritedContext()
    {
        RequireWindows();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var environment = StartEnvironment("v1", duplicate);
        using BootstrapStartGate gate = BootstrapStartGate.Capture(TransportFixture.Names);
        Assert.Equal(BootstrapStartGateInheritanceOutcome.Inherited, gate.Outcome);
        AssertStartEnvironmentCleared();
        Assert.Equal(0u, WindowsPipeHandles.Flags(duplicate) & 1u);
        pipe.Write("START\n"u8);
        pipe.Flush();
        Assert.True(await gate.WaitForStartAsync(TestContext.Current.CancellationToken));
        Assert.False(await gate.WaitForStartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The production gate waits for input before accepting either frozen START line ending.</summary>
    [Theory]
    [InlineData("START\n")]
    [InlineData("START\r\n")]
    public async Task BootstrapStartGateWaitsForSignalBeforeAcceptingLine(string text)
    {
        RequireWindows();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var environment = StartEnvironment("v1", duplicate);
        using BootstrapStartGate gate = BootstrapStartGate.Capture(TransportFixture.Names);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        Task<bool> pending = gate.WaitForStartAsync(deadline.Token).AsTask();
        Assert.False(pending.IsCompleted);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(text), deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        Assert.True(await pending);
        Assert.False(WindowsPipeHandles.IsOpen(duplicate));
        Assert.False(await gate.WaitForStartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Cancellation during the production read propagates and closes the captured handle.</summary>
    [Fact]
    public async Task BootstrapStartGateCancellationDuringWaitClosesCapturedHandle()
    {
        RequireWindows();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var environment = StartEnvironment("v1", duplicate);
        using BootstrapStartGate gate = BootstrapStartGate.Capture(TransportFixture.Names);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        caller.CancelAfter(TimeSpan.FromSeconds(5));
        Task<bool> pending = gate.WaitForStartAsync(caller.Token).AsTask();
        Assert.False(pending.IsCompleted);
        caller.Cancel();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.False(WindowsPipeHandles.IsOpen(duplicate));
        Assert.False(await gate.WaitForStartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>EOF accepts complete partial START text, rejects truncation, and applies the eight-character bound before trimming.</summary>
    [Theory]
    [InlineData("START", true)]
    [InlineData("STAR", false)]
    [InlineData("START\r\r", true)]
    [InlineData("START\r\r\r", true)]
    [InlineData("START\r\r\r\r", false)]
    [InlineData("", false)]
    public async Task BootstrapStartGatePartialLineBoundaryIsExact(string text, bool accepted)
    {
        RequireWindows();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var environment = StartEnvironment("v1", duplicate);
        using BootstrapStartGate gate = BootstrapStartGate.Capture(TransportFixture.Names);
        pipe.Write(Encoding.UTF8.GetBytes(text));
        pipe.Dispose();
        Assert.Equal(accepted, await gate.WaitForStartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Strict UTF-8 failure is a false START admission.</summary>
    [Fact]
    public async Task BootstrapStartGateRejectsInvalidUtf8()
    {
        RequireWindows();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var environment = StartEnvironment("v1", duplicate);
        using BootstrapStartGate gate = BootstrapStartGate.Capture(TransportFixture.Names);
        pipe.Write(Convert.FromHexString("C328"));
        pipe.Dispose();
        Assert.False(await gate.WaitForStartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Bad companion metadata closes the real captured handle before returning invalid.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v2")]
    public void BootstrapStartGateClosesHandleWhenContextIsInvalid(string? context)
    {
        RequireWindows();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var environment = StartEnvironment(context, duplicate);
        using BootstrapStartGate gate = BootstrapStartGate.Capture(TransportFixture.Names);
        Assert.Equal(BootstrapStartGateInheritanceOutcome.Invalid, gate.Outcome);
        Assert.False(WindowsPipeHandles.IsOpen(duplicate));
        AssertStartEnvironmentCleared();
        pipe.DisposeLocalCopyOfClientHandle();
        _ = Assert.Throws<IOException>(() => { pipe.WriteByte(1); pipe.Flush(); });
    }

    /// <summary>Cancellation propagates from START and consumes the one-use handle.</summary>
    [Fact]
    public async Task BootstrapStartGateCancellationClosesCapturedHandle()
    {
        RequireWindows();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var environment = StartEnvironment("v1", duplicate);
        using BootstrapStartGate gate = BootstrapStartGate.Capture(TransportFixture.Names);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        caller.Cancel();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await gate.WaitForStartAsync(caller.Token));
        Assert.False(WindowsPipeHandles.IsOpen(duplicate));
        Assert.False(await gate.WaitForStartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>An aborted authorization can never later emit START.</summary>
    [Fact]
    public void BootstrapStartAuthorizationCannotAuthorizeAfterAbort()
    {
        using var authorization = new BootstrapStartAuthorization(TransportFixture.Names);
        Assert.True(authorization.TryAbort());
        Assert.False(authorization.TryAuthorize());
        Assert.False(authorization.TryAbort());
    }

    /// <summary>Concurrent completion and abort compete for one atomic phase transition.</summary>
    [Fact]
    public async Task BootstrapStartAuthorizationRaceHasExactlyOneWinner()
    {
        using var authorization = new BootstrapStartAuthorization(TransportFixture.Names);
        var ready = new CountdownEvent(2);
        using (ready)
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> authorize = Task.Run(async () =>
            {
                ready.Signal();
                await release.Task.WaitAsync(TestContext.Current.CancellationToken);
                return authorization.TryAuthorize();
            }, TestContext.Current.CancellationToken);
            Task<bool> abort = Task.Run(async () =>
            {
                ready.Signal();
                await release.Task.WaitAsync(TestContext.Current.CancellationToken);
                return authorization.TryAbort();
            }, TestContext.Current.CancellationToken);
            Assert.True(ready.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            release.SetResult();
            bool[] results = await Task.WhenAll(authorize, abort);
            _ = Assert.Single(results, static value => value);
        }
    }

    /// <summary>Missing ADMITTED inheritance is an idempotent success without a write.</summary>
    [Fact]
    public async Task BootstrapAdmissionWithoutInheritanceSucceeds()
    {
        using var environment = AdmissionEnvironment(null);
        using BootstrapAdmissionSignal signal = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        Assert.Equal(BootstrapAdmissionInheritanceOutcome.NotInherited, signal.Outcome);
        Assert.True(await signal.ReportAdmittedAsync(TestContext.Current.CancellationToken));
        Assert.True(await signal.ReportAdmittedAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Setting an empty environment value removes it on Windows, preserving absent admission.</summary>
    [Fact]
    public async Task BootstrapAdmissionEmptyEnvironmentValueIsAbsent()
    {
        RequireWindows();
        using var environment = AdmissionEnvironment("");
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapAdmissionHandle));
        using BootstrapAdmissionSignal signal = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        Assert.Equal(BootstrapAdmissionInheritanceOutcome.NotInherited, signal.Outcome);
        Assert.True(await signal.ReportAdmittedAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Invalid ADMITTED handles are consumed and never become authority.</summary>
    [Theory]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task BootstrapAdmissionRejectsMalformedInheritance(string handle)
    {
        using var environment = AdmissionEnvironment(handle);
        using BootstrapAdmissionSignal signal = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        Assert.Equal(BootstrapAdmissionInheritanceOutcome.Invalid, signal.Outcome);
        Assert.False(await signal.ReportAdmittedAsync(TestContext.Current.CancellationToken));
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapAdmissionHandle));
    }

    /// <summary>ADMITTED is exactly nine UTF-8 bytes, emitted once, with EOF after the owned handle closes.</summary>
    [Fact]
    public async Task BootstrapAdmissionWritesExactBytesOnce()
    {
        RequireWindows();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var environment = AdmissionEnvironment(duplicate);
        using BootstrapAdmissionSignal signal = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        Assert.Equal(0u, WindowsPipeHandles.Flags(duplicate) & 1u);
        pipe.DisposeLocalCopyOfClientHandle();
        Assert.True(await signal.ReportAdmittedAsync(TestContext.Current.CancellationToken));
        Assert.True(await signal.ReportAdmittedAsync(TestContext.Current.CancellationToken));
        using var bytes = new MemoryStream();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await pipe.CopyToAsync(bytes, deadline.Token);
        Assert.Equal("ADMITTED\n"u8.ToArray(), bytes.ToArray());
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapAdmissionHandle));
    }

    /// <summary>A failed write consumes admission and later calls cannot claim success.</summary>
    [Fact]
    public async Task BootstrapAdmissionWriteFailureStaysFailed()
    {
        RequireWindows();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        string duplicate = WindowsPipeHandles.DuplicateClient(pipe);
        using var environment = AdmissionEnvironment(duplicate);
        using BootstrapAdmissionSignal signal = BootstrapAdmissionSignal.Capture(TransportFixture.Names);
        pipe.Dispose();
        Assert.False(await signal.ReportAdmittedAsync(TestContext.Current.CancellationToken));
        Assert.False(await signal.ReportAdmittedAsync(TestContext.Current.CancellationToken));
        Assert.False(WindowsPipeHandles.IsOpen(duplicate));
    }

    private static ProtocolEnvironmentScope StartEnvironment(string? context, string? handle) =>
        new((TransportFixture.Names.BootstrapStartContext, context), (TransportFixture.Names.BootstrapStartHandle, handle));
    private static ProtocolEnvironmentScope AdmissionEnvironment(string? handle) =>
        new((TransportFixture.Names.BootstrapAdmissionHandle, handle));
    private static void AssertStartEnvironmentCleared()
    {
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapStartContext));
        Assert.Null(Environment.GetEnvironmentVariable(TransportFixture.Names.BootstrapStartHandle));
    }
    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test requires Windows captured pipe handles.");
        }
    }
}
