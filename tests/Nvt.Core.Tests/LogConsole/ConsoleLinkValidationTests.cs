// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Structured link validation bounds producer comparisons and preserves overlap rejection.</summary>
public sealed class ConsoleLinkValidationTests(ITestOutputHelper output)
{
    /// <summary>Ordered spans use linear comparisons; reverse order uses bounded sorting and adjacent checks.</summary>
    /// <param name="reverse">Whether spans arrive in reverse order.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeSpanArraysValidateWithBoundedComparisons(bool reverse)
    {
        const int count = 10_000;
        var ordered = Enumerable.Range(0, count).Select(index => new ConsoleLinkSpan(index * 2, 1,
            new LinkTarget(LinkKind.File, "a.txt"))).ToImmutableArray();
        var supplied = reverse ? ordered.Reverse().ToImmutableArray() : ordered;
        var comparisons = ConsoleLinkIndex.Validate(supplied, count * 2);
        var linear = 2 * (count - 1);
        if (reverse) Assert.InRange(comparisons, linear + 1, linear + 4 * count * (int)Math.Ceiling(Math.Log2(count)));
        else Assert.Equal(linear, comparisons);
        output.WriteLine($"reverse={reverse}: {comparisons} order, sort and overlap comparisons.");
        Assert.Equal(ordered, new ConsoleLinkIndex(supplied, count * 2).Spans);
    }

    /// <summary>Neighbour checks reject equal starts, containment and crossing overlaps in either order.</summary>
    /// <param name="reverse">Whether spans arrive in reverse order.</param>
    /// <param name="overlapLength">The length of the interval that overlaps its successor.</param>
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void LargeSpanArraysRejectOverlapsInEitherOrder(bool reverse, int overlapLength)
    {
        var spans = Enumerable.Range(0, 10_000).Select(index => new ConsoleLinkSpan(index * 2, 1,
            new LinkTarget(LinkKind.File, "a.txt"))).ToImmutableArray();
        spans = spans.SetItem(5000, spans[5000] with { Length = overlapLength });
        if (reverse) spans = spans.Reverse().ToImmutableArray();
        Assert.Throws<ArgumentException>(() => ConsoleLinkIndex.Validate(spans, 20_000));
        Assert.Throws<ArgumentException>(() => new ConsoleLinkIndex(spans, 20_000));
    }

    /// <summary>Default arrays, nulls and invalid intervals retain the same argument exception type.</summary>
    [Fact]
    public void InvalidSpanMetadataRetainsArgumentException()
    {
        var target = new LinkTarget(LinkKind.File, "a.txt");
        ImmutableArray<ConsoleLinkSpan>[] invalid =
        [
            default, [null!], [new ConsoleLinkSpan(0, 1, null!)],
            [new ConsoleLinkSpan(-1, 1, target)], [new ConsoleLinkSpan(0, 0, target)],
            [new ConsoleLinkSpan(0, -1, target)], [new ConsoleLinkSpan(2, 2, target)],
            [new ConsoleLinkSpan(int.MaxValue, int.MaxValue, target)],
            [new ConsoleLinkSpan(0, 2, target), new ConsoleLinkSpan(0, 1, target)],
        ];
        foreach (var spans in invalid)
        {
            Assert.Throws<ArgumentException>(() => ConsoleLinkIndex.Validate(spans, 3));
            Assert.Throws<ArgumentException>(() => new ConsoleLinkIndex(spans, 3));
        }
    }
}
