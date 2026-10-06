// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Nvt.Core.Processes;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>Exercises the one process-local native containment boundary.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class ProcessLaunchGateTests
{
    private const string AllowedHandleEnvironment = "CORE_TEST_PROBE_ALLOWED_HANDLE";
    private const string CrossHandleEnvironment = "CORE_TEST_PROBE_CROSS_HANDLE";

    /// <summary>An ambient inheritable handle is excluded when absent from the exact allowlist.</summary>
    [Fact]
    public async Task ContainedChildExcludesUnstatedAmbientInheritableHandle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test needs the Windows contained starter.");
            return;
        }
        using var workspace = TestWorkspace.Create();
        using var ambient = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        string marker = workspace.PathFor("containment/ambient.txt");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        ProcessStartInfo startInfo = CreateProbe(marker, ambient.GetClientHandleAsString());

        using Process process = ProcessLaunchGate.StartContained(startInfo, []) ??
            throw new InvalidOperationException("Containment probe did not start.");
        ambient.DisposeLocalCopyOfClientHandle();
        using var reader = new StreamReader(ambient, Encoding.UTF8, leaveOpen: true);
        string leaked = await reader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
        Assert.Empty(leaked);
        Assert.Equal("started", await File.ReadAllTextAsync(
            marker,
            TestContext.Current.CancellationToken));
    }

    /// <summary>A duplication failure starts nothing, cleans up, and leaves the gate reusable.</summary>
    [Fact]
    public async Task InvalidAllowlistHandleStartsNoChildAndDoesNotPoisonNextLaunch()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test needs the Windows contained starter.");
            return;
        }
        using var workspace = TestWorkspace.Create();
        using var ambient = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        using var cleanupPipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.None);
        string failedMarker = workspace.PathFor("containment/failed.txt");
        string recoveredMarker = workspace.PathFor("containment/recovered.txt");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(failedMarker)!);

        _ = Assert.Throws<Win32Exception>(() => ProcessLaunchGate.StartContained(
            CreateProbe(failedMarker, ambient.GetClientHandleAsString()),
            [
                ProcessInheritedHandle.Parse(
                    "CORE_TEST_VALID_HANDLE",
                    cleanupPipe.GetClientHandleAsString()),
                new ProcessInheritedHandle("CORE_TEST_INVALID_HANDLE", new IntPtr(0x12345)),
            ]));
        Assert.False(File.Exists(failedMarker));
        cleanupPipe.DisposeLocalCopyOfClientHandle();
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                   TestContext.Current.CancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var cleanupReader = new StreamReader(cleanupPipe, Encoding.UTF8, leaveOpen: true);
            Assert.Empty(await cleanupReader.ReadToEndAsync(timeout.Token)
                .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
        }

        using Process recovered = ProcessLaunchGate.StartContained(
            CreateProbe(recoveredMarker, ambient.GetClientHandleAsString()),
            []) ?? throw new InvalidOperationException("Recovery probe did not start.");
        ambient.DisposeLocalCopyOfClientHandle();
        using var reader = new StreamReader(ambient, Encoding.UTF8, leaveOpen: true);
        string leaked = await reader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        await recovered.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);

        Assert.Equal(0, recovered.ExitCode);
        Assert.Empty(leaked);
        Assert.Equal("started", await File.ReadAllTextAsync(
            recoveredMarker,
            TestContext.Current.CancellationToken));
    }

    /// <summary>Every zero or negative raw handle is rejected before native work begins.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void InheritedHandleRejectsEveryNonPositiveValue(long value)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessInheritedHandle("CORE_TEST_HANDLE", new IntPtr(value)));
    }

    /// <summary>Concurrent contained starts inherit only their own physical pipe.</summary>
    [Fact]
    public async Task ParallelContainedStartsDoNotCrossInheritHandles()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test needs the Windows contained starter.");
            return;
        }
        using var firstPipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.None);
        using var secondPipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.None);
        ProcessStartInfo firstInfo = CreateIsolationProbe(
            "first",
            secondPipe.GetClientHandleAsString());
        ProcessStartInfo secondInfo = CreateIsolationProbe(
            "second",
            firstPipe.GetClientHandleAsString());

        Task<Process?> firstStart = Task.Run(() => ProcessLaunchGate.StartContained(
            firstInfo,
            [ProcessInheritedHandle.Parse(
                AllowedHandleEnvironment,
                firstPipe.GetClientHandleAsString())]));
        Task<Process?> secondStart = Task.Run(() => ProcessLaunchGate.StartContained(
            secondInfo,
            [ProcessInheritedHandle.Parse(
                AllowedHandleEnvironment,
                secondPipe.GetClientHandleAsString())]));
        Process?[] started = await Task.WhenAll(firstStart, secondStart)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        using Process first = started[0] ?? throw new InvalidOperationException("First probe did not start.");
        using Process second = started[1] ?? throw new InvalidOperationException("Second probe did not start.");
        firstPipe.DisposeLocalCopyOfClientHandle();
        secondPipe.DisposeLocalCopyOfClientHandle();

        using var firstReader = new StreamReader(firstPipe, Encoding.UTF8, leaveOpen: true);
        using var secondReader = new StreamReader(secondPipe, Encoding.UTF8, leaveOpen: true);
        Task<string> firstRead = firstReader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        Task<string> secondRead = secondReader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        await Task.WhenAll(
            first.WaitForExitAsync(TestContext.Current.CancellationToken),
            second.WaitForExitAsync(TestContext.Current.CancellationToken))
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal("first", await firstRead);
        Assert.Equal("second", await secondRead);
    }

    /// <summary>The native boundary preserves Unicode arguments, environment, and working paths.</summary>
    [Fact]
    public async Task ContainedStartPreservesUnicodeArgumentsAndEnvironment()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test needs the Windows contained starter.");
            return;
        }
        using var workspace = TestWorkspace.Create();
        string workingDirectory = workspace.PathFor("containment/路徑 空間");
        _ = Directory.CreateDirectory(workingDirectory);
        string marker = Path.Combine(workingDirectory, "引數 結果.txt");
        string[] arguments = ["第一 個", "quote\"inside", "尾端\\", ""];
        const string environmentValue = "環境 值 ✓";
        ProcessStartInfo startInfo = CreateArgumentProbe(
            marker,
            workingDirectory,
            environmentValue,
            arguments);

        using Process process = ProcessLaunchGate.StartContained(startInfo, []) ??
            throw new InvalidOperationException("Unicode probe did not start.");
        await process.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
        string[] expected = [environmentValue, Path.GetFullPath(workingDirectory), .. arguments];
        Assert.Equal(
            expected,
            await File.ReadAllLinesAsync(marker, TestContext.Current.CancellationToken));
    }

    /// <summary>Final validation runs after native preparation and rejects changed custody.</summary>
    [Fact]
    public async Task FinalValidationRejectsChangeAfterNativePreparation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test needs the Windows contained starter.");
            return;
        }
        using var workspace = TestWorkspace.Create();
        using var pipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.None);
        string marker = workspace.PathFor("containment/adjacent-validation.txt");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        int custodyValid = 1;

        Process? rejected = WindowsContainedProcessStarter.Start(
            CreateProbe(marker, pipe.GetClientHandleAsString()),
            [ProcessInheritedHandle.Parse("CORE_TEST_VALID_HANDLE", pipe.GetClientHandleAsString())],
            () => Volatile.Read(ref custodyValid) != 0,
            beforeFinalValidationForTesting: () => Volatile.Write(ref custodyValid, 0));

        Assert.Null(rejected);
        Assert.False(File.Exists(marker));
        pipe.DisposeLocalCopyOfClientHandle();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        Assert.Empty(await reader.ReadToEndAsync(timeout.Token)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
    }

    /// <summary>Final validation executes only after the shared start gate is acquired.</summary>
    [Fact]
    public async Task ValidationWaitsForGateAndRejectsChangedStateBeforeStart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test needs the Windows contained starter.");
            return;
        }
        using var workspace = TestWorkspace.Create();
        string firstMarker = workspace.PathFor("containment/gate-first.txt");
        string secondMarker = workspace.PathFor("containment/gate-second.txt");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(firstMarker)!);
        using var firstValidationEntered = new ManualResetEventSlim();
        using var releaseFirstValidation = new ManualResetEventSlim();
        using var secondAttemptingStart = new ManualResetEventSlim();
        using var secondValidationEntered = new ManualResetEventSlim();
        int secondMayStart = 1;

        Task<Process?> firstStart = Task.Factory.StartNew(
            () => ProcessLaunchGate.StartContained(
                CreateArgumentProbe(firstMarker, workspace.Root, "first", []),
                [],
                () =>
                {
                    firstValidationEntered.Set();
                    Assert.True(releaseFirstValidation.Wait(
                        ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
                    return true;
                }),
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Assert.True(firstValidationEntered.Wait(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken));

        Task<Process?> secondStart = Task.Factory.StartNew(
            () =>
            {
                secondAttemptingStart.Set();
                return ProcessLaunchGate.StartContained(
                    CreateArgumentProbe(secondMarker, workspace.Root, "second", []),
                    [],
                    () =>
                    {
                        secondValidationEntered.Set();
                        return Volatile.Read(ref secondMayStart) != 0;
                    });
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Assert.True(secondAttemptingStart.Wait(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken));
        Assert.False(secondValidationEntered.Wait(
            TimeSpan.FromMilliseconds(200),
            TestContext.Current.CancellationToken));
        Volatile.Write(ref secondMayStart, 0);
        releaseFirstValidation.Set();

        using Process first = await firstStart.WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken) ??
            throw new InvalidOperationException("First gated probe did not start.");
        Process? rejected = await secondStart.WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        await first.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);

        Assert.True(secondValidationEntered.IsSet);
        Assert.Null(rejected);
        Assert.Equal(0, first.ExitCode);
        Assert.True(File.Exists(firstMarker));
        Assert.False(File.Exists(secondMarker));
    }

    /// <summary>A failure after CreateProcess terminates the suspended child and closes every duplicate.</summary>
    [Fact]
    public async Task PostCreateFailureTerminatesSuspendedChildAndReleasesPhysicalPipe()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test needs the Windows contained starter.");
            return;
        }
        using var pipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.None);
        var injected = new InvalidOperationException("injected after CreateProcess");
        ProcessStartInfo startInfo = CreateIsolationProbe(
            "must-not-run",
            pipe.GetClientHandleAsString());

        InvalidOperationException? observed = null;
        try
        {
            _ = WindowsContainedProcessStarter.Start(
                startInfo,
                [ProcessInheritedHandle.Parse(
                    AllowedHandleEnvironment,
                    pipe.GetClientHandleAsString())],
                static () => true,
                afterCreateProcessForTesting: () => throw injected);
        }
        catch (InvalidOperationException error)
        {
            observed = error;
        }
        Assert.Same(injected, observed);

        pipe.DisposeLocalCopyOfClientHandle();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        Assert.Empty(await reader.ReadToEndAsync(timeout.Token)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
    }

    private static ProcessStartInfo CreateProbe(string marker, string ambientHandle)
    {
        ProcessStartInfo startInfo = ProcessProbe.Create("ambient-pipe");
        startInfo.Environment["CORE_TEST_PROBE_MARKER"] = marker;
        startInfo.Environment["CORE_TEST_PROBE_AMBIENT_HANDLE"] = ambientHandle;
        return startInfo;
    }

    private static ProcessStartInfo CreateIsolationProbe(string payload, string crossHandle)
    {
        ProcessStartInfo startInfo = ProcessProbe.Create("contained-isolation");
        startInfo.Environment["CORE_TEST_PROBE_PAYLOAD"] = payload;
        startInfo.Environment[CrossHandleEnvironment] = crossHandle;
        return startInfo;
    }

    private static ProcessStartInfo CreateArgumentProbe(
        string marker,
        string workingDirectory,
        string environmentValue,
        IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = ProcessProbe.Create("arguments-environment");
        startInfo.WorkingDirectory = workingDirectory;
        startInfo.Environment["CORE_TEST_PROBE_MARKER"] = marker;
        startInfo.Environment["CORE_TEST_PROBE_TEXT"] = environmentValue;
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }
}
