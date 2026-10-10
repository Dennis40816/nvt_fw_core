// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>
/// Pins the child process fixture with real Windows PowerShell children. The children only print, exit or sleep.
/// No test decides a result by elapsed time; the watchdog is long, or a manual clock.
/// </summary>
public sealed class ChildProcessFixtureTests
{
    private const string Sleep = "Start-Sleep -Seconds 600";

    private static void RequireWindows() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "These tests start Windows PowerShell as the child process.");

    private static ChildProcessFixture Start(string script, int maxOutputCharacters = ChildProcessFixture.DefaultMaxOutputCharacters,
        TimeSpan? watchdog = null, TimeProvider? clock = null) =>
        ChildProcessFixture.Start("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script],
            watchdog: watchdog, maxOutputCharacters: maxOutputCharacters, clock: clock);

    /// <summary>The fixture returns the exit code and captures standard output and standard error.</summary>
    [Fact]
    public async Task WaitForExitAsyncReturnsTheExitCodeAndCapturesBothStreams()
    {
        RequireWindows();
        await using ChildProcessFixture fixture = Start("Write-Output 'out-line'; [Console]::Error.WriteLine('err-line'); exit 7");

        int exitCode = await fixture.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7, exitCode);
        Assert.Contains("out-line", fixture.Output, StringComparison.Ordinal);
        Assert.Contains("err-line", fixture.Output, StringComparison.Ordinal);
        Assert.False(fixture.OutputTruncated);
        Assert.False(fixture.WatchdogExpired);
    }

    /// <summary>The captured output stops at the limit and says that it was cut.</summary>
    [Fact]
    public async Task OutputIsLimitedAndMarkedTruncated()
    {
        RequireWindows();
        await using ChildProcessFixture fixture = Start("1..50 | ForEach-Object { 'x' * 10 }", maxOutputCharacters: 100);

        _ = await fixture.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(100, fixture.Output.Length);
        Assert.True(fixture.OutputTruncated);
    }

    /// <summary>WaitForOutputAsync completes when the text arrives, and the test can then end the child.</summary>
    [Fact]
    public async Task WaitForOutputAsyncCompletesWhenTheTextArrives()
    {
        RequireWindows();
        await using ChildProcessFixture fixture = Start("Write-Output 'ready'; " + Sleep);

        await fixture.WaitForOutputAsync("ready", TestContext.Current.CancellationToken);
        Assert.False(fixture.HasExited);

        fixture.KillTree();
        _ = await fixture.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(fixture.HasExited);
    }

    /// <summary>When the output ends without the text, the wait fails instead of hanging.</summary>
    [Fact]
    public async Task WaitForOutputAsyncOutputEndsWithoutTheTextThrows()
    {
        RequireWindows();
        await using ChildProcessFixture fixture = Start("Write-Output 'other'");

        _ = await fixture.WaitForExitAsync(TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.WaitForOutputAsync("never", TestContext.Current.CancellationToken));
    }

    /// <summary>Disposal ends the child and its descendant.</summary>
    [Fact]
    public async Task DisposeEndsTheWholeProcessTree()
    {
        RequireWindows();
        const string Script = "$p = Start-Process powershell.exe -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile','-Command','" + Sleep + "'; " +
            "Write-Output \"grandchild=$($p.Id)\"; " + Sleep;
        ChildProcessFixture fixture = Start(Script);
        int grandchildId;
        try
        {
            await fixture.WaitForOutputAsync("grandchild=", TestContext.Current.CancellationToken);
            string line = fixture.Output.Split('\n').First(text => text.StartsWith("grandchild=", StringComparison.Ordinal));
            grandchildId = int.Parse(line["grandchild=".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            await fixture.DisposeAsync();
        }

        Assert.True(fixture.HasExited);
        try
        {
            using Process grandchild = Process.GetProcessById(grandchildId);
            await SignalWait.WaitAsync(grandchild.WaitForExitAsync(TestContext.Current.CancellationToken), "grandchild exit",
                cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (ArgumentException)
        {
            // The grandchild already ended.
        }
    }

    /// <summary>The watchdog ends the process tree, and the wait reports it as a timeout.</summary>
    [Fact]
    public async Task WatchdogEndsTheProcessTreeAndWaitForExitThrowsTimeout()
    {
        RequireWindows();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));
        var watchdog = TimeSpan.FromSeconds(5);
        await using ChildProcessFixture fixture = Start("Write-Output 'ready'; " + Sleep, watchdog: watchdog, clock: clock);
        await fixture.WaitForOutputAsync("ready", TestContext.Current.CancellationToken);

        clock.Advance(watchdog);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => fixture.WaitForExitAsync(TestContext.Current.CancellationToken));
        Assert.Contains("did not exit", exception.Message, StringComparison.Ordinal);
        Assert.True(fixture.WatchdogExpired);
    }

    /// <summary>A child that exits by itself before the watchdog is not a timeout.</summary>
    [Fact]
    public async Task WatchdogAfterNormalExitDoesNotReportAnExpiry()
    {
        RequireWindows();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));
        var watchdog = TimeSpan.FromSeconds(5);
        await using ChildProcessFixture fixture = Start("exit 0", watchdog: watchdog, clock: clock);
        Assert.Equal(0, await fixture.WaitForExitAsync(TestContext.Current.CancellationToken));

        clock.Advance(watchdog);

        Assert.False(fixture.WatchdogExpired);
    }

    /// <summary>Disposal can be repeated.</summary>
    [Fact]
    public async Task DisposeIsIdempotent()
    {
        RequireWindows();
        ChildProcessFixture fixture = Start(Sleep);

        fixture.Dispose();
        fixture.Dispose();
        await fixture.DisposeAsync();

        Assert.True(fixture.HasExited);
    }

    /// <summary>Invalid arguments are rejected before a process starts.</summary>
    [Fact]
    public void StartInvalidArgumentsThrow()
    {
        _ = Assert.Throws<ArgumentException>(() => ChildProcessFixture.Start(" "));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ChildProcessFixture.Start("powershell.exe", maxOutputCharacters: 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ChildProcessFixture.Start("powershell.exe", watchdog: TimeSpan.Zero));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ChildProcessFixture.Start("powershell.exe", watchdog: Timeout.InfiniteTimeSpan));
    }

    /// <summary>An executable that does not exist fails at start and leaves nothing running.</summary>
    [Fact]
    public void StartMissingExecutableThrows()
    {
        using var workspace = TestWorkspace.Create();
        string missing = workspace.GetPath("missing.exe");

        _ = Assert.ThrowsAny<Win32Exception>(() => ChildProcessFixture.Start(missing));
    }
}
