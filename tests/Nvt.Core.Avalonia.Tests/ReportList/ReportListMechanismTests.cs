// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Avalonia.ReportList;
using Nvt.Core.ReportList;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.ReportList;

/// <summary>Synthetic equivalents of frozen bounded, deferred and memoized paging assertions.</summary>
public sealed class ReportListMechanismTests
{
    /// <summary>The frozen 1,000-row, 40-section mechanism retains 8-row summaries and deferred 24-row details.</summary>
    [Fact]
    public void LargeIndexedProjectionUsesBoundedDeferredAndMemoizedPages()
    {
        const int differenceCount = 1_000;
        const int sectionCount = 40;
        var rows = new MemoizedIndexedReadOnlyList<SyntheticRow>(
            differenceCount, static index => new SyntheticRow($"diff-{index:D5}"));
        SyntheticGroup[] groups = [.. Enumerable.Range(0, sectionCount).Select(index =>
        {
            var selectedRows = new IndexedReadOnlyList<SyntheticRow>(
                rows, [.. Enumerable.Range(index * 25, 25)]);
            return new SyntheticGroup($"Section {index:D2}", selectedRows,
                ReportPagedListViewModel.Create(selectedRows, 24, ReportListTestData.English, false));
        })];
        string[] expectedSectionOrder = [.. Enumerable.Range(0, sectionCount).Select(index => $"Section {index:D2}")];
        var groupPage = ReportPagedListViewModel.Create(groups, 8, ReportListTestData.English);
        var summaryPage = ReportPagedListViewModel.Create(expectedSectionOrder, 8, ReportListTestData.English);

        Assert.Equal(differenceCount, rows.Count);
        Assert.Equal(0, rows.MaterializedCount);
        Assert.Equal(sectionCount, groups.Length);
        Assert.Equal(expectedSectionOrder, groups.Select(static group => group.Title));
        Assert.Equal(expectedSectionOrder.Take(8).Cast<object>(), summaryPage.Items);
        Assert.Equal(8, groupPage.VisibleCount);
        Assert.Equal(8, summaryPage.VisibleCount);
        Assert.True(groupPage.HasMoreItems);

        SyntheticGroup firstGroup = Assert.IsType<SyntheticGroup>(groupPage.Items[0]);
        Assert.Equal("Section 00", firstGroup.Title);
        Assert.Equal(25, firstGroup.RowsPage.TotalCount);
        Assert.Equal(0, firstGroup.RowsPage.VisibleCount);
        Assert.Equal(0, rows.MaterializedCount);
        firstGroup.RowsPage.EnsureInitialPage();
        Assert.Equal(24, firstGroup.RowsPage.VisibleCount);
        Assert.Equal(24, rows.MaterializedCount);
        SyntheticRow firstRow = Assert.IsType<SyntheticRow>(firstGroup.RowsPage.Items[0]);
        Assert.Equal("diff-00000", firstRow.Title);
        Assert.Same(firstRow, firstGroup.Rows[0]);
        Assert.Same(firstRow, rows[0]);
        Assert.Equal(24, rows.MaterializedCount);
        firstGroup.RowsPage.EnsureInitialPage();
        firstGroup.RowsPage.EnsureInitialPage();
        Assert.Equal(24, firstGroup.RowsPage.VisibleCount);
        Assert.Equal(24, rows.MaterializedCount);
        Assert.True(firstGroup.RowsPage.LoadMoreCommand.CanExecute(null));
        firstGroup.RowsPage.LoadMoreCommand.Execute(null);
        Assert.Equal(25, firstGroup.RowsPage.VisibleCount);
        Assert.Equal(25, rows.MaterializedCount);
        Assert.False(firstGroup.RowsPage.HasMoreItems);
        Assert.False(firstGroup.RowsPage.LoadMoreCommand.CanExecute(null));
        Assert.Equal("Showing 25/25", firstGroup.RowsPage.PageStatus);
        Assert.Equal("All items loaded", firstGroup.RowsPage.LoadMoreLabel);

        groupPage.LoadMoreCommand.Execute(null);
        Assert.Equal(16, groupPage.VisibleCount);
        Assert.Equal(differenceCount, rows.Count);
        Assert.Equal(25, rows.MaterializedCount);
    }

    /// <summary>Windowed revisits reuse memoized rows while non-retained factories create only each current window.</summary>
    [Fact]
    public void WindowNavigationUsesTheSharedCollectionOwners()
    {
        var memoized = new MemoizedIndexedReadOnlyList<object>(130, static _ => new object());
        var navigator = ReportWindowedListViewModel.Create(memoized, 64, ReportListTestData.Custom);
        object first = navigator.Items[0];
        Assert.Equal(64, memoized.MaterializedCount);
        navigator.ShowItemAt(129);
        Assert.Equal(66, memoized.MaterializedCount);
        Assert.Equal(2, navigator.VisibleCount);
        navigator.ShowItemAt(0);
        Assert.Same(first, navigator.Items[0]);
        Assert.Equal(66, memoized.MaterializedCount);
    }

    /// <summary>Null source elements pass through the internal object adapter just as value and reference rows do.</summary>
    [Fact]
    public void BothModelsPreserveNullItems()
    {
        string?[] rows = [null, "row"];
        var window = ReportWindowedListViewModel.Create(rows, 2, ReportListTestData.Custom);
        var page = ReportPagedListViewModel.Create(rows, 1, ReportListTestData.Custom);
        page.LoadMoreCommand.Execute(null);
        Assert.Null(window.Items[0]);
        Assert.Null(page.Items[0]);
        Assert.Same(rows[1], window.Items[1]);
        Assert.Same(rows[1], page.Items[1]);
    }

    /// <summary>A copied labels record affects new models only; existing models retain their immutable label set.</summary>
    [Fact]
    public void LabelsAreAnImmutableConstructionChoice()
    {
        ReportListLabels original = ReportListTestData.Custom;
        var window = ReportWindowedListViewModel.Create(ReportListTestData.OneRow, 1, original, false);
        var page = ReportPagedListViewModel.Create(Array.Empty<string>(), 1, original);
        ReportListLabels changed = original with { NoItems = "different", AllItemsLoaded = "finished" };

        Assert.Equal("vacant", window.PageStatus);
        Assert.Equal("complete", page.LoadMoreLabel);
        var newWindow = ReportWindowedListViewModel.Create(ReportListTestData.OneRow, 1, changed, false);
        var newPage = ReportPagedListViewModel.Create(Array.Empty<string>(), 1, changed);
        Assert.Equal("different", newWindow.PageStatus);
        Assert.Equal("finished", newPage.LoadMoreLabel);
    }

    /// <summary>Both models reject a labels copy whose label or formatter member is null.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ModelsRejectNullLabelMembersAtConstruction(int member)
    {
        ReportListLabels incomplete = WithNullMember(ReportListTestData.Custom, member);

        Assert.Throws<ArgumentNullException>(() =>
            ReportWindowedListViewModel.Create(ReportListTestData.OneRow, 1, incomplete, false));
        Assert.Throws<ArgumentNullException>(() =>
            ReportPagedListViewModel.Create(ReportListTestData.OneRow, 1, incomplete));
    }

    private static ReportListLabels WithNullMember(ReportListLabels labels, int member) => member switch
    {
        0 => labels with { NoItems = null! },
        1 => labels with { PreviousPage = null! },
        2 => labels with { NextPage = null! },
        3 => labels with { AllItemsLoaded = null! },
        4 => labels with { WindowStatus = null! },
        5 => labels with { PagedStatus = null! },
        6 => labels with { LoadMore = null! },
        _ => throw new ArgumentOutOfRangeException(nameof(member)),
    };

    private sealed record SyntheticRow(string Title);

    private sealed record SyntheticGroup(
        string Title,
        IReadOnlyList<SyntheticRow> Rows,
        ReportPagedListViewModel RowsPage);
}
