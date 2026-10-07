// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nvt.Core.ReportList;

namespace Nvt.Core.Avalonia.ReportList;

/// <summary>Shows one fixed-size window over a stable-count indexed list.</summary>
/// <remarks>Construction, access, commands and notifications are the caller's UI-thread responsibility.</remarks>
public sealed class ReportWindowedListViewModel : ObservableObject
{
    private readonly IReadOnlyList<object> _allItems;
    // Mutable contents are owned by this model and accessed only on the caller's UI thread.
    private readonly ResettableObservableCollection<object> _items = [];
    private readonly int _pageSize;
    private readonly ReportListLabels _labels;
    private readonly RelayCommand _previousPageCommand;
    private readonly RelayCommand _nextPageCommand;

    private ReportWindowedListViewModel(
        IReadOnlyList<object> allItems,
        int pageSize,
        ReportListLabels labels,
        bool loadInitialPage)
    {
        ArgumentNullException.ThrowIfNull(allItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentNullException.ThrowIfNull(labels);
        labels.EnsureComplete();
        _allItems = allItems;
        _pageSize = pageSize;
        _labels = labels;
        Items = new ReadOnlyObservableCollection<object>(_items);
        _previousPageCommand = new RelayCommand(ShowPreviousPage, () => HasPreviousPage);
        _nextPageCommand = new RelayCommand(ShowNextPage, () => HasNextPage);
        if (loadInitialPage && TotalCount > 0)
        {
            ShowPage(0);
        }
    }

    /// <summary>Gets the stable collection containing only the current window's rows.</summary>
    public ReadOnlyObservableCollection<object> Items { get; }

    /// <summary>Gets the source count, which the caller must keep stable.</summary>
    public int TotalCount => _allItems.Count;

    /// <summary>Gets the number of rows in the current window.</summary>
    public int VisibleCount => Items.Count;

    /// <summary>Gets the zero-based page index; zero also represents a deferred or empty window.</summary>
    // Mutable page position is UI-thread-only. Its property notification follows collection replacement.
    public int PageIndex { get; private set; }

    /// <summary>Gets the number of pages using checked integer arithmetic.</summary>
    public int PageCount => TotalCount == 0 ? 0 : checked(((TotalCount - 1) / _pageSize) + 1);

    /// <summary>Gets whether the current page has a predecessor.</summary>
    public bool HasPreviousPage => PageIndex > 0;

    /// <summary>Gets whether the first deferred page or a subsequent page can be shown.</summary>
    public bool HasNextPage => VisibleCount == 0 ? PageCount > 0 : PageIndex + 1 < PageCount;

    /// <summary>Gets whether the source spans more than one page.</summary>
    public bool HasMultiplePages => PageCount > 1;

    /// <summary>Gets the injected empty status or formatted one-based visible range.</summary>
    public string PageStatus
    {
        get
        {
            if (VisibleCount == 0)
            {
                return _labels.NoItems;
            }

            int first = checked((PageIndex * _pageSize) + 1);
            int last = checked(first + VisibleCount - 1);
            return _labels.WindowStatus(first, last, TotalCount);
        }
    }

    /// <summary>Gets the injected previous-page label.</summary>
    public string PreviousPageLabel => _labels.PreviousPage;

    /// <summary>Gets the injected next-page label.</summary>
    public string NextPageLabel => _labels.NextPage;

    /// <summary>Gets the command that shows the previous fixed window when available.</summary>
    public IRelayCommand PreviousPageCommand => _previousPageCommand;

    /// <summary>Gets the command that shows the first deferred window or the next window when available.</summary>
    public IRelayCommand NextPageCommand => _nextPageCommand;

    /// <summary>Creates a non-copying, nonenumerating indexed view, optionally loading the first window.</summary>
    /// <typeparam name="T">The source item type.</typeparam>
    /// <param name="items">An indexed list whose count remains stable for the model's lifetime.</param>
    /// <param name="pageSize">A positive window size supplied by the host.</param>
    /// <param name="labels">Immutable labels and stable formatters supplied by the host.</param>
    /// <param name="loadInitialPage">Whether to read the first window immediately.</param>
    /// <returns>A model that retains the source without enumerating it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/>, <paramref name="labels"/>, or a label member is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageSize"/> is zero or negative.</exception>
    /// <exception cref="OverflowException">Checked window arithmetic overflows.</exception>
    public static ReportWindowedListViewModel Create<T>(
        IReadOnlyList<T> items,
        int pageSize,
        ReportListLabels labels,
        bool loadInitialPage = true)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new ReportWindowedListViewModel(
            new ObjectReadOnlyList<T>(items),
            pageSize,
            labels,
            loadInitialPage);
    }

    /// <summary>Shows the window containing an item; reselecting the visible page performs no work.</summary>
    /// <param name="index">The zero-based source index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the source.</exception>
    /// <exception cref="OverflowException">Checked window arithmetic overflows, before changing the window.</exception>
    public void ShowItemAt(int index)
    {
        if ((uint)index >= (uint)TotalCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int pageIndex = index / _pageSize;
        if (VisibleCount == 0 || PageIndex != pageIndex)
        {
            ShowPage(pageIndex);
        }
    }

    private void ShowPreviousPage()
    {
        if (HasPreviousPage)
        {
            ShowPage(PageIndex - 1);
        }
    }

    private void ShowNextPage()
    {
        if (HasNextPage)
        {
            ShowPage(VisibleCount == 0 ? 0 : PageIndex + 1);
        }
    }

    private void ShowPage(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        int start = checked(pageIndex * _pageSize);
        int endExclusive = Math.Min(checked(start + _pageSize), TotalCount);
        object[] rows = new object[endExclusive - start];
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = _allItems[start + index];
        }

        PageIndex = pageIndex;
        _items.ReplaceAll(rows);
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(PageIndex));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(HasMultiplePages));
        OnPropertyChanged(nameof(PageStatus));
        _previousPageCommand.NotifyCanExecuteChanged();
        _nextPageCommand.NotifyCanExecuteChanged();
    }
}
