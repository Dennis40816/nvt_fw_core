// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections;
using Nvt.Core.Avalonia.ReportList;

namespace Nvt.Core.Avalonia.Tests.ReportList;

internal static class ReportListTestData
{
    internal static readonly string[] OneRow = ["row"];

    internal static ReportListLabels English { get; } = new(
        "No items", "Previous page", "Next page", "All items loaded",
        static (first, last, total) => $"Showing {first}-{last} of {total}",
        static (visible, total) => $"Showing {visible}/{total}",
        static (next, remaining) => $"Load {next} more ({remaining} remaining)");

    internal static ReportListLabels Chinese { get; } = new(
        "沒有項目", "上一頁", "下一頁", "已載入全部項目",
        static (first, last, total) => $"顯示第 {first}-{last} 筆，共 {total} 筆",
        static (visible, total) => $"已顯示 {visible}/{total} 筆",
        static (next, remaining) => $"再載入 {next} 筆（尚餘 {remaining} 筆）");

    internal static ReportListLabels Custom { get; } = new(
        "vacant", "rewind", "advance", "complete",
        static (first, last, total) => $"window({first},{last},{total})",
        static (visible, total) => $"prefix({visible},{total})",
        static (next, remaining) => $"append({next},{remaining})");
}

internal sealed class TrackingReadOnlyList<T>(int count, Func<int, T> factory) : IReadOnlyList<T>
{
    // Test-thread-only mutable instrumentation; the lists are not shared between tests.
    private int _enumerationCount;
    private readonly List<int> _reads = [];

    public int Count { get; } = count;

    internal int EnumerationCount => _enumerationCount;

    internal IReadOnlyList<int> Reads => _reads;

    public T this[int index]
    {
        get
        {
            _reads.Add(index);
            return factory(index);
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        _enumerationCount++;
        throw new InvalidOperationException("Synthetic source must not be enumerated.");
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
