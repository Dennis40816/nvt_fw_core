// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Structured link validation before ownership transfers.</summary>
public sealed class StructuredLinkAdmissionTests
{
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
        var valid = new ScannerTestContent("seed", 4);
        var invalid = new ScannerTestContent("text", 4);
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
}
