// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using Nvt.Core.ReportList;
using Xunit;

namespace Nvt.Core.Tests.ReportList;

/// <summary>Characterizes memoized projection boundaries, identity and failed rows.</summary>
public sealed class MemoizedIndexedReadOnlyListTests
{
    /// <summary>The allocation diagnostic remains internal while row creation remains deferred and cached.</summary>
    [Fact]
    public void MaterializationDiagnosticIsImplementationOnly()
    {
        Assert.Null(typeof(MemoizedIndexedReadOnlyList<object>).GetProperty("MaterializedCount"));
        int factoryCalls = 0;
        var rows = new MemoizedIndexedReadOnlyList<object>(1, _ =>
        {
            factoryCalls++;
            return new object();
        });
        Assert.Equal(0, factoryCalls);
        Assert.Same(rows[0], rows[0]);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, rows.MaterializedCount);
    }

    /// <summary>Negative counts are rejected before the factory is checked.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeCountIsValidatedBeforeNullFactory(int count)
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new MemoizedIndexedReadOnlyList<object>(count, null!));

        Assert.Equal("count", error.ParamName);
        Assert.Equal(count, error.ActualValue);
    }

    /// <summary>Even empty projections require a factory.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void NullFactoryIsRejected(int count)
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(
            () => new MemoizedIndexedReadOnlyList<object>(count, null!));

        Assert.Equal("factory", error.ParamName);
    }

    /// <summary>Empty, single and boundary-sized lists materialize only their first and last accessed rows.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(10_000)]
    public void CountAndIndexBoundariesPreserveDeferredIdentity(int count)
    {
        var calls = new List<int>();
        var rows = new MemoizedIndexedReadOnlyList<object>(count, index =>
        {
            calls.Add(index);
            return new object();
        });

        Assert.Equal(count, rows.Count);
        Assert.Equal(0, rows.MaterializedCount);
        foreach (int invalidIndex in new[] { int.MinValue, -1, count, count + 1, int.MaxValue })
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => rows[invalidIndex]);
            Assert.Equal("index", error.ParamName);
            Assert.Null(error.ActualValue);
        }

        Assert.Empty(calls);
        if (count == 0)
        {
            Assert.Empty(rows);
            return;
        }

        object last = rows[count - 1];
        Assert.Same(last, rows[count - 1]);
        Assert.Equal(1, rows.MaterializedCount);
        object first = rows[0];
        Assert.Same(first, rows[0]);
        Assert.Equal(count == 1 ? 1 : 2, rows.MaterializedCount);
        Assert.Equal(count == 1 ? [0] : new[] { count - 1, 0 }, calls);
    }

    /// <summary>A null factory result is a cached failure with the original message.</summary>
    [Fact]
    public void NullResultCachesTheOriginalExceptionWithoutMaterializing()
    {
        int calls = 0;
        var rows = new MemoizedIndexedReadOnlyList<object>(1, _ =>
        {
            calls++;
            return null!;
        });

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(() => rows[0]);
        InvalidOperationException second = Assert.Throws<InvalidOperationException>(() => rows[0]);

        Assert.Equal("A report row factory returned null.", first.Message);
        Assert.Same(first, second);
        Assert.Equal(1, calls);
        Assert.Equal(0, rows.MaterializedCount);
        Assert.False(rows.HasMaterializedReference(0, null));
    }

    /// <summary>A failed index does not prevent another index from succeeding.</summary>
    [Fact]
    public void FailureIsCachedSeparatelyForEachIndex()
    {
        var failure = new InvalidOperationException("Synthetic failure.");
        object expected = new();
        var rows = new MemoizedIndexedReadOnlyList<object>(2, index => index == 0 ? throw failure : expected);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => rows[0]));
        Assert.Same(expected, rows[1]);
        Assert.Equal(1, rows.MaterializedCount);
        Assert.False(rows.HasMaterializedReference(0, expected));
        Assert.True(rows.HasMaterializedReference(1, expected));
        Assert.False(rows.HasMaterializedReference(1, new object()));
        Assert.False(rows.HasMaterializedReference(-1, expected));
        Assert.False(rows.HasMaterializedReference(2, expected));
    }

    /// <summary>Enumerator creation and reference probes never materialize unseen rows.</summary>
    [Fact]
    public void EnumerationIsDeferredAndBothInterfacesShareCachedRows()
    {
        object[] expected = [new(), new()];
        var rows = new MemoizedIndexedReadOnlyList<object>(2, index => expected[index]);
        using IEnumerator<object> enumerator = rows.GetEnumerator();
        IEnumerator untyped = ((IEnumerable)rows).GetEnumerator();

        Assert.Equal(0, rows.MaterializedCount);
        Assert.False(rows.HasMaterializedReference(0, expected[0]));
        Assert.True(enumerator.MoveNext());
        Assert.Same(expected[0], enumerator.Current);
        Assert.Equal(1, rows.MaterializedCount);
        Assert.True(untyped.MoveNext());
        Assert.Same(expected[0], untyped.Current);
        Assert.Equal(1, rows.MaterializedCount);
        Assert.True(enumerator.MoveNext());
        Assert.Same(expected[1], enumerator.Current);
        Assert.False(enumerator.MoveNext());
        Assert.Equal(2, rows.MaterializedCount);
        Assert.Equal(expected, ((IEnumerable)rows).Cast<object>());
    }
}
