// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Quote grammar, filename evidence and bounded target materialization.</summary>
public sealed class ScannerSyntaxTests
{
    /// <summary>Quotes in prose cannot hide a path or terminate a later quoted candidate.</summary>
    /// <param name="text">The complete log line.</param>
    /// <param name="path">The expected target.</param>
    /// <param name="line">The expected line.</param>
    /// <param name="column">The expected column.</param>
    [Theory]
    [InlineData("Can't find C:\\Demo\\a.txt, it's missing", @"C:\Demo\a.txt", null, null)]
    [InlineData("Can't open \"C:\\My Docs\\a b.txt\"(3,4)", @"C:\My Docs\a b.txt", 3, 4)]
    [InlineData("'unpaired then \"C:\\My Docs\\a b.txt\"(3,4)", @"C:\My Docs\a b.txt", 3, 4)]
    [InlineData("\"unpaired then 'C:\\My Docs\\a b.txt'(3,4)", @"C:\My Docs\a b.txt", 3, 4)]
    [InlineData("\"unpaired then \"C:\\My Docs\\a b.txt\"(3,4)", @"C:\My Docs\a b.txt", 3, 4)]
    [InlineData("'unpaired then 'C:\\My Docs\\a b.txt'(3,4)", @"C:\My Docs\a b.txt", 3, 4)]
    [InlineData("\"unpaired then \"./My Docs/a b.txt\"(3,4)", "./My Docs/a b.txt", 3, 4)]
    [InlineData("'unpaired then '/My Docs/a b.txt'(3,4)", "/My Docs/a b.txt", 3, 4)]
    [InlineData("'C:\\O'Brien file.txt'(3,4)", @"C:\O'Brien file.txt", 3, 4)]
    public void ProseAndUnpairedQuotesPreservePathsAcrossEveryChunkBoundary(string text, string path, int? line, int? column)
        => VerifySplits(text, spans => Assert.Equal(new LinkTarget(LinkKind.File, path, line, column), Assert.Single(spans).Target));

    /// <summary>Recovery cannot promote an abandoned quote range containing prose separators.</summary>
    /// <param name="quote">The matching delimiter.</param>
    /// <param name="path">The full later path.</param>
    [Theory]
    [InlineData('"', "./My Docs/a b.txt")]
    [InlineData('\'', "./My Docs/a b.txt")]
    [InlineData('"', "../My Docs/a b.txt")]
    [InlineData('\'', "../My Docs/a b.txt")]
    [InlineData('"', @"\\server\My Docs\a b.txt")]
    [InlineData('\'', @"\\server\My Docs\a b.txt")]
    public void SameQuoteRecoveryPreservesWholePathsAfterSeparatorProse(char quote, string path)
    {
        var prefix = quote + "unpaired load/save then ";
        var syntax = quote + path + quote + "(3,4)";
        VerifySplits(prefix + syntax, spans =>
        {
            Assert.Equal(2, spans.Length);
            Assert.Equal(new LinkTarget(LinkKind.File, "load/save"), spans[0].Target);
            Assert.Equal(new LinkTarget(LinkKind.File, path, 3, 4), spans[1].Target);
            Assert.Equal(syntax.Length, spans[1].Length);
        });
    }

    /// <summary>Terminal whitespace and separators do not turn closing quotes into empty openings.</summary>
    /// <param name="quote">The enclosing delimiter.</param>
    /// <param name="after">The closing boundary.</param>
    [Theory]
    [InlineData('"', "")]
    [InlineData('\'', "")]
    [InlineData('"', ".")]
    [InlineData('\'', ".")]
    [InlineData('"', " done")]
    [InlineData('\'', " done")]
    [InlineData('"', "(12,3)")]
    [InlineData('\'', ":12:3")]
    public void QuotedTrailingSeparatorsKeepFullTargets(char quote, string after)
    {
        const string path = "d/a /";
        var located = after is "(12,3)" or ":12:3";
        VerifySplits(quote + path + quote + after, spans => Assert.Equal(
            new LinkTarget(LinkKind.Folder, path, located ? 12 : null, located ? 3 : null),
            Assert.Single(spans).Target));
    }

    /// <summary>A separator-free location target needs filename evidence.</summary>
    /// <param name="text">Ordinary log syntax without a file target.</param>
    [Theory]
    [InlineData("log:12:30")]
    [InlineData("12:30")]
    [InlineData("00:01:23")]
    [InlineData("localhost:8080")]
    [InlineData("retry:3")]
    [InlineData("{\"retries\":3}")]
    [InlineData("\"12\"(3,4)")]
    [InlineData(".cs:12")]
    [InlineData("name.123:4")]
    public void OrdinaryLogTokensAreNotFileLinksAcrossEveryChunkBoundary(string text)
        => VerifySplits(text, spans => Assert.Empty(spans));

    /// <summary>Filename evidence keeps supported unquoted and quoted location forms.</summary>
    [Fact]
    public void FilenameLocationsKeepFullTargetsAcrossEveryChunkBoundary()
    {
        VerifySplits("Export.cs:12:3", spans => Assert.Equal(new LinkTarget(LinkKind.File, "Export.cs", 12, 3), Assert.Single(spans).Target));
        VerifySplits("\"Export Writer.cs\"(142,18)", spans => Assert.Equal(new LinkTarget(LinkKind.File, "Export Writer.cs", 142, 18), Assert.Single(spans).Target));
    }

    /// <summary>Every target form has the same fixed materialization bound.</summary>
    /// <param name="kind">The candidate syntax.</param>
    /// <param name="extra">Characters above the cap.</param>
    [Theory]
    [InlineData("url", 0)]
    [InlineData("url", 1)]
    [InlineData("drive", 0)]
    [InlineData("drive", 1)]
    [InlineData("unc", 0)]
    [InlineData("unc", 1)]
    [InlineData("relative", 0)]
    [InlineData("relative", 1)]
    [InlineData("quoted", 0)]
    [InlineData("quoted", 1)]
    [InlineData("filename", 0)]
    [InlineData("filename", 1)]
    public void TargetMaterializationIsCapped(string kind, int extra)
    {
        const int cap = 4096;
        var prefix = kind switch { "url" => "https://", "drive" => @"C:\", "unc" => @"\\s\", "relative" => "d/", _ => "" };
        var suffix = kind == "filename" ? ".cs:1" : "";
        var target = prefix + new string('a', cap + extra - prefix.Length - suffix.Length) + suffix;
        var syntax = kind == "quoted" ? "\"" + target[..^3] + "/aa\"" : target;
        using var content = new TestContent(syntax, 0);
        var spans = ConsoleLinkScanner.Scan(content);
        Assert.Equal(spans, ConsoleLinkScanner.Scan(syntax));
        if (extra == 0) Assert.Single(spans); else Assert.Empty(spans);
        Assert.InRange(content.MaximumRead, 1, 1024);
    }
    /// <summary>Skipping a large quoted target does not expose its internal path fragments.</summary>
    [Fact]
    public void OversizedQuotedTargetDoesNotLinkInternalFragments()
    {
        var text = "\"C:\\" + new string('a', 4096) + " tail/a.cs\"(3,4) C:\\keep\\a.cs";
        using var content = new TestContent(text, 0);
        var spans = ConsoleLinkScanner.Scan(content);
        Assert.Equal(new LinkTarget(LinkKind.File, @"C:\keep\a.cs"), Assert.Single(spans).Target);
        Assert.Equal(spans, ConsoleLinkScanner.Scan(text));
    }

    /// <summary>The quote-free cap excludes the whole candidate while preserving a later independent path.</summary>
    /// <param name="quote">The enclosing quote type.</param>
    /// <param name="extra">Characters above the target cap.</param>
    [Theory]
    [InlineData('"', 0)]
    [InlineData('"', 1)]
    [InlineData('\'', 0)]
    [InlineData('\'', 1)]
    public void QuotedTargetCapDoesNotExposeTrailingSeparatorAcrossEveryChunkBoundary(char quote, int extra)
    {
        var target = "d/" + new string('a', 4092 + extra) + " /";
        var candidate = quote + target + quote;
        VerifySplits(candidate, spans =>
        {
            if (extra == 0) Assert.Equal(new LinkTarget(LinkKind.Folder, target), Assert.Single(spans).Target);
            else Assert.Empty(spans);
        });
        VerifySplits(candidate + @" C:\keep\a.cs", spans =>
        {
            Assert.Equal(extra == 0 ? 2 : 1, spans.Length);
            if (extra == 0) Assert.Equal(new LinkTarget(LinkKind.Folder, target), spans[0].Target);
            Assert.Equal(new LinkTarget(LinkKind.File, @"C:\keep\a.cs"), spans[^1].Target);
        });
    }

    /// <summary>A long candidate is scanned in chunks and never becomes a target-sized string.</summary>
    [Fact]
    public void OversizedUrlUsesBoundedAllocation()
    {
        using var content = new TestContent("https://" + new string('a', 1024 * 1024), 0);
        ConsoleLinkScanner.Scan("https://example.test/");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var spans = ConsoleLinkScanner.Scan(content);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(spans);
        Assert.InRange(allocated, 0, 256 * 1024);
        Assert.InRange(content.MaximumRead, 1, 1024);
    }

    private static void VerifySplits(string text, Action<ImmutableArray<ConsoleLinkSpan>> verify)
    {
        for (var split = 0; split < text.Length; split++)
        {
            var value = new string(' ', (1024 - split % 1024) % 1024) + text;
            using var content = new TestContent(value, 0);
            var spans = ConsoleLinkScanner.Scan(value);
            Assert.Equal(spans, ConsoleLinkScanner.Scan(content));
            verify(spans);
            Assert.InRange(content.MaximumRead, 1, 1024);
        }
    }
}
