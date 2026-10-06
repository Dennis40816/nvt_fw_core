// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;

namespace Nvt.Core.ReportList;

/// <summary>Indexed projection that creates non-retained rows for fixed-size UI windows.</summary>
/// <typeparam name="T">The type of each row.</typeparam>
/// <param name="count">The number of rows.</param>
/// <param name="factory">Creates a row for the supplied index on every access.</param>
/// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
/// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
public sealed class FactoryReadOnlyList<T>(int count, Func<int, T> factory) : IReadOnlyList<T>
{
    private readonly Func<int, T> _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>Gets the number of rows in the projection.</summary>
    public int Count { get; } = count >= 0 ? count : throw new ArgumentOutOfRangeException(nameof(count));

    /// <summary>Creates a row at the supplied index without retaining it.</summary>
    /// <param name="index">The zero-based row index.</param>
    /// <returns>The factory result, including null when the factory returns null.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the projection.</exception>
    public T this[int index] => (uint)index < (uint)Count
        ? _factory(index)
        : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Enumerates rows in index order, invoking the factory as each row is reached.</summary>
    /// <returns>An enumerator over non-retained factory results.</returns>
    public IEnumerator<T> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
