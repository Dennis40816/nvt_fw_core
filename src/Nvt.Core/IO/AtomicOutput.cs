// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.IO;

/// <summary>Writes an ordinary output through a temporary file in the destination directory.</summary>
public static class AtomicOutput
{
    /// <summary>Publishes the output after the writer completes, flushing and checking cancellation first.</summary>
    /// <param name="path">The destination path. Its parent directory is created if needed.</param>
    /// <param name="write">The delegate that writes to the temporary stream without closing it.</param>
    /// <param name="cancellationToken">The token passed to the writer and checked before publication.</param>
    /// <returns>A task that completes when the output has been published.</returns>
    public static Task WriteAsync(
        string path,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken = default)
    {
        return PublishAsync(path, write, byteContract: false, CreateTemporaryStream, cancellationToken);
    }

    /// <summary>Publishes bytes through a sibling temporary file after flushing them to disk and checking cancellation.</summary>
    /// <param name="path">The destination path. Its parent directory is created if needed.</param>
    /// <param name="bytes">The bytes to write without additional encoding or transformation.</param>
    /// <param name="cancellationToken">The token passed to writing and flushing and checked before publication.</param>
    /// <returns>A task that completes when the bytes have been published.</returns>
    /// <remarks>
    /// The write and flush awaits do not capture the current context. Cancellation is not checked between writing and flushing.
    /// Temporary-file cleanup suppresses <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/>.
    /// </remarks>
    public static Task WriteBytesAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        return WriteBytesAsync(path, bytes, CreateTemporaryStream, cancellationToken);
    }

    // Per-call factories let tests inject stream faults without changing global state.
    internal static Task WriteBytesAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        Func<string, FileStream> createStream,
        CancellationToken cancellationToken = default)
    {
        return PublishAsync(path, (stream, token) => stream.WriteAsync(bytes, token).AsTask(),
            byteContract: true, createStream, cancellationToken);
    }

    internal static Task WriteAsync(
        string path,
        Func<Stream, CancellationToken, Task> write,
        Func<string, FileStream> createStream,
        CancellationToken cancellationToken = default)
    {
        return PublishAsync(path, write, byteContract: false, createStream, cancellationToken);
    }

    private static FileStream CreateTemporaryStream(string temporaryPath)
    {
        return new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
    }

    private static async Task PublishAsync(
        string path,
        Func<Stream, CancellationToken, Task> write,
        bool byteContract,
        Func<string, FileStream> createStream,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Output path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var stream = createStream(temporaryPath);
            if (byteContract)
            {
                // The frozen NFC loop does not configure the await of DisposeAsync.
                await using (stream)
                {
                    await write(stream, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
            }
            else
            {
                await using (stream)
                {
                    await write(stream, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch when (!byteContract)
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
        finally
        {
            if (byteContract && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
