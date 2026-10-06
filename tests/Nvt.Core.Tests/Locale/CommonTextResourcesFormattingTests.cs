// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Text;
using Nvt.Core.Locale;
using Xunit;

namespace Nvt.Core.Tests.Locale;

/// <summary>Characterizes frozen paging text with synthetic counts and integer boundaries.</summary>
public sealed class CommonTextResourcesFormattingTests
{
    /// <summary>Ports the window navigator's bilingual unloaded and one-item text assertions.</summary>
    [Fact]
    public void NavigationLabelsFollowTheSelectedLanguage()
    {
        CommonTextResources english = CommonTextResources.For(CommonLanguage.English);
        CommonTextResources traditionalChinese = CommonTextResources.For(CommonLanguage.TraditionalChinese);

        AssertText("Previous page", english.PreviousPageLabel);
        AssertText("Next page", english.NextPageLabel);
        AssertText("No items", english.NoItemsLabel);
        AssertText("上一頁", traditionalChinese.PreviousPageLabel);
        AssertText("下一頁", traditionalChinese.NextPageLabel);
        AssertText("沒有項目", traditionalChinese.NoItemsLabel);
        AssertText("All items loaded", english.AllItemsLoadedLabel);
        AssertText("已載入全部項目", traditionalChinese.AllItemsLoadedLabel);
        AssertBoth(static resources => resources.FormatWindowStatus(1, 1, 1),
            "Showing 1-1 of 1", "顯示第 1-1 筆，共 1 筆");
    }

    /// <summary>Preserves window literals for frozen examples and unvalidated edge arguments.</summary>
    /// <param name="first">The first position.</param>
    /// <param name="last">The last position.</param>
    /// <param name="total">The total count.</param>
    /// <param name="english">The expected English text.</param>
    /// <param name="traditionalChinese">The expected Traditional Chinese text.</param>
    [Theory]
    [InlineData(0, 0, 0, "Showing 0-0 of 0", "顯示第 0-0 筆，共 0 筆")]
    [InlineData(1, 1, 1, "Showing 1-1 of 1", "顯示第 1-1 筆，共 1 筆")]
    [InlineData(1, 63, 63, "Showing 1-63 of 63", "顯示第 1-63 筆，共 63 筆")]
    [InlineData(1, 64, 64, "Showing 1-64 of 64", "顯示第 1-64 筆，共 64 筆")]
    [InlineData(1, 64, 65, "Showing 1-64 of 65", "顯示第 1-64 筆，共 65 筆")]
    [InlineData(65, 65, 65, "Showing 65-65 of 65", "顯示第 65-65 筆，共 65 筆")]
    [InlineData(1, 64, 130, "Showing 1-64 of 130", "顯示第 1-64 筆，共 130 筆")]
    [InlineData(65, 128, 130, "Showing 65-128 of 130", "顯示第 65-128 筆，共 130 筆")]
    [InlineData(129, 130, 130, "Showing 129-130 of 130", "顯示第 129-130 筆，共 130 筆")]
    [InlineData(9985, 10000, 10000, "Showing 9985-10000 of 10000", "顯示第 9985-10000 筆，共 10000 筆")]
    [InlineData(10001, 10001, 10001, "Showing 10001-10001 of 10001", "顯示第 10001-10001 筆，共 10001 筆")]
    [InlineData(2, 1, -1, "Showing 2-1 of -1", "顯示第 2-1 筆，共 -1 筆")]
    [InlineData(int.MinValue, int.MaxValue, 0,
        "Showing -2147483648-2147483647 of 0", "顯示第 -2147483648-2147483647 筆，共 0 筆")]
    public void WindowStatusMatchesFrozenInterpolation(
        int first, int last, int total, string english, string traditionalChinese)
    {
        AssertBoth(resources => resources.FormatWindowStatus(first, last, total), english, traditionalChinese);
    }

    /// <summary>Preserves paged-status literals for empty, bounded, complete and invalid counts.</summary>
    /// <param name="visible">The visible count.</param>
    /// <param name="total">The total count.</param>
    /// <param name="english">The expected English text.</param>
    /// <param name="traditionalChinese">The expected Traditional Chinese text.</param>
    [Theory]
    [InlineData(0, 0, "Showing 0/0", "已顯示 0/0 筆")]
    [InlineData(0, 1, "Showing 0/1", "已顯示 0/1 筆")]
    [InlineData(1, 1, "Showing 1/1", "已顯示 1/1 筆")]
    [InlineData(63, 63, "Showing 63/63", "已顯示 63/63 筆")]
    [InlineData(64, 64, "Showing 64/64", "已顯示 64/64 筆")]
    [InlineData(64, 65, "Showing 64/65", "已顯示 64/65 筆")]
    [InlineData(65, 65, "Showing 65/65", "已顯示 65/65 筆")]
    [InlineData(64, 130, "Showing 64/130", "已顯示 64/130 筆")]
    [InlineData(128, 130, "Showing 128/130", "已顯示 128/130 筆")]
    [InlineData(130, 130, "Showing 130/130", "已顯示 130/130 筆")]
    [InlineData(64, 10000, "Showing 64/10000", "已顯示 64/10000 筆")]
    [InlineData(10000, 10000, "Showing 10000/10000", "已顯示 10000/10000 筆")]
    [InlineData(10001, 10001, "Showing 10001/10001", "已顯示 10001/10001 筆")]
    [InlineData(2, 1, "Showing 2/1", "已顯示 2/1 筆")]
    [InlineData(-1, -2, "Showing -1/-2", "已顯示 -1/-2 筆")]
    [InlineData(int.MinValue, int.MaxValue,
        "Showing -2147483648/2147483647", "已顯示 -2147483648/2147483647 筆")]
    public void PagedStatusMatchesFrozenInterpolation(
        int visible, int total, string english, string traditionalChinese)
    {
        AssertBoth(resources => resources.FormatPagedStatus(visible, total), english, traditionalChinese);
    }

    /// <summary>Preserves load-more punctuation and supplied counts without introducing a limit.</summary>
    /// <param name="next">The next count.</param>
    /// <param name="remaining">The remaining count.</param>
    /// <param name="english">The expected English text.</param>
    /// <param name="traditionalChinese">The expected Traditional Chinese text.</param>
    [Theory]
    [InlineData(0, 0, "Load 0 more (0 remaining)", "再載入 0 筆（尚餘 0 筆）")]
    [InlineData(1, 1, "Load 1 more (1 remaining)", "再載入 1 筆（尚餘 1 筆）")]
    [InlineData(63, 63, "Load 63 more (63 remaining)", "再載入 63 筆（尚餘 63 筆）")]
    [InlineData(64, 64, "Load 64 more (64 remaining)", "再載入 64 筆（尚餘 64 筆）")]
    [InlineData(64, 65, "Load 64 more (65 remaining)", "再載入 64 筆（尚餘 65 筆）")]
    [InlineData(65, 65, "Load 65 more (65 remaining)", "再載入 65 筆（尚餘 65 筆）")]
    [InlineData(64, 66, "Load 64 more (66 remaining)", "再載入 64 筆（尚餘 66 筆）")]
    [InlineData(2, 2, "Load 2 more (2 remaining)", "再載入 2 筆（尚餘 2 筆）")]
    [InlineData(64, 9936, "Load 64 more (9936 remaining)", "再載入 64 筆（尚餘 9936 筆）")]
    [InlineData(64, 10000, "Load 64 more (10000 remaining)", "再載入 64 筆（尚餘 10000 筆）")]
    [InlineData(64, 10001, "Load 64 more (10001 remaining)", "再載入 64 筆（尚餘 10001 筆）")]
    [InlineData(2, 1, "Load 2 more (1 remaining)", "再載入 2 筆（尚餘 1 筆）")]
    [InlineData(-1, -2, "Load -1 more (-2 remaining)", "再載入 -1 筆（尚餘 -2 筆）")]
    [InlineData(int.MinValue, int.MaxValue,
        "Load -2147483648 more (2147483647 remaining)", "再載入 -2147483648 筆（尚餘 2147483647 筆）")]
    public void LoadMoreMatchesFrozenInterpolation(
        int next, int remaining, string english, string traditionalChinese)
    {
        AssertBoth(resources => resources.FormatLoadMore(next, remaining), english, traditionalChinese);
    }

    private static void AssertBoth(
        Func<CommonTextResources, string> format, string english, string traditionalChinese)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            AssertText(english, format(CommonTextResources.For(CommonLanguage.English)));
            AssertText(traditionalChinese, format(CommonTextResources.For(CommonLanguage.TraditionalChinese)));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static void AssertText(string expected, string actual)
    {
        Assert.Equal(expected, actual, StringComparer.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }
}
