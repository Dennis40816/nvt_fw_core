// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Buffers;
using System.Text;
using Nvt.Core.Processes;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>Characterizes capture, chunk, UTF-16, task-start, and cancellation boundaries.</summary>
public sealed class BoundedProcessOutputReaderBoundaryTests
{
    /// <summary>The fixed capture capacity and truncation marker retain their frozen values.</summary>
    [Fact]
    public void CaptureConstantsRetainFrozenValues()
    {
        Assert.Equal(65536, BoundedProcessOutputReader.MaximumCapturedCharacters);
        Assert.Equal("\n...[process output truncated]...\n", BoundedProcessOutputReader.TruncationMarker);
    }

    /// <summary>Empty output and values below, at, and above the prefix and capture boundaries retain exact text.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32767)]
    [InlineData(32768)]
    [InlineData(32769)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    public async Task CaptureBoundariesRetainExactExpectedText(int length)
    {
        string input = CreatePattern(length);
        using var reader = new StringReader(input);

        BoundedProcessOutput actual = await BoundedProcessOutputReader.DrainAsync(reader, TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedAsciiOutput(input), actual.Text);
        Assert.True(actual.ReachedEndOfStream);
        Assert.True(actual.Text.Length <= 65536);
    }

    /// <summary>Reads below, at, and above a buffer boundary always request exactly 4,096 characters.</summary>
    [Theory]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    public async Task ReadBufferBoundariesUseFixedReadRequests(int length)
    {
        string input = CreatePattern(length);
        using var reader = new ChunkedReader(input, int.MaxValue);

        BoundedProcessOutput actual = await BoundedProcessOutputReader.DrainAsync(reader, TestContext.Current.CancellationToken);

        Assert.Equal(input, actual.Text);
        Assert.True(actual.ReachedEndOfStream);
        Assert.All(reader.RequestedLengths, count => Assert.Equal(4096, count));
        Assert.Equal(((length + 4095) / 4096) + 1, reader.RequestedLengths.Count);
    }

    /// <summary>Single delivery and chunked delivery have identical complete and truncated results.</summary>
    [Theory]
    [InlineData(65535, 4096)]
    [InlineData(65536, 4096)]
    [InlineData(65537, 4096)]
    [InlineData(65537, 4095)]
    [InlineData(65537, 4097)]
    [InlineData(65537, 1)]
    [InlineData(196745, 17)]
    public async Task SingleAndChunkedSendsRetainIdenticalOutput(int length, int chunkLength)
    {
        string input = CreatePattern(length);
        using var single = new StringReader(input);
        using var chunked = new ChunkedReader(input, chunkLength);

        BoundedProcessOutput expected = await BoundedProcessOutputReader.DrainAsync(single, TestContext.Current.CancellationToken);
        BoundedProcessOutput actual = await BoundedProcessOutputReader.DrainAsync(chunked, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
        Assert.Equal(ExpectedAsciiOutput(input), actual.Text);
    }

    /// <summary>Complete capture preserves pairs around the read and prefix boundaries without scalar loss.</summary>
    [Theory]
    [InlineData(4094)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(32766)]
    [InlineData(32767)]
    [InlineData(32768)]
    [InlineData(65534)]
    public async Task CompleteCapturePreservesSurrogatePairsAcrossBoundaries(int pairStart)
    {
        string input = PlacePair(new string('A', 65536), pairStart);
        using var reader = new ChunkedReader(input, 4096);

        BoundedProcessOutput actual = await BoundedProcessOutputReader.DrainAsync(reader, TestContext.Current.CancellationToken);

        Assert.Equal(input, actual.Text);
        Assert.True(actual.ReachedEndOfStream);
        AssertWellFormedUtf16(actual.Text);
    }

    /// <summary>Truncated capture keeps whole pairs around read, prefix, and ring-wrap boundaries and drops a split prefix pair.</summary>
    [Theory]
    [InlineData(4094, false)]
    [InlineData(4095, false)]
    [InlineData(4096, false)]
    [InlineData(32766, false)]
    [InlineData(32767, true)]
    [InlineData(32768, false)]
    [InlineData(98302, false)]
    [InlineData(98303, false)]
    [InlineData(98304, false)]
    public async Task TruncationPreservesPairsAroundReadAndPrefixBoundaries(int pairStart, bool dropsPrefixHigh)
    {
        string input = PlacePair(new string('A', 100000), pairStart);
        int prefixLength = dropsPrefixHigh ? 32767 : 32768;
        int tailLength = 65536 - prefixLength - BoundedProcessOutputReader.TruncationMarker.Length;
        string expected = input[..prefixLength] + BoundedProcessOutputReader.TruncationMarker + input[^tailLength..];
        using var reader = new ChunkedReader(input, 4096);

        BoundedProcessOutput actual = await BoundedProcessOutputReader.DrainAsync(reader, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual.Text);
        Assert.True(actual.ReachedEndOfStream);
        AssertWellFormedUtf16(actual.Text);
    }

    /// <summary>Pairs below, at, and above the retained tail start are kept whole or omitted whole.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task TruncationPreservesPairsAroundTailStart(int pairOffset)
    {
        int tailLength = 65536 - 32768 - BoundedProcessOutputReader.TruncationMarker.Length;
        int tailStart = 100000 - tailLength;
        string input = PlacePair(new string('A', 100000), tailStart + pairOffset);
        int expectedStart = pairOffset == -1 ? tailStart + 1 : tailStart;
        string expected = input[..32768] + BoundedProcessOutputReader.TruncationMarker + input[expectedStart..];
        using var reader = new ChunkedReader(input, 4095);

        BoundedProcessOutput actual = await BoundedProcessOutputReader.DrainAsync(reader, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual.Text);
        AssertWellFormedUtf16(actual.Text);
    }

    /// <summary>A final unpaired high surrogate is dropped from truncated output while a complete pair is retained.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncationHandlesSurrogatesAtTailEnd(bool completePair)
    {
        string input = new string('A', 100000) + (completePair ? "\U0001F600" : "\uD83D");
        int tailLength = 65536 - 32768 - BoundedProcessOutputReader.TruncationMarker.Length;
        string expectedTail = completePair ? input[^tailLength..] : input[^tailLength..^1];
        string expected = input[..32768] + BoundedProcessOutputReader.TruncationMarker + expectedTail;
        using var reader = new ChunkedReader(input, 4096);

        BoundedProcessOutput actual = await BoundedProcessOutputReader.DrainAsync(reader, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual.Text);
        AssertWellFormedUtf16(actual.Text);
    }

    /// <summary>Untruncated diagnostics retain source characters even when the input itself contains unpaired surrogates.</summary>
    [Fact]
    public async Task CompleteOutputPreservesSourceUtf16WithoutDecoding()
    {
        const string input = "\uDC00middle\uD800";
        using var reader = new StringReader(input);

        Assert.Equal(input, await BoundedProcessOutputReader.ReadAsync(reader));
    }

    /// <summary>A stopped, oversized drain retains its bounded prefix and tail without reporting end of stream.</summary>
    [Fact]
    public async Task StoppedDrainRetainsTruncatedOutputWithoutEndOfStream()
    {
        string input = CreatePattern(65537);
        using var reader = new HeldChunkedReader(input);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<BoundedProcessOutput> drain = BoundedProcessOutputReader.DrainAsync(reader, stop.Token);
        await reader.AllTextServed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await stop.CancelAsync();
        BoundedProcessOutput actual = await drain.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedAsciiOutput(input), actual.Text);
        Assert.False(actual.ReachedEndOfStream);
    }

    /// <summary>Null reader validation is observed by awaiting DrainAsync and retains its parameter name.</summary>
    [Fact]
    public async Task DrainAsyncRejectsNullReaderWhenAwaited()
    {
        Task<BoundedProcessOutput>? drain = null;
        Exception? synchronousFailure = Record.Exception(() =>
        {
            drain = BoundedProcessOutputReader.DrainAsync(null!, TestContext.Current.CancellationToken);
        });

        Assert.Null(synchronousFailure);
        Assert.NotNull(drain);
        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(() => drain);
        Assert.Equal("reader", exception.ParamName);
    }

    /// <summary>The text-only wrapper preserves null validation as an asynchronous failure.</summary>
    [Fact]
    public async Task ReadAsyncRejectsNullReaderWhenAwaited()
    {
        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BoundedProcessOutputReader.ReadAsync(null!));

        Assert.Equal("reader", exception.ParamName);
    }

    /// <summary>Cancellation from a reader is a failure when the supplied stop token has not fired.</summary>
    [Fact]
    public async Task UnrequestedReadCancellationPropagates()
    {
        var failure = new OperationCanceledException("Synthetic unrelated cancellation.");
        using var reader = new FailingReader(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() =>
            BoundedProcessOutputReader.DrainAsync(reader, TestContext.Current.CancellationToken)));
    }

    /// <summary>Only Windows error 995 combined with a fired stop token is treated as a stopped drain.</summary>
    [Theory]
    [InlineData(995, true)]
    [InlineData(994, true)]
    [InlineData(995, false)]
    public async Task OperationAbortedRequiresWindowsAndStoppedToken(int errorCode, bool stopBeforeFailure)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var failure = new IOException("Synthetic read failure.", unchecked((int)0x80070000) | errorCode);
        using var reader = new FailingReader(failure, stopBeforeFailure ? () => stop.Cancel() : null);

        Task<BoundedProcessOutput> drain = BoundedProcessOutputReader.DrainAsync(reader, stop.Token);

        if (OperatingSystem.IsWindows() && errorCode == 995 && stopBeforeFailure)
        {
            Assert.Equal(new BoundedProcessOutput(string.Empty, false), await drain);
        }
        else
        {
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => drain));
        }
    }

    /// <summary>The process drain uses synchronous reads off the pool on Windows and asynchronous reads elsewhere.</summary>
    [Fact]
    public async Task ProcessDrainUsesPlatformReadPath()
    {
        using var reader = new ChunkedReader("synthetic output", 3);

        BoundedProcessOutput actual = await BoundedProcessOutputReader.DrainProcessStreamAsync(reader, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(new BoundedProcessOutput("synthetic output", true), actual);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(reader.SynchronousReads > 0);
            Assert.Equal(0, reader.AsynchronousReads);
            Assert.False(reader.ReadOnThreadPool);
        }
        else
        {
            Assert.Equal(0, reader.SynchronousReads);
            Assert.True(reader.AsynchronousReads > 0);
        }
    }

    private static string ExpectedAsciiOutput(string input)
    {
        const int capacity = 65536;
        const int prefixLength = 32768;
        int tailLength = capacity - prefixLength - BoundedProcessOutputReader.TruncationMarker.Length;
        return input.Length <= capacity
            ? input
            : input[..prefixLength] + BoundedProcessOutputReader.TruncationMarker + input[^tailLength..];
    }

    private static string CreatePattern(int length)
    {
        return string.Create(length, 0, static (buffer, _) =>
        {
            for (int index = 0; index < buffer.Length; index++)
            {
                buffer[index] = (char)('!' + (index % 90));
            }
        });
    }

    private static string PlacePair(string text, int index)
    {
        return text.Remove(index, 2).Insert(index, "\U0001F600");
    }

    private static void AssertWellFormedUtf16(string value)
    {
        ReadOnlySpan<char> remaining = value;
        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(remaining, out _, out int consumed);
            Assert.Equal(OperationStatus.Done, status);
            remaining = remaining[consumed..];
        }
    }

    private sealed class ChunkedReader(string text, int chunkLength) : TextReader
    {
        private int _position;

        internal List<int> RequestedLengths { get; } = [];
        internal int SynchronousReads { get; private set; }
        internal int AsynchronousReads { get; private set; }
        internal bool ReadOnThreadPool { get; private set; }

        /// <inheritdoc/>
        public override int Read(char[] buffer, int index, int count)
        {
            SynchronousReads++;
            ReadOnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
            return ReadCore(buffer.AsSpan(index, count));
        }

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AsynchronousReads++;
            return ValueTask.FromResult(ReadCore(buffer.Span));
        }

        private int ReadCore(Span<char> buffer)
        {
            RequestedLengths.Add(buffer.Length);
            int count = Math.Min(Math.Min(buffer.Length, chunkLength), text.Length - _position);
            text.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }

    private sealed class HeldChunkedReader(string text) : TextReader
    {
        private readonly TaskCompletionSource _allTextServed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;

        internal Task AllTextServed => _allTextServed.Task;

        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < text.Length)
            {
                int count = Math.Min(buffer.Length, text.Length - _position);
                text.AsSpan(_position, count).CopyTo(buffer.Span);
                _position += count;
                return count;
            }

            _ = _allTextServed.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }

    private sealed class FailingReader(Exception failure, Action? beforeFailure = null) : TextReader
    {
        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            beforeFailure?.Invoke();
            return ValueTask.FromException<int>(failure);
        }
    }
}
