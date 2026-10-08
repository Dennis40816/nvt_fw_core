// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Processes;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Ports parent-side immutable admission, completion, and actual Windows Job cleanup assertions.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class ImmutableBootstrapProcessLaunchTests
{
    private static readonly ImmutableBootstrapWaitBudget AdmissionBudget = new(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(5_500));
    private static readonly ImmutableBootstrapWaitBudget CompletionBudget = new(TimeSpan.FromMilliseconds(44_500), TimeSpan.FromSeconds(45));
    private static ManagedLifetimeProtocol Protocol { get; } = new(TransportFixture.Names, @"Local\CoreFixture.ManagedTree");

    /// <summary>Admission cancellation terminates both Root Bootstrap and a real descendant.</summary>
    [Fact]
    public async Task BootstrapAdmissionCancellationTerminatesOuterJobDescendant()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string marker = workspace.PathFor("bootstrap-admission-cancel/grandchild.txt");
        using ImmutableBootstrapProcessLaunch launch = StartBootstrapTree(
            workspace.Root,
            marker,
            "tree-root-wait",
            out AnonymousPipeClientStream client,
            out int rootId);
        await using (client)
        {
            int childId = await WaitForProcessMarkerAsync(marker);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

            ImmutableBootstrapAdmissionResult result =
                await launch.WaitForAdmissionAsync(AdmissionBudget, cancellation.Token);

            Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, result.Outcome);
            Assert.False(IsRunning(rootId));
            Assert.False(IsRunning(childId));
        }
    }

    /// <summary>An exit before ADMITTED cannot strand a descendant outside cleanup custody.</summary>
    [Fact]
    public async Task BootstrapExitBeforeAdmissionTerminatesOuterJobDescendant()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string marker = workspace.PathFor("bootstrap-exit-before-admission/grandchild.txt");
        using ImmutableBootstrapProcessLaunch launch =
            StartBootstrapTreeWithoutAdmissionWriter(
            workspace.Root,
            marker,
            "tree-root-exit",
            out int rootId);
        int childId = await WaitForProcessMarkerAsync(marker);

        ImmutableBootstrapAdmissionResult result = await launch.WaitForAdmissionAsync(
            AdmissionBudget,
            TestContext.Current.CancellationToken);

        Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, result.Outcome);
        Assert.False(IsRunning(rootId));
        Assert.False(IsRunning(childId));
    }

    /// <summary>Completion timeout terminates the admitted Root Bootstrap and its descendant.</summary>
    [Fact]
    public async Task BootstrapCompletionTimeoutTerminatesOuterJobDescendant()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string marker = workspace.PathFor("bootstrap-completion-timeout/grandchild.txt");
        using ImmutableBootstrapProcessLaunch launch = StartBootstrapTree(
            workspace.Root,
            marker,
            "tree-root-wait",
            out AnonymousPipeClientStream client,
            out int rootId);
        await using (client)
        {
            int childId = await WaitForProcessMarkerAsync(marker);
            await client.WriteAsync("ADMITTED\n"u8.ToArray(), TestContext.Current.CancellationToken);
            await client.FlushAsync(TestContext.Current.CancellationToken);
            ImmutableBootstrapAdmissionResult admission = await launch.WaitForAdmissionAsync(
                AdmissionBudget,
                TestContext.Current.CancellationToken);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

            ImmutableBootstrapCompletionResult completion =
                await launch.WaitForCompletionAsync(CompletionBudget, cancellation.Token);

            Assert.Equal(ImmutableBootstrapAdmissionOutcome.Admitted, admission.Outcome);
            Assert.Equal(ImmutableBootstrapCompletionOutcome.Unavailable, completion.Outcome);
            Assert.False(IsRunning(rootId));
            Assert.False(IsRunning(childId));
        }
    }

    /// <summary>Successful exit zero or rollback one releases the accepted outer job tree.</summary>
    [Theory]
    [InlineData("tree-root-exit", ImmutableBootstrapCompletionOutcome.Ready, 0)]
    [InlineData("tree-root-exit", ImmutableBootstrapCompletionOutcome.RolledBack, 1)]
    public async Task BootstrapSuccessReleasesAcceptedOuterJobDescendant(
        string behavior,
        ImmutableBootstrapCompletionOutcome expected,
        int exitCode)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        string marker = workspace.PathFor($"bootstrap-accepted-{exitCode}/grandchild.txt");
        int childId = 0;
        try
        {
            using ImmutableBootstrapProcessLaunch launch = StartBootstrapTree(
                workspace.Root,
                marker,
                behavior,
                out AnonymousPipeClientStream client,
                out _,
                exitCode);
            await using (client)
            {
                childId = await WaitForProcessMarkerAsync(marker);
                await client.WriteAsync("ADMITTED\n"u8.ToArray(), TestContext.Current.CancellationToken);
                await client.FlushAsync(TestContext.Current.CancellationToken);

                ImmutableBootstrapAdmissionResult admission = await launch.WaitForAdmissionAsync(
                    AdmissionBudget,
                    TestContext.Current.CancellationToken);
                ImmutableBootstrapCompletionResult completion = await launch.WaitForCompletionAsync(
                    CompletionBudget,
                    TestContext.Current.CancellationToken);

                Assert.Equal(ImmutableBootstrapAdmissionOutcome.Admitted, admission.Outcome);
                Assert.Equal(expected, completion.Outcome);
                Assert.Equal(exitCode, completion.ExitCode);
                Assert.True(IsRunning(childId));
            }
        }
        finally
        {
            TerminateProcess(childId);
        }
    }

    /// <summary>A later failed invocation cannot recapture an earlier accepted Desktop tree.</summary>
    [Fact]
    public async Task SequentialBootstrapFailurePreservesPriorAcceptedDescendant()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        int acceptedChildId = 0;
        try
        {
            string acceptedMarker = workspace.PathFor("bootstrap-first-accepted/grandchild.txt");
            using (ImmutableBootstrapProcessLaunch accepted = StartBootstrapTree(
                       workspace.Root,
                       acceptedMarker,
                       "tree-root-exit",
                       out AnonymousPipeClientStream acceptedClient,
                       out _))
            await using (acceptedClient)
            {
                acceptedChildId = await WaitForProcessMarkerAsync(acceptedMarker);
                await acceptedClient.WriteAsync(
                    "ADMITTED\n"u8.ToArray(),
                    TestContext.Current.CancellationToken);
                await acceptedClient.FlushAsync(TestContext.Current.CancellationToken);

                ImmutableBootstrapAdmissionResult admission = await accepted.WaitForAdmissionAsync(
                    AdmissionBudget,
                    TestContext.Current.CancellationToken);
                ImmutableBootstrapCompletionResult completion = await accepted.WaitForCompletionAsync(
                    CompletionBudget,
                    TestContext.Current.CancellationToken);

                Assert.Equal(ImmutableBootstrapAdmissionOutcome.Admitted, admission.Outcome);
                Assert.Equal(ImmutableBootstrapCompletionOutcome.Ready, completion.Outcome);
                Assert.True(IsRunning(acceptedChildId));
            }

            string failedMarker = workspace.PathFor("bootstrap-second-failed/grandchild.txt");
            using ImmutableBootstrapProcessLaunch failed = StartBootstrapTree(
                workspace.Root,
                failedMarker,
                "tree-root-wait",
                out AnonymousPipeClientStream failedClient,
                out int failedRootId);
            await using (failedClient)
            {
                int failedChildId = await WaitForProcessMarkerAsync(failedMarker);
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

                ImmutableBootstrapAdmissionResult failedAdmission = await failed.WaitForAdmissionAsync(
                    AdmissionBudget,
                    cancellation.Token);

                Assert.Equal(
                    ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
                    failedAdmission.Outcome);
                Assert.False(IsRunning(failedRootId));
                Assert.False(IsRunning(failedChildId));
                Assert.True(IsRunning(acceptedChildId));
            }
        }
        finally
        {
            TerminateProcess(acceptedChildId);
        }
    }

    /// <summary>Admission cancellation kills and confirms the already-started Root Bootstrap.</summary>
    [Fact]
    public async Task BootstrapAdmissionCancellationTerminatesStartedProcess()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using Process process = StartSilentProbe(workspace.Root);
        int processId = process.Id;
        using var pipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        await using var client = new AnonymousPipeClientStream(
            PipeDirection.Out,
            WindowsPipeHandles.DuplicateClient(pipe));
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch =
            CreateBootstrapLaunch(process, pipe, workspace.Root);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        ImmutableBootstrapAdmissionResult result = await launch.WaitForAdmissionAsync(
            AdmissionBudget,
            cancellation.Token);

        Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, result.Outcome);
        AssertProcessExited(processId);
    }

    /// <summary>A token cancelled before the wait still aborts and confirms the started tree.</summary>
    [Fact]
    public async Task BootstrapAdmissionPreCancellationStillTerminatesStartedProcess()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using Process process = StartSilentProbe(workspace.Root);
        int processId = process.Id;
        using var pipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        await using var client = new AnonymousPipeClientStream(
            PipeDirection.Out,
            WindowsPipeHandles.DuplicateClient(pipe));
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch =
            CreateBootstrapLaunch(process, pipe, workspace.Root);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        ImmutableBootstrapAdmissionResult result = await launch.WaitForAdmissionAsync(
            AdmissionBudget,
            cancellation.Token);

        Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, result.Outcome);
        AssertProcessExited(processId);
    }

    /// <summary>Completion cancellation after ADMITTED still terminates Root Bootstrap.</summary>
    [Fact]
    public async Task BootstrapCompletionCancellationTerminatesAdmittedProcess()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using Process process = StartSilentProbe(workspace.Root);
        int processId = process.Id;
        using var pipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        await using var client = new AnonymousPipeClientStream(
            PipeDirection.Out,
            WindowsPipeHandles.DuplicateClient(pipe));
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch =
            CreateBootstrapLaunch(process, pipe, workspace.Root);
        await client.WriteAsync("ADMITTED\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await client.FlushAsync(TestContext.Current.CancellationToken);

        ImmutableBootstrapAdmissionResult admission = await launch.WaitForAdmissionAsync(
            AdmissionBudget,
            TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        ImmutableBootstrapCompletionResult completion =
            await launch.WaitForCompletionAsync(CompletionBudget, cancellation.Token);

        Assert.Equal(ImmutableBootstrapAdmissionOutcome.Admitted, admission.Outcome);
        Assert.Equal(ImmutableBootstrapCompletionOutcome.Unavailable, completion.Outcome);
        AssertProcessExited(processId);
    }

    /// <summary>Admission is one-use and completion cannot run before admission.</summary>
    [Fact]
    public async Task AdmissionAndCompletionHaveExactOneUseOrder()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using Process process = StartSilentProbe(workspace.Root);
        int processId = process.Id;
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        await using var writer = new AnonymousPipeClientStream(PipeDirection.Out, WindowsPipeHandles.DuplicateClient(pipe));
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch = CreateBootstrapLaunch(process, pipe, workspace.Root);
        Assert.Equal(ImmutableBootstrapCompletionOutcome.Unavailable,
            (await launch.WaitForCompletionAsync(CompletionBudget, TestContext.Current.CancellationToken)).Outcome);
        await writer.WriteAsync("ADMITTED\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.Admitted,
            (await launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable,
            (await launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken)).Outcome);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.Equal(ImmutableBootstrapCompletionOutcome.Unavailable,
            (await launch.WaitForCompletionAsync(CompletionBudget, cancellation.Token)).Outcome);
        Assert.False(IsRunning(processId));
    }

    /// <summary>The 32-character ADMITTED bound rejects both adjacent invalid lines and the exact bound.</summary>
    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    public async Task AdmissionLineCharacterBoundaryCannotAuthenticateInvalidText(int characters)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using Process process = StartSilentProbe(workspace.Root);
        int processId = process.Id;
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        await using var writer = new AnonymousPipeClientStream(PipeDirection.Out, WindowsPipeHandles.DuplicateClient(pipe));
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch = CreateBootstrapLaunch(process, pipe, workspace.Root);
        await writer.WriteAsync(Encoding.UTF8.GetBytes(new string('界', characters) + "\n"), TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        ImmutableBootstrapAdmissionResult result = await launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken);
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.HealthUnavailable, result.Outcome);
        Assert.True(result.HasValidShape);
        Assert.False(IsRunning(processId));
    }

    /// <summary>An exact partial line at EOF remains admissible before successful zero or rollback-one completion.</summary>
    [Theory]
    [InlineData(0, ImmutableBootstrapCompletionOutcome.Ready)]
    [InlineData(1, ImmutableBootstrapCompletionOutcome.RolledBack)]
    public async Task ExactAdmissionAtPartialEofAcceptsObservedSuccessfulExit(int code, ImmutableBootstrapCompletionOutcome expected)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        ProcessStartInfo info = ProcessProbe.Create("exit");
        info.Environment["CORE_TEST_PROBE_EXIT_CODE"] = code.ToString(CultureInfo.InvariantCulture);
        using Process process = ProcessLaunchGate.StartContained(info, [], static () => true)
            ?? throw new InvalidOperationException("Exit probe did not start.");
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        await using var writer = new AnonymousPipeClientStream(PipeDirection.Out, WindowsPipeHandles.DuplicateClient(pipe));
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch = CreateBootstrapLaunch(process, pipe, workspace.Root);
        await writer.WriteAsync("ADMITTED"u8.ToArray(), TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        await writer.DisposeAsync();
        Assert.Equal(ImmutableBootstrapAdmissionOutcome.Admitted,
            (await launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken)).Outcome);
        ImmutableBootstrapCompletionResult completion = await launch.WaitForCompletionAsync(CompletionBudget, TestContext.Current.CancellationToken);
        Assert.Equal(expected, completion.Outcome);
        Assert.Equal(code, completion.ExitCode);
        Assert.True(completion.HasValidShape);
    }

    /// <summary>An admitted Root Bootstrap exit 19 preserves typed termination uncertainty.</summary>
    [Fact]
    public async Task BootstrapAdmissionThenExitNineteenRemainsTerminationUnconfirmed()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        ProcessStartInfo info = ProcessProbe.Create("exit");
        info.Environment["CORE_TEST_PROBE_EXIT_CODE"] = "19";
        using Process process = ProcessLaunchGate.StartContained(info, [], static () => true)
            ?? throw new InvalidOperationException("Exit probe did not start.");
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        await using var writer = new AnonymousPipeClientStream(PipeDirection.Out, WindowsPipeHandles.DuplicateClient(pipe));
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch = CreateBootstrapLaunch(process, pipe, workspace.Root);
        await writer.WriteAsync("ADMITTED\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        ImmutableBootstrapAdmissionResult admission = await launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken);
        ImmutableBootstrapCompletionResult completion = await launch.WaitForCompletionAsync(CompletionBudget, TestContext.Current.CancellationToken);

        Assert.Equal(ImmutableBootstrapAdmissionOutcome.Admitted, admission.Outcome);
        Assert.Equal(ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed, completion.Outcome);
        Assert.Equal(19, completion.ExitCode);
    }

    /// <summary>Observed failure codes retain their exact typed reasons when the admission pipe reaches EOF.</summary>
    [Theory]
    [InlineData(18, ImmutableBootstrapExitIssue.StateUnavailable, ImmutableBootstrapAdmissionOutcome.HealthUnavailable)]
    [InlineData(19, ImmutableBootstrapExitIssue.TerminationUnconfirmed, ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed)]
    [InlineData(22, ImmutableBootstrapExitIssue.InvalidInheritedContext, ImmutableBootstrapAdmissionOutcome.HealthUnavailable)]
    public async Task ObservedPreAdmissionExitPreservesCodeAndIssue(int code, ImmutableBootstrapExitIssue issue, ImmutableBootstrapAdmissionOutcome outcome)
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        ProcessStartInfo info = ProcessProbe.Create("exit");
        info.Environment["CORE_TEST_PROBE_EXIT_CODE"] = code.ToString(CultureInfo.InvariantCulture);
        using Process process = ProcessLaunchGate.StartContained(info, [], static () => true)
            ?? throw new InvalidOperationException("Exit probe did not start.");
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch = CreateBootstrapLaunch(process, pipe, workspace.Root);
        ImmutableBootstrapAdmissionResult result = await launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(code, result.ExitCode);
        Assert.Equal(issue, result.ExitIssue);
        Assert.True(result.HasValidShape);
    }

    /// <summary>Unfinished creation cannot reserve a caller-visible wait or become an unobserved late launch.</summary>
    [Fact]
    public async Task UnfinishedCreationReturnsTerminationUnconfirmedAndCleanupOwnsLateReceipt()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        using var gate = new BootstrapStartAuthorization(TransportFixture.Names);
        using ManagedProcessLifetimeLease lifetime = Acquire(workspace.PathFor("bootstrap-state.json"));
        var creation = new TaskCompletionSource<Process?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var launch = new ImmutableBootstrapProcessLaunch(creation.Task, pipe, gate, lifetime,
            ManagedProcessTermination.Instance, TimeSpan.FromMilliseconds(500));
        try
        {
            var timer = Stopwatch.StartNew();
            ImmutableBootstrapAdmissionResult result = await launch.WaitForAdmissionAsync(
                new ImmutableBootstrapWaitBudget(TimeSpan.Zero, TimeSpan.Zero), TestContext.Current.CancellationToken);
            Assert.Equal(ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed, result.Outcome);
            Assert.True(result.HasValidShape);
            Assert.InRange(timer.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            using Process process = StartSilentProbe(workspace.Root);
            using Process observer = Process.GetProcessById(process.Id);
            creation.SetResult(process);
            Assert.True(await Task.Run(() => observer.WaitForExit(5_000), TestContext.Current.CancellationToken));
        }
        finally { creation.TrySetResult(null); }
    }

    private static ImmutableBootstrapProcessLaunch CreateBootstrapLaunch(
        Process process, AnonymousPipeServerStream admissionPipe, string root,
        IManagedProcessTermination? termination = null)
    {
        ManagedProcessLifetimeLease lifetime = Acquire(Path.Combine(root, "state.json"));
        return new(Task.FromResult<Process?>(process), admissionPipe,
            new BootstrapStartAuthorization(TransportFixture.Names), lifetime,
            termination ?? ManagedProcessTermination.Instance, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>Slow cleanup cannot extend the frozen admission wall-clock budget.</summary>
    [Fact]
    public async Task BootstrapAdmissionSlowCleanupReturnsTypedUncertaintyWithinTotalDeadline()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        WindowsCustodyCapability.RequireFile(workspace.Root);
        using Process process = StartSilentProbe(workspace.Root);
        int processId = process.Id;
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        await using var client = new AnonymousPipeClientStream(PipeDirection.Out, WindowsPipeHandles.DuplicateClient(pipe));
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch = CreateBootstrapLaunch(process, pipe, workspace.Root,
            new SlowThenRealTermination(TimeSpan.FromSeconds(1)));
        TimeSpan operationBudget = TimeSpan.FromMilliseconds(1500);
        var budget = new ImmutableBootstrapWaitBudget(operationBudget, operationBudget + TimeSpan.FromMilliseconds(500));
        using var operationCutoff = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        operationCutoff.CancelAfter(operationBudget);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ImmutableBootstrapAdmissionResult result = await launch.WaitForAdmissionAsync(budget, operationCutoff.Token);
            stopwatch.Stop();
            Assert.Equal(ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed, result.Outcome);
            Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(1800), TimeSpan.FromMilliseconds(3000));
            Assert.Equal(ImmutableBootstrapExitIssue.TerminationUnconfirmed, result.ExitIssue);
            Assert.True(result.HasValidShape);
            await WaitForProcessExitAsync(processId);
            await WaitForBootstrapLifetimeExitAsync(Path.Combine(workspace.Root, "state.json"));
        }
        finally { TerminateProcess(processId); }
    }

    /// <summary>Slow completion cleanup consumes only its frozen caller-visible half second.</summary>
    [Fact]
    public async Task BootstrapCompletionSlowCleanupReturnsTypedUncertaintyWithinCleanupBudget()
    {
        RequireWindows();
        using var workspace = TestWorkspace.Create();
        WindowsCustodyCapability.RequireFile(workspace.Root);
        using Process process = StartSilentProbe(workspace.Root);
        int processId = process.Id;
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        await using var client = new AnonymousPipeClientStream(PipeDirection.Out, WindowsPipeHandles.DuplicateClient(pipe));
        pipe.DisposeLocalCopyOfClientHandle();
        using ImmutableBootstrapProcessLaunch launch = CreateBootstrapLaunch(process, pipe, workspace.Root,
            new SlowThenRealTermination(TimeSpan.FromSeconds(1)));
        await client.WriteAsync("ADMITTED\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await client.FlushAsync(TestContext.Current.CancellationToken);
        ImmutableBootstrapAdmissionResult admission = await launch.WaitForAdmissionAsync(AdmissionBudget, TestContext.Current.CancellationToken);
        var budget = new ImmutableBootstrapWaitBudget(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(600));
        using var operationCutoff = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        operationCutoff.CancelAfter(budget.RemainingOperation);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ImmutableBootstrapCompletionResult result = await launch.WaitForCompletionAsync(budget, operationCutoff.Token);
            stopwatch.Stop();
            Assert.Equal(ImmutableBootstrapAdmissionOutcome.Admitted, admission.Outcome);
            Assert.Equal(ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed, result.Outcome);
            Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));
            Assert.Equal(ImmutableBootstrapExitIssue.TerminationUnconfirmed, result.ExitIssue);
            Assert.True(result.HasValidShape);
            await WaitForProcessExitAsync(processId);
            await WaitForBootstrapLifetimeExitAsync(Path.Combine(workspace.Root, "state.json"));
        }
        finally { TerminateProcess(processId); }
    }

    private static async Task WaitForProcessExitAsync(int processId)
    {
        long deadline = Environment.TickCount64 + 2_000;
        while (IsRunning(processId) && Environment.TickCount64 < deadline)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.False(IsRunning(processId));
    }

    private static async Task WaitForBootstrapLifetimeExitAsync(string state)
    {
        long deadline = Environment.TickCount64 + 2_000;
        while (ManagedProcessLifetimeLease.GetStatus(Protocol, state, ManagedProcessLifetimeKind.Bootstrap) != ManagedProcessLifetimeStatus.Exited &&
            Environment.TickCount64 < deadline)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.Equal(ManagedProcessLifetimeStatus.Exited, ManagedProcessLifetimeLease.GetStatus(Protocol, state, ManagedProcessLifetimeKind.Bootstrap));
    }

    private sealed class SlowThenRealTermination(TimeSpan delay) : IManagedProcessTermination
    {
        public ManagedProcessTerminationResult ConfirmExited(Process process)
        {
            Thread.Sleep(delay);
            return ManagedProcessTermination.Instance.ConfirmExited(process);
        }
    }

    private static ImmutableBootstrapProcessLaunch StartBootstrapTree(
        string root, string marker, string behavior, out AnonymousPipeClientStream client,
        out int processId, int exitCode = 0)
    {
        ImmutableBootstrapProcessLaunch launch = StartBootstrapTreeCore(root, marker, behavior, true,
            out AnonymousPipeClientStream? created, out processId, exitCode);
        client = created ?? throw new InvalidOperationException("Bootstrap admission writer was not created.");
        return launch;
    }

    private static ImmutableBootstrapProcessLaunch StartBootstrapTreeWithoutAdmissionWriter(
        string root, string marker, string behavior, out int processId) =>
        StartBootstrapTreeCore(root, marker, behavior, false, out _, out processId, 0);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
        "Process, pipe, gate, and lifetime transfer into the returned receipt; the writer transfers to the caller.")]
    private static ImmutableBootstrapProcessLaunch StartBootstrapTreeCore(
        string root, string marker, string behavior, bool provideWriter,
        out AnonymousPipeClientStream? client, out int processId, int exitCode)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        ManagedProcessLifetimeLease lifetime = Acquire(Path.Combine(root, "bootstrap-state.json"));
        var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        var gate = new BootstrapStartAuthorization(TransportFixture.Names);
        client = null;
        try
        {
            if (provideWriter)
            {
                client = new AnonymousPipeClientStream(PipeDirection.Out, WindowsPipeHandles.DuplicateClient(pipe));
            }
            pipe.DisposeLocalCopyOfClientHandle();
            ProcessStartInfo info = ProcessProbe.Create(behavior);
            info.Environment["CORE_TEST_PROBE_TREE_MARKER"] = marker;
            info.Environment["CORE_TEST_PROBE_EXIT_CODE"] = exitCode.ToString(CultureInfo.InvariantCulture);
            // This BCL fixture proves parent receipt and Job behavior; linked capture tests
            // separately exercise Core capture and disposal inside the child.
            info.Environment[TransportFixture.Names.LifetimeJob] = lifetime.JobName;
            Process process = ProcessLaunchGate.StartContained(info, [],
                static () => true) ?? throw new InvalidOperationException("Bootstrap tree probe did not start.");
            processId = process.Id;
            return new(Task.FromResult<Process?>(process), pipe, gate, lifetime,
                ManagedProcessTermination.Instance, TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            client?.Dispose();
            pipe.Dispose();
            gate.Dispose();
            lifetime.Dispose();
            throw;
        }
    }

    private static ManagedProcessLifetimeLease Acquire(string state) =>
        ManagedProcessLifetimeLease.TryAcquire(Protocol, state, ManagedProcessLifetimeKind.Bootstrap)
        ?? throw new InvalidOperationException("Bootstrap lifetime was not acquired.");

    private static Process StartSilentProbe(string root)
    {
        ProcessStartInfo info = ProcessProbe.Create("silent-wait");
        info.Environment["CORE_TEST_PROBE_MARKER"] = Path.Combine(root, "silent-probe");
        return ProcessLaunchGate.StartContained(info, [], static () => true)
            ?? throw new InvalidOperationException("Silent probe did not start.");
    }

    private static async Task<int> WaitForProcessMarkerAsync(string marker)
    {
        long deadline = Environment.TickCount64 + 5_000;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                if (File.Exists(marker) && int.TryParse(await File.ReadAllTextAsync(marker,
                    TestContext.Current.CancellationToken), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                {
                    return id;
                }
            }
            catch (IOException) { }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        throw new InvalidOperationException("Process marker was not readable before its deadline.");
    }

    private static bool IsRunning(int id)
    {
        if (id == 0) { return false; }
        try { using Process process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static void TerminateProcess(int id)
    {
        if (id == 0) { return; }
        try
        {
            using Process process = Process.GetProcessById(id);
            if (!process.HasExited) { process.Kill(entireProcessTree: true); _ = process.WaitForExit(5_000); }
        }
        catch (Exception exception) when (exception is ArgumentException or Win32Exception) { }
    }

    private static void AssertProcessExited(int id) =>
        _ = Assert.Throws<ArgumentException>(() => Process.GetProcessById(id));
    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This test requires Windows contained creation and named Job descendant supervision."); }
    }
}
