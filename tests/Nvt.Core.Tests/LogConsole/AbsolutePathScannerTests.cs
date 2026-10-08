// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Absolute prefixes beside CJK prose across scanner boundaries.</summary>
public sealed class AbsolutePathScannerTests
{
    /// <summary>Both scanner entry points prioritize absolute prefixes adjoining CJK prose.</summary>
    /// <param name="prose">The adjacent prose.</param>
    /// <param name="path">The absolute path.</param>
    [Theory]
    [InlineData("檔案", @"C:\Demo\file.cs")]
    [InlineData("開啟", @"\\server\share\檔案.log")]
    [InlineData("𠀀", @"D:/Demo/檔案.log")]
    public void AbsolutePathsBesideCjkHaveScannerParity(string prose, string path)
    {
        // Place the read boundary at each interior position of the drive or UNC prefix.
        foreach (var prefixOffset in new[] { 0, 1, 2 })
        {
            var padding = prefixOffset == 0 ? 0 : 1024 - prose.Length - prefixOffset;
            var text = new string(' ', padding) + prose + path + "。完成";
            var expected = new ConsoleLinkSpan(padding + prose.Length, path.Length, new LinkTarget(LinkKind.File, path));
            using var content = new TestContent(text, 0);
            Assert.Equal(expected, Assert.Single(ConsoleLinkScanner.Scan(text)));
            Assert.Equal(expected, Assert.Single(ConsoleLinkScanner.Scan(content)));
            Assert.InRange(content.MaximumRead, 1, 1024);
        }
    }
}
