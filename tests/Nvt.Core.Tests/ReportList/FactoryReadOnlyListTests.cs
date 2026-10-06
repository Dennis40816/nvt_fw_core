// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using Nvt.Core.ReportList;
using Xunit;

namespace Nvt.Core.Tests.ReportList;

/// <summary>Characterizes non-retained factory results and integer boundaries.</summary>
public sealed class FactoryReadOnlyListTests
{
    private static readonly int[] RepeatedFirstIndices = [0, 0];
    private static readonly int[] SequentialIndices = [0, 1];
    private static readonly int[] RepeatedFirstThenSecondIndices = [0, 0, 1];

    /// <summary>The factory is validated before a negative count.</summary>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NullFactoryIsValidatedFirst(int count)
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(
            () => new FactoryReadOnlyList<int>(count, null!));

        Assert.Equal("factory", error.ParamName);
    }

    /// <summary>Negative counts use the source constructor exception without an actual value.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeCountIsRejected(int count)
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new FactoryReadOnlyList<int>(count, index => index));

        Assert.Equal("count", error.ParamName);
        Assert.Null(error.ActualValue);
    }

    /// <summary>All nonnegative counts are accepted without invoking the factory.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(10_000)]
    [InlineData(int.MaxValue)]
    public void CountAndIndexBoundariesPreserveDeferredFactoryCalls(int count)
    {
        var calls = new List<int>();
        var rows = new FactoryReadOnlyList<int>(count, index =>
        {
            calls.Add(index);
            return index;
        });

        Assert.Equal(count, rows.Count);
        foreach (int invalidIndex in new[] { int.MinValue, -1, count })
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => rows[invalidIndex]);
            Assert.Equal("index", error.ParamName);
            Assert.Null(error.ActualValue);
        }

        if (count < int.MaxValue)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => rows[count + 1]);
        }

        Assert.Empty(calls);
        if (count == 0)
        {
            Assert.Empty(rows);
            return;
        }

        Assert.Equal(0, rows[0]);
        Assert.Equal(count - 1, rows[count - 1]);
        Assert.Equal(new[] { 0, count - 1 }, calls);
    }

    /// <summary>Repeated access creates a fresh result and accepts null results.</summary>
    [Fact]
    public void ResultsAreNeitherRetainedNorNullChecked()
    {
        int calls = 0;
        var rows = new FactoryReadOnlyList<object>(1, _ =>
        {
            calls++;
            return new object();
        });

        Assert.NotSame(rows[0], rows[0]);
        Assert.Equal(2, calls);
        var nullableRows = new FactoryReadOnlyList<object?>(1, _ => null);
        Assert.Null(nullableRows[0]);
        Assert.Null(Assert.Single(nullableRows));
    }

    /// <summary>Factory failures are retried rather than cached.</summary>
    [Fact]
    public void FactoryFailureIsRetriedOnEveryAccess()
    {
        var calls = new List<int>();
        var rows = new FactoryReadOnlyList<object>(1, index =>
        {
            calls.Add(index);
            throw new InvalidOperationException("Synthetic factory failure.");
        });

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(() => rows[0]);
        InvalidOperationException second = Assert.Throws<InvalidOperationException>(() => rows[0]);

        Assert.NotSame(first, second);
        Assert.Equal("Synthetic factory failure.", first.Message);
        Assert.Equal(RepeatedFirstIndices, calls);
    }

    /// <summary>Both enumerator interfaces call the factory only when advancing.</summary>
    [Fact]
    public void EnumerationIsDeferredAndRepeatsFactoryCalls()
    {
        var calls = new List<int>();
        var rows = new FactoryReadOnlyList<int>(2, index =>
        {
            calls.Add(index);
            return index;
        });
        using IEnumerator<int> enumerator = rows.GetEnumerator();
        IEnumerator untyped = ((IEnumerable)rows).GetEnumerator();

        Assert.Empty(calls);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(0, enumerator.Current);
        Assert.True(untyped.MoveNext());
        Assert.Equal(0, untyped.Current);
        Assert.Equal(RepeatedFirstIndices, calls);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(1, enumerator.Current);
        Assert.False(enumerator.MoveNext());
        Assert.Equal(RepeatedFirstThenSecondIndices, calls);
        Assert.Equal(SequentialIndices, ((IEnumerable)rows).Cast<int>());
    }
}
