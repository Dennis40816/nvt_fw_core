// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Nvt.Core.Locale;
using Xunit;

namespace Nvt.Core.Tests.Locale;

/// <summary>Checks the frozen current-culture interpolation independently of bundle caching.</summary>
public sealed class CommonTextResourcesCultureTests
{
    /// <summary>Compares the three formatters with the frozen expressions in several cultures.</summary>
    /// <param name="cultureName">The number-formatting culture name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("en-US")]
    [InlineData("zh-TW")]
    [InlineData("fr-FR")]
    [InlineData("ar-EG")]
    public void FormattersMatchFrozenCurrentCultureExpressions(string cultureName)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-TW");
            CommonTextResources english = CommonTextResources.For(CommonLanguage.English);
            CommonTextResources traditionalChinese = CommonTextResources.For(CommonLanguage.TraditionalChinese);
            const int first = -1;
            const int last = int.MinValue;
            const int total = int.MaxValue;

            Assert.Equal($"Showing {first}-{last} of {total}", english.FormatWindowStatus(first, last, total));
            Assert.Equal($"顯示第 {first}-{last} 筆，共 {total} 筆", traditionalChinese.FormatWindowStatus(first, last, total));
            Assert.Equal($"Showing {last}/{total}", english.FormatPagedStatus(last, total));
            Assert.Equal($"已顯示 {last}/{total} 筆", traditionalChinese.FormatPagedStatus(last, total));
            Assert.Equal($"Load {last} more ({total} remaining)", english.FormatLoadMore(last, total));
            Assert.Equal($"再載入 {last} 筆（尚餘 {total} 筆）", traditionalChinese.FormatLoadMore(last, total));
            Assert.Equal("Home", english.HomeLabel);
            Assert.Equal("首頁", traditionalChinese.HomeLabel);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    /// <summary>Detects invariant formatting or a culture captured when a bundle was created.</summary>
    [Fact]
    public void CachedBundlesUseTheCultureAtEachFormattingCall()
    {
        CommonTextResources english = CommonTextResources.For(CommonLanguage.English);
        CommonTextResources traditionalChinese = CommonTextResources.For(CommonLanguage.TraditionalChinese);
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "minus:";
        culture.NumberFormat.NumberGroupSeparator = "*";
        try
        {
            CultureInfo.CurrentCulture = culture;

            Assert.Equal("Showing minus:1-minus:2 of 10000", english.FormatWindowStatus(-1, -2, 10000));
            Assert.Equal("顯示第 minus:1-minus:2 筆，共 10000 筆", traditionalChinese.FormatWindowStatus(-1, -2, 10000));
            Assert.Equal("Showing minus:1/10000", english.FormatPagedStatus(-1, 10000));
            Assert.Equal("已顯示 minus:1/10000 筆", traditionalChinese.FormatPagedStatus(-1, 10000));
            Assert.Equal("Load minus:1 more (minus:2 remaining)", english.FormatLoadMore(-1, -2));
            Assert.Equal("再載入 minus:1 筆（尚餘 minus:2 筆）", traditionalChinese.FormatLoadMore(-1, -2));

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Same(english, CommonTextResources.For(CommonLanguage.English));
            Assert.Same(traditionalChinese, CommonTextResources.For(CommonLanguage.TraditionalChinese));
            Assert.Equal("Showing -1--2 of 10000", english.FormatWindowStatus(-1, -2, 10000));
            Assert.Equal("顯示第 -1--2 筆，共 10000 筆", traditionalChinese.FormatWindowStatus(-1, -2, 10000));
            Assert.Equal("Showing -1/10000", english.FormatPagedStatus(-1, 10000));
            Assert.Equal("已顯示 -1/10000 筆", traditionalChinese.FormatPagedStatus(-1, 10000));
            Assert.Equal("Load -1 more (-2 remaining)", english.FormatLoadMore(-1, -2));
            Assert.Equal("再載入 -1 筆（尚餘 -2 筆）", traditionalChinese.FormatLoadMore(-1, -2));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
