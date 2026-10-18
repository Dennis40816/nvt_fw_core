// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Verification;

// Borrows the package stream only for its bounded hash read. Files retains all exact-read checks.
// Disposal leaves the caller's stream open for the ZIP reader and its existing custody owner.
internal sealed class PackageReadProgressStream(
    Stream source, long length, Action<long, long> progress) : Stream
{
    private long _completed;

    public override bool CanRead => source.CanRead;
    public override bool CanSeek => source.CanSeek;
    public override bool CanWrite => false;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => source.Position = value; }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        ReportRead(read);
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        ReportRead(read);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void ReportRead(int read)
    {
        if (read > 0 && read <= length - _completed)
        {
            _completed = checked(_completed + read);
            progress(_completed, length);
        }
    }
}
