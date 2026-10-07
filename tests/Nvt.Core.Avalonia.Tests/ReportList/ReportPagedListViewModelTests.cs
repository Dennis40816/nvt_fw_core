// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Specialized;
using System.ComponentModel;
using Nvt.Core.Avalonia.ReportList;
using Nvt.Core.ReportList;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.ReportList;

/// <summary>Direct synthetic coverage of cumulative loading, labels, checked math and publication.</summary>
public sealed class ReportPagedListViewModelTests
{
    private static readonly string[] ModelNotifications =
    [
        "model:VisibleCount", "model:RemainingCount", "model:HasMoreItems",
        "model:PageStatus", "model:LoadMoreLabel", "command:load",
    ];
    private static readonly string[] AddNotifications = ["items:Count", "items:Item[]", "items:Add"];
    private static readonly string[] CountNotification = ["items:Count"];

    /// <summary>Empty sources keep the formatted zero status and all-loaded label with either initial-load choice.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptySourceHasNoLoadableItems(bool loadInitialPage)
    {
        var page = ReportPagedListViewModel.Create(Array.Empty<object>(), 1, ReportListTestData.Custom, loadInitialPage);
        List<string> notifications = Observe(page);
        page.EnsureInitialPage();

        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.VisibleCount);
        Assert.Equal(0, page.RemainingCount);
        Assert.Empty(page.Items);
        Assert.False(page.HasMoreItems);
        Assert.False(page.LoadMoreCommand.CanExecute(null));
        Assert.Equal("prefix(0,0)", page.PageStatus);
        Assert.Equal("complete", page.LoadMoreLabel);
        Assert.Empty(notifications);

        // RelayCommand.Execute does not enforce CanExecute; the frozen body still publishes its notifications.
        page.LoadMoreCommand.Execute(null);
        Assert.Equal(ModelNotifications, notifications);
    }

    /// <summary>Deferred construction, labels and command availability cause no row access or source enumeration.</summary>
    [Fact]
    public void DeferredInitialPageLoadsOnlyWhenRequested()
    {
        var source = new TrackingReadOnlyList<int>(5, static index => index);
        var page = ReportPagedListViewModel.Create(source, 2, ReportListTestData.Custom, false);

        Assert.Empty(source.Reads);
        Assert.Equal(0, source.EnumerationCount);
        Assert.Empty(page.Items);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(0, page.VisibleCount);
        Assert.Equal(5, page.RemainingCount);
        Assert.True(page.HasMoreItems);
        Assert.True(page.LoadMoreCommand.CanExecute(null));
        Assert.Equal("prefix(0,5)", page.PageStatus);
        Assert.Equal("append(2,5)", page.LoadMoreLabel);

        page.EnsureInitialPage();

        Assert.Equal(Enumerable.Range(0, 2), source.Reads);
        Assert.Equal(2, page.VisibleCount);
        Assert.Equal(3, page.RemainingCount);
        Assert.Equal("prefix(2,5)", page.PageStatus);
        Assert.Equal("append(2,3)", page.LoadMoreLabel);
        Assert.Equal(0, source.EnumerationCount);
    }

    /// <summary>Initial loading performs only indexed reads and never enumerates a typed source.</summary>
    [Fact]
    public void EagerConstructionOnlyReadsTheFirstBatch()
    {
        var source = new TrackingReadOnlyList<int>(5, static index => index);
        var page = ReportPagedListViewModel.Create(source, 2, ReportListTestData.Custom);
        Assert.Equal(Enumerable.Range(0, 2), source.Reads);
        Assert.Equal(0, source.EnumerationCount);
        Assert.Equal(2, page.VisibleCount);
    }

    /// <summary>Multiple batches retain all previous items and finish with a partial page and a disabled command.</summary>
    [Fact]
    public void MultipleLoadsAccumulateThroughTheLastPartialPage()
    {
        var source = new TrackingReadOnlyList<int>(5, static index => index);
        var page = ReportPagedListViewModel.Create(source, 2, ReportListTestData.Custom);
        page.LoadMoreCommand.Execute(null);

        Assert.Equal(4, page.VisibleCount);
        Assert.Equal(1, page.RemainingCount);
        Assert.Equal("prefix(4,5)", page.PageStatus);
        Assert.Equal("append(1,1)", page.LoadMoreLabel);
        Assert.True(page.LoadMoreCommand.CanExecute(null));

        page.LoadMoreCommand.Execute(null);

        Assert.Equal(Enumerable.Range(0, 5).Cast<object>(), page.Items);
        Assert.Equal(5, page.VisibleCount);
        Assert.Equal(0, page.RemainingCount);
        Assert.False(page.HasMoreItems);
        Assert.False(page.LoadMoreCommand.CanExecute(null));
        Assert.Equal("prefix(5,5)", page.PageStatus);
        Assert.Equal("complete", page.LoadMoreLabel);
        Assert.Equal(Enumerable.Range(0, 5), source.Reads);
        Assert.Equal(0, source.EnumerationCount);

        List<string> notifications = Observe(page);
        page.LoadMoreCommand.Execute(null);
        Assert.Equal(ModelNotifications, notifications);
        Assert.Equal(5, source.Reads.Count);
    }

    /// <summary>Repeated expansion requests never reload an already visible prefix or publish duplicate events.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnsureInitialPageIsIdempotent(bool loadInitialPage)
    {
        var source = new TrackingReadOnlyList<object>(3, static _ => new object());
        var page = ReportPagedListViewModel.Create(source, 2, ReportListTestData.Custom, loadInitialPage);
        page.EnsureInitialPage();
        object first = page.Items[0];
        List<string> notifications = Observe(page);
        page.EnsureInitialPage();
        page.EnsureInitialPage();

        Assert.Equal(2, source.Reads.Count);
        Assert.Equal(2, page.VisibleCount);
        Assert.Same(first, page.Items[0]);
        Assert.Empty(notifications);

        page.LoadMoreCommand.Execute(null);
        notifications.Clear();
        page.EnsureInitialPage();
        Assert.Equal(3, page.VisibleCount);
        Assert.Equal(3, source.Reads.Count);
        Assert.Empty(notifications);
    }

    /// <summary>Exact page boundaries, their neighbours and page size one preserve cumulative counts.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(7, 8)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    [InlineData(23, 24)]
    [InlineData(24, 24)]
    [InlineData(25, 24)]
    [InlineData(39, 40)]
    [InlineData(40, 40)]
    [InlineData(41, 40)]
    [InlineData(47, 24)]
    [InlineData(48, 24)]
    [InlineData(49, 24)]
    public void BatchCountsMatchPageBoundaries(int count, int pageSize)
    {
        var source = new TrackingReadOnlyList<int>(count, static index => index);
        var page = ReportPagedListViewModel.Create(source, pageSize, ReportListTestData.Custom, false);
        int expectedVisible = 0;
        while (page.HasMoreItems)
        {
            int expectedNext = Math.Min(pageSize, count - expectedVisible);
            Assert.Equal($"append({expectedNext},{count - expectedVisible})", page.LoadMoreLabel);
            page.LoadMoreCommand.Execute(null);
            expectedVisible += expectedNext;
            Assert.Equal(expectedVisible, page.VisibleCount);
            Assert.Equal(count - expectedVisible, page.RemainingCount);
            Assert.Equal(Enumerable.Range(0, expectedVisible).Cast<object>(), page.Items);
            Assert.Equal(expectedVisible, source.Reads.Count);
        }

        Assert.Equal(count, page.VisibleCount);
        Assert.Equal("complete", page.LoadMoreLabel);
        Assert.False(page.LoadMoreCommand.CanExecute(null));
        Assert.Equal(0, source.EnumerationCount);
    }

    /// <summary>Page-size validation preserves the positive domain including the representable maximum.</summary>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(int.MaxValue - 1)]
    [InlineData(int.MaxValue)]
    public void PageSizeValidationPreservesThePositiveDomain(int pageSize)
    {
        if (pageSize <= 0)
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                ReportPagedListViewModel.Create(ReportListTestData.OneRow, pageSize, ReportListTestData.Custom));
            Assert.Equal("pageSize", error.ParamName);
            Assert.Equal(pageSize, error.ActualValue);
        }
        else
        {
            var page = ReportPagedListViewModel.Create(ReportListTestData.OneRow, pageSize, ReportListTestData.Custom);
            Assert.Equal("row", Assert.Single(page.Items));
            Assert.False(page.HasMoreItems);
        }
    }

    /// <summary>Items validation precedes page-size validation, which precedes the new labels validation.</summary>
    [Fact]
    public void ArgumentValidationKeepsTheFrozenCheckOrder()
    {
        ArgumentNullException itemsError = Assert.Throws<ArgumentNullException>(() =>
            ReportPagedListViewModel.Create<object>(null!, 0, null!));
        Assert.Equal("items", itemsError.ParamName);
        ArgumentOutOfRangeException sizeError = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReportPagedListViewModel.Create(ReportListTestData.OneRow, 0, null!));
        Assert.Equal("pageSize", sizeError.ParamName);
        ArgumentNullException labelsError = Assert.Throws<ArgumentNullException>(() =>
            ReportPagedListViewModel.Create(ReportListTestData.OneRow, 1, null!));
        Assert.Equal("labels", labelsError.ParamName);
    }

    /// <summary>Each row publishes Count, Item[] and Add before the model properties and command availability.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LoadKeepsExactNotificationOrderAndIncrementalState(int pageSize)
    {
        var source = new FactoryReadOnlyList<int>(3, static index => index);
        var page = ReportPagedListViewModel.Create(source, pageSize, ReportListTestData.Custom, false);
        List<string> notifications = Observe(page);
        var counts = new List<int>();
        ((INotifyCollectionChanged)page.Items).CollectionChanged += (_, args) =>
        {
            counts.Add(page.VisibleCount);
            Assert.Equal(NotifyCollectionChangedAction.Add, args.Action);
            Assert.Equal(page.VisibleCount - 1, args.NewStartingIndex);
            Assert.Equal(page.VisibleCount - 1, Assert.Single(args.NewItems!.Cast<object>()));
            Assert.Null(args.OldItems);
            Assert.Equal(-1, args.OldStartingIndex);
            Assert.Equal(3 - page.VisibleCount, page.RemainingCount);
        };

        page.LoadMoreCommand.Execute(null);

        Assert.Equal(Enumerable.Range(0, pageSize).SelectMany(static _ => AddNotifications).Concat(ModelNotifications), notifications);
        Assert.Equal(Enumerable.Range(1, pageSize), counts);
        notifications.Clear();
        counts.Clear();
        page.LoadMoreCommand.Execute(null);
        int nextCount = Math.Min(pageSize, 3 - pageSize);
        Assert.Equal(Enumerable.Range(0, nextCount).SelectMany(static _ => AddNotifications).Concat(ModelNotifications), notifications);
        Assert.Equal(Enumerable.Range(pageSize + 1, nextCount), counts);
    }

    /// <summary>The model retains original row references, collection identity and command identity across batches.</summary>
    [Fact]
    public void LoadingRetainsTypedSourceAndStableIdentities()
    {
        object[] rows = [new(), new(), new()];
        var page = ReportPagedListViewModel.Create(rows, 1, ReportListTestData.Custom, false);
        object collection = page.Items;
        object command = page.LoadMoreCommand;
        rows[0] = new object();
        page.EnsureInitialPage();
        Assert.Same(rows[0], Assert.Single(page.Items));
        page.LoadMoreCommand.Execute(null);
        page.LoadMoreCommand.Execute(null);
        Assert.Same(collection, page.Items);
        Assert.Same(command, page.LoadMoreCommand);
        for (int index = 0; index < rows.Length; index++)
        {
            Assert.Same(rows[index], page.Items[index]);
        }
    }

    /// <summary>Both frozen language formatter contracts preserve all count arguments and end labels.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InjectedLanguageFormattersKeepTheFrozenValues(bool chinese)
    {
        var source = new FactoryReadOnlyList<int>(40, static index => index);
        var page = ReportPagedListViewModel.Create(source, 8, chinese ? ReportListTestData.Chinese : ReportListTestData.English);
        Assert.Equal(chinese ? "已顯示 8/40 筆" : "Showing 8/40", page.PageStatus);
        Assert.Equal(chinese ? "再載入 8 筆（尚餘 32 筆）" : "Load 8 more (32 remaining)", page.LoadMoreLabel);
        for (int index = 0; index < 4; index++)
        {
            page.LoadMoreCommand.Execute(null);
        }

        Assert.Equal(chinese ? "已顯示 40/40 筆" : "Showing 40/40", page.PageStatus);
        Assert.Equal(chinese ? "已載入全部項目" : "All items loaded", page.LoadMoreLabel);
    }

    /// <summary>Deferred construction supports maximum counts and sizes without materializing rows.</summary>
    [Theory]
    [InlineData(int.MaxValue - 1, 1)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(int.MaxValue, int.MaxValue - 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void DeferredCountsSupportTheIntegerBoundary(int count, int pageSize)
    {
        var source = new TrackingReadOnlyList<int>(count, static index => index);
        var page = ReportPagedListViewModel.Create(source, pageSize, ReportListTestData.Custom, false);
        Assert.Equal(count, page.TotalCount);
        Assert.Equal(count, page.RemainingCount);
        Assert.Equal($"prefix(0,{count})", page.PageStatus);
        Assert.Equal($"append({Math.Min(count, pageSize)},{count})", page.LoadMoreLabel);
        Assert.Empty(source.Reads);
        Assert.Equal(0, source.EnumerationCount);
    }

    /// <summary>Checked addition runs before clamping, even when Execute is called on a disabled end command.</summary>
    [Theory]
    [InlineData(int.MaxValue - 2, false)]
    [InlineData(int.MaxValue - 1, false)]
    [InlineData(int.MaxValue, true)]
    public void LoadAdditionPreservesBelowAtAndAboveOverflowBoundary(int pageSize, bool overflows)
    {
        var source = new TrackingReadOnlyList<int>(1, static index => index);
        var page = ReportPagedListViewModel.Create(source, pageSize, ReportListTestData.Custom);
        List<string> notifications = Observe(page);
        Assert.False(page.LoadMoreCommand.CanExecute(null));

        if (overflows)
        {
            Assert.Throws<OverflowException>(() => page.LoadMoreCommand.Execute(null));
            Assert.Empty(notifications);
        }
        else
        {
            page.LoadMoreCommand.Execute(null);
            Assert.Equal(ModelNotifications, notifications);
        }

        Assert.Equal(0, Assert.Single(page.Items));
        Assert.Equal(0, Assert.Single(source.Reads));
    }

    /// <summary>Source failures retain the appended prefix and its Add events without model or command notifications.</summary>
    [Fact]
    public void SourceFailureRetainsItsPublishedPrefix()
    {
        var failure = new InvalidOperationException("Synthetic row failure.");
        var source = new FactoryReadOnlyList<int>(3, index => index == 1 ? throw failure : index);
        var page = ReportPagedListViewModel.Create(source, 2, ReportListTestData.Custom, false);
        List<string> notifications = Observe(page);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => page.EnsureInitialPage()));
        Assert.Equal(0, Assert.Single(page.Items));
        Assert.Equal(2, page.RemainingCount);
        Assert.True(page.HasMoreItems);
        Assert.Equal(AddNotifications, notifications);
        notifications.Clear();
        page.EnsureInitialPage();
        Assert.Empty(notifications);
    }

    /// <summary>An observer exception propagates after the row is appended and stops subsequent notifications.</summary>
    [Fact]
    public void ObserverFailureStopsAtTheFrozenNotification()
    {
        var page = ReportPagedListViewModel.Create(ReportListTestData.OneRow, 1, ReportListTestData.Custom, false);
        List<string> notifications = Observe(page);
        var failure = new InvalidOperationException("Synthetic observer failure.");
        ((INotifyPropertyChanged)page.Items).PropertyChanged += (_, _) => throw failure;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => page.LoadMoreCommand.Execute(null)));

        Assert.Equal("row", Assert.Single(page.Items));
        Assert.Equal(0, page.RemainingCount);
        Assert.False(page.LoadMoreCommand.CanExecute(null));
        Assert.Equal(CountNotification, notifications);
    }

    /// <summary>An enabled command after a partial source failure still checks addition before another indexed read.</summary>
    [Fact]
    public void PartialInitialLoadKeepsTheEnabledCommandOverflowBoundary()
    {
        var failure = new InvalidOperationException("Synthetic row failure.");
        var source = new TrackingReadOnlyList<int>(2, index => index == 1 ? throw failure : index);
        var page = ReportPagedListViewModel.Create(source, int.MaxValue, ReportListTestData.Custom, false);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => page.EnsureInitialPage()));
        Assert.Equal(1, page.VisibleCount);
        Assert.True(page.LoadMoreCommand.CanExecute(null));
        List<string> notifications = Observe(page);

        Assert.Throws<OverflowException>(() => page.LoadMoreCommand.Execute(null));

        Assert.Equal(2, source.Reads.Count);
        Assert.Equal(0, Assert.Single(page.Items));
        Assert.Empty(notifications);
    }

    private static List<string> Observe(ReportPagedListViewModel page)
    {
        var notifications = new List<string>();
        ((INotifyPropertyChanged)page.Items).PropertyChanged += (_, args) => notifications.Add($"items:{args.PropertyName}");
        ((INotifyCollectionChanged)page.Items).CollectionChanged += (_, args) => notifications.Add($"items:{args.Action}");
        page.PropertyChanged += (_, args) => notifications.Add($"model:{args.PropertyName}");
        page.PropertyChanging += (_, args) => notifications.Add($"model:changing:{args.PropertyName}");
        page.LoadMoreCommand.CanExecuteChanged += (_, _) => notifications.Add("command:load");
        return notifications;
    }
}
