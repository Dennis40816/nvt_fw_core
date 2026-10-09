// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;

namespace Nvt.Core.Tests.LogConsole;

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
