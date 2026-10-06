// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Files;

/// <summary>A measured file length exceeds a caller or capture storage limit.</summary>
public sealed class FileSizeLimitExceededException : Exception
{
    /// <summary>Creates a size rejection for a limit set by the caller.</summary>
    /// <param name="observedBytes">The measured file length in bytes.</param>
    /// <param name="maximumBytes">The positive maximum length in bytes.</param>
    public FileSizeLimitExceededException(long observedBytes, long maximumBytes)
        : this(observedBytes, maximumBytes, isCaptureStorageLimit: false)
    {
    }

    /// <summary>Creates a size rejection and identifies the source of the limit.</summary>
    /// <param name="observedBytes">The measured file length in bytes.</param>
    /// <param name="maximumBytes">The positive maximum length in bytes.</param>
    /// <param name="isCaptureStorageLimit">Whether capture storage imposed the limit.</param>
    public FileSizeLimitExceededException(
        long observedBytes,
        long maximumBytes,
        bool isCaptureStorageLimit)
        : base(
            isCaptureStorageLimit
                ? $"File length {observedBytes} exceeds the capture storage limit {maximumBytes} bytes."
                : $"File length {observedBytes} exceeds the resolved maximum {maximumBytes} bytes.")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ObservedBytes = observedBytes;
        MaximumBytes = maximumBytes;
        IsCaptureStorageLimit = isCaptureStorageLimit;
    }

    /// <summary>Gets the file length measured before reading.</summary>
    public long ObservedBytes { get; }

    /// <summary>Gets the inclusive length limit set by the caller or capture storage.</summary>
    public long MaximumBytes { get; }

    /// <summary>Gets whether capture storage imposed the limit.</summary>
    public bool IsCaptureStorageLimit { get; }
}
