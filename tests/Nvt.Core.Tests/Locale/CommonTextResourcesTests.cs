// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using System.Text;
using Nvt.Core.Locale;
using Xunit;

namespace Nvt.Core.Tests.Locale;

/// <summary>Characterizes the frozen common labels and language-bundle contract.</summary>
public sealed class CommonTextResourcesTests
{
    /// <summary>Compares every allowed label with independently copied frozen literals.</summary>
    /// <param name="propertyName">The common label property.</param>
    /// <param name="english">The frozen English value.</param>
    /// <param name="traditionalChinese">The frozen Traditional Chinese value.</param>
    [Theory]
    [InlineData("HomeLabel", "Home", "首頁")]
    [InlineData("RetryLabel", "Retry", "重試")]
    [InlineData("OpenLabel", "Open", "開啟")]
    [InlineData("BackLabel", "Back", "返回")]
    [InlineData("CloseLabel", "Close", "關閉")]
    [InlineData("CancelLabel", "Cancel", "取消")]
    [InlineData("ExitLabel", "Exit", "離開")]
    [InlineData("ContinueLabel", "Continue", "繼續")]
    [InlineData("StayOnPageLabel", "Stay on this page", "留在此頁")]
    [InlineData("PreferencesLabel", "Preferences", "偏好設定")]
    [InlineData("ThemeLabel", "Theme", "主題")]
    [InlineData("LanguageLabel", "Language", "語言")]
    [InlineData("SystemThemeLabel", "System", "跟隨系統")]
    [InlineData("LightThemeLabel", "Light", "淺色")]
    [InlineData("DarkThemeLabel", "Dark", "深色")]
    [InlineData("EnglishLanguageLabel", "English", "英文")]
    [InlineData("TraditionalChineseLanguageLabel", "Traditional Chinese", "繁體中文")]
    [InlineData("ReducedMotionLabel", "Reduced motion", "減少動態效果")]
    [InlineData("ReducedMotionDescription",
        "Keep step status visible while removing non-essential progress animation.",
        "保留步驟狀態，同時停用非必要的進度動畫。")]
    [InlineData("NoItemsLabel", "No items", "沒有項目")]
    [InlineData("PreviousPageLabel", "Previous page", "上一頁")]
    [InlineData("NextPageLabel", "Next page", "下一頁")]
    [InlineData("AllItemsLoadedLabel", "All items loaded", "已載入全部項目")]
    public void LabelsMatchFrozenLiteralsOrdinallyAndInUtf8(
        string propertyName,
        string english,
        string traditionalChinese)
    {
        PropertyInfo property = Assert.IsType<PropertyInfo>(
            typeof(CommonTextResources).GetProperty(propertyName), exactMatch: false);
        AssertText(english, Assert.IsType<string>(
            property.GetValue(CommonTextResources.For(CommonLanguage.English))));
        AssertText(traditionalChinese, Assert.IsType<string>(
            property.GetValue(CommonTextResources.For(CommonLanguage.TraditionalChinese))));
    }

    /// <summary>Ports the frozen cached-bundle identity assertion for both languages.</summary>
    /// <param name="language">The bundle language.</param>
    [Theory]
    [InlineData(CommonLanguage.English)]
    [InlineData(CommonLanguage.TraditionalChinese)]
    public void LocalizedBundlesAreCached(CommonLanguage language)
    {
        CommonTextResources resources = CommonTextResources.For(language);

        Assert.Same(resources, CommonTextResources.For(language));
        Assert.Equal(language, resources.Language);
        Assert.NotSame(CommonTextResources.For(CommonLanguage.English),
            CommonTextResources.For(CommonLanguage.TraditionalChinese));
    }

    /// <summary>Preserves the frozen invalid-language exception, including its actual value.</summary>
    /// <param name="language">An undefined enum value.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void UnsupportedLanguagesKeepTheFrozenException(int language)
    {
        var commonLanguage = (CommonLanguage)language;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CommonTextResources.For(commonLanguage));

        Assert.Equal("language", exception.ParamName);
        Assert.Equal(commonLanguage, Assert.IsType<CommonLanguage>(exception.ActualValue));
        Assert.Equal(new ArgumentOutOfRangeException(nameof(language), commonLanguage, null).Message,
            exception.Message);
    }

    /// <summary>Ensures the resource surface contains only the agreed immutable allowlist.</summary>
    [Fact]
    public void PublicSurfaceIsExactlyTheCommonTextAllowlist()
    {
        string[] expectedProperties =
        [
            "Language", "HomeLabel", "RetryLabel", "OpenLabel", "BackLabel", "CloseLabel",
            "CancelLabel", "ExitLabel", "ContinueLabel", "StayOnPageLabel", "PreferencesLabel",
            "ThemeLabel", "LanguageLabel", "SystemThemeLabel", "LightThemeLabel", "DarkThemeLabel",
            "EnglishLanguageLabel", "TraditionalChineseLanguageLabel", "ReducedMotionLabel",
            "ReducedMotionDescription", "NoItemsLabel", "PreviousPageLabel", "NextPageLabel",
            "AllItemsLoadedLabel",
        ];
        PropertyInfo[] properties = typeof(CommonTextResources).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(expectedProperties.Order(StringComparer.Ordinal),
            properties.Select(static property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, static property =>
        {
            Assert.NotNull(property.GetMethod);
            Assert.Null(property.SetMethod);
        });
        Assert.Empty(typeof(CommonTextResources).GetConstructors());
        string[] expectedMethods = ["For", "FormatLoadMore", "FormatPagedStatus", "FormatWindowStatus"];
        Assert.Equal(expectedMethods, typeof(CommonTextResources)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Select(static method => method.Name)
            .Order(StringComparer.Ordinal));
        CommonLanguage[] expectedLanguages = [CommonLanguage.English, CommonLanguage.TraditionalChinese];
        Assert.Equal(expectedLanguages, Enum.GetValues<CommonLanguage>());
    }

    private static void AssertText(string expected, string actual)
    {
        Assert.Equal(expected, actual, StringComparer.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }
}
