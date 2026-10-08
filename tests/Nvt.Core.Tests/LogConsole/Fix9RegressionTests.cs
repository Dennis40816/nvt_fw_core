// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Path components remain whole at every storage and scanner read boundary.</summary>
public sealed class Fix9RegressionTests(ITestOutputHelper output)
{
    /// <summary>Leading dots belong to the path, including filename-only location targets.</summary>
    /// <param name="path">The complete relative target.</param>
    [Theory]
    [InlineData(".config/settings.json")]
    [InlineData(".github/x.yml")]
    [InlineData(".settings.json")]
    [InlineData("./a")]
    [InlineData("../a")]
    public void DotPrefixedRelativePathsStayWholeAcrossEveryChunkBoundary(string path)
    {
        VerifyEverySplit(Path(path, ":12:3"));
        VerifyEverySplit(Path(path, "(12,3)"));
    }

    /// <summary>All mark categories and composed forms preserve the first component and its relative kind.</summary>
    /// <param name="path">The complete relative target.</param>
    [Theory]
    [InlineData("cafe\u0301/報表.cs")]
    [InlineData("café/報表.cs")]
    [InlineData("cafe\u0301\u0327/報表.cs")]
    [InlineData("a\u0903/報表.cs")]
    [InlineData("a\u20DD/報表.cs")]
    [InlineData("a\U0001D165/報表.cs")]
    [InlineData("\u0301a/報表.cs")]
    [InlineData("cafe\u0301.cs")]
    public void CombiningMarksStayInRelativePathsAcrossEveryChunkBoundary(string path)
    {
        VerifyEverySplit(Path(path, ":12:3"));
        VerifyEverySplit(Path(path, "(12,3)"));
    }

    /// <summary>A mark inside a word cannot act as a boundary before a drive or URL prefix.</summary>
    /// <param name="mark">A scalar in one of the mark categories.</param>
    [Theory]
    [InlineData("\u0301")]
    [InlineData("\u0903")]
    [InlineData("\u20DD")]
    [InlineData("\U0001D165")]
    public void MarksBeforeRecognizedPrefixesDoNotSplitComponents(string mark)
    {
        VerifyEverySplit(new LineCase("a" + mark + @"C:\Demo\file.cs", []));
        VerifyEverySplit(new LineCase("a" + mark + "https://example.test/export_(v2)", []));
    }

    /// <summary>Independent expected spans prevent shared scanner bugs from passing parity alone.</summary>
    [Fact]
    public void DeterministicCorpusHasScannerParityAndWholeRelativeTargetsAtEverySplit()
    {
        var corpus = Corpus().ToArray();
        Assert.True(corpus.Length >= 60);
        Assert.Equal(corpus.Length, corpus.Select(item => item.Text).Distinct(StringComparer.Ordinal).Count());
        var scans = 0;
        var threeWaySplits = 0;
        foreach (var item in corpus)
        {
            scans += VerifyEverySplit(item);
            // Exhaust every pair of interior split positions for shorter lines.
            if (item.Text.Length > 32) continue;
            for (var first = 1; first < item.Text.Length - 1; first++)
                for (var second = first + 1; second < item.Text.Length; second++)
                {
                    Verify(item, 1024 - first, [first, second]);
                    scans++;
                    threeWaySplits++;
                }
        }
        Assert.True(threeWaySplits > 0);
        output.WriteLine($"{corpus.Length} corpus lines; {scans} comparisons; {threeWaySplits} three-way splits.");
    }

    private static int VerifyEverySplit(LineCase item)
    {
        // Include both endpoint splits and place every interior split on the cursor's actual read seam.
        for (var split = 0; split <= item.Text.Length; split++)
            Verify(item, split == 0 ? 0 : 1024 - split, [split]);
        return item.Text.Length + 1;
    }

    private static void Verify(LineCase item, int padding, int[] splits)
    {
        var text = new string(' ', padding) + item.Text;
        using var content = new SplitContent(text, splits.Select(split => padding + split).ToArray());
        var fromString = ConsoleLinkScanner.Scan(text);
        var fromSegments = ConsoleLinkScanner.Scan(content);
        Assert.Equal(fromString, fromSegments);
        var expected = item.Expected.Select(span => span with { Start = padding + span.Start }).ToImmutableArray();
        foreach (var actual in new[] { fromString, fromSegments })
        {
            Assert.True(expected.Length == actual.Length,
                $"Unexpected link count in '{item.Text}' at splits [{string.Join(',', splits)}].");
            for (var index = 0; index < actual.Length; index++)
            {
                var wanted = expected[index];
                var found = actual[index];
                // These checks are independent of either scanner's character and boundary helpers.
                Assert.Equal(wanted.Start, found.Start); // A span must start at the whole component or prefix.
                if (wanted.Target.Kind != LinkKind.Url && !IsRooted(wanted.Target.Path))
                    Assert.False(IsRooted(found.Target.Path));
                Assert.Equal(wanted, found); // Also retain the full leading component, suffix and UTF-16 length.
            }
        }
        Assert.InRange(content.MaximumRead, 1, 1024);
    }

    private static bool IsRooted(string path) => path.StartsWith('/') || path.StartsWith('\\')
        || path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\';

    private static IEnumerable<LineCase> Corpus()
    {
        // Both location forms on names covering all M* categories, normalization forms and scalar widths.
        foreach (var path in new[]
        {
            ".config/settings.json", ".github/x.yml", ".hidden/報表", ".file.cs", "./a", "../a",
            "cafe\u0301/報表.cs", "café/報表.cs", "cafe\u0301\u0327/x", "a\u0903/x", "a\u20DD/x",
            "a\U0001D165/x", "\u0301a/x", "𠀀/報表", "報𠀀/表", "報表/𠀀", "𠀀.cs",
            "src/Core/x.cs", @"src\Core\x.cs", "_cache/x", "-cache/x", "a١/x", "資料/報表",
            "cafe\u0301.cs", ".cafe\u0301/x", "a\U00010400/x", "é/x", "a\u02B0/x", "a\u01C5/x",
        })
        {
            yield return Path(path, ":12:3");
            yield return Path(path, "(12,3)");
        }
        // Verbatim scanner examples from the fix4 through fix8 reviews and their boundary variants.
        yield return Path(@"C:\Demo\O'Brien file.txt", "(142,18)", quote: '"', line: 142, column: 18, rooted: true);
        yield return Path(@"C:\Demo\a""b file.txt", "(142,18)", quote: '\'', line: 142, column: 18, rooted: true);
        yield return Path(@"C:\Demo\file.cs", before: "檔案", after: "。", rooted: true);
        yield return Path(@"\\server\share\檔案.log", before: "開啟", after: "。", rooted: true);
        yield return Path("D:/Demo/檔案.log", before: "𠀀", after: "。完成", rooted: true);
        yield return Path(@"C:\Demo\file", ":2:3", after: "：完成", line: 2, column: 3, rooted: true);
        yield return Path(@"C:\Demo\Core\ExportWriter.cs", "(142,18)", before: "at ", line: 142, column: 18, rooted: true);
        yield return Path(@"\\server\share\報表.log", ":7:2", line: 7, column: 2, rooted: true);
        foreach (var name in new[] { "Export Writer.cs", "報表 檔案.cs", "𠀀 報表.cs", ".cafe\u0301 file.cs" })
            foreach (var quote in new[] { '"', '\'' })
            {
                yield return Path(name, ":142:18", quote: quote, line: 142, column: 18);
                yield return Path(name, "(142,18)", quote: quote, line: 142, column: 18);
                yield return new LineCase(quote + name + quote + "。完成", []);
            }
        const string url = "https://example.test/export_(v2)";
        foreach (var before in new[] { "請見", "𠀀", "", "(" })
            foreach (var after in new[] { "。", "。完成" })
                yield return new LineCase(before + url + after,
                    [new ConsoleLinkSpan(before.Length, url.Length, new LinkTarget(LinkKind.Url, url))]);
        yield return new LineCase("(" + url + ").",
            [new ConsoleLinkSpan(1, url.Length, new LinkTarget(LinkKind.Url, url))]);
        yield return new LineCase("請見" + url + "。檔案C:\\Demo\\file.cs。",
            [new ConsoleLinkSpan(2, url.Length, new LinkTarget(LinkKind.Url, url)),
             new ConsoleLinkSpan(2 + url.Length + 3, 15, new LinkTarget(LinkKind.File, @"C:\Demo\file.cs"))]);
        yield return Path(".config/", kind: LinkKind.Folder);
        yield return Path("./artifacts/", kind: LinkKind.Folder);
        yield return Path(@"C:\Demo\Core\", kind: LinkKind.Folder, rooted: true);
        yield return Path(@"\\server\share\output\", kind: LinkKind.Folder, rooted: true);
        yield return Path("/資料/報表", rooted: true);
        yield return Path(".config/x", before: "輸出 ", after: "。完成");
        yield return new LineCase("ordinary text, cafe\u0301 and .config", []);
        foreach (var mark in new[] { "\u0301", "\u0903", "\u20DD", "\U0001D165" })
        {
            yield return new LineCase("a" + mark + @"C:\Demo\file.cs", []);
            yield return new LineCase("a" + mark + "https://example.test/export_(v2)", []);
        }
    }

    private static LineCase Path(string path, string suffix = "", string before = "", string after = "",
        char? quote = null, int line = 12, int column = 3, LinkKind kind = LinkKind.File, bool rooted = false)
    {
        Assert.Equal(rooted, IsRooted(path));
        var syntax = quote is { } delimiter ? delimiter + path + delimiter + suffix : path + suffix;
        var target = new LinkTarget(kind, path, suffix.Length == 0 ? null : line, suffix.Length == 0 ? null : column);
        return new LineCase(before + syntax + after, [new ConsoleLinkSpan(before.Length, syntax.Length, target)]);
    }

    private sealed record LineCase(string Text, ImmutableArray<ConsoleLinkSpan> Expected);

    // The test thread owns MaximumRead. Immutable physical chunks may split even one Unicode scalar.
    private sealed class SplitContent : ILogTextContent
    {
        private readonly (int Start, string Text)[] _chunks;
        internal int MaximumRead { get; private set; }
        public int Length { get; }
        public int ResidentCharacterCount => 0;
        public long Version => 0;

        internal SplitContent(string text, int[] splits)
        {
            Length = text.Length;
            var boundaries = splits.Prepend(0).Append(text.Length).Distinct().Order().ToArray();
            _chunks = boundaries.Zip(boundaries.Skip(1), (start, end) => (start, text[start..end])).ToArray();
        }

        public void Read(int offset, Span<char> destination)
        {
            Assert.InRange(destination.Length, 1, 1024);
            MaximumRead = Math.Max(MaximumRead, destination.Length);
            var copied = 0;
            foreach (var (start, chunk) in _chunks)
            {
                if (offset >= start + chunk.Length || offset < start) continue;
                var count = Math.Min(destination.Length - copied, start + chunk.Length - offset);
                chunk.AsSpan(offset - start, count).CopyTo(destination[copied..]);
                copied += count;
                offset += count;
                if (copied == destination.Length) return;
            }
            Assert.Fail("Segmented content did not fill the requested range.");
        }

        public void Dispose() { }
    }
}
