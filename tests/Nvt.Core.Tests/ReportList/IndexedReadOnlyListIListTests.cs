// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using Nvt.Core.ReportList;
using Xunit;

namespace Nvt.Core.Tests.ReportList;

/// <summary>Characterizes non-generic collection flags, mutation refusals and copying failures.</summary>
public sealed class IndexedReadOnlyListIListTests
{
    /// <summary>Every mutation fails with the original message before validating arguments.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EveryMutationIsRejectedWithTheOriginalMessage(int count)
    {
        var source = new MemoizedIndexedReadOnlyList<object>(1, _ => new object());
        var projection = new IndexedReadOnlyList<object>(source, count == 0 ? [] : [0]);
        IList view = projection;
        Action[] mutations =
        [
            () => view.Add(new object()),
            () => view.Add(null),
            view.Clear,
            () => view.Insert(0, new object()),
            () => view.Insert(-1, null),
            () => view.Insert(int.MaxValue, "unrelated"),
            () => view.Remove(new object()),
            () => view.Remove(null),
            () => view.RemoveAt(0),
            () => view.RemoveAt(-1),
            () => view.RemoveAt(int.MaxValue),
            () => view[0] = new object(),
            () => view[-1] = null,
            () => view[int.MaxValue] = "unrelated",
        ];

        foreach (Action mutation in mutations)
        {
            NotSupportedException error = Assert.Throws<NotSupportedException>(mutation);
            Assert.Equal("The indexed report projection is read-only.", error.Message);
        }

        Assert.True(view.IsFixedSize);
        Assert.True(view.IsReadOnly);
        Assert.False(view.IsSynchronized);
        Assert.Same(projection, view.SyncRoot);
        Assert.Equal(count, view.Count);
        Assert.Equal(0, source.MaterializedCount);
    }

    /// <summary>Copying preserves selected order, duplicates, offsets and object identity.</summary>
    [Fact]
    public void CopyToPreservesOrderDuplicatesAndDestinationOffset()
    {
        object[] source = [new(), new(), new()];
        ICollection view = new IndexedReadOnlyList<object>(source, [2, 0, 2]);
        object sentinel = new();
        object[] destination = [sentinel, sentinel, sentinel, sentinel, sentinel];

        view.CopyTo(destination, 1);

        Assert.Equal(new[] { sentinel, source[2], source[0], source[2], sentinel }, destination);
        Assert.Same(source[2], destination[1]);
        Assert.Same(destination[1], destination[3]);
    }

    /// <summary>Even an empty view checks a null destination.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CopyToRejectsNullDestination(int count)
    {
        ICollection view = new IndexedReadOnlyList<object>([new object()], count == 0 ? [] : [0]);

        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => view.CopyTo(null!, -1));

        Assert.Equal("array", error.ParamName);
    }

    /// <summary>An empty copy performs no index, rank or element-type validation.</summary>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void EmptyCopyAcceptsAnyIndexAndArrayShape(int index)
    {
        ICollection view = new IndexedReadOnlyList<object>([], []);

        view.CopyTo(Array.Empty<object>(), index);
        view.CopyTo(new int[1, 1], index);
    }

    /// <summary>A nonempty copy materializes its first row before an invalid destination index fails.</summary>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void InvalidCopyIndexUsesArrayExceptionsAfterMaterialization(int index)
    {
        var source = new MemoizedIndexedReadOnlyList<object>(1, _ => new object());
        ICollection view = new IndexedReadOnlyList<object>(source, [0]);

        Assert.Throws<IndexOutOfRangeException>(() => view.CopyTo(new object[1], index));

        Assert.Equal(1, source.MaterializedCount);
    }

    /// <summary>An exact-capacity copy succeeds while capacity plus one retains the written prefix.</summary>
    [Fact]
    public void CopyCapacityFailureRetainsTheWrittenPrefix()
    {
        object[] expected = [new(), new(), new()];
        var source = new MemoizedIndexedReadOnlyList<object>(3, index => expected[index]);
        ICollection exact = new IndexedReadOnlyList<object>(source, [0, 1]);
        var destination = new object[2];
        exact.CopyTo(destination, 0);
        Assert.Equal(new[] { expected[0], expected[1] }, destination);

        ICollection tooMany = new IndexedReadOnlyList<object>(source, [2, 0, 1]);
        Assert.Throws<IndexOutOfRangeException>(() => tooMany.CopyTo(destination, 0));

        Assert.Equal(new[] { expected[2], expected[0] }, destination);
        Assert.Equal(3, source.MaterializedCount);
    }

    /// <summary>Rank and type failures propagate from Array.SetValue.</summary>
    [Fact]
    public void CopyToRetainsArrayRankAndTypeExceptions()
    {
        ICollection view = new IndexedReadOnlyList<object>([new object()], [0]);

        Assert.Throws<ArgumentException>(() => view.CopyTo(new object[1, 1], 0));
        Assert.Throws<InvalidCastException>(() => view.CopyTo(new string[1], 0));
    }

    /// <summary>Copying uses absolute array indices and accepts a nonzero lower bound.</summary>
    [Fact]
    public void CopyToUsesTheArrayLowerBoundWithoutNormalizingIndices()
    {
        object expected = new();
        ICollection view = new IndexedReadOnlyList<object>([expected], [0]);
        Array destination = Array.CreateInstance(typeof(object), [1], [4]);

        view.CopyTo(destination, 4);

        Assert.Same(expected, destination.GetValue(4));
        Assert.Throws<IndexOutOfRangeException>(() => view.CopyTo(destination, 0));
    }
}
