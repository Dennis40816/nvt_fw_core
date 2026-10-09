// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using Nvt.Core.Threading;
using Xunit;

namespace Nvt.Core.Tests.Threading;

/// <summary>Verifies event failure observation, cancellation, and calling-context behavior.</summary>
public sealed class UiEventRunnerTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    /// <summary>Successful operations report nothing and allow an empty identifier.</summary>
    /// <param name="operation">The identifier passed to the runner.</param>
    [Theory]
    [InlineData("Save")]
    [InlineData("")]
    public async Task SuccessReportsNothing(string operation)
    {
        var reports = new List<(string, Exception)>();
        var fallbacks = new List<(string, Exception)>();
        var runner = new UiEventRunner((name, error) => reports.Add((name, error)),
            (name, error) => fallbacks.Add((name, error)));
        var calls = 0;

        await runner.RunAsync(operation, _ =>
        {
            calls++;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        Assert.Empty(reports);
        Assert.Empty(fallbacks);
    }

    /// <summary>Synchronous throws and faulted tasks preserve the original failure and identifier.</summary>
    /// <param name="synchronous">Whether the delegate throws before returning a task.</param>
    /// <param name="operation">The identifier passed to the runner; an empty name is allowed.</param>
    [Theory]
    [InlineData(false, "Save")]
    [InlineData(true, "Save")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public async Task OperationFailureIsReportedOnce(bool synchronous, string operation)
    {
        var expected = new InvalidOperationException("save failed");
        var reports = new List<(string, Exception)>();
        var fallbacks = new List<Exception>();
        var runner = new UiEventRunner((name, error) => reports.Add((name, error)),
            (_, error) => fallbacks.Add(error));

        await runner.RunAsync(operation, _ => synchronous ? throw expected : Task.FromException(expected),
            TestContext.Current.CancellationToken);

        var report = Assert.Single(reports);
        Assert.Equal(operation, report.Item1);
        Assert.Same(expected, report.Item2);
        Assert.Empty(fallbacks);
    }

    /// <summary>Cancellation carrying the cancelled operation token is silent for either failure form.</summary>
    /// <param name="synchronous">Whether the delegate throws before returning a task.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuppliedTokenCancellationIsSilent(bool synchronous)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reports = new List<Exception>();
        var runner = new UiEventRunner((_, error) => reports.Add(error), (_, error) => reports.Add(error));
        CancellationToken received = default;

        await runner.RunAsync("Cancel", token =>
        {
            received = token;
            return synchronous ? throw new OperationCanceledException(token) : Task.FromCanceled(token);
        }, cancellation.Token);

        Assert.Equal(cancellation.Token, received);
        Assert.Empty(reports);
    }

    /// <summary>An exception carrying a matching but uncancelled token is still an error.</summary>
    /// <param name="synchronous">Whether the delegate throws before returning a task.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncancelledSuppliedTokenIsReported(bool synchronous)
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new OperationCanceledException(cancellation.Token);
        var reports = new List<Exception>();
        var runner = new UiEventRunner((_, error) => reports.Add(error), (_, _) => { });

        await runner.RunAsync("Cancel", _ => synchronous ? throw expected : Task.FromException(expected), cancellation.Token);

        Assert.Same(expected, Assert.Single(reports));
    }

    /// <summary>Cancellation with another token is reported regardless of the operation token's state.</summary>
    /// <param name="cancelOperation">Whether the supplied operation token is also cancelled.</param>
    /// <param name="synchronous">Whether the delegate throws before returning a task.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DifferentTokenCancellationIsReported(bool cancelOperation, bool synchronous)
    {
        using var operationCancellation = new CancellationTokenSource();
        using var otherCancellation = new CancellationTokenSource();
        otherCancellation.Cancel();
        if (cancelOperation) operationCancellation.Cancel();
        var expected = new OperationCanceledException(otherCancellation.Token);
        var reports = new List<(string, Exception)>();
        var runner = new UiEventRunner((name, error) => reports.Add((name, error)), (_, _) => { });

        await runner.RunAsync("Cancel", _ => synchronous ? throw expected : Task.FromException(expected),
            operationCancellation.Token);

        var report = Assert.Single(reports);
        Assert.Equal("Cancel", report.Item1);
        Assert.Same(expected, report.Item2);
    }

    /// <summary>A failing primary reporter invokes the fallback once with the original operation failure.</summary>
    [Fact]
    public async Task PrimaryReporterFailureUsesFallback()
    {
        var expected = new InvalidOperationException("copy failed");
        var reports = new List<(string, Exception)>();
        var fallbacks = new List<(string, Exception)>();
        var runner = new UiEventRunner((name, error) =>
        {
            reports.Add((name, error));
            throw new InvalidOperationException("report failed");
        }, (name, error) => fallbacks.Add((name, error)));

        await runner.RunAsync("Copy", _ => throw expected, TestContext.Current.CancellationToken);

        Assert.Equal(("Copy", expected), Assert.Single(reports));
        Assert.Equal(("Copy", expected), Assert.Single(fallbacks));
    }

    /// <summary>Neither reporter can fault the returned observation task.</summary>
    [Fact]
    public async Task BothReporterFailuresAreSwallowed()
    {
        var calls = new List<string>();
        var runner = new UiEventRunner((_, _) =>
        {
            calls.Add("primary");
            throw new InvalidOperationException("primary failed");
        }, (_, _) =>
        {
            calls.Add("fallback");
            throw new InvalidOperationException("fallback failed");
        });

        var observation = runner.RunAsync("Copy", _ => Task.FromException(new InvalidOperationException("copy failed")),
            TestContext.Current.CancellationToken);
        await observation;

        Assert.True(observation.IsCompletedSuccessfully);
        Assert.Equal(["primary", "fallback"], calls);
    }

    /// <summary>Run starts on its caller, returns while work is pending, and contains dispatcher failures.</summary>
    /// <param name="reportersThrow">Whether both reporters also throw.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunObservesPendingFailureOnCallingContext(bool reportersThrow)
    {
        using var context = new RecordingContext();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("pending failure");
        var reports = new List<(string, Exception)>();
        var fallbacks = new List<(string, Exception)>();
        var runner = new UiEventRunner((name, error) =>
        {
            Assert.Same(context, SynchronizationContext.Current);
            reports.Add((name, error));
            if (reportersThrow) throw new InvalidOperationException("primary failed");
        }, (name, error) =>
        {
            fallbacks.Add((name, error));
            throw new InvalidOperationException("fallback failed");
        });
        var callingThread = Environment.CurrentManagedThreadId;
        var started = false;

        context.Invoke(() => runner.Run("Pending", _ =>
        {
            Assert.Equal(callingThread, Environment.CurrentManagedThreadId);
            Assert.Same(context, SynchronizationContext.Current);
            started = true;
            return pending.Task;
        }, TestContext.Current.CancellationToken));

        Assert.True(started);
        Assert.False(pending.Task.IsCompleted);
        Assert.Empty(reports);
        pending.SetException(expected);
        await context.RunNextAsync();

        Assert.Equal(("Pending", expected), Assert.Single(reports));
        if (reportersThrow) Assert.Equal(("Pending", expected), Assert.Single(fallbacks));
        else Assert.Empty(fallbacks);
        Assert.Empty(context.PostExceptions);
        Assert.Equal(1, context.PostCount);
    }

    /// <summary>Run contains synchronous operation and reporter failures before returning.</summary>
    [Fact]
    public void RunContainsSynchronousAndReporterFailures()
    {
        var calls = new List<string>();
        var runner = new UiEventRunner((_, _) =>
        {
            calls.Add("primary");
            throw new InvalidOperationException("primary failed");
        }, (_, _) =>
        {
            calls.Add("fallback");
            throw new InvalidOperationException("fallback failed");
        });

        runner.Run("Copy", _ => throw new InvalidOperationException("copy failed"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["primary", "fallback"], calls);
    }

    /// <summary>RunAsync invokes the delegate immediately on the calling thread and context.</summary>
    [Fact]
    public async Task RunAsyncStartsOnCallingContext()
    {
        using var context = new RecordingContext();
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports = new List<Exception>();
        var runner = new UiEventRunner((_, error) => reports.Add(error), (_, error) => reports.Add(error));
        var callingThread = Environment.CurrentManagedThreadId;
        var started = false;
        Task? observation = null;

        context.Invoke(() => observation = runner.RunAsync("Pending", token =>
        {
            Assert.Equal(callingThread, Environment.CurrentManagedThreadId);
            Assert.Same(context, SynchronizationContext.Current);
            Assert.Equal(cancellation.Token, token);
            started = true;
            return pending.Task;
        }, cancellation.Token));

        Assert.True(started);
        Assert.NotNull(observation);
        Assert.False(observation.IsCompleted);
        pending.SetResult();
        await context.RunNextAsync();
        await observation.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        Assert.Empty(reports);
        Assert.Empty(context.PostExceptions);
    }

    /// <summary>Both constructor reporters are required.</summary>
    /// <param name="fallback">Whether to omit the fallback rather than the primary reporter.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorRejectsNullReporters(bool fallback)
    {
        Action<string, Exception> reporter = (_, _) => { };
        var error = Assert.Throws<ArgumentNullException>(() =>
            new UiEventRunner(fallback ? reporter : null!, fallback ? null! : reporter));

        Assert.Equal(fallback ? "fallbackReport" : "report", error.ParamName);
    }

    /// <summary>Both entry points reject null operation names and actions synchronously.</summary>
    /// <param name="parameter">The parameter to omit.</param>
    /// <param name="useAsync">Whether to call RunAsync rather than Run.</param>
    [Theory]
    [InlineData("operation", false)]
    [InlineData("operation", true)]
    [InlineData("action", false)]
    [InlineData("action", true)]
    public void EntryPointsRejectNullArguments(string parameter, bool useAsync)
    {
        var calls = 0;
        var runner = new UiEventRunner((_, _) => calls++, (_, _) => calls++);
        string operation = parameter == "operation" ? null! : "Valid";
        Func<CancellationToken, Task> action = parameter == "action" ? null! : _ =>
        {
            calls++;
            return Task.CompletedTask;
        };

        var error = Assert.Throws<ArgumentNullException>(() =>
        {
            if (useAsync) _ = runner.RunAsync(operation, action, TestContext.Current.CancellationToken);
            else runner.Run(operation, action, TestContext.Current.CancellationToken);
        });

        Assert.Equal(parameter, error.ParamName);
        Assert.Equal(0, calls);
    }

    private sealed class RecordingContext : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        private readonly SemaphoreSlim posted = new(0);
        private int postCount;

        internal List<Exception> PostExceptions { get; } = [];

        internal int PostCount => Volatile.Read(ref postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            callbacks.Enqueue((d, state));
            Interlocked.Increment(ref postCount);
            posted.Release();
        }

        internal void Invoke(Action action)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { action(); }
            finally { SetSynchronizationContext(previous); }
        }

        internal async Task RunNextAsync()
        {
            Assert.True(await posted.WaitAsync(WaitLimit, TestContext.Current.CancellationToken));
            Assert.True(callbacks.TryDequeue(out var callback));
            Invoke(() =>
            {
                try { callback.Callback(callback.State); }
                catch (Exception error) { PostExceptions.Add(error); }
            });
        }

        public void Dispose() => posted.Dispose();
    }
}
