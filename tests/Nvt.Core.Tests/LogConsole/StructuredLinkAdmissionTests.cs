// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Structured link validation before ownership transfers.</summary>
public sealed class StructuredLinkAdmissionTests
{
    private static readonly int[] SortedStarts = [0, 3];
    /// <summary>Invalid structured links fail before single or batch ownership transfers.</summary>
    /// <param name="batch">Whether to admit a complete batch.</param>
    /// <param name="start">The supplied span start.</param>
    /// <param name="length">The supplied span length.</param>
    [Theory]
    [InlineData(false, 3, 2)]
    [InlineData(true, 3, 2)]
    [InlineData(false, -1, 1)]
    [InlineData(true, -1, 1)]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(false, int.MaxValue, int.MaxValue)]
    [InlineData(true, int.MaxValue, int.MaxValue)]
    public void InvalidLinkSpansFailAtAdmissionWithoutTransferringAnyContent(bool batch, int start, int length)
    {
        using var store = LogStoreTests.CreateStore();
        var valid = new TestContent("seed", 4);
        var invalid = new TestContent("text", 4);
        var write = new LogWrite(LogLevel.Info, "app", invalid, LinkSpans: [new ConsoleLinkSpan(start, length, new LinkTarget(LinkKind.File, "a.cs"))]);
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            if (batch) store.AddBatch(store.Generation, [new LogWrite(LogLevel.Info, "app", valid), write]);
            else store.Add(write);
        });
        Assert.Equal((0, 0L), store.PendingUsage);
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(1, store.Add(LogLevel.Info, "app", "next"));
        Assert.Equal(0, valid.Disposals);
        Assert.Equal(0, invalid.Disposals);
    }
    /// <summary>Missing span arrays, spans, or targets fail with a consistent argument exception.</summary>
    /// <param name="batch">Whether to validate a complete batch.</param>
    /// <param name="invalidKind">The absent structured value.</param>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void MissingLinkSpanValuesThrowArgumentExceptionBeforeOwnershipTransfer(bool batch, int invalidKind)
    {
        using var store = LogStoreTests.CreateStore();
        var valid = new TestContent("seed", 4);
        var invalid = new TestContent("text", 4);
        ImmutableArray<ConsoleLinkSpan> spans = invalidKind switch
        {
            0 => default,
            1 => [null!],
            _ => [new ConsoleLinkSpan(0, 1, null!)],
        };
        var write = new LogWrite(LogLevel.Info, "app", invalid, LinkSpans: spans);
        Assert.Throws<ArgumentException>(() =>
        {
            if (batch) store.AddBatch(store.Generation, [new LogWrite(LogLevel.Info, "app", valid), write]);
            else store.Add(write);
        });
        Assert.Equal((0, 0L), store.PendingUsage);
        Assert.Equal(0, valid.Disposals);
        Assert.Equal(0, invalid.Disposals);
        Assert.Equal(1, store.Add(LogLevel.Info, "app", "next"));
        valid.Dispose();
        invalid.Dispose();
    }

    /// <summary>Admission accepts nonoverlapping spans in the supplied order without allocating a sorted index.</summary>
    [Fact]
    public void UnsortedValidLinkSpansRetainTheirSuppliedOrder()
    {
        using var store = LogStoreTests.CreateStore();
        var target = new LinkTarget(LinkKind.File, "a.cs");
        ImmutableArray<ConsoleLinkSpan> spans = [new(3, 1, target), new(0, 1, target)];
        store.Add(new LogWrite(LogLevel.Info, "app", new InMemoryLogTextContent("text"), LinkSpans: spans));
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(spans, Assert.Single(snapshot.Entries).LinkSpans);
        var index = new ConsoleLinkCache().GetLinks(snapshot, snapshot.Entries[0]);
        Assert.Equal(SortedStarts, index.Spans.Select(span => span.Start));
    }
}
