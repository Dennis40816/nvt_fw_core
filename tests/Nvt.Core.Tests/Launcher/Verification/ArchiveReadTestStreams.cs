// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Tests.Launcher.Verification;

// Supplies expanded bytes without exposing seek, position or length metadata.
internal sealed class GeneratedExpandedStream(long readableLength, int maximumReadSize = int.MaxValue) : Stream
{
    internal long TotalBytesRead { get; private set; }
    internal int ReadCalls { get; private set; }
    internal int MaximumRequested { get; private set; }
    internal int LastRequested { get; private set; }
    internal CancellationToken LastToken { get; private set; }
    internal bool WasDisposed { get; private set; }
    internal int FaultOnRead { get; init; }
    internal Exception? ReadFault { get; init; }

    public override bool CanRead => !WasDisposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ReadCalls++;
        LastRequested = buffer.Length;
        MaximumRequested = Math.Max(MaximumRequested, buffer.Length);
        LastToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadCalls == FaultOnRead)
        {
            return ValueTask.FromException<int>(ReadFault!);
        }

        int read = (int)Math.Min(Math.Min(buffer.Length, maximumReadSize), readableLength - TotalBytesRead);
        buffer.Span[..read].Fill(0xA5);
        TotalBytesRead += read;
        return ValueTask.FromResult(read);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class GatedExpandedStream : MemoryStream
{
    internal TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ReadStarted.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}

internal sealed class FaultingWriteStream(IOException fault) : MemoryStream
{
    internal CancellationToken LastToken { get; private set; }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        LastToken = cancellationToken;
        return ValueTask.FromException(fault);
    }
}
