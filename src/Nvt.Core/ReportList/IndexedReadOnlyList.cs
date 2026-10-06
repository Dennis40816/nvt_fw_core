// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;

namespace Nvt.Core.ReportList;

/// <summary>Read-only ordered view over selected indices from a shared report row projection.</summary>
/// <typeparam name="T">The reference type of each row.</typeparam>
public sealed class IndexedReadOnlyList<T> : IReadOnlyList<T>, IList
    where T : class
{
    private const string ReadOnlyMessage = "The indexed report projection is read-only.";
    private readonly IReadOnlyList<T> _source;
    private readonly int[] _indices;

    /// <summary>Creates a view that copies the selected indices and retains the source.</summary>
    /// <param name="source">The shared row source.</param>
    /// <param name="indices">The ordered source indices, which may contain duplicates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="indices"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A selected index is outside <paramref name="source"/>.</exception>
    public IndexedReadOnlyList(IReadOnlyList<T> source, IReadOnlyList<int> indices)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(indices);
        _source = source;
        _indices = [.. indices];
        if (_indices.Any(index => index < 0 || index >= source.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(indices));
        }
    }

    /// <summary>Gets the number of selected indices.</summary>
    public int Count => _indices.Length;

    /// <summary>Gets the source row at the selected position.</summary>
    /// <param name="index">The zero-based position in the view.</param>
    /// <returns>The source row at that position's selected index.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside the view.</exception>
    public T this[int index] => _source[_indices[index]];

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException(ReadOnlyMessage);
    }

    bool IList.IsFixedSize => true;

    bool IList.IsReadOnly => true;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    int IList.Add(object? value)
    {
        throw new NotSupportedException(ReadOnlyMessage);
    }

    void IList.Clear()
    {
        throw new NotSupportedException(ReadOnlyMessage);
    }

    bool IList.Contains(object? value)
    {
        return ((IList)this).IndexOf(value) >= 0;
    }

    int IList.IndexOf(object? value)
    {
        for (int index = 0; index < _indices.Length; index++)
        {
            int sourceIndex = _indices[index];
            bool matches = _source is MemoizedIndexedReadOnlyList<T> memoized
                ? memoized.HasMaterializedReference(sourceIndex, value)
                : Equals(_source[sourceIndex], value);
            if (matches)
            {
                return index;
            }
        }

        return -1;
    }

    void IList.Insert(int index, object? value)
    {
        throw new NotSupportedException(ReadOnlyMessage);
    }

    void IList.Remove(object? value)
    {
        throw new NotSupportedException(ReadOnlyMessage);
    }

    void IList.RemoveAt(int index)
    {
        throw new NotSupportedException(ReadOnlyMessage);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (int sourceIndex = 0; sourceIndex < Count; sourceIndex++)
        {
            array.SetValue(this[sourceIndex], checked(index + sourceIndex));
        }
    }

    /// <summary>Enumerates source rows in the selected order, including duplicates.</summary>
    /// <returns>An enumerator over the selected rows.</returns>
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
