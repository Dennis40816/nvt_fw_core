// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;

namespace Nvt.Core.Avalonia.ReportList;

/// <summary>Non-copying object view over a typed read-only list.</summary>
internal sealed class ObjectReadOnlyList<T>(IReadOnlyList<T> items) : IReadOnlyList<object>
{
    private readonly IReadOnlyList<T> _items = items ?? throw new ArgumentNullException(nameof(items));

    public int Count => _items.Count;

    public object this[int index] => _items[index]!;

    public IEnumerator<object> GetEnumerator()
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
