// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Nvt.Core.ReportList;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> whose bulk replacement raises exactly one
/// <see cref="NotifyCollectionChangedAction.Reset"/> instead of a Clear followed by one Add per element.
/// </summary>
/// <typeparam name="T">The type of each collection item.</typeparam>
public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Replaces every element with <paramref name="items"/>, keeping this collection's identity while
    /// raising exactly one Reset (plus Count and Item[] property changes) instead of per-item Add notifications.
    /// </summary>
    /// <param name="items">The items to enumerate after clearing the collection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Collection notification reentrancy is disallowed.</exception>
    /// <remarks>An enumeration failure leaves the added prefix without publishing replacement notifications.</remarks>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();
        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
