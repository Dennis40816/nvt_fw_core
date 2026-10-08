// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Characterizes strict decoded-character bounds, line termination, and stream ownership.</summary>
public sealed class BoundedUtf8LineReaderTests
{
    /// <summary>Each READY, START and ADMITTED limit accepts its boundary and rejects one character over.</summary>
    [Theory]
    [InlineData(8, 7)]
    [InlineData(8, 8)]
    [InlineData(8, 9)]
    [InlineData(32, 31)]
    [InlineData(32, 32)]
    [InlineData(32, 33)]
    [InlineData(128, 127)]
    [InlineData(128, 128)]
    [InlineData(128, 129)]
    [InlineData(4096, 4095)]
    [InlineData(4096, 4096)]
    [InlineData(4096, 4097)]
    public async Task CharacterBoundaryIsExact(int maximum, int length)
    {
        string text = new('x', length);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text + "\n"));
        string? result = await BoundedUtf8LineReader.ReadAsync(
            stream, maximum, 256, TestContext.Current.CancellationToken);
        Assert.Equal(length <= maximum ? text : null, result);
        Assert.True(stream.CanRead);
    }

    /// <summary>Multibyte UTF-8 is bounded by decoded UTF-16 characters, including surrogate pairs.</summary>
    [Theory]
    [InlineData(128, 127, "界")]
    [InlineData(128, 128, "界")]
    [InlineData(128, 129, "界")]
    [InlineData(4096, 4095, "界")]
    [InlineData(4096, 4096, "界")]
    [InlineData(4096, 4097, "界")]
    [InlineData(128, 63, "😀")]
    [InlineData(128, 64, "😀")]
    [InlineData(128, 65, "😀")]
    public async Task MultibyteInputUsesCharacterCeiling(int maximum, int count, string character)
    {
        string text = string.Concat(Enumerable.Repeat(character, count));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text + "\n"));
        Assert.True(stream.Length > maximum);
        Assert.Equal(text.Length <= maximum ? text : null,
            await BoundedUtf8LineReader.ReadAsync(
                stream, maximum, 256, TestContext.Current.CancellationToken));
    }

    /// <summary>EOF accepts a complete partial line and strips every trailing carriage return.</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("READY:1.2.3", "READY:1.2.3")]
    [InlineData("READY:1.2", "READY:1.2")]
    [InlineData("READY:1.2.3\r", "READY:1.2.3")]
    [InlineData("READY:1.2.3\r\r\nignored", "READY:1.2.3")]
    [InlineData("a\rb\n", "a\rb")]
    [InlineData("\n", "")]
    [InlineData("\uFEFFREADY:1.2.3\n", "\uFEFFREADY:1.2.3")]
    public async Task LineTerminationPreservesFrozenText(string input, string expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        Assert.Equal(expected, await BoundedUtf8LineReader.ReadAsync(
            stream, 128, 256, TestContext.Current.CancellationToken));
        Assert.True(stream.CanRead);
    }

    /// <summary>Carriage returns count toward the bound before trimming.</summary>
    [Theory]
    [InlineData(127, "x")]
    [InlineData(128, null)]
    public async Task CarriageReturnBoundPrecedesTrimming(int carriageReturns, string? expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("x" + new string('\r', carriageReturns)));
        Assert.Equal(expected, await BoundedUtf8LineReader.ReadAsync(
            stream, 128, 256, TestContext.Current.CancellationToken));
    }

    /// <summary>Malformed and truncated byte sequences escape only as strict decoder failures.</summary>
    [Theory]
    [InlineData("C3")]
    [InlineData("C328")]
    [InlineData("C0AF")]
    [InlineData("EDA080")]
    [InlineData("F4908080")]
    [InlineData("80")]
    public async Task InvalidUtf8IsRejected(string hex)
    {
        using var stream = new MemoryStream(Convert.FromHexString(hex));
        _ = await Assert.ThrowsAsync<DecoderFallbackException>(async () =>
            await BoundedUtf8LineReader.ReadAsync(
                stream, 128, 256, TestContext.Current.CancellationToken));
        Assert.True(stream.CanRead);
    }

    /// <summary>The internal helper preserves its source behavior for zero and negative character bounds.</summary>
    [Theory]
    [InlineData(-1, "", null)]
    [InlineData(0, "", "")]
    [InlineData(0, "x", null)]
    [InlineData(1, "x", "x")]
    public async Task HelperCharacterDomainIsPreserved(int maximum, string input, string? expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        Assert.Equal(expected, await BoundedUtf8LineReader.ReadAsync(
            stream, maximum, 1, TestContext.Current.CancellationToken));
    }

    /// <summary>The StreamReader rejects zero and values below its reserved default-buffer sentinel.</summary>
    [Theory]
    [InlineData(-2)]
    [InlineData(0)]
    public async Task InvalidBufferSizeIsRejected(int bufferSize)
    {
        using var stream = new MemoryStream();
        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await BoundedUtf8LineReader.ReadAsync(
                stream, 128, bufferSize, TestContext.Current.CancellationToken));
        Assert.True(stream.CanRead);
    }

    /// <summary>The BCL default-buffer sentinel remains accepted by the frozen helper.</summary>
    [Fact]
    public async Task DefaultBufferSentinelIsAccepted()
    {
        using var stream = new MemoryStream("READY:1.2.3\n"u8.ToArray());
        Assert.Equal("READY:1.2.3", await BoundedUtf8LineReader.ReadAsync(
            stream, 128, -1, TestContext.Current.CancellationToken));
        Assert.True(stream.CanRead);
    }

    /// <summary>An already cancelled read propagates cancellation and keeps stream ownership with its caller.</summary>
    [Fact]
    public async Task CancellationLeavesInputOpen()
    {
        using var stream = new MemoryStream("READY:1.2.3\n"u8.ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await BoundedUtf8LineReader.ReadAsync(stream, 128, 256, cancellation.Token));
        Assert.True(stream.CanRead);
    }
}
