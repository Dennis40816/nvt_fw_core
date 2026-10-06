// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files;

namespace Nvt.Core.Launcher.Persistence;

internal static class BoundedStateFile
{
    internal static async ValueTask<byte[]?> ReadAsync(
        string path, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long admittedLength = stream.Length;
        if (admittedLength is < 1 || admittedLength > maximumBytes)
        {
            return null;
        }
        try
        {
            BoundedReadResult result = await BoundedFileReader.ReadAndHashAsync(
                stream, admittedLength, FileCaptureMode.CaptureBytes, cancellationToken).ConfigureAwait(false);
            return result.Bytes;
        }
        catch (FileChangedDuringReadException exception) when (exception.ChangeKind == FileChangeKind.ShortRead)
        {
            // ReadExactlyAsync in the source throws this type when content ends prematurely.
            throw new EndOfStreamException("Unable to read beyond the end of the stream.");
        }
        catch (FileChangedDuringReadException)
        {
            return null;
        }
    }
}
