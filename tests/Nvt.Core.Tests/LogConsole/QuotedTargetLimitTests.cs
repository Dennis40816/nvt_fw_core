// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Quoted target limits preserve URL priority and later independent paths.</summary>
public sealed class QuotedTargetLimitTests
{
    private const string Url = "https://example.test/";
    private const string Later = @"C:\keep\a.cs";
    private const string Tail = "tail/a.cs";
    private const string Location = "(3,4)";

    /// <summary>An oversized enclosing candidate excludes internal paths even when it overlaps a URL.</summary>
    /// <param name="quote">The enclosing delimiter.</param>
    /// <param name="segmented">Whether to use the content entry point.</param>
    [Theory]
    [InlineData('"', false)]
    [InlineData('"', true)]
    [InlineData('\'', false)]
    [InlineData('\'', true)]
    public void OversizedQuotedUrlPreservesUrlAndLaterPathAcrossChunkBoundaries(char quote, bool segmented)
    {
        var lead = @"C:\" + new string('a', 4100);
        var candidate = quote + lead + " " + Url + " " + Tail + quote + Location;
        var text = candidate + " " + Later;
        ImmutableArray<ConsoleLinkSpan> expected =
        [
            new(lead.Length + 2, Url.Length, new LinkTarget(LinkKind.Url, Url)),
            new(candidate.Length + 1, Later.Length, new LinkTarget(LinkKind.File, Later)),
        ];
        VerifyBoundaries(text, expected, segmented);
    }

    /// <summary>The cap counts locations but not quotes, and rejects embedded absolute and relative fragments together.</summary>
    /// <param name="quote">The enclosing delimiter.</param>
    /// <param name="extra">Characters above the cap.</param>
    /// <param name="embedded">The embedded target syntax.</param>
    [Theory]
    [InlineData('"', 0, "none")]
    [InlineData('"', 1, "none")]
    [InlineData('\'', 0, "none")]
    [InlineData('\'', 1, "none")]
    [InlineData('"', 0, "url")]
    [InlineData('"', 1, "url")]
    [InlineData('\'', 0, "url")]
    [InlineData('\'', 1, "url")]
    [InlineData('"', 0, "absolute")]
    [InlineData('"', 1, "absolute")]
    [InlineData('\'', 0, "absolute")]
    [InlineData('\'', 1, "absolute")]
    public void QuotedTargetLimitPreservesEligibleLinksAcrossChunkBoundaries(char quote, int extra, string embedded)
    {
        var middle = embedded switch { "url" => " " + Url, "absolute" => @" D:\inside\a.cs", _ => "" };
        var lead = @"C:\" + new string('a', 4096 + extra - 3 - middle.Length - 1 - Tail.Length - Location.Length);
        var path = lead + middle + " " + Tail;
        Assert.Equal(4096 + extra, path.Length + Location.Length);
        var candidate = quote + path + quote + Location;
        var text = candidate + " " + Later;
        var expected = ImmutableArray<ConsoleLinkSpan>.Empty;
        if (embedded == "url")
        {
            if (extra == 0)
                expected = expected.Add(new ConsoleLinkSpan(1, lead.Length, new LinkTarget(LinkKind.File, lead)));
            expected = expected.Add(new ConsoleLinkSpan(lead.Length + 2, Url.Length, new LinkTarget(LinkKind.Url, Url)));
            if (extra == 0)
                expected = expected.Add(new ConsoleLinkSpan(lead.Length + middle.Length + 2, Tail.Length, new LinkTarget(LinkKind.File, Tail)));
        }
        else if (extra == 0)
            expected = expected.Add(new ConsoleLinkSpan(0, candidate.Length, new LinkTarget(LinkKind.File, path, 3, 4)));
        expected = expected.Add(new ConsoleLinkSpan(candidate.Length + 1, Later.Length, new LinkTarget(LinkKind.File, Later)));
        VerifyBoundaries(text, expected, segmented: false);
        VerifyBoundaries(text, expected, segmented: true);
    }

    private static void VerifyBoundaries(string text, ImmutableArray<ConsoleLinkSpan> expected, bool segmented)
    {
        var boundaries = new List<int> { 0, 1, 512, 1024, 2048, 3072, 4096, text.Length };
        foreach (var token in new[] { Url, @"D:\inside\a.cs", Tail, Location, Later })
        {
            var start = text.IndexOf(token, StringComparison.Ordinal);
            if (start < 0) continue;
            boundaries.AddRange([start - 1, start, start + 1, start + token.Length / 2, start + token.Length]);
        }
        foreach (var split in boundaries.Distinct().Order())
        {
            var padding = (1024 - split % 1024) % 1024;
            var padded = new string(' ', padding) + text;
            using var content = new ScannerTestContent(padded, 0);
            var actual = segmented ? ConsoleLinkScanner.Scan(content) : ConsoleLinkScanner.Scan(padded);
            Assert.Equal(expected.Select(span => span with { Start = padding + span.Start }), actual);
            if (segmented) Assert.InRange(content.MaximumRead, 1, 1024);
        }
    }
}
