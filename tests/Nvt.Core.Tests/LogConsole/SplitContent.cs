// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

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
