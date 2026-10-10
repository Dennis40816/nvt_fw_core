// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Constructed text parts supply an independent oracle for quote boundaries and recovery.</summary>
public sealed class ScannerQuoteOracleTests(ITestOutputHelper output)
{
    /// <summary>Every generated line preserves exactly its constructed targets through both scanner entry points.</summary>
    /// <remarks>
    /// The cases are independent and the scanner has no shared state, so the cases run in parallel. The oracle still
    /// compares every case at every split.
    /// </remarks>
    [Fact]
    public void GeneratedQuoteGrammarMatchesConstructedLinksAtEveryChunkSplit()
    {
        var cases = Cases().ToArray();
        Assert.Equal(cases.Length, cases.Select(item => item.Text).Distinct(StringComparer.Ordinal).Count());
        var comparisons = 0L;
        output.WriteLine($"{cases.Length} generated texts; {2 * cases.Sum(item => item.Text.Length + 1)} oracle comparisons.");
        var options = new ParallelOptions { CancellationToken = TestContext.Current.CancellationToken };
        _ = Parallel.ForEach(cases, options, item =>
        {
            for (var split = 0; split <= item.Text.Length; split++)
            {
                // Align the physical split with the cursor seam, including both endpoint splits.
                var padding = (1024 - split % 1024) % 1024;
                var text = new string(' ', padding) + item.Text;
                using var content = new SplitContent(text, padding + split);
                var expected = item.Expected.Select(span => span with { Start = padding + span.Start }).ToImmutableArray();
                Verify(ConsoleLinkScanner.Scan(text), "string");
                Verify(ConsoleLinkScanner.Scan(content), "segmented");
                Assert.InRange(content.MaximumRead, 1, 1024);

                void Verify(ImmutableArray<ConsoleLinkSpan> actual, string entryPoint)
                {
                    if (!expected.SequenceEqual(actual))
                        Assert.Fail($"{entryPoint} scanner at split {split} in '{item.Text}': expected [{string.Join("; ", expected)}], actual [{string.Join("; ", actual)}].");
                    _ = Interlocked.Increment(ref comparisons);
                }
            }
        });
        Assert.Equal(2L * cases.Sum(item => item.Text.Length + 1), Interlocked.Read(ref comparisons));
    }

    private static IEnumerable<LineCase> Cases()
    {
        // Standalone separator-bearing prose tokens follow the existing relative-path grammar.
        var prefixes = new[]
        {
            Prefix(""), Prefix("load/save ", "load/save"), Prefix("a/b ", "a/b"), Prefix(@"x\y ", @"x\y"),
            Prefix("end. "), Prefix("it's "), Prefix("can't "), Prefix("end!"), Prefix("("),
            Prefix("\"unpaired then "), Prefix("'unpaired then "),
            Prefix("\"unpaired load/save then ", "load/save"), Prefix("'unpaired load/save then ", "load/save"),
            Prefix("\"unpaired a/b then ", "a/b"), Prefix("'unpaired x\\y then ", @"x\y"),
        };
        var targets = new[]
        {
            Target(@"C:\Demo\a.txt", false, (@"C:\Demo\a.txt", false)),
            Target(@"\\server\share\a.txt", false, (@"\\server\share\a.txt", false)),
            Target("./rel/a.txt", false, ("./rel/a.txt", false)),
            Target("../rel/a.txt", false, ("../rel/a.txt", false)),
            Target("rel/a.txt", false, ("rel/a.txt", false)),
            Target("Export Writer.cs", true, ("Writer.cs", true)),
            Target(@"C:\My Docs\a b.txt", false, (@"C:\My", false), (@"Docs\a", false), ("b.txt", true)),
            Target(@"\\server\My Docs\a b.txt", false, (@"\\server\My", false), (@"Docs\a", false), ("b.txt", true)),
            Target("./My Docs/a b.txt", false, ("./My", false), ("Docs/a", false), ("b.txt", true)),
            Target("../My Docs/a b.txt", false, ("../My", false), ("Docs/a", false), ("b.txt", true)),
            Target("My Docs/a b.txt", false, ("Docs/a", false), ("b.txt", true)),
            Target("Export.cs", true, ("Export.cs", true)),
            Target(@"C:\My Docs\", false, (@"C:\My", false), (@"Docs\", false)),
            Target("./My Docs/", false, ("./My", false), ("Docs/", false)),
            Target(@"C:\我的 文件\", false, (@"C:\我的", false), (@"文件\", false)),
            Target("./我的 文件/", false, ("./我的", false), ("文件/", false)),
        };
        var locations = new[] { new Location("", null, null), new Location(":12:3", 12, 3), new Location("(12,3)", 12, 3) };
        foreach (var quote in new[] { '"', '\'' })
            yield return Compose(Prefix(quote + "unpaired load/save then ", "load/save"), quote,
                targets[8], new Location("(3,4)", 3, 4), "");
        foreach (var prefix in prefixes)
            foreach (var quote in new[] { '"', '\'', '\0' })
                foreach (var target in targets)
                    foreach (var location in locations)
                        foreach (var suffix in new[] { "", " done", ", it's missing", "。完成" })
                            yield return Compose(prefix, quote, target, location, suffix);
        foreach (var prefix in prefixes)
            foreach (var quote in new[] { '"', '\'' })
                foreach (var target in targets)
                    foreach (var location in locations)
                        foreach (var before in new[] { "", "開啟" })
                            foreach (var after in new[] { "", "失敗" })
                                foreach (var suffix in new[] { "", " and \"more", " and 'more", " and \"more\"", " and 'more'" })
                                {
                                    if (before.Length == 0 && after.Length == 0 && suffix.Length == 0) continue;
                                    yield return Compose(new LineCase(prefix.Text + before, prefix.Expected), quote, target, location, after + suffix);
                                }
        foreach (var quote in new[] { '"', '\'' })
            foreach (var extra in new[] { 0, 1 })
                foreach (var embeddedUrl in new[] { false, true })
                    foreach (var location in locations)
                        yield return CappedCandidate(quote, extra, embeddedUrl, location);
    }

    private static LineCase CappedCandidate(char quote, int extra, bool embeddedUrl, Location location)
    {
        const string url = "https://example.test/";
        const string tail = "tail/a.cs";
        const string later = @"C:\keep\a.cs";
        var middle = embeddedUrl ? " " + url : "";
        var lead = @"C:\" + new string('a', 4096 + extra - 3 - middle.Length - 1 - tail.Length - location.Text.Length);
        var path = lead + middle + " " + tail;
        Assert.Equal(4096 + extra, path.Length + location.Text.Length);
        var syntax = quote + path + quote + location.Text;
        var expected = ImmutableArray<ConsoleLinkSpan>.Empty;
        if (embeddedUrl)
        {
            // At the cap, URL priority leaves the ordinary unquoted path parts eligible.
            if (extra == 0)
                expected = expected.Add(new ConsoleLinkSpan(1, lead.Length, new LinkTarget(LinkKind.File, lead)));
            expected = expected.Add(new ConsoleLinkSpan(lead.Length + 2, url.Length, new LinkTarget(LinkKind.Url, url)));
            if (extra == 0)
                expected = expected.Add(new ConsoleLinkSpan(lead.Length + middle.Length + 2, tail.Length, new LinkTarget(LinkKind.File, tail)));
        }
        else if (extra == 0)
            expected = expected.Add(new ConsoleLinkSpan(0, syntax.Length,
                new LinkTarget(LinkKind.File, path, location.Line, location.Column)));
        expected = expected.Add(new ConsoleLinkSpan(syntax.Length + 1, later.Length, new LinkTarget(LinkKind.File, later)));
        return new LineCase(syntax + " " + later, expected);
    }

    private static LineCase Compose(LineCase prefix, char quote, TargetForm target, Location location, string suffix)
    {
        var quoted = quote != '\0';
        var syntax = quoted ? quote + target.Path + quote + location.Text : target.Path + location.Text;
        var expected = prefix.Expected;
        if (quoted && (!target.RequiresLocation || location.Line is not null))
            expected = expected.Add(new ConsoleLinkSpan(prefix.Text.Length, syntax.Length,
                new LinkTarget(PathKind(target.Path), target.Path, location.Line, location.Column)));
        else if (!quoted)
            foreach (var part in target.UnquotedParts)
            {
                if (part.RequiresLocation && location.Line is null) continue;
                var hasLocation = part.Start + part.Path.Length == target.Path.Length;
                expected = expected.Add(new ConsoleLinkSpan(prefix.Text.Length + part.Start,
                    part.Path.Length + (hasLocation ? location.Text.Length : 0),
                    new LinkTarget(PathKind(part.Path), part.Path, hasLocation ? location.Line : null, hasLocation ? location.Column : null)));
            }
        return new LineCase(prefix.Text + syntax + suffix, expected);
    }

    private static LineCase Prefix(string text, string? path = null) => new(text,
        path is null ? [] : [new ConsoleLinkSpan(text.IndexOf(path, StringComparison.Ordinal), path.Length, new LinkTarget(LinkKind.File, path))]);

    private static LinkKind PathKind(string path) => path[^1] is '/' or '\\' ? LinkKind.Folder : LinkKind.File;

    private static TargetForm Target(string path, bool requiresLocation, params (string Path, bool RequiresLocation)[] parts)
        => new(path, requiresLocation, parts.Select(part =>
            new TargetPart(path.IndexOf(part.Path, StringComparison.Ordinal), part.Path, part.RequiresLocation)).ToImmutableArray());

    private sealed record LineCase(string Text, ImmutableArray<ConsoleLinkSpan> Expected);
    private sealed record TargetForm(string Path, bool RequiresLocation, ImmutableArray<TargetPart> UnquotedParts);
    private sealed record TargetPart(int Start, string Path, bool RequiresLocation);
    private sealed record Location(string Text, int? Line, int? Column);

}
