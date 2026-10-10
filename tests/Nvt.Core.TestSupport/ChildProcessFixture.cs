// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Nvt.Core.TestSupport;

/// <summary>Runs a child process for a test: it caps the captured output, has a watchdog, and ends the process tree that is still reachable from the root on disposal.</summary>
/// <remarks>
/// A child that stays alive holds files open and makes the next test fail at random. The fixture prevents that.
/// The watchdog only prevents a hang; it is never an assertion. Standard output and standard error are captured
/// together, in arrival order, up to a character limit. Standard input is closed at once. A descendant that outlives
/// its parent is not reachable. After disposal, <see cref="WaitForExitAsync"/> throws <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class ChildProcessFixture : IDisposable, IAsyncDisposable
{
    /// <summary>The default time after which the fixture ends the process tree.</summary>
    public static readonly TimeSpan DefaultWatchdog = TimeSpan.FromSeconds(60);

    /// <summary>The default limit of captured output characters.</summary>
    public const int DefaultMaxOutputCharacters = 64 * 1024;

    private static readonly TimeSpan DisposeExitWait = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly StringBuilder _output = new();
    private readonly List<OutputWaiter> _outputWaiters = [];
    private readonly Process _process;
    private readonly TimeSpan _watchdog;
    private readonly TimeProvider _clock;
    private readonly int _maxOutputCharacters;
    private ITimer? _watchdogTimer;
    private int _processId;
    private bool _exitedAtDispose;
    private bool _truncated;
    private bool _watchdogExpired;
    private bool _outputEnded;
    private bool _disposed;

    private ChildProcessFixture(Process process, TimeSpan watchdog, TimeProvider clock, int maxOutputCharacters)
    {
        _process = process;
        _watchdog = watchdog;
        _clock = clock;
        _maxOutputCharacters = maxOutputCharacters;
    }

    /// <summary>Starts a child process without a shell and begins capturing its output.</summary>
    /// <param name="fileName">The executable.</param>
    /// <param name="arguments">The arguments, passed without any quoting rules of a shell.</param>
    /// <param name="workingDirectory">The working directory, or <see langword="null"/> for the current one.</param>
    /// <param name="watchdog">A finite, positive limit. <see langword="null"/> means <see cref="DefaultWatchdog"/>.</param>
    /// <param name="maxOutputCharacters">The capture limit, at least 1.</param>
    /// <param name="clock">The watchdog clock. <see langword="null"/> means <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="Win32Exception">The executable cannot be started.</exception>
    public static ChildProcessFixture Start(
        string fileName,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        TimeSpan? watchdog = null,
        int maxOutputCharacters = DefaultMaxOutputCharacters,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOutputCharacters, 1);
        TimeSpan limit = watchdog ?? DefaultWatchdog;
        // Timeout.InfiniteTimeSpan is negative, so this also rejects "no limit".
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero, nameof(watchdog));

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }
        foreach (string argument in arguments ?? [])
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var fixture = new ChildProcessFixture(process, limit, clock ?? TimeProvider.System, maxOutputCharacters);
        process.OutputDataReceived += (_, e) => fixture.Append(e.Data);
        process.ErrorDataReceived += (_, e) => fixture.Append(e.Data);
        process.Exited += (_, _) => _ = Task.Run(fixture.OnExited);
        try
        {
            _ = process.Start();
            fixture._processId = process.Id;
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            fixture._watchdogTimer = fixture._clock.CreateTimer(
                static state => ((ChildProcessFixture)state!).OnWatchdog(), fixture, limit, Timeout.InfiniteTimeSpan);
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
        return fixture;
    }

    /// <summary>Gets the process id. It stays available after disposal.</summary>
    public int ProcessId => _processId;

    /// <summary>Gets whether the root process has exited.</summary>
    public bool HasExited
    {
        get
        {
            lock (_gate)
            {
                return _disposed ? _exitedAtDispose : _process.HasExited;
            }
        }
    }

    /// <summary>Gets the captured output so far: standard output and standard error in arrival order.</summary>
    public string Output
    {
        get
        {
            lock (_gate)
            {
                return _output.ToString();
            }
        }
    }

    /// <summary>Gets whether the output limit cut off some output.</summary>
    public bool OutputTruncated
    {
        get
        {
            lock (_gate)
            {
                return _truncated;
            }
        }
    }

    /// <summary>Gets whether the watchdog ended the process tree.</summary>
    public bool WatchdogExpired
    {
        get
        {
            lock (_gate)
            {
                return _watchdogExpired;
            }
        }
    }

    /// <summary>Waits for the root process to exit and for its output to end, and returns the exit code.</summary>
    /// <exception cref="TimeoutException">The watchdog ended the process tree before the process exited by itself.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (WatchdogExpired)
        {
            throw new TimeoutException($"Watchdog: the child process did not exit within {_watchdog}; the fixture ended its process tree.");
        }
        return _process.ExitCode;
    }

    /// <summary>Waits until the captured output contains the text.</summary>
    /// <exception cref="InvalidOperationException">The output ended without the text.</exception>
    /// <exception cref="TimeoutException">The watchdog expired. The message names the text.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public Task WaitForOutputAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        OutputWaiter waiter;
        lock (_gate)
        {
            if (_output.ToString().Contains(text, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }
            if (_outputEnded || _disposed)
            {
                return Task.FromException(new InvalidOperationException($"The output ended without '{text}'."));
            }
            waiter = new OutputWaiter(text);
            _outputWaiters.Add(waiter);
        }
        return WaitForWaiterAsync(waiter, cancellationToken);
    }

    private async Task WaitForWaiterAsync(OutputWaiter waiter, CancellationToken cancellationToken)
    {
        try
        {
            await SignalWait.WaitAsync(waiter.Source.Task, $"child output containing '{waiter.Text}'", _watchdog, _clock, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _ = _outputWaiters.Remove(waiter);
            }
        }
    }

    /// <summary>Ends the root process and every descendant that is still reachable from it.</summary>
    public void KillTree()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            KillTreeCore();
        }
    }

    /// <summary>Ends the process tree that is still reachable from the root, waits a bounded time for the root to exit, and releases the process handle.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _watchdogTimer?.Dispose();
            KillTreeCore();
        }

        try
        {
            _exitedAtDispose = _process.WaitForExit(DisposeExitWait);
        }
        catch (InvalidOperationException)
        {
            // The process never started.
        }
        finally
        {
            _process.Dispose();
            FailWaiters();
        }
    }

    /// <summary>Performs the same bounded cleanup as <see cref="Dispose"/>.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    // Guard: callers hold _gate.
    private void KillTreeCore()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited or was never started.
        }
        catch (Win32Exception)
        {
            // The process is already ending.
        }
        catch (AggregateException)
        {
            // Some processes of the tree could not be ended. Disposal still releases the handle.
        }
    }

    private void OnWatchdog()
    {
        lock (_gate)
        {
            if (_disposed || _process.HasExited)
            {
                return;
            }
            _watchdogExpired = true;
            KillTreeCore();
        }
    }

    private void OnExited()
    {
        try
        {
            // Wait for the end of the redirected streams, so late output is not lost.
            _process.WaitForExit();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            // Disposed while exiting.
        }
        lock (_gate)
        {
            _outputEnded = true;
        }
        FailWaiters();
    }

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }
        List<OutputWaiter>? completed = null;
        lock (_gate)
        {
            int room = _maxOutputCharacters - _output.Length;
            if (room > 0)
            {
                string text = line + "\n";
                if (text.Length > room)
                {
                    text = text[..room];
                    _truncated = true;
                }
                _ = _output.Append(text);
            }
            else
            {
                _truncated = true;
            }
            if (_outputWaiters.Count > 0)
            {
                string captured = _output.ToString();
                completed = _outputWaiters.FindAll(waiter => captured.Contains(waiter.Text, StringComparison.Ordinal));
                _ = _outputWaiters.RemoveAll(waiter => completed.Contains(waiter));
            }
        }
        // Completion runs continuations asynchronously, so it is safe outside the lock.
        completed?.ForEach(waiter => waiter.Source.TrySetResult());
    }

    private void FailWaiters()
    {
        List<OutputWaiter> remaining;
        lock (_gate)
        {
            remaining = [.. _outputWaiters];
            _outputWaiters.Clear();
        }
        foreach (OutputWaiter waiter in remaining)
        {
            _ = waiter.Source.TrySetException(new InvalidOperationException($"The output ended without '{waiter.Text}'."));
        }
    }

    private sealed class OutputWaiter(string text)
    {
        internal string Text { get; } = text;

        internal TaskCompletionSource Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
