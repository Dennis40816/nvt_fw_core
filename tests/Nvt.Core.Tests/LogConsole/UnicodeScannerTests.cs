// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Tests.LogConsole.StoreRegressionSupport;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Unicode scalar and CJK scanner boundaries.</summary>
public sealed class UnicodeScannerTests
{
    /// <summary>Both scanner entry points preserve whole scalar values, including split surrogate pairs.</summary>
    /// <param name="path">The relative path.</param>
    /// <param name="padding">Padding that places surrogate pairs across read boundaries.</param>
    /// <param name="segmented">Whether to use the segmented scanner entry point.</param>
    [Theory]
    [InlineData("𠀀/報表", 0, false)]
    [InlineData("𠀀/報表", 0, true)]
    [InlineData("報𠀀/表", 0, false)]
    [InlineData("報𠀀/表", 0, true)]
    [InlineData("𠀀/報表", 1023, false)]
    [InlineData("𠀀/報表", 1023, true)]
    [InlineData("報𠀀/表", 1022, false)]
    [InlineData("報𠀀/表", 1022, true)]
    [InlineData("報表/𠀀", 1020, false)]
    [InlineData("報表/𠀀", 1020, true)]
    [InlineData("𠀀.cs", 1023, false)]
    [InlineData("𠀀.cs", 1023, true)]
    public void SupplementaryRelativePathsHaveScannerParity(string path, int padding, bool segmented)
    {
        var text = new string(' ', padding) + path + ":12:3";
        var expected = new ConsoleLinkSpan(padding, path.Length + 5, new LinkTarget(LinkKind.File, path, 12, 3));
        using var content = new Content(text, 0);
        Assert.Equal(expected, Assert.Single(segmented ? ConsoleLinkScanner.Scan(content) : ConsoleLinkScanner.Scan(text)));
        if (segmented) Assert.InRange(content.MaximumRead, 1, 1024);
    }

    /// <summary>CJK prose does not mask recognized URL schemes or leak into a punctuation-delimited target.</summary>
    /// <param name="before">Text before the URL.</param>
    /// <param name="after">Text after the URL.</param>
    /// <param name="segmented">Whether to use the segmented scanner entry point.</param>
    [Theory]
    [InlineData("請見", "。", false)]
    [InlineData("請見", "。", true)]
    [InlineData("請見", "。完成", false)]
    [InlineData("請見", "。完成", true)]
    [InlineData("", "。完成", false)]
    [InlineData("", "。完成", true)]
    [InlineData("", "", false)]
    [InlineData("", "", true)]
    [InlineData("𠀀", "。完成", false)]
    [InlineData("𠀀", "。完成", true)]
    public void UrlBesideCjkProseHasScannerParity(string before, string after, bool segmented)
    {
        const string url = "https://example.test/export_(v2)";
        var text = before + url + after;
        var expected = new ConsoleLinkSpan(before.Length, url.Length, new LinkTarget(LinkKind.Url, url));
        using var content = new Content(text, 0);
        Assert.Equal(expected, Assert.Single(segmented ? ConsoleLinkScanner.Scan(content) : ConsoleLinkScanner.Scan(text)));
    }
}
