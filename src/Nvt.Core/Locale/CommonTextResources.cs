// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Locale;

/// <summary>Provides cached, immutable common labels and list-paging text.</summary>
public sealed class CommonTextResources
{
    private static readonly Lazy<CommonTextResources> English = new(
        static () => new CommonTextResources(CommonLanguage.English));
    private static readonly Lazy<CommonTextResources> TraditionalChinese = new(
        static () => new CommonTextResources(CommonLanguage.TraditionalChinese));

    private CommonTextResources(CommonLanguage language)
    {
        Language = language;
        HomeLabel = Pick("Home", "首頁");
        RetryLabel = Pick("Retry", "重試");
        OpenLabel = Pick("Open", "開啟");
        BackLabel = Pick("Back", "返回");
        CloseLabel = Pick("Close", "關閉");
        CancelLabel = Pick("Cancel", "取消");
        ExitLabel = Pick("Exit", "離開");
        ContinueLabel = Pick("Continue", "繼續");
        StayOnPageLabel = Pick("Stay on this page", "留在此頁");
        PreferencesLabel = Pick("Preferences", "偏好設定");
        ThemeLabel = Pick("Theme", "主題");
        LanguageLabel = Pick("Language", "語言");
        SystemThemeLabel = Pick("System", "跟隨系統");
        LightThemeLabel = Pick("Light", "淺色");
        DarkThemeLabel = Pick("Dark", "深色");
        EnglishLanguageLabel = Pick("English", "英文");
        TraditionalChineseLanguageLabel = Pick("Traditional Chinese", "繁體中文");
        ReducedMotionLabel = Pick("Reduced motion", "減少動態效果");
        ReducedMotionDescription = Pick(
            "Keep step status visible while removing non-essential progress animation.",
            "保留步驟狀態，同時停用非必要的進度動畫。");
        NoItemsLabel = Pick("No items", "沒有項目");
        PreviousPageLabel = Pick("Previous page", "上一頁");
        NextPageLabel = Pick("Next page", "下一頁");
        AllItemsLoadedLabel = Pick("All items loaded", "已載入全部項目");
    }

    /// <summary>Gets the cached resource bundle for the specified language.</summary>
    /// <param name="language">The common text language.</param>
    /// <returns>The immutable bundle shared by callers using that language.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The language is unsupported.</exception>
    public static CommonTextResources For(CommonLanguage language)
    {
        return language switch
        {
            CommonLanguage.English => English.Value,
            CommonLanguage.TraditionalChinese => TraditionalChinese.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
    }

    /// <summary>Gets the bundle's language.</summary>
    public CommonLanguage Language { get; }

    /// <summary>Gets the home action label.</summary>
    public string HomeLabel { get; }

    /// <summary>Gets the retry action label.</summary>
    public string RetryLabel { get; }

    /// <summary>Gets the open action label.</summary>
    public string OpenLabel { get; }

    /// <summary>Gets the back action label.</summary>
    public string BackLabel { get; }

    /// <summary>Gets the close action label.</summary>
    public string CloseLabel { get; }

    /// <summary>Gets the cancel action label.</summary>
    public string CancelLabel { get; }

    /// <summary>Gets the exit action label.</summary>
    public string ExitLabel { get; }

    /// <summary>Gets the continue action label.</summary>
    public string ContinueLabel { get; }

    /// <summary>Gets the action label for staying on the current page.</summary>
    public string StayOnPageLabel { get; }

    /// <summary>Gets the preferences label.</summary>
    public string PreferencesLabel { get; }

    /// <summary>Gets the theme preference label.</summary>
    public string ThemeLabel { get; }

    /// <summary>Gets the language preference label.</summary>
    public string LanguageLabel { get; }

    /// <summary>Gets the system theme choice label.</summary>
    public string SystemThemeLabel { get; }

    /// <summary>Gets the light theme choice label.</summary>
    public string LightThemeLabel { get; }

    /// <summary>Gets the dark theme choice label.</summary>
    public string DarkThemeLabel { get; }

    /// <summary>Gets the English language choice label.</summary>
    public string EnglishLanguageLabel { get; }

    /// <summary>Gets the Traditional Chinese language choice label.</summary>
    public string TraditionalChineseLanguageLabel { get; }

    /// <summary>Gets the reduced motion preference label.</summary>
    public string ReducedMotionLabel { get; }

    /// <summary>Gets the reduced motion preference description.</summary>
    public string ReducedMotionDescription { get; }

    /// <summary>Gets the status label for a window with no visible items.</summary>
    public string NoItemsLabel { get; }

    /// <summary>Gets the previous page action label.</summary>
    public string PreviousPageLabel { get; }

    /// <summary>Gets the next page action label.</summary>
    public string NextPageLabel { get; }

    /// <summary>Gets the status label when no more items remain to load.</summary>
    public string AllItemsLoadedLabel { get; }

    /// <summary>Formats a window status using the current culture.</summary>
    /// <param name="first">The first visible item's one-based position.</param>
    /// <param name="last">The last visible item's one-based position.</param>
    /// <param name="total">The total item count.</param>
    /// <returns>The supplied values interpolated without validation or clamping.</returns>
    /// <remarks>Callers use <see cref="NoItemsLabel"/> when no items are visible.</remarks>
    public string FormatWindowStatus(int first, int last, int total)
    {
        return Language == CommonLanguage.TraditionalChinese
            ? $"顯示第 {first}-{last} 筆，共 {total} 筆"
            : $"Showing {first}-{last} of {total}";
    }

    /// <summary>Formats a paged status using the current culture.</summary>
    /// <param name="visible">The visible item count.</param>
    /// <param name="total">The total item count.</param>
    /// <returns>The supplied values interpolated without validation or clamping.</returns>
    public string FormatPagedStatus(int visible, int total)
    {
        return Language == CommonLanguage.TraditionalChinese
            ? $"已顯示 {visible}/{total} 筆"
            : $"Showing {visible}/{total}";
    }

    /// <summary>Formats a load-more action label using the current culture.</summary>
    /// <param name="next">The number of items the caller will load next.</param>
    /// <param name="remaining">The remaining item count.</param>
    /// <returns>The supplied values interpolated without validation or clamping.</returns>
    /// <remarks>Callers use <see cref="AllItemsLoadedLabel"/> when no items remain.</remarks>
    public string FormatLoadMore(int next, int remaining)
    {
        return Language == CommonLanguage.TraditionalChinese
            ? $"再載入 {next} 筆（尚餘 {remaining} 筆）"
            : $"Load {next} more ({remaining} remaining)";
    }

    private string Pick(string english, string traditionalChinese)
    {
        return Language == CommonLanguage.TraditionalChinese ? traditionalChinese : english;
    }
}
