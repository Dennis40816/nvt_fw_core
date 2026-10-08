// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Quoted paths preserve complete targets beside prose and across physical and scanner seams.</summary>
public sealed class QuotedBoundaryTests
{
    /// <summary>CJK scalars form boundaries on either side of both quote types.</summary>
    [Fact]
    public void CjkAdjacentQuotesPreserveFullTargetAtEveryChunkSplit()
    {
        foreach (var path in new[]
        {
            @"C:\My Docs\a b.txt", @"C:\My Docs\資料", @"C:\我的 文件\資料",
            @"C:\My Docs\", "C:/My Docs/", @"C:\我的 文件\", "./我的 文件/",
        })
            foreach (var quote in new[] { '"', '\'' })
                foreach (var before in new[] { "", "開啟", "𠀀" })
                    foreach (var after in new[] { "", "失敗", "𠀀" })
                    {
                        if (before.Length == 0 && after.Length == 0) continue;
                        foreach (var suffix in new[] { "", "(3,4)", ":3:4" })
                        {
                            var syntax = quote + path + quote + suffix;
                            VerifyEverySplit(before + syntax + after,
                                [new ConsoleLinkSpan(before.Length, syntax.Length, new LinkTarget(
                                    path[^1] is '/' or '\\' ? LinkKind.Folder : LinkKind.File, path,
                                    suffix.Length == 0 ? null : 3, suffix.Length == 0 ? null : 4))]);
                        }
                    }
    }

    /// <summary>Abandoning a quote resumes at its original opening so nested targets remain eligible.</summary>
    [Fact]
    public void AbandonedQuotesPreserveNestedTargetsAtEveryChunkSplit()
    {
        const string path = @"C:\My Docs\a b.txt";
        foreach (var outer in new[] { '"', '\'' })
            foreach (var inner in new[] { '"', '\'' })
                foreach (var later in new[] { "more", "more\"", "more'" })
                {
                    var prefix = outer + "unpaired ";
                    var syntax = inner + path + inner + "(3,4)";
                    var text = prefix + syntax + " and " + outer + later;
                    VerifyEverySplit(text, [new ConsoleLinkSpan(prefix.Length, syntax.Length, new LinkTarget(LinkKind.File, path, 3, 4))]);
                }
    }

    /// <summary>Home and environment leaders are syntax only and require no app expansion.</summary>
    /// <param name="path">The unchanged quoted path.</param>
    [Theory]
    [InlineData("~/My Docs/a b.txt")]
    [InlineData(@"%TEMP%\a b.txt")]
    [InlineData("$HOME/a b")]
    public void QuotedPathLeadersAreAcceptedByBothEntryPoints(string path)
    {
        foreach (var quote in new[] { '"', '\'' })
        {
            var text = quote + path + quote;
            VerifyEverySplit(text, [new ConsoleLinkSpan(0, text.Length, new LinkTarget(LinkKind.File, path))]);
        }
    }

    /// <summary>Known limitation: paired separator-bearing prose is one path, even around a quoted target.</summary>
    /// <param name="text">The quoted prose.</param>
    /// <param name="path">The retained target including nested other-type quotes.</param>
    [Theory]
    [InlineData("\"could not open 'C:\\My Docs\\a.txt'\"", "could not open 'C:\\My Docs\\a.txt'")]
    [InlineData("\"Read/write error\"", "Read/write error")]
    public void PairedQuotedProseWithSeparatorLimitationKeepsOuterTarget(string text, string path)
        => VerifyEverySplit(text, [new ConsoleLinkSpan(0, text.Length, new LinkTarget(LinkKind.File, path))]);

    /// <summary>Known limitation: CJK closing precedence splits unpaired separator prose from the following path.</summary>
    [Fact]
    public void UnpairedProseBeforeCjkClosureLimitationKeepsQuotedProseAndFollowingPath()
    {
        const string adjacent = "\"unpaired load/save then 開啟\"資料/a.txt\"";
        var close = adjacent.IndexOf("\"資料", StringComparison.Ordinal);
        VerifyEverySplit(adjacent,
        [
            new ConsoleLinkSpan(0, close + 1, new LinkTarget(LinkKind.File, "unpaired load/save then 開啟")),
            new ConsoleLinkSpan(close + 1, "資料/a.txt".Length, new LinkTarget(LinkKind.File, "資料/a.txt")),
        ]);
    }

    private static void VerifyEverySplit(string text, ImmutableArray<ConsoleLinkSpan> expected)
    {
        for (var split = 0; split <= text.Length; split++)
        {
            var padding = (1024 - split % 1024) % 1024;
            var padded = new string(' ', padding) + text;
            var shifted = expected.Select(span => span with { Start = padding + span.Start }).ToImmutableArray();
            using var content = new SplitContent(padded, padding + split);
            Assert.Equal(shifted, ConsoleLinkScanner.Scan(padded));
            Assert.Equal(shifted, ConsoleLinkScanner.Scan(content));
            Assert.InRange(content.MaximumRead, 1, 1024);
        }
    }
}
