// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Bounded first-line metadata derived directly from leased, segmented content.</summary>
public sealed class FirstLineTests
{
    /// <summary>All original newline forms end the preview, and empty or single-line content stays exact.</summary>
    /// <param name="text">The full content.</param>
    /// <param name="expected">The first line.</param>
    /// <param name="more">Whether any original content is omitted.</param>
    [Theory]
    [InlineData("", "", false)]
    [InlineData("first", "first", false)]
    [InlineData("first\rsecond", "first", true)]
    [InlineData("first\nsecond", "first", true)]
    [InlineData("first\r\nsecond", "first", true)]
    [InlineData("first\r\n", "first", true)]
    [InlineData("\nsecond", "", true)]
    public void FirstLineHandlesEmptyContentAndOriginalLineBreaks(string text, string expected, bool more)
    {
        using var content = new InMemoryLogTextContent(text);
        Assert.Equal(new ConsoleFirstLine(expected, more), ConsoleFirstLine.Read(content));
    }

    /// <summary>CR, LF, and CRLF work on either side of a segmented read boundary.</summary>
    /// <param name="length">The first-line length.</param>
    /// <param name="lineBreak">The unchanged newline sequence.</param>
    [Theory]
    [InlineData(1023, "\r")]
    [InlineData(1024, "\r")]
    [InlineData(1023, "\n")]
    [InlineData(1024, "\n")]
    [InlineData(1023, "\r\n")]
    [InlineData(1024, "\r\n")]
    public void FirstLineBreakCanCrossChunkBoundary(int length, string lineBreak)
    {
        var first = new string('x', length);
        using var content = new TestContent(first + lineBreak + "second", 64);
        Assert.Equal(new ConsoleFirstLine(first, true), ConsoleFirstLine.Read(content, 2048));
        Assert.InRange(content.MaximumRead, 1, 1024);
    }

    /// <summary>A very long first line reads only the requested cap plus one character.</summary>
    [Fact]
    public void LongFirstLineUsesBoundedReadsAndOutput()
    {
        using var content = new GeneratedContent(1_000_000);
        var preview = ConsoleFirstLine.Read(content, 16);
        Assert.Equal(new string('x', 16), preview.Text);
        Assert.True(preview.HasMoreContent);
        Assert.Equal(17, content.CharactersRead);
        Assert.InRange(content.MaximumRead, 1, 1024);
    }

    /// <summary>The UTF-16 cap never splits a supplementary character.</summary>
    /// <param name="text">The full content with a surrogate pair.</param>
    /// <param name="cap">The UTF-16 cap.</param>
    /// <param name="expected">The scalar-safe preview.</param>
    /// <param name="more">Whether content remains.</param>
    [Theory]
    [InlineData("A😀B", 0, "", true)]
    [InlineData("A😀B", 1, "A", true)]
    [InlineData("A😀B", 2, "A", true)]
    [InlineData("A😀B", 3, "A😀", true)]
    [InlineData("A😀B", 4, "A😀B", false)]
    [InlineData("😀", 1, "", true)]
    [InlineData("😀", 2, "😀", false)]
    public void FirstLineCapPreservesSupplementaryCharacters(string text, int cap, string expected, bool more)
    {
        using var content = new InMemoryLogTextContent(text);
        Assert.Equal(new ConsoleFirstLine(expected, more), ConsoleFirstLine.Read(content, cap));
    }

    /// <summary>The hard output cap cannot be disabled by an app request.</summary>
    /// <param name="cap">An invalid output cap.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(4097)]
    public void FirstLineRejectsCapsOutsideHardBound(int cap)
    {
        using var content = new InMemoryLogTextContent("text");
        Assert.Throws<ArgumentOutOfRangeException>(() => ConsoleFirstLine.Read(content, cap));
    }

    /// <summary>A row computes its preview on demand from the same lease even after ring eviction.</summary>
    [Fact]
    public void RowFirstLineUsesLeasedContentOnDemandAfterEviction()
    {
        using var store = LogStoreTests.CreateStore(1);
        var content = new LogStoreTests.TrackingContent("first\r\nsecond", 13);
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        using var snapshot = LogStoreTests.Capture(store);
        using var projection = ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState());
        var row = Assert.Single(projection.Rows);
        snapshot.Dispose();
        store.Add(LogLevel.Info, "app", "replacement");
        using var latest = LogStoreTests.Capture(store);
        var reads = content.Reads;
        Assert.Equal(new ConsoleFirstLine("first", true), row.GetFirstLine());
        Assert.True(content.Reads > reads);
        Assert.Equal(0, content.Disposals);
    }

    /// <summary>The maximum permitted preview reads only 4,097 characters from a large message.</summary>
    [Fact]
    public void MaximumFirstLineCapKeepsInputAndOutputBounded()
    {
        using var content = new GeneratedContent(1_000_000);
        var preview = ConsoleFirstLine.Read(content, 4096);
        Assert.Equal(4096, preview.Text.Length);
        Assert.True(preview.HasMoreContent);
        Assert.Equal(4097, content.CharactersRead);
        Assert.Equal(1024, content.MaximumRead);
    }

    /// <summary>A surrogate pair split between read chunks is still kept whole at the output cap.</summary>
    [Fact]
    public void SupplementaryCharacterAtChunkBoundaryIsNotSplitByCap()
    {
        var first = new string('x', 1023);
        using var content = new TestContent(first + "\U0001F600tail", 64);
        Assert.Equal(new ConsoleFirstLine(first, true), ConsoleFirstLine.Read(content, 1024));
        Assert.Equal(1024, content.MaximumRead);
    }

    // This synchronous test owns read diagnostics; content itself is generated without a full string.
    private sealed class GeneratedContent(int length) : ILogTextContent
    {
        internal int CharactersRead { get; private set; }
        internal int MaximumRead { get; private set; }
        public int Length => length;
        public int ResidentCharacterCount => 0;
        public long Version => 0;
        public void Read(int offset, Span<char> destination)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(destination.Length, 1024);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + destination.Length, length);
            CharactersRead += destination.Length;
            MaximumRead = Math.Max(MaximumRead, destination.Length);
            destination.Fill('x');
        }
        public void Dispose() { }
    }
}
