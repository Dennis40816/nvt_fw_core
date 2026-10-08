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

    // Tests control reads; Interlocked protects disposal across writer and test threads.
    internal sealed class Content(string text, int resident, Action? onDispose = null) : ILogTextContent
    {
        private int _disposals;
        internal int Disposals => Volatile.Read(ref _disposals);
        internal Exception? Failure { get; private set; }
        internal int MaximumRead { get; private set; }
        public int Length => text.Length;
        public int ResidentCharacterCount => resident;
        public long Version => 0;
        public void Read(int offset, Span<char> destination)
        {
            MaximumRead = Math.Max(MaximumRead, destination.Length);
            text.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposals);
            try { onDispose?.Invoke(); }
            catch (Exception exception) { Failure = exception; }
        }
    }
}
