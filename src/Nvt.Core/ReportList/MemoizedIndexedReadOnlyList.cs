// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;

namespace Nvt.Core.ReportList;

/// <summary>Thread-safe indexed projection that creates each report row at most once.</summary>
/// <typeparam name="T">The reference type of each row.</typeparam>
public sealed class MemoizedIndexedReadOnlyList<T> : IReadOnlyList<T>
    where T : class
{
    private readonly Func<int, T> _factory;
    private readonly Lazy<T>?[] _items;
    private int _materializedCount;

    /// <summary>Creates a projection that defers each row's allocation until access.</summary>
    /// <param name="count">The number of rows.</param>
    /// <param name="factory">Creates a non-null row for the supplied index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    public MemoizedIndexedReadOnlyList(int count, Func<int, T> factory)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _items = new Lazy<T>?[count];
    }

    /// <summary>Gets the number of rows in the projection.</summary>
    public int Count => _items.Length;

    /// <summary>Gets the number of successfully created rows.</summary>
    public int MaterializedCount => Volatile.Read(ref _materializedCount);

    internal bool HasMaterializedReference(int index, object? value)
    {
        if ((uint)index >= (uint)_items.Length)
        {
            return false;
        }

        Lazy<T>? item = Volatile.Read(ref _items[index]);
        return item is { IsValueCreated: true } && ReferenceEquals(item.Value, value);
    }

    /// <summary>Gets the cached row at the supplied index, creating it on first access.</summary>
    /// <param name="index">The zero-based row index.</param>
    /// <returns>The row shared by all readers of this index.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the projection.</exception>
    /// <exception cref="InvalidOperationException">The factory returned null.</exception>
    /// <remarks>Factory exceptions are cached for each index.</remarks>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_items.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Lazy<T>? item = Volatile.Read(ref _items[index]);
            if (item is null)
            {
                int itemIndex = index;
                var candidate = new Lazy<T>(
                    () => CreateItem(_factory, itemIndex),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                item = Interlocked.CompareExchange(ref _items[index], candidate, null) ?? candidate;
            }

            return item.Value;
        }
    }

    private T CreateItem(Func<int, T> factory, int index)
    {
        T item = factory(index) ?? throw new InvalidOperationException("A report row factory returned null.");
        _ = Interlocked.Increment(ref _materializedCount);
        return item;
    }

    /// <summary>Enumerates rows in index order, creating each row when it is reached.</summary>
    /// <returns>An enumerator over the cached rows.</returns>
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
