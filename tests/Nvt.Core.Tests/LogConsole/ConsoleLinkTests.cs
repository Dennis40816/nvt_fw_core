// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Unicode syntax, structured precedence, bounded caching, and disk-free scanner contracts.</summary>
public sealed class ConsoleLinkTests
{
    /// <summary>Apostrophes inside double quotes and double quotes inside single quotes retain the full target.</summary>
    /// <param name="text">The quoted target syntax.</param>
    /// <param name="path">The full path.</param>
    [Theory]
    [InlineData("\"C:\\Demo\\O'Brien file.txt\"(142,18)", "C:\\Demo\\O'Brien file.txt")]
    [InlineData("'C:\\Demo\\a\"b file.txt'(142,18)", "C:\\Demo\\a\"b file.txt")]
    public void MatchingQuotesPreserveFullPathAndLocationInBothScanners(string text, string path)
    {
        var expected = new LinkTarget(LinkKind.File, path, 142, 18);
        var span = Assert.Single(ConsoleLinkScanner.Scan(text));
        Assert.Equal(expected, span.Target);
        Assert.Equal(text.Length, span.Length);
        var content = new ChunkedContent(new string('x', 1009) + " " + text);
        var segmented = Assert.Single(ConsoleLinkScanner.Scan(content));
        Assert.Equal(expected, segmented.Target);
        Assert.Equal(1010, segmented.Start);
        Assert.Equal(text.Length, segmented.Length);
    }

    /// <summary>Scanner targets retain paths, folders, line and column, Unicode, and URL parentheses.</summary>
    /// <param name="text">The synthetic message.</param>
    /// <param name="kind">The expected target kind.</param>
    /// <param name="path">The unchanged target path.</param>
    /// <param name="line">The optional line.</param>
    /// <param name="column">The optional column.</param>
    [Theory]
    [InlineData(@"at C:\Demo\Core\ExportWriter.cs(142,18)", LinkKind.File, @"C:\Demo\Core\ExportWriter.cs", 142, 18)]
    [InlineData(@"at C:\Demo\Core\ExportWriter.cs:142:18", LinkKind.File, @"C:\Demo\Core\ExportWriter.cs", 142, 18)]
    [InlineData(@"C:\Demo\file:2:3：完成", LinkKind.File, @"C:\Demo\file", 2, 3)]
    [InlineData("ExportWriter.cs(142,18)", LinkKind.File, "ExportWriter.cs", 142, 18)]
    [InlineData(@"at \\server\share\報表.log:7:2", LinkKind.File, @"\\server\share\報表.log", 7, 2)]
    [InlineData("at \"C:\\Demo\\sample A\\file with spaces\"(4,2)", LinkKind.File, @"C:\Demo\sample A\file with spaces", 4, 2)]
    [InlineData(@"輸出 C:\測試\樣品A\資料.bin。完成", LinkKind.File, @"C:\測試\樣品A\資料.bin", null, null)]
    [InlineData(@"folder C:\Demo\Core\。", LinkKind.Folder, @"C:\Demo\Core\", null, null)]
    [InlineData(@"folder \\server\share\output\", LinkKind.Folder, @"\\server\share\output\", null, null)]
    [InlineData("./artifacts/", LinkKind.Folder, "./artifacts/", null, null)]
    [InlineData(@"src\Core\ExportWriter.cs:9", LinkKind.File, @"src\Core\ExportWriter.cs", 9, null)]
    [InlineData("../資料/報表:12:3", LinkKind.File, "../資料/報表", 12, 3)]
    [InlineData(@"C:\Demo\LICENSE", LinkKind.File, @"C:\Demo\LICENSE", null, null)]
    [InlineData("https://example.test/export_(v2)。完成", LinkKind.Url, "https://example.test/export_(v2)", null, null)]
    [InlineData("(https://example.test/export_(v2)).", LinkKind.Url, "https://example.test/export_(v2)", null, null)]
    public void ScannerPreservesCompleteTargets(string text, LinkKind kind, string path, int? line, int? column)
    {
        var span = Assert.Single(ConsoleLinkScanner.Scan(text));
        Assert.Equal(new LinkTarget(kind, path, line, column), span.Target);
        Assert.InRange(span.Start, 0, text.Length - 1);
        Assert.InRange(span.Length, 1, text.Length - span.Start);
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", text);
        using var snapshot = LogStoreTests.Capture(store);
        Assert.Equal(span, Assert.Single(new ConsoleLinkCache().GetLinks(snapshot, snapshot.Entries[0]).Spans));
    }

    /// <summary>Prefixes, paths, quotes, suffixes, and balanced parentheses can cross read boundaries.</summary>
    /// <param name="offset">The target's absolute offset.</param>
    /// <param name="target">The complete syntax to scan.</param>
    [Theory]
    [InlineData(1021, "https://example.test/export_(v2)。完成")]
    [InlineData(1000, "https://example.test/export_(v2)。完成")]
    [InlineData(1023, @"C:\Demo\file:2:3：完成")]
    [InlineData(1010, "\"C:\\Demo\\file with spaces\"(142,18)")]
    [InlineData(1010, "ExportWriter.cs(142,18)")]
    [InlineData(1023, @"\\server\share\報表.log:7:2")]
    [InlineData(1023, "../資料/報表:12:3")]
    public void SegmentedScannerFindsLinksAcrossChunkBoundaries(int offset, string target)
    {
        var text = new string('x', offset - 1) + " " + target;
        var content = new ChunkedContent(text);
        using var store = LogStoreTests.CreateStore(characters: 128, pending: 128);
        store.Add(new LogWrite(LogLevel.Info, "app", content));
        using var snapshot = LogStoreTests.Capture(store);
        var spans = new ConsoleLinkCache().GetLinks(snapshot, snapshot.Entries[0]).Spans;
        Assert.Equal(ConsoleLinkScanner.Scan(text), spans);
        Assert.Equal(offset, Assert.Single(spans).Start);
        Assert.InRange(content.MaximumRead, 1, 1024);
    }

    /// <summary>URL priority prevents scanning embedded path syntax as a second target.</summary>
    [Fact]
    public void UrlWinsAndTargetsStopAtCjkPunctuation()
    {
        var spans = ConsoleLinkScanner.Scan(@"https://example.test/C:/Demo/export_(v2)。然後 C:\Demo\file:2:3，完成");
        Assert.Equal(2, spans.Length);
        Assert.Equal(LinkKind.Url, spans[0].Target.Kind);
        Assert.Equal("https://example.test/C:/Demo/export_(v2)", spans[0].Target.Path);
        Assert.Equal(new LinkTarget(LinkKind.File, @"C:\Demo\file", 2, 3), spans[1].Target);
    }

    /// <summary>App structured spans, including an empty set, completely replace inferred links.</summary>
    [Fact]
    public void StructuredSpansWinOverScanning()
    {
        using var store = LogStoreTests.CreateStore();
        var target = new LinkTarget(LinkKind.Folder, "app-folder");
        store.Add(new LogWrite(LogLevel.Info, "app", new InMemoryLogTextContent("shown https://example.test"),
            LinkSpans: [new ConsoleLinkSpan(0, 5, target)]));
        store.Add(new LogWrite(LogLevel.Info, "app", new InMemoryLogTextContent("https://example.test"), LinkSpans: []));
        using var snapshot = LogStoreTests.Capture(store);
        var cache = new ConsoleLinkCache();
        Assert.Equal(target, Assert.Single(cache.GetLinks(snapshot, snapshot.Entries[0]).Spans).Target);
        Assert.Empty(cache.GetLinks(snapshot, snapshot.Entries[1]).Spans);
    }

    /// <summary>Cache keys ignore search and invalidate only through snapshot and policy synchronization.</summary>
    [Fact]
    public void CacheReusesLiveEntriesAndRejectsStaleSnapshots()
    {
        using var store = LogStoreTests.CreateStore(2);
        var content = new LogStoreTests.TrackingContent(@"C:\Demo\file:7:2", 16, 42);
        var id = store.Add(new LogWrite(LogLevel.Info, "app", content));
        using var first = LogStoreTests.Capture(store);
        var cache = new ConsoleLinkCache(1);
        var index = cache.GetLinks(first, first.Entries[0]);
        var reads = content.Reads;
        Assert.Same(index, cache.GetLinks(first, first.Entries[0]));
        Assert.Equal(reads, content.Reads);
        using var searched = ConsoleProjector.Project(first, new ConsoleFilter { SearchText = "Demo" }, new ConsoleViewState());
        Assert.Same(index, cache.GetLinks(first, first.Entries[0]));
        store.Add(LogLevel.Info, "app", "second");
        using var second = LogStoreTests.Capture(store);
        Assert.Same(index, cache.GetLinks(second, second.Entries[0]));
        Assert.NotSame(index, cache.GetLinks(second, second.Entries[0], 1));
        store.Add(LogLevel.Info, "app", "third");
        using var third = LogStoreTests.Capture(store);
        cache.Synchronize(third, 1);
        Assert.False(store.IsCurrent(first.Generation, id, 42));
        Assert.NotSame(index, cache.GetLinks(first, first.Entries[0]));
        store.Clear();
        using var cleared = LogStoreTests.Capture(store);
        cache.Synchronize(cleared);
        Assert.False(store.IsCurrent(first.Generation));
    }

    /// <summary>Entry and span budgets evict cached results without truncating returned links.</summary>
    [Fact]
    public void CacheLimitsDoNotTruncateResults()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "https://example.test/1 https://example.test/2");
        using var snapshot = LogStoreTests.Capture(store);
        var cache = new ConsoleLinkCache(1, 1);
        var first = cache.GetLinks(snapshot, snapshot.Entries[0]);
        Assert.Equal(2, first.Spans.Length);
        Assert.NotSame(first, cache.GetLinks(snapshot, snapshot.Entries[0]));
    }

    /// <summary>Interval hit testing has exclusive ends and validates supplied spans.</summary>
    [Fact]
    public void IntervalIndexFindsRowLocalSpans()
    {
        var target = new LinkTarget(LinkKind.File, @"C:\Demo\file", 4, 2);
        var index = new ConsoleLinkIndex([new ConsoleLinkSpan(5, 3, target), new ConsoleLinkSpan(0, 2, target)], 10);
        Assert.Null(index.HitTest(-1));
        Assert.Equal(target, index.HitTest(1)?.Target);
        Assert.Null(index.HitTest(2));
        Assert.Equal(target, index.HitTest(7)?.Target);
        Assert.Null(index.HitTest(8));
        Assert.Throws<ArgumentException>(() => new ConsoleLinkIndex([new ConsoleLinkSpan(9, 2, target)], 10));
        Assert.Throws<ArgumentException>(() => new ConsoleLinkIndex([new ConsoleLinkSpan(0, 3, target), new ConsoleLinkSpan(2, 2, target)], 10));
    }

    /// <summary>Scanner IL and its local helpers reference no System.IO API.</summary>
    [Fact]
    public void ScannerHasNoFileSystemCalls()
    {
        var scanner = typeof(ConsoleLinkScanner);
        var methods = scanner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Concat(scanner.GetNestedTypes(BindingFlags.NonPublic).SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(c => unchecked((ushort)c.Value));
        var inspected = 0;
        foreach (var method in methods)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null) continue;
            for (var offset = 0; offset < il.Length;)
            {
                ushort key = il[offset++];
                if (key == 0xfe) key = (ushort)(0xfe00 | il[offset++]);
                var code = codes[key];
                if (code.OperandType == OperandType.InlineMethod)
                {
                    var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset));
                    Assert.False(called?.DeclaringType?.Namespace?.StartsWith("System.IO", StringComparison.Ordinal) ?? false);
                    inspected++;
                }
                offset += code.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                    _ => 4,
                };
            }
        }
        Assert.True(inspected > 0);
    }

    // The deterministic test thread owns this read counter.
    private sealed class ChunkedContent(string text) : ILogTextContent
    {
        internal int MaximumRead { get; private set; }
        public int Length => text.Length;
        public int ResidentCharacterCount => 64;
        public long Version => 0;
        public void Read(int offset, Span<char> destination)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(destination.Length, 1024);
            MaximumRead = Math.Max(MaximumRead, destination.Length);
            text.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        public void Dispose() { }
    }
}
