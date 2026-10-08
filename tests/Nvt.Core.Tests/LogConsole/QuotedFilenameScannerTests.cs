// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Quoted filename and location syntax across read boundaries.</summary>
public sealed class QuotedFilenameScannerTests
{
    /// <summary>Quoted names need no separator when a location suffix identifies the path.</summary>
    /// <param name="name">The full filename, including spaces and Unicode.</param>
    /// <param name="suffix">The line and column syntax.</param>
    [Theory]
    [InlineData("Export Writer.cs", "(142,18)")]
    [InlineData("Export Writer.cs", ":142:18")]
    [InlineData("報表 檔案.cs", "(142,18)")]
    [InlineData("報表 檔案.cs", ":142:18")]
    [InlineData("𠀀 報表.cs", "(142,18)")]
    [InlineData("𠀀 報表.cs", ":142:18")]
    public void QuotedFilenameWithLocationHasScannerParityAcrossChunkBoundaries(string name, string suffix)
    {
        foreach (var quote in new[] { '\'', '"' })
        {
            var syntax = quote + name + quote + suffix;
            // Move the 1,024-character read boundary through every interior name and suffix position.
            foreach (var split in Enumerable.Range(0, syntax.Length))
            {
                var padding = split == 0 ? 0 : 1024 - split;
                var text = new string(' ', padding) + syntax + "。完成";
                var expected = new ConsoleLinkSpan(padding, syntax.Length, new LinkTarget(LinkKind.File, name, 142, 18));
                using var content = new ScannerTestContent(text, 0);
                Assert.Equal(expected, Assert.Single(ConsoleLinkScanner.Scan(text)));
                Assert.Equal(expected, Assert.Single(ConsoleLinkScanner.Scan(content)));
                Assert.InRange(content.MaximumRead, 1, 1024);
            }
        }
    }

    /// <summary>Quoting a filename alone supplies no path evidence.</summary>
    /// <param name="name">The name without a path separator or location suffix.</param>
    [Theory]
    [InlineData("Export Writer.cs")]
    [InlineData("報表 檔案.cs")]
    [InlineData("𠀀 報表.cs")]
    public void QuotedFilenameWithoutLocationOrSeparatorIsNotALink(string name)
    {
        foreach (var quote in new[] { '\'', '"' })
        {
            var text = new string(' ', 1020) + quote + name + quote + "。完成";
            using var content = new ScannerTestContent(text, 0);
            Assert.Empty(ConsoleLinkScanner.Scan(text));
            Assert.Empty(ConsoleLinkScanner.Scan(content));
        }
    }
}
