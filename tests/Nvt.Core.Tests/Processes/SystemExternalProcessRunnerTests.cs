// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Nvt.Core.Processes;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>Verifies external process cancellation leaves no child process running.</summary>
public sealed class SystemExternalProcessRunnerTests
{
    /// <summary>Approved external tools never allocate a visible console or use a shell.</summary>
    [Fact]
    public void CreateProcessStartInfoIsHeadlessAndShellFree()
    {
        var request = new ExternalProcessStartInfo(
            "approved-tool.exe",
            Environment.CurrentDirectory,
            ["first", "second value"],
            TimeSpan.FromSeconds(5));

        ProcessStartInfo actual = SystemExternalProcessRunner.CreateProcessStartInfo(request);

        Assert.False(actual.UseShellExecute);
        Assert.True(actual.CreateNoWindow);
        Assert.True(actual.RedirectStandardOutput);
        Assert.True(actual.RedirectStandardError);
        Assert.Equal(request.ExecutablePath, actual.FileName);
        Assert.Equal(request.WorkingDirectory, actual.WorkingDirectory);
        Assert.Equal(request.Arguments, [.. actual.ArgumentList]);
    }

    /// <summary>An OS start failure is typed and releases its reservation before the next run.</summary>
    [Fact]
    public async Task RunAsyncTranslatesOperatingSystemStartFailureToTypedExceptionAndReleasesCapacity()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        var capacity = new ExternalProcessCapacity(1);
        var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with { Capacity = capacity });
        string missingExecutable = Path.Combine(Environment.CurrentDirectory, $"core-missing-tool-{Guid.NewGuid():N}.exe");
        var failingStartInfo = new ExternalProcessStartInfo(
            missingExecutable, Environment.CurrentDirectory, [], TimeSpan.FromSeconds(5));

        ExternalProcessStartFailedException failure = await Assert.ThrowsAsync<ExternalProcessStartFailedException>(
            () => runner.RunAsync(failingStartInfo, TestContext.Current.CancellationToken).AsTask());

        _ = Assert.IsType<Win32Exception>(failure.InnerException);
        Assert.Equal(0, capacity.InUse);

        // The failed launch released its reservation, so an ordinary run is accepted right after at the same limit.
        var succeedingStartInfo = new ExternalProcessStartInfo(
            ProcessProbe.Executable,
            Environment.CurrentDirectory,
            ["--mode", "exit"],
            TimeSpan.FromSeconds(5));

        ExternalProcessResult result = await runner.RunAsync(succeedingStartInfo, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
    }

    /// <summary>Cancellation kills the launched process tree before the caller receives cancellation.</summary>
    [Fact]
    public async Task RunAsyncCancellationKillsChildProcessBeforeThrowing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        string marker = workspace.PathFor("child.pid");
        var parentReady = new TaskCompletionSource<TestProcessIdentity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = CreateTreeRunner(parentReady);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        ExternalProcessStartInfo startInfo = CreateStartInfo(workspace.Root, "tree-root-wait", marker, TimeSpan.FromSeconds(30));

        Task<ExternalProcessResult>? run = null;
        TestProcessIdentity? parentProcess = null;
        TestProcessIdentity? childProcess = null;
        try
        {
            run = runner.RunAsync(startInfo, cancellation.Token).AsTask();
            (parentProcess, childProcess) = await ReadProcessIdentitiesAsync(
                marker,
                parentReady.Task,
                run,
                TestContext.Current.CancellationToken);

            cancellation.Cancel();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            await AssertProcessExitedAsync(parentProcess.Value, TestContext.Current.CancellationToken);
            await AssertProcessExitedAsync(childProcess.Value, TestContext.Current.CancellationToken);
        }
        finally
        {
            cancellation.Cancel();

            // Kill first so the join cannot wait on pending cleanup timers of a frozen manual clock.
            KillTestProcessTree(parentProcess ?? (parentReady.Task.IsCompletedSuccessfully ? await parentReady.Task : null));
            KillTestProcessTree(childProcess);
            if (run is not null)
            {
                try
                {
                    _ = await run.WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the expected cleanup outcome for this test process.
                }
                catch (TimeoutException)
                {
                    // The processes are already killed; do not hide the original test failure.
                }
            }
        }
    }

    /// <summary>Timeout kills the launched process tree before the timeout result is returned.</summary>
    [Fact]
    public async Task RunAsyncTimeoutKillsChildProcessBeforeReturning()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        using var workspace = TestWorkspace.Create();
        string marker = workspace.PathFor("child.pid");
        var time = new ManualTimeProvider();
        TimeSpan timeout = TimeSpan.FromSeconds(10);
        var parentReady = new TaskCompletionSource<TestProcessIdentity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = CreateTreeRunner(parentReady, time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        ExternalProcessStartInfo startInfo = CreateStartInfo(workspace.Root, "tree-root-wait", marker, timeout);
        Task<ExternalProcessResult>? run = null;
        TestProcessIdentity? parentProcess = null;
        TestProcessIdentity? childProcess = null;
        try
        {
            run = runner.RunAsync(startInfo, cancellation.Token).AsTask();
            (parentProcess, childProcess) = await ReadProcessIdentitiesAsync(
                marker,
                parentReady.Task,
                run,
                TestContext.Current.CancellationToken);

            // Establish the real child identities before making the execution timeout eligible to fire.
            Assert.False(run.IsCompleted, "The timeout preceded the child handshake.");
            await time.WhenPendingAsync(1, timeout, TestContext.Current.CancellationToken)
                .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
            time.Advance(timeout);
            ExternalProcessResult result = await run.WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);

            Assert.True(result.TimedOut);
            Assert.Equal(-1, result.ExitCode);
            await AssertProcessExitedAsync(parentProcess.Value, TestContext.Current.CancellationToken);
            await AssertProcessExitedAsync(childProcess.Value, TestContext.Current.CancellationToken);
        }
        finally
        {
            cancellation.Cancel();

            // Kill first so the join cannot wait on pending cleanup timers of a frozen manual clock.
            KillTestProcessTree(parentProcess ?? (parentReady.Task.IsCompletedSuccessfully ? await parentReady.Task : null));
            KillTestProcessTree(childProcess);
            if (run is not null)
            {
                try
                {
                    _ = await run.WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the expected emergency cleanup outcome when setup fails.
                }
                catch (TimeoutException)
                {
                    // The processes are already killed; do not hide the original test failure.
                }
            }
        }
    }

    /// <summary>Large stdout and stderr are drained concurrently but retained only within the diagnostic cap.</summary>
    [Fact]
    public async Task RunAsyncBoundsAndDrainsBothOutputStreams()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        var runner = new SystemExternalProcessRunner();
        ExternalProcessStartInfo startInfo = CreateStartInfo(
            Environment.CurrentDirectory, "dual-output-exit", TimeSpan.FromSeconds(10));

        ExternalProcessResult result = await runner.RunAsync(
            startInfo,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal(BoundedProcessOutputReader.MaximumCapturedCharacters, result.StandardOutput.Length);
        Assert.Equal(BoundedProcessOutputReader.MaximumCapturedCharacters, result.StandardError.Length);
        Assert.StartsWith(new string('A', 256), result.StandardOutput, StringComparison.Ordinal);
        Assert.StartsWith(new string('B', 256), result.StandardError, StringComparison.Ordinal);
        Assert.Contains(BoundedProcessOutputReader.TruncationMarker, result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains(BoundedProcessOutputReader.TruncationMarker, result.StandardError, StringComparison.Ordinal);
        Assert.EndsWith("OUT-END", result.StandardOutput, StringComparison.Ordinal);
        Assert.EndsWith("ERR-END", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>Timeout waits for killed-stream drainage and retains bounded partial diagnostics.</summary>
    [Fact]
    public async Task RunAsyncTimeoutRetainsBoundedPartialOutputAfterKill()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows process execution is required.");
        }

        var time = new ManualTimeProvider();
        TimeSpan timeout = TimeSpan.FromSeconds(10);
        var outputCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int streams = 0;
        var runner = new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with
        {
            Time = time,
            ScheduleTermination = static termination => Task.FromResult(termination()),
            Drain = (reader, stop) => ++streams == 1
                ? DrainWithCaptureSignalAsync(reader, "OUT-PARTIAL-END", outputCaptured, stop)
                : DrainWithCaptureSignalAsync(reader, "ERR-PARTIAL-END", errorCaptured, stop),
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        ExternalProcessStartInfo startInfo = CreateStartInfo(
            Environment.CurrentDirectory, "dual-output-wait", timeout);

        Task<ExternalProcessResult> run = runner.RunAsync(startInfo, cancellation.Token).AsTask();
        try
        {
            // Both suffixes must be retained by the production drains before the kill can interrupt output.
            await Task.WhenAll(outputCaptured.Task, errorCaptured.Task)
                .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
            Assert.False(run.IsCompleted, "The timeout preceded output capture.");
            await time.WhenPendingAsync(1, timeout, TestContext.Current.CancellationToken)
                .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
            time.Advance(timeout);
            ExternalProcessResult result = await run.WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);

            Assert.Equal(-1, result.ExitCode);
            Assert.True(result.TimedOut);
            Assert.Equal(BoundedProcessOutputReader.MaximumCapturedCharacters, result.StandardOutput.Length);
            Assert.Equal(BoundedProcessOutputReader.MaximumCapturedCharacters, result.StandardError.Length);
            Assert.EndsWith("OUT-PARTIAL-END", result.StandardOutput, StringComparison.Ordinal);
            Assert.EndsWith("ERR-PARTIAL-END", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                _ = await run.WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Join emergency termination if output setup fails before advancing the clock.
            }
        }
    }

    private static async Task<BoundedProcessOutput> DrainWithCaptureSignalAsync(
        TextReader reader, string expected, TaskCompletionSource captured, CancellationToken stop)
    {
        using var observed = new CaptureSignalReader(reader, expected, captured);
        return await ExternalProcessRunnerSeams.Production.Drain(observed, stop).ConfigureAwait(false);
    }

    /// <summary>Signals on the read after the expected text, once the production drain has retained that text.</summary>
    private sealed class CaptureSignalReader(TextReader reader, string expected, TaskCompletionSource captured) : TextReader
    {
        // Only the dedicated production reader thread accesses these fields.
        private string _tail = string.Empty;
        private bool _matched;

        /// <inheritdoc/>
        public override int Read(char[] buffer, int index, int count)
        {
            if (_matched)
            {
                _ = captured.TrySetResult();
            }

            int read = reader.Read(buffer, index, count);
            string text = string.Concat(_tail, new string(buffer, index, read));
            _matched |= text.Contains(expected, StringComparison.Ordinal);
            _tail = text.Length <= expected.Length ? text : text[^expected.Length..];
            return read;
        }
    }

    private static ExternalProcessStartInfo CreateStartInfo(string root, string mode, TimeSpan timeout)
    {
        return new ExternalProcessStartInfo(ProcessProbe.Executable, root, ["--mode", mode], timeout);
    }

    private static ExternalProcessStartInfo CreateStartInfo(string root, string mode, string marker, TimeSpan timeout)
    {
        return new ExternalProcessStartInfo(
            ProcessProbe.Executable, root, ["--mode", mode, "--tree-marker", marker], timeout);
    }

    private static SystemExternalProcessRunner CreateTreeRunner(TaskCompletionSource<TestProcessIdentity> parentReady, TimeProvider? time = null)
    {
        return new SystemExternalProcessRunner(ExternalProcessRunnerSeams.Production with
        {
            Time = time ?? ExternalProcessRunnerSeams.Production.Time,
            ScheduleTermination = time is null
                ? ExternalProcessRunnerSeams.Production.ScheduleTermination
                : static termination => Task.FromResult(termination()),
            ObserveExit = (process, token) =>
            {
                _ = parentReady.TrySetResult(CaptureProcessIdentity(process.Id));
                return process.WaitForExitAsync(token);
            },
        });
    }

    private static async Task<(TestProcessIdentity Parent, TestProcessIdentity Child)> ReadProcessIdentitiesAsync(
        string marker,
        Task<TestProcessIdentity> parentReady,
        Task<ExternalProcessResult> run,
        CancellationToken cancellationToken)
    {
        TestProcessIdentity parent = await parentReady.WaitAsync(ProcessProbe.FixtureBound, cancellationToken);
        var clock = Stopwatch.StartNew();
        string? childText = null;
        while (clock.Elapsed < ProcessProbe.FixtureBound)
        {
            if (File.Exists(marker))
            {
                try
                {
                    childText = await File.ReadAllTextAsync(marker, cancellationToken);
                    if (int.TryParse(childText, NumberStyles.None, CultureInfo.InvariantCulture, out int childId) && childId > 0)
                    {
                        return (parent, CaptureProcessIdentity(childId));
                    }
                }
                catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
                {
                    // The probe's marker is visible before its writer closes; retry inside the fixture bound.
                }
            }

            if (run.IsCompleted)
            {
                ExternalProcessResult result = await run;
                Assert.Fail(
                    "The test process exited before publishing readiness: " +
                    $"exit={result.ExitCode}, timedOut={result.TimedOut}, stderr={result.StandardError}");
            }
            await Task.Delay(10, cancellationToken);
        }

        Assert.Fail($"The test process did not publish a valid identity: {childText ?? "<null>"}");
        return default;
    }

    private static TestProcessIdentity CaptureProcessIdentity(int processId)
    {
        using var process = Process.GetProcessById(processId);
        return new TestProcessIdentity(processId, process.StartTime);
    }

    private static async Task AssertProcessExitedAsync(TestProcessIdentity expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(expected.ProcessId);
            if (process.HasExited || process.StartTime != expected.StartTime)
            {
                return;
            }

            using var exitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            exitCancellation.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await process.WaitForExitAsync(exitCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Assert.Fail(
                    $"Test process {expected.ProcessId} (started {expected.StartTime:O}) is still running 3 seconds after runner completion.");
            }
        }
        catch (ArgumentException)
        {
            return;
        }
    }

    private static void KillTestProcessTree(TestProcessIdentity? expected)
    {
        if (expected is not TestProcessIdentity value)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(value.ProcessId);
            if (!process.HasExited && process.StartTime == value.StartTime)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
            // The process already exited, which is the expected outcome.
        }
    }

    private readonly record struct TestProcessIdentity(int ProcessId, DateTime StartTime);
}
