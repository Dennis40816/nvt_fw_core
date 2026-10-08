// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Specialized;
using System.ComponentModel;
using Nvt.Core.Avalonia.ReportList;
using Nvt.Core.ReportList;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.ReportList;

/// <summary>Frozen fixed-window assertions and boundary, identity and notification characterization.</summary>
public sealed class ReportWindowedListViewModelTests
{
    private static readonly string[] PageNotifications =
    [
        "items:Count", "items:Item[]", "items:Reset",
        "model:VisibleCount", "model:PageIndex", "model:HasPreviousPage", "model:HasNextPage",
        "model:HasMultiplePages", "model:PageStatus", "command:previous", "command:next",
    ];

    /// <summary>Page navigation replaces retained rows and never grows beyond the declared window.</summary>
    [Fact]
    public void NavigationReplacesTheCurrentFixedSizeWindow()
    {
        int[] source = [.. Enumerable.Range(0, 130)];
        var navigator = ReportWindowedListViewModel.Create(source, 64, ReportListTestData.English);
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)navigator.Items).CollectionChanged +=
            (_, args) => collectionChanges.Add(args.Action);

        Assert.Equal(0, navigator.PageIndex);
        Assert.Equal(3, navigator.PageCount);
        Assert.True(navigator.HasMultiplePages);
        Assert.Equal(64, navigator.VisibleCount);
        Assert.Equal(0, navigator.Items[0]);
        Assert.Equal("Showing 1-64 of 130", navigator.PageStatus);
        Assert.False(navigator.PreviousPageCommand.CanExecute(null));
        Assert.True(navigator.NextPageCommand.CanExecute(null));

        navigator.NextPageCommand.Execute(null);

        Assert.Equal(1, navigator.PageIndex);
        Assert.Equal(64, navigator.VisibleCount);
        Assert.Equal(64, navigator.Items[0]);
        Assert.Equal("Showing 65-128 of 130", navigator.PageStatus);
        Assert.Equal([NotifyCollectionChangedAction.Reset], collectionChanges);

        collectionChanges.Clear();
        navigator.NextPageCommand.Execute(null);

        Assert.Equal(2, navigator.PageIndex);
        Assert.Equal(2, navigator.VisibleCount);
        Assert.Equal(128, navigator.Items[0]);
        Assert.Equal("Showing 129-130 of 130", navigator.PageStatus);
        Assert.Equal([NotifyCollectionChangedAction.Reset], collectionChanges);
        Assert.True(navigator.PreviousPageCommand.CanExecute(null));
        Assert.False(navigator.NextPageCommand.CanExecute(null));

        navigator.PreviousPageCommand.Execute(null);

        Assert.Equal(1, navigator.PageIndex);
        Assert.Equal(64, navigator.VisibleCount);
        Assert.Equal(64, navigator.Items[0]);
    }

    /// <summary>Direct navigation only creates the containing window and reselecting it does no work.</summary>
    [Fact]
    public void DirectItemNavigationShowsOnlyTheContainingWindow()
    {
        int projectedRowCount = 0;
        var source = new FactoryReadOnlyList<int>(10_000, index => { projectedRowCount++; return index; });
        var navigator = ReportWindowedListViewModel.Create(source, 64, ReportListTestData.English);

        Assert.Equal(64, projectedRowCount);
        navigator.ShowItemAt(9_999);

        Assert.Equal(156, navigator.PageIndex);
        Assert.Equal(16, navigator.VisibleCount);
        Assert.Equal(9_984, navigator.Items[0]);
        Assert.Equal(9_999, navigator.Items[^1]);
        Assert.Equal("Showing 9985-10000 of 10000", navigator.PageStatus);
        Assert.Equal(80, projectedRowCount);

        navigator.ShowItemAt(9_998);
        Assert.Equal(80, projectedRowCount);
    }

    /// <summary>The frozen bilingual assertions use injected labels and deferred navigation.</summary>
    [Fact]
    public void NavigationLabelsFollowTheInjectedLabels()
    {
        var navigator = ReportWindowedListViewModel.Create(
            ReportListTestData.OneRow, 64, ReportListTestData.Chinese, loadInitialPage: false);

        Assert.Equal("上一頁", navigator.PreviousPageLabel);
        Assert.Equal("下一頁", navigator.NextPageLabel);
        Assert.Equal("沒有項目", navigator.PageStatus);
        Assert.Equal(0, navigator.VisibleCount);
        Assert.False(navigator.HasMultiplePages);
        Assert.True(navigator.NextPageCommand.CanExecute(null));

        navigator.NextPageCommand.Execute(null);

        Assert.Equal("顯示第 1-1 筆，共 1 筆", navigator.PageStatus);
        Assert.Equal(1, navigator.VisibleCount);
        Assert.False(navigator.NextPageCommand.CanExecute(null));
    }

    /// <summary>Empty sources remain empty with either loading choice and commands produce no notifications.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptySourceHasNoPages(bool loadInitialPage)
    {
        var navigator = ReportWindowedListViewModel.Create(
            Array.Empty<object>(), 1, ReportListTestData.Custom, loadInitialPage);
        List<string> notifications = Observe(navigator);

        navigator.NextPageCommand.Execute(null);
        navigator.PreviousPageCommand.Execute(null);

        Assert.Empty(navigator.Items);
        Assert.Equal(0, navigator.TotalCount);
        Assert.Equal(0, navigator.VisibleCount);
        Assert.Equal(0, navigator.PageIndex);
        Assert.Equal(0, navigator.PageCount);
        Assert.False(navigator.HasPreviousPage);
        Assert.False(navigator.HasNextPage);
        Assert.False(navigator.HasMultiplePages);
        Assert.False(navigator.NextPageCommand.CanExecute(null));
        Assert.False(navigator.PreviousPageCommand.CanExecute(null));
        Assert.Equal("vacant", navigator.PageStatus);
        Assert.Empty(notifications);
    }

    /// <summary>Construction and status queries never enumerate the source; deferred construction reads no rows.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public void ConstructionOnlyReadsTheRequestedWindow(bool loadInitialPage, int reads)
    {
        var source = new TrackingReadOnlyList<int>(5, static index => index);
        var navigator = ReportWindowedListViewModel.Create(source, 2, ReportListTestData.Custom, loadInitialPage);

        Assert.Equal(5, navigator.TotalCount);
        Assert.Equal(3, navigator.PageCount);
        Assert.True(navigator.HasNextPage);
        Assert.Equal(reads, navigator.VisibleCount);
        Assert.Equal(reads == 0 ? "vacant" : "window(1,2,5)", navigator.PageStatus);
        Assert.Equal(reads, source.Reads.Count);
        Assert.Equal(0, source.EnumerationCount);

        if (!loadInitialPage)
        {
            navigator.ShowItemAt(4);
            Assert.Equal(4, Assert.Single(source.Reads));
            Assert.Equal(2, navigator.PageIndex);
            Assert.Equal(4, Assert.Single(navigator.Items));
        }

        Assert.Equal(0, source.EnumerationCount);
    }

    /// <summary>Positive page-size validation preserves zero, negative and representable maximum boundaries.</summary>
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
                ReportWindowedListViewModel.Create(ReportListTestData.OneRow, pageSize, ReportListTestData.Custom));
            Assert.Equal("pageSize", error.ParamName);
            Assert.Equal(pageSize, error.ActualValue);
        }
        else
        {
            var navigator = ReportWindowedListViewModel.Create(ReportListTestData.OneRow, pageSize, ReportListTestData.Custom);
            Assert.Equal("row", Assert.Single(navigator.Items));
            Assert.Equal(1, navigator.PageCount);
        }
    }

    /// <summary>Items validation precedes page-size validation, which precedes the new labels validation.</summary>
    [Fact]
    public void ArgumentValidationKeepsTheFrozenCheckOrder()
    {
        ArgumentNullException itemsError = Assert.Throws<ArgumentNullException>(() =>
            ReportWindowedListViewModel.Create<object>(null!, 0, null!));
        Assert.Equal("items", itemsError.ParamName);
        ArgumentOutOfRangeException sizeError = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReportWindowedListViewModel.Create(ReportListTestData.OneRow, 0, null!));
        Assert.Equal("pageSize", sizeError.ParamName);
        ArgumentNullException labelsError = Assert.Throws<ArgumentNullException>(() =>
            ReportWindowedListViewModel.Create(ReportListTestData.OneRow, 1, null!));
        Assert.Equal("labels", labelsError.ParamName);
    }

    /// <summary>Exact page boundaries, their neighbours, and page size one retain correct final windows.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(63, 64)]
    [InlineData(64, 64)]
    [InlineData(65, 64)]
    [InlineData(127, 64)]
    [InlineData(128, 64)]
    [InlineData(129, 64)]
    public void LastWindowMatchesExactPageBoundaries(int count, int pageSize)
    {
        var source = new FactoryReadOnlyList<int>(count, static index => index);
        var navigator = ReportWindowedListViewModel.Create(source, pageSize, ReportListTestData.Custom);
        navigator.ShowItemAt(count - 1);
        int pageIndex = (count - 1) / pageSize;
        int start = pageIndex * pageSize;

        Assert.Equal(pageIndex + 1, navigator.PageCount);
        Assert.Equal(pageIndex, navigator.PageIndex);
        Assert.Equal(count - start, navigator.VisibleCount);
        Assert.Equal(Enumerable.Range(start, count - start).Cast<object>(), navigator.Items);
        Assert.Equal($"window({start + 1},{count},{count})", navigator.PageStatus);
        Assert.Equal(pageIndex > 0, navigator.HasPreviousPage);
        Assert.False(navigator.HasNextPage);
        Assert.Equal(pageIndex > 0, navigator.HasMultiplePages);
    }

    /// <summary>The unsigned index predicate rejects both ends without mutating state or notifying.</summary>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void InvalidIndexLeavesTheWindowUnchanged(int index)
    {
        var source = new TrackingReadOnlyList<int>(3, static item => item);
        var navigator = ReportWindowedListViewModel.Create(source, 2, ReportListTestData.Custom);
        List<string> notifications = Observe(navigator);
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => navigator.ShowItemAt(index));

        Assert.Equal("index", error.ParamName);
        Assert.Null(error.ActualValue);
        Assert.Equal(0, navigator.PageIndex);
        Assert.Equal(2, navigator.VisibleCount);
        Assert.Equal(2, source.Reads.Count);
        Assert.Empty(notifications);
    }

    /// <summary>Both valid index boundaries and the page-boundary neighbours resolve their containing page.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void ValidIndexBoundaryShowsItsContainingPage(int index, int pageIndex)
    {
        var source = new FactoryReadOnlyList<int>(3, static item => item);
        var navigator = ReportWindowedListViewModel.Create(source, 2, ReportListTestData.Custom, false);
        navigator.ShowItemAt(index);
        Assert.Equal(pageIndex, navigator.PageIndex);
        Assert.Contains(index, navigator.Items);
    }

    /// <summary>An empty list has no valid index, including zero and its neighbours.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void EmptySourceRejectsEveryIndex(int index)
    {
        var navigator = ReportWindowedListViewModel.Create(Array.Empty<int>(), 1, ReportListTestData.Custom);
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => navigator.ShowItemAt(index));
        Assert.Equal("index", error.ParamName);
    }

    /// <summary>Replacement publishes complete state before ordered property and command notifications.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageChangesKeepExactNotificationOrder(bool deferred)
    {
        var source = new FactoryReadOnlyList<int>(3, static index => index);
        var navigator = ReportWindowedListViewModel.Create(source, 2, ReportListTestData.Custom, !deferred);
        List<string> notifications = Observe(navigator);
        int expectedIndex = deferred ? 0 : 1;
        int expectedVisible = deferred ? 2 : 1;
        ((INotifyPropertyChanged)navigator.Items).PropertyChanged += (_, _) =>
        {
            Assert.Equal(expectedIndex, navigator.PageIndex);
            Assert.Equal(expectedVisible, navigator.VisibleCount);
        };

        navigator.NextPageCommand.Execute(null);
        Assert.Equal(PageNotifications, notifications);

        notifications.Clear();
        navigator.ShowItemAt(deferred ? 1 : 2);
        Assert.Empty(notifications);

        if (!deferred)
        {
            expectedIndex = 0;
            expectedVisible = 2;
            navigator.PreviousPageCommand.Execute(null);
            Assert.Equal(PageNotifications, notifications);
        }
    }

    /// <summary>Disabled boundary commands are harmless and current-page reselection preserves item identity.</summary>
    [Fact]
    public void CommandsAndReselectionRetainCollectionAndRowIdentity()
    {
        object[] rows = [new(), new(), new()];
        var source = new TrackingReadOnlyList<object>(rows.Length, index => rows[index]);
        var navigator = ReportWindowedListViewModel.Create(source, 2, ReportListTestData.Custom);
        object collection = navigator.Items;
        object previous = navigator.PreviousPageCommand;
        object next = navigator.NextPageCommand;
        List<string> notifications = Observe(navigator);

        navigator.ShowItemAt(0);
        navigator.ShowItemAt(1);
        navigator.PreviousPageCommand.Execute(null);
        Assert.Empty(notifications);
        Assert.Equal(2, source.Reads.Count);
        Assert.Same(rows[0], navigator.Items[0]);
        Assert.Same(rows[1], navigator.Items[1]);

        navigator.NextPageCommand.Execute(null);
        Assert.Same(rows[2], Assert.Single(navigator.Items));
        notifications.Clear();
        navigator.NextPageCommand.Execute(null);
        Assert.Empty(notifications);
        navigator.PreviousPageCommand.Execute(null);
        Assert.Same(rows[0], navigator.Items[0]);
        Assert.Same(collection, navigator.Items);
        Assert.Same(previous, navigator.PreviousPageCommand);
        Assert.Same(next, navigator.NextPageCommand);
        Assert.Equal(0, source.EnumerationCount);
    }

    /// <summary>The adapter retains the source, so deferred loading sees replacements without copying or enumeration.</summary>
    [Fact]
    public void DeferredModelRetainsTheTypedSource()
    {
        object[] rows = [new()];
        var navigator = ReportWindowedListViewModel.Create(rows, 1, ReportListTestData.Custom, false);
        rows[0] = new object();
        navigator.NextPageCommand.Execute(null);
        Assert.Same(rows[0], Assert.Single(navigator.Items));
        Assert.Equal("rewind", navigator.PreviousPageLabel);
        Assert.Equal("advance", navigator.NextPageLabel);
        Assert.Equal("window(1,1,1)", navigator.PageStatus);
    }

    /// <summary>Page count supports the full integer domain without materializing its rows.</summary>
    [Theory]
    [InlineData(int.MaxValue - 1, 1, int.MaxValue - 1)]
    [InlineData(int.MaxValue, 1, int.MaxValue)]
    [InlineData(int.MaxValue, 2, 1_073_741_824)]
    [InlineData(int.MaxValue, int.MaxValue - 1, 2)]
    [InlineData(int.MaxValue, int.MaxValue, 1)]
    public void PageCountPreservesCheckedIntegerMath(int count, int pageSize, int pageCount)
    {
        var source = new TrackingReadOnlyList<int>(count, static index => index);
        var navigator = ReportWindowedListViewModel.Create(source, pageSize, ReportListTestData.Custom, false);
        Assert.Equal(pageCount, navigator.PageCount);
        Assert.Empty(source.Reads);
        Assert.Equal(0, source.EnumerationCount);
    }

    /// <summary>End addition and status intermediate addition retain their exact overflow boundaries.</summary>
    [Fact]
    public void LastRepresentableWindowPreservesOverflowBeforeClamping()
    {
        var source = new TrackingReadOnlyList<int>(int.MaxValue, static index => index);
        var navigator = ReportWindowedListViewModel.Create(source, 2, ReportListTestData.Custom, false);
        navigator.ShowItemAt(int.MaxValue - 3);
        Assert.Equal(2, navigator.VisibleCount);
        Assert.Equal($"window({int.MaxValue - 2},{int.MaxValue - 1},{int.MaxValue})", navigator.PageStatus);
        List<string> notifications = Observe(navigator);

        Assert.Throws<OverflowException>(() => navigator.ShowItemAt(int.MaxValue - 1));
        Assert.Equal(2, source.Reads.Count);
        Assert.Equal((int.MaxValue - 3) / 2, navigator.PageIndex);
        Assert.Empty(notifications);

        var single = ReportWindowedListViewModel.Create(source, 1, ReportListTestData.Custom, false);
        single.ShowItemAt(int.MaxValue - 2);
        Assert.Equal($"window({int.MaxValue - 1},{int.MaxValue - 1},{int.MaxValue})", single.PageStatus);
        single.ShowItemAt(int.MaxValue - 1);
        Assert.Equal(int.MaxValue - 1, Assert.Single(single.Items));
        Assert.Throws<OverflowException>(() => single.PageStatus);
    }

    /// <summary>Row creation failure occurs before replacing the window or publishing a new page position.</summary>
    [Fact]
    public void SourceFailureLeavesThePreviousWindowIntact()
    {
        var failure = new InvalidOperationException("Synthetic row failure.");
        var source = new FactoryReadOnlyList<int>(4, index => index == 3 ? throw failure : index);
        var navigator = ReportWindowedListViewModel.Create(source, 2, ReportListTestData.Custom);
        List<string> notifications = Observe(navigator);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => navigator.NextPageCommand.Execute(null)));
        Assert.Equal(0, navigator.PageIndex);
        Assert.Equal(Enumerable.Range(0, 2).Cast<object>(), navigator.Items);
        Assert.Empty(notifications);
    }

    /// <summary>The status intermediate sum is checked below, at and above the integer maximum.</summary>
    [Theory]
    [InlineData(int.MaxValue - 3, false)]
    [InlineData(int.MaxValue - 2, false)]
    [InlineData(int.MaxValue - 1, true)]
    public void StatusAdditionKeepsItsIntermediateOverflowBoundary(int index, bool overflows)
    {
        var source = new FactoryReadOnlyList<int>(int.MaxValue, static item => item);
        var navigator = ReportWindowedListViewModel.Create(source, 1, ReportListTestData.Custom, false);
        navigator.ShowItemAt(index);
        Assert.Equal(index, Assert.Single(navigator.Items));
        if (overflows)
        {
            Assert.Throws<OverflowException>(() => navigator.PageStatus);
        }
        else
        {
            Assert.Equal($"window({index + 1},{index + 1},{int.MaxValue})", navigator.PageStatus);
        }
    }

    /// <summary>Collection observers see the new page and rows even when they stop later model notifications.</summary>
    [Fact]
    public void ObserverFailureKeepsThePublishedWindowAndPageIndex()
    {
        var source = new FactoryReadOnlyList<int>(3, static index => index);
        var navigator = ReportWindowedListViewModel.Create(source, 2, ReportListTestData.Custom);
        List<string> notifications = Observe(navigator);
        var failure = new InvalidOperationException("Synthetic observer failure.");
        ((INotifyCollectionChanged)navigator.Items).CollectionChanged += (_, _) => throw failure;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => navigator.NextPageCommand.Execute(null)));

        Assert.Equal(1, navigator.PageIndex);
        Assert.Equal(2, Assert.Single(navigator.Items));
        Assert.Equal(PageNotifications.Take(3), notifications);
        Assert.False(navigator.NextPageCommand.CanExecute(null));
        Assert.True(navigator.PreviousPageCommand.CanExecute(null));
    }

    private static List<string> Observe(ReportWindowedListViewModel navigator)
    {
        var notifications = new List<string>();
        ((INotifyPropertyChanged)navigator.Items).PropertyChanged += (_, args) => notifications.Add($"items:{args.PropertyName}");
        ((INotifyCollectionChanged)navigator.Items).CollectionChanged += (_, args) =>
        {
            Assert.Equal(NotifyCollectionChangedAction.Reset, args.Action);
            Assert.Null(args.NewItems);
            Assert.Null(args.OldItems);
            Assert.Equal(-1, args.NewStartingIndex);
            Assert.Equal(-1, args.OldStartingIndex);
            notifications.Add($"items:{args.Action}");
        };
        navigator.PropertyChanged += (_, args) => notifications.Add($"model:{args.PropertyName}");
        navigator.PropertyChanging += (_, args) => notifications.Add($"model:changing:{args.PropertyName}");
        navigator.PreviousPageCommand.CanExecuteChanged += (_, _) => notifications.Add("command:previous");
        navigator.NextPageCommand.CanExecuteChanged += (_, _) => notifications.Add("command:next");
        return notifications;
    }
}
