// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nvt.Core.Avalonia.ReportList;

/// <summary>Accumulates indexed rows in bounded batches when requested.</summary>
/// <remarks>Construction, access, commands and notifications are the caller's UI-thread responsibility.</remarks>
public sealed class ReportPagedListViewModel : ObservableObject
{
    private readonly IReadOnlyList<object> _allItems;
    // Mutable contents are owned by this model and accessed only on the caller's UI thread.
    private readonly ObservableCollection<object> _items = [];
    private readonly int _pageSize;
    private readonly ReportListLabels _labels;
    private readonly RelayCommand _loadMoreCommand;

    private ReportPagedListViewModel(
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
        _loadMoreCommand = new RelayCommand(LoadMore, () => HasMoreItems);
        if (loadInitialPage)
        {
            LoadMore();
        }
    }

    /// <summary>Gets the stable collection retaining all loaded rows in source order.</summary>
    public ReadOnlyObservableCollection<object> Items { get; }

    /// <summary>Gets the source count, which the caller must keep stable.</summary>
    public int TotalCount => _allItems.Count;

    /// <summary>Gets the cumulative number of loaded rows.</summary>
    public int VisibleCount => Items.Count;

    /// <summary>Gets the source count minus the cumulative loaded count.</summary>
    public int RemainingCount => TotalCount - VisibleCount;

    /// <summary>Gets whether another batch of rows is available.</summary>
    public bool HasMoreItems => RemainingCount > 0;

    /// <summary>Gets the injected status formatted with visible and total counts, including an empty source.</summary>
    public string PageStatus => _labels.PagedStatus(VisibleCount, TotalCount);

    /// <summary>Gets the injected next-batch label or the all-items-loaded label.</summary>
    public string LoadMoreLabel
    {
        get
        {
            if (!HasMoreItems)
            {
                return _labels.AllItemsLoaded;
            }

            int nextCount = Math.Min(_pageSize, RemainingCount);
            return _labels.LoadMore(nextCount, RemainingCount);
        }
    }

    /// <summary>Gets the command that appends one more batch; availability reflects remaining items.</summary>
    public IRelayCommand LoadMoreCommand => _loadMoreCommand;

    /// <summary>Creates a non-copying, nonenumerating indexed view, optionally loading the first batch.</summary>
    /// <typeparam name="T">The source item type.</typeparam>
    /// <param name="items">An indexed list whose count remains stable for the model's lifetime.</param>
    /// <param name="pageSize">A positive batch size supplied by the host.</param>
    /// <param name="labels">Immutable labels and stable formatters supplied by the host.</param>
    /// <param name="loadInitialPage">Whether to read the first batch immediately.</param>
    /// <returns>A model that retains the source without enumerating it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/>, <paramref name="labels"/>, or a label member is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageSize"/> is zero or negative.</exception>
    /// <exception cref="OverflowException">Checked batch arithmetic overflows.</exception>
    public static ReportPagedListViewModel Create<T>(
        IReadOnlyList<T> items,
        int pageSize,
        ReportListLabels labels,
        bool loadInitialPage = true)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new ReportPagedListViewModel(
            new ObjectReadOnlyList<T>(items),
            pageSize,
            labels,
            loadInitialPage);
    }

    /// <summary>Loads the first batch only when the source is nonempty and no rows are visible.</summary>
    /// <exception cref="OverflowException">Checked batch arithmetic overflows.</exception>
    public void EnsureInitialPage()
    {
        if (VisibleCount == 0 && TotalCount > 0)
        {
            LoadMore();
        }
    }

    private void LoadMore()
    {
        int endExclusive = Math.Min(checked(VisibleCount + _pageSize), TotalCount);
        for (int index = VisibleCount; index < endExclusive; index++)
        {
            _items.Add(_allItems[index]);
        }

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(HasMoreItems));
        OnPropertyChanged(nameof(PageStatus));
        OnPropertyChanged(nameof(LoadMoreLabel));
        _loadMoreCommand.NotifyCanExecuteChanged();
    }
}
