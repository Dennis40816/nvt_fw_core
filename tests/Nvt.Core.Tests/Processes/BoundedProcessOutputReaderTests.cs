// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Buffers;
using System.IO.Pipes;
using System.Text;
using Nvt.Core.Processes;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>Contracts for bounded, deadlock-free external-process diagnostics.</summary>
public sealed class BoundedProcessOutputReaderTests
{
    /// <summary>Startup validation is a reader task fault, never a synchronous exception escaping the runner.</summary>
    [Fact]
    public async Task ProductionDrainStartupFailureReturnsFaultedTask()
    {
        Task<BoundedProcessOutput>? drain = null;
        Exception? synchronousFailure = Record.Exception(() =>
        {
            drain = BoundedProcessOutputReader.DrainProcessStreamAsync(null!, TestContext.Current.CancellationToken);
        });

        Assert.Null(synchronousFailure);
        Assert.NotNull(drain);
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => drain);
    }

    /// <summary>Stop before dedicated startup does not deadlock an inline cancellation registration.</summary>
    [Fact]
    public async Task ProductionDrainAlreadyStoppedReturnsWithoutReading()
    {
        using var reader = new StringReader("must not be read");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await stop.CancelAsync();

        BoundedProcessOutput result = await BoundedProcessOutputReader.DrainProcessStreamAsync(reader, stop.Token)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(new BoundedProcessOutput(string.Empty, false), result);
    }

    /// <summary>Stop between entering Read and the kernel pipe read must not be lost.</summary>
    [Fact]
    public async Task ProductionDrainStopsWhenCancellationPrecedesKernelRead()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This race exercises CancelSynchronousIo on a synchronous Windows pipe.");
        }

        using var writer = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        using var pipeReader = new StreamReader(new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle));
        using var gated = new BeforeKernelReadReader(pipeReader);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<BoundedProcessOutput> drain = BoundedProcessOutputReader.DrainProcessStreamAsync(gated, stop.Token);
        Task stopping = Task.CompletedTask;
        try
        {
            await gated.Entered.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            stopping = stop.CancelAsync();
            Assert.True(stop.IsCancellationRequested);
            gated.Open();

            BoundedProcessOutput result = await drain.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await stopping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(new BoundedProcessOutput(string.Empty, false), result);
        }
        finally
        {
            gated.Open();
            // EOF releases even a regressed uncancellable drain, so the test never leaves a blocked thread.
            writer.Dispose();
            _ = await drain.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await stopping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    private sealed class BeforeKernelReadReader(TextReader reader) : TextReader
    {
        private readonly ManualResetEventSlim _open = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Entered => _entered.Task;

        internal void Open()
        {
            _open.Set();
        }

        /// <inheritdoc/>
        public override int Read(char[] buffer, int index, int count)
        {
            Assert.False(Thread.CurrentThread.IsThreadPoolThread);
            _entered.SetResult();
            _open.Wait();
            return reader.Read(buffer, index, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _open.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Small process output remains byte-for-character exact.</summary>
    [Fact]
    public async Task SmallOutputRemainsExact()
    {
        const string expected = "first line\r\nsecond line";
        using var reader = new StringReader(expected);

        string actual = await BoundedProcessOutputReader.ReadAsync(reader);

        Assert.Equal(expected, actual);
    }

    /// <summary>A stopped drain keeps the captured text and reports that end of stream was not reached.</summary>
    [Fact]
    public async Task StoppedDrainKeepsCapturedTextWithoutEndOfStream()
    {
        using var reader = new HeldOpenReader("partial");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<BoundedProcessOutput> drain = BoundedProcessOutputReader.DrainAsync(reader, stop.Token);
        await reader.FirstChunkServed.WaitAsync(TestContext.Current.CancellationToken);

        await stop.CancelAsync();
        BoundedProcessOutput actual = await drain.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("partial", actual.Text);
        Assert.False(actual.ReachedEndOfStream);
    }

    /// <summary>A drain that reaches end of stream reports it even when a stop token is supplied.</summary>
    [Fact]
    public async Task CompletedDrainReportsEndOfStream()
    {
        using var reader = new StringReader("complete");

        BoundedProcessOutput actual = await BoundedProcessOutputReader.DrainAsync(reader, TestContext.Current.CancellationToken);

        Assert.Equal("complete", actual.Text);
        Assert.True(actual.ReachedEndOfStream);
    }
    /// <summary>Output exactly at the cap remains complete and receives no truncation marker.</summary>
    [Fact]
    public async Task ExactCaptureLimitRemainsExact()
    {
        string expected = new('X', BoundedProcessOutputReader.MaximumCapturedCharacters);
        using var reader = new StringReader(expected);

        string actual = await BoundedProcessOutputReader.ReadAsync(reader);

        Assert.Equal(expected, actual);
        Assert.DoesNotContain(BoundedProcessOutputReader.TruncationMarker, actual, StringComparison.Ordinal);
    }

    /// <summary>The first truncated character retains the exact prefix, marker, and suffix.</summary>
    [Fact]
    public async Task FirstTruncatedCharacterRetainsExactPrefixAndTail()
    {
        string input = CreatePattern(BoundedProcessOutputReader.MaximumCapturedCharacters + 1);
        int prefixLength = BoundedProcessOutputReader.MaximumCapturedCharacters / 2;
        int tailLength = BoundedProcessOutputReader.MaximumCapturedCharacters -
            prefixLength -
            BoundedProcessOutputReader.TruncationMarker.Length;
        string expected = input[..prefixLength] +
            BoundedProcessOutputReader.TruncationMarker +
            input[^tailLength..];
        using var reader = new StringReader(input);

        string actual = await BoundedProcessOutputReader.ReadAsync(reader);

        Assert.Equal(expected, actual);
    }

    /// <summary>Multiple ring wraps preserve the exact deterministic suffix ordering.</summary>
    [Fact]
    public async Task MultipleRingWrapsRetainExactOrderedTail()
    {
        string input = CreatePattern((BoundedProcessOutputReader.MaximumCapturedCharacters * 3) + 137);
        int prefixLength = BoundedProcessOutputReader.MaximumCapturedCharacters / 2;
        int tailLength = BoundedProcessOutputReader.MaximumCapturedCharacters -
            prefixLength -
            BoundedProcessOutputReader.TruncationMarker.Length;
        string expected = input[..prefixLength] +
            BoundedProcessOutputReader.TruncationMarker +
            input[^tailLength..];
        using var reader = new StringReader(input);

        string actual = await BoundedProcessOutputReader.ReadAsync(reader);

        Assert.Equal(expected, actual);
    }

    /// <summary>A surrogate split at the prefix boundary is omitted rather than published unpaired.</summary>
    [Fact]
    public async Task TruncationDoesNotRetainUnpairedHighSurrogateBeforeMarker()
    {
        int prefixLength = BoundedProcessOutputReader.MaximumCapturedCharacters / 2;
        string expectedPrefix = new('P', prefixLength - 1);
        string input = expectedPrefix + "\U0001F600" + new string('T', BoundedProcessOutputReader.MaximumCapturedCharacters);
        int tailLength = BoundedProcessOutputReader.MaximumCapturedCharacters -
            expectedPrefix.Length -
            BoundedProcessOutputReader.TruncationMarker.Length;
        string expected = expectedPrefix +
            BoundedProcessOutputReader.TruncationMarker +
            new string('T', tailLength);
        using var reader = new StringReader(input);

        string actual = await BoundedProcessOutputReader.ReadAsync(reader);

        Assert.Equal(expected, actual);
        AssertWellFormedUtf16(actual);
    }

    /// <summary>A retained tail starts at the next scalar when its raw boundary lands on a low surrogate.</summary>
    [Fact]
    public async Task TruncationDoesNotRetainUnpairedLowSurrogateAtTailStart()
    {
        int prefixLength = BoundedProcessOutputReader.MaximumCapturedCharacters / 2;
        int rawTailLength = BoundedProcessOutputReader.MaximumCapturedCharacters -
            prefixLength -
            BoundedProcessOutputReader.TruncationMarker.Length;
        string input = new string('A', BoundedProcessOutputReader.MaximumCapturedCharacters) +
            "\U0001F600" +
            new string('T', rawTailLength - 1);
        string expected = new string('A', prefixLength) +
            BoundedProcessOutputReader.TruncationMarker +
            new string('T', rawTailLength - 1);
        using var reader = new StringReader(input);

        string actual = await BoundedProcessOutputReader.ReadAsync(reader);

        Assert.Equal(expected, actual);
        AssertWellFormedUtf16(actual);
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
    /// <summary>Serves one chunk, then behaves like a pipe whose writer never closes.</summary>
    private sealed class HeldOpenReader(string firstChunk) : TextReader
    {
        private readonly TaskCompletionSource _firstChunkServed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _served;

        internal Task FirstChunkServed => _firstChunkServed.Task;

        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (!_served)
            {
                _served = true;
                firstChunk.AsSpan().CopyTo(buffer.Span);
                _ = _firstChunkServed.TrySetResult();
                return firstChunk.Length;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }
}
