// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using Nvt.Core.ReportList;
using Xunit;

namespace Nvt.Core.Tests.ReportList;

/// <summary>Characterizes ordered views and their source-dependent lookup semantics.</summary>
public sealed class IndexedReadOnlyListTests
{
    private static readonly int[] LastIndexOnly = [2];
    private static readonly int[] LastThenFirstIndices = [2, 0];

    /// <summary>The source is validated before the selected indices.</summary>
    [Fact]
    public void NullArgumentsRetainValidationOrder()
    {
        ArgumentNullException sourceError = Assert.Throws<ArgumentNullException>(
            () => new IndexedReadOnlyList<object>(null!, null!));
        ArgumentNullException indicesError = Assert.Throws<ArgumentNullException>(
            () => new IndexedReadOnlyList<object>([], null!));

        Assert.Equal("source", sourceError.ParamName);
        Assert.Equal("indices", indicesError.ParamName);
    }

    /// <summary>Selected indices are checked without creating source rows.</summary>
    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 0)]
    [InlineData(1, int.MinValue)]
    [InlineData(1, -1)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, int.MaxValue)]
    [InlineData(64, 64)]
    [InlineData(64, 65)]
    public void InvalidSourceIndexIsRejectedWithoutMaterialization(int count, int index)
    {
        var source = new MemoizedIndexedReadOnlyList<object>(count, _ => new object());

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new IndexedReadOnlyList<object>(source, [index]));

        Assert.Equal("indices", error.ParamName);
        Assert.Null(error.ActualValue);
        Assert.Equal(0, source.MaterializedCount);
    }

    /// <summary>Empty and single views retain array index exceptions at their boundaries.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EmptyAndSingleViewBoundariesUseArrayExceptions(int count)
    {
        object expected = new();
        var source = new MemoizedIndexedReadOnlyList<object>(1, _ => expected);
        var view = new IndexedReadOnlyList<object>(source, count == 0 ? [] : [0]);
        IList untyped = view;

        Assert.Equal(count, view.Count);
        foreach (int invalidIndex in new[] { int.MinValue, -1, count, count + 1, int.MaxValue })
        {
            Assert.Throws<IndexOutOfRangeException>(() => view[invalidIndex]);
            Assert.Throws<IndexOutOfRangeException>(() => untyped[invalidIndex]);
        }

        Assert.Equal(0, source.MaterializedCount);
        if (count == 0)
        {
            Assert.Empty(view);
            Assert.Equal(-1, untyped.IndexOf(expected));
            Assert.False(untyped.Contains(null));
            return;
        }

        Assert.Same(expected, Assert.Single(view));
        Assert.Same(expected, untyped[0]);
        Assert.Equal(1, source.MaterializedCount);
    }

    /// <summary>Indices are copied while source rows remain shared and observable.</summary>
    [Fact]
    public void ViewCopiesIndicesAndPreservesOrderDuplicatesAndSourceIdentity()
    {
        object[] source = [new(), new(), new()];
        int[] indices = [2, 0, 2, 1];
        var view = new IndexedReadOnlyList<object>(source, indices);
        indices[0] = 1;

        Assert.Equal(4, view.Count);
        Assert.Same(source[2], view[0]);
        Assert.Same(view[0], view[2]);
        Assert.Equal(new[] { source[2], source[0], source[2], source[1] }, view);
        Assert.Equal(view, ((IEnumerable)view).Cast<object>());
        object replacement = new();
        source[2] = replacement;
        Assert.Same(replacement, view[0]);
        Assert.Same(replacement, view[2]);
    }

    /// <summary>Enumeration advances through selected indices without creating unselected rows.</summary>
    [Fact]
    public void ViewEnumerationDefersMaterialization()
    {
        var calls = new List<int>();
        var source = new MemoizedIndexedReadOnlyList<object>(3, index =>
        {
            calls.Add(index);
            return new object();
        });
        var view = new IndexedReadOnlyList<object>(source, [2, 0, 2]);
        using IEnumerator<object> enumerator = view.GetEnumerator();

        Assert.Empty(calls);
        Assert.True(enumerator.MoveNext());
        object first = enumerator.Current;
        Assert.Equal(LastIndexOnly, calls);
        Assert.True(enumerator.MoveNext());
        Assert.True(enumerator.MoveNext());
        Assert.Same(first, enumerator.Current);
        Assert.False(enumerator.MoveNext());
        Assert.Equal(LastThenFirstIndices, calls);
        Assert.Equal(2, source.MaterializedCount);
    }

    /// <summary>Memoized lookups use existing references and ignore value equality.</summary>
    [Fact]
    public void MemoizedLookupNeverMaterializesAndMatchesOnlyPublishedReferences()
    {
        SyntheticRow[] expected = [new(0), new(1), new(2)];
        var source = new MemoizedIndexedReadOnlyList<SyntheticRow>(3, index => expected[index]);
        IList view = new IndexedReadOnlyList<SyntheticRow>(source, [2, 0, 2]);

        Assert.Equal(-1, view.IndexOf(expected[2]));
        Assert.False(view.Contains(expected[0]));
        Assert.False(view.Contains(null));
        Assert.False(view.Contains("unrelated"));
        Assert.Equal(0, source.MaterializedCount);

        _ = source[2];
        Assert.Equal(0, view.IndexOf(expected[2]));
        Assert.True(view.Contains(expected[2]));
        Assert.False(view.Contains(new SyntheticRow(2)));
        Assert.Equal(-1, view.IndexOf(new SyntheticRow(2)));
        Assert.False(view.Contains(expected[1]));
        Assert.Equal(1, source.MaterializedCount);

        _ = source[0];
        Assert.Equal(1, view.IndexOf(expected[0]));
        Assert.Equal(2, source.MaterializedCount);
    }

    /// <summary>Unseen and faulted memoized indices are skipped without invoking their factories.</summary>
    [Fact]
    public void MemoizedLookupSkipsUnseenAndFaultedRows()
    {
        var failure = new InvalidOperationException("Synthetic failure.");
        object expected = new();
        int calls = 0;
        var source = new MemoizedIndexedReadOnlyList<object>(3, index =>
        {
            calls++;
            return index == 1 ? expected : throw failure;
        });
        IList view = new IndexedReadOnlyList<object>(source, [0, 2, 1]);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => source[0]));
        _ = source[1];

        Assert.Equal(2, view.IndexOf(expected));
        Assert.True(view.Contains(expected));
        Assert.False(view.Contains(null));
        Assert.Equal(2, calls);
        Assert.Equal(1, source.MaterializedCount);
    }

    /// <summary>Other sources use value equality and support null rows.</summary>
    [Fact]
    public void NonMemoizedLookupUsesValueEqualityIncludingNull()
    {
        SyntheticRow[] source = [new(0), new(1)];
        IList view = new IndexedReadOnlyList<SyntheticRow>(source, [1, 0, 1]);

        Assert.Equal(0, view.IndexOf(new SyntheticRow(1)));
        Assert.True(view.Contains(new SyntheticRow(0)));
        Assert.False(view.Contains("unrelated"));
        IList nullableView = new IndexedReadOnlyList<object>([null!, new object()], [1, 0]);
        Assert.True(nullableView.Contains(null));
        Assert.Equal(1, nullableView.IndexOf(null));
    }

    /// <summary>A non-memoized factory is invoked by lookup until the first equal row is found.</summary>
    [Fact]
    public void NonMemoizedLookupInvokesTheFactoryInSelectedOrder()
    {
        var calls = new List<int>();
        var source = new FactoryReadOnlyList<SyntheticRow>(3, index =>
        {
            calls.Add(index);
            return new SyntheticRow(index);
        });
        IList view = new IndexedReadOnlyList<SyntheticRow>(source, [2, 0, 2]);

        Assert.Empty(calls);
        Assert.Equal(1, view.IndexOf(new SyntheticRow(0)));
        Assert.Equal(LastThenFirstIndices, calls);
        calls.Clear();

        Assert.True(view.Contains(new SyntheticRow(2)));
        Assert.Equal(LastIndexOnly, calls);
    }

    private sealed record SyntheticRow(int Key);
}
