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
    public static async Task WriteAsync(
        string path,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Output path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await write(stream, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }
}
