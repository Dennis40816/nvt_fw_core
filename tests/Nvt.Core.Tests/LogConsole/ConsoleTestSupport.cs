// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

// Shared deterministic writer controls and immutable test content.
internal static class StoreRegressionSupport
{
    internal static object? Field(object value, string name) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(value);
    internal static int PendingCount(LogStore store) => ((IEnumerable)Field(store, "_pending")!).Cast<object>().Count();
    internal static long PendingCharge(LogStore store) => store.PendingUsage.Characters;
    internal static void AwaitCaptureOrFence(LogStore store, Task capture) => Assert.True(SpinWait.SpinUntil(() =>
    {
        lock (Field(store, "_gate")!)
            return capture.IsCompleted || Field(store, "_snapshotWaiters") is ICollection { Count: > 0 };
    }, TimeSpan.FromSeconds(10)));
    internal static Action Take(ConcurrentQueue<Action> callbacks)
    {
        Action? result = null;
        Assert.True(SpinWait.SpinUntil(() => callbacks.TryDequeue(out result), TimeSpan.FromSeconds(10)));
        return result!;
    }

    internal static Task<LogSnapshot> Latest(LogStore store, CancellationToken cancellationToken)
        => store.CaptureLatestAsync(cancellationToken).AsTask();
}

// Immutable reads; Interlocked owns the lifetime counter across writer and test threads.
internal sealed class TestContent(string text, int resident, Action? onDispose = null) : ILogTextContent
{
    private int _disposals;
    internal int Disposals => Volatile.Read(ref _disposals);
    internal Exception? Failure { get; private set; }
    internal int MaximumRead { get; private set; }
    internal Action? OnRead { get; set; }
    public int Length => text.Length;
    public int ResidentCharacterCount => resident;
    public long Version => 0;
    public void Read(int offset, Span<char> destination)
    {
        OnRead?.Invoke();
        MaximumRead = Math.Max(MaximumRead, destination.Length);
        text.AsSpan(offset, destination.Length).CopyTo(destination);
    }
    public void Dispose()
    {
        try { onDispose?.Invoke(); }
        catch (Exception exception) { Failure = exception; throw; }
        finally { Interlocked.Increment(ref _disposals); }
    }
}

// The test thread owns MaximumRead. Immutable physical chunks may split even one Unicode scalar.
internal sealed class SplitContent : ILogTextContent
{
    private readonly (int Start, string Text)[] _chunks;
    internal int MaximumRead { get; private set; }
    public int Length { get; }
    public int ResidentCharacterCount => 0;
    public long Version => 0;

    internal SplitContent(string text, params int[] splits)
    {
        Length = text.Length;
        var boundaries = splits.Prepend(0).Append(text.Length).Distinct().Order().ToArray();
        _chunks = boundaries.Zip(boundaries.Skip(1), (start, end) => (start, text[start..end])).ToArray();
    }

    public void Read(int offset, Span<char> destination)
    {
        Assert.InRange(destination.Length, 1, 1024);
        MaximumRead = Math.Max(MaximumRead, destination.Length);
        var copied = 0;
        foreach (var (start, chunk) in _chunks)
        {
            if (offset >= start + chunk.Length || offset < start) continue;
            var count = Math.Min(destination.Length - copied, start + chunk.Length - offset);
            chunk.AsSpan(offset - start, count).CopyTo(destination[copied..]);
            copied += count;
            offset += count;
            if (copied == destination.Length) return;
        }
        Assert.Fail("Segmented content did not fill the requested range.");
    }

    public void Dispose() { }
}

// One dedicated thread owns this context and executes posted continuations and writer turns.
internal sealed class SingleThreadPump : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;
    internal SingleThreadPump()
    {
        _thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
        }) { IsBackground = true };
        _thread.Start();
    }
    public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
    internal void Schedule(Action action) => Post(_ => action(), null);
    internal Task Run(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try { await action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        }, null);
        return completion.Task;
    }
    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join();
        _queue.Dispose();
    }
}
