// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using Nvt.Core.Files;
using Xunit;

namespace Nvt.Core.Tests.Files;

/// <summary>Tests the unchanged capture-storage ceiling without widening its allocation bound.</summary>
public sealed class BoundedFileReaderStorageBoundaryTests
{
    /// <summary>Capture admits the exact array ceiling and one below, and rejects one above before reading.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ReadAndHashAsyncEnforcesCaptureStorageBoundary(int delta)
    {
        long length = (long)Array.MaxLength + delta;
        await using var stream = new GeneratedReadStream(length);
        if (delta > 0)
        {
            FileSizeLimitExceededException exception = await Assert.ThrowsAsync<FileSizeLimitExceededException>(() =>
                BoundedFileReader.ReadAndHashAsync(stream, length, FileCaptureMode.CaptureBytes,
                    TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(length, exception.ObservedBytes);
            Assert.Equal(Array.MaxLength, exception.MaximumBytes);
            Assert.True(exception.IsCaptureStorageLimit);
            Assert.Equal(0, stream.ReadCalls);
        }
        else
        {
            Assert.SkipUnless(
                Environment.GetEnvironmentVariable("NVT_CORE_TEST_LARGE_CAPTURE") == "1" &&
                Environment.Is64BitProcess && GC.GetGCMemoryInfo().TotalAvailableMemoryBytes >= 3L * Array.MaxLength,
                "Near-Array.MaxLength capture requires a dedicated 64-bit host with at least 6 GiB available to the runtime; set NVT_CORE_TEST_LARGE_CAPTURE=1 there.");

            BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
                stream, length, FileCaptureMode.CaptureBytes, TestContext.Current.CancellationToken);
            Assert.Equal(length, result.Length);
            Assert.NotNull(result.Bytes);
            Assert.Equal(length, result.Bytes.LongLength);
            Assert.Equal((byte)0xA5, result.Bytes[0]);
            Assert.Equal((byte)0xA5, result.Bytes[^1]);
            Assert.Equal(SHA256.HashData(result.Bytes), result.Sha256);
            Assert.Equal(length, stream.TotalBytesRead);
            Assert.Equal(((length + 65535) / 65536) + 1, stream.ReadCalls);
        }
    }

    /// <summary>Cancellation wins before capture allocation below, at, and above the unchanged array ceiling.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ReadAndHashAsyncCancelsBeforeCaptureStorageBoundary(int delta)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        long length = (long)Array.MaxLength + delta;
        await using var stream = new GeneratedReadStream(length);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedFileReader.ReadAndHashAsync(stream, length, FileCaptureMode.CaptureBytes, cancellation.Token).AsTask());

        Assert.Equal(0, stream.ReadCalls);
    }
}
