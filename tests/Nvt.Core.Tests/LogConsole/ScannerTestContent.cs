// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;

namespace Nvt.Core.Tests.LogConsole;

// Immutable reads; Interlocked owns the lifetime counter across writer and test threads.
internal sealed class ScannerTestContent(string text, int resident, Action? onDispose = null) : ILogTextContent
{
    private int _disposals;
    internal int Disposals => Volatile.Read(ref _disposals);
    internal int MaximumRead { get; private set; }
    public int Length => text.Length;
    public int ResidentCharacterCount => resident;
    public long Version => 0;
    public void Read(int offset, Span<char> destination)
    {
        MaximumRead = Math.Max(MaximumRead, destination.Length);
        text.AsSpan(offset, destination.Length).CopyTo(destination);
    }
    public void Dispose() { onDispose?.Invoke(); Interlocked.Increment(ref _disposals); }
}
