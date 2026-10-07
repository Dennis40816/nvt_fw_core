// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Avalonia.ReportList;

/// <summary>Immutable host-supplied labels and status formatters for list paging.</summary>
/// <param name="NoItems">Status for a window with no visible items.</param>
/// <param name="PreviousPage">Label for the previous-page command.</param>
/// <param name="NextPage">Label for the next-page command.</param>
/// <param name="AllItemsLoaded">Load-more label when no items remain.</param>
/// <param name="WindowStatus">Formats the first and last one-based visible positions and total count.</param>
/// <param name="PagedStatus">Formats the visible count and total count.</param>
/// <param name="LoadMore">Formats the next load count and remaining count.</param>
/// <remarks>Supply stable formatters. Models reject any null member at construction and do not change or relocalize these labels.</remarks>
public sealed record ReportListLabels(
    string NoItems,
    string PreviousPage,
    string NextPage,
    string AllItemsLoaded,
    Func<int, int, int, string> WindowStatus,
    Func<int, int, string> PagedStatus,
    Func<int, int, string> LoadMore)
{
    /// <summary>Rejects null labels and formatters, including members cleared by a <c>with</c> copy.</summary>
    internal void EnsureComplete()
    {
        ArgumentNullException.ThrowIfNull(NoItems);
        ArgumentNullException.ThrowIfNull(PreviousPage);
        ArgumentNullException.ThrowIfNull(NextPage);
        ArgumentNullException.ThrowIfNull(AllItemsLoaded);
        ArgumentNullException.ThrowIfNull(WindowStatus);
        ArgumentNullException.ThrowIfNull(PagedStatus);
        ArgumentNullException.ThrowIfNull(LoadMore);
    }
}
