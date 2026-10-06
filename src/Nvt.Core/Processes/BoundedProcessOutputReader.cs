// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Buffers;
using System.Text;

namespace Nvt.Core.Processes;

/// <summary>Continuously drains one process stream while retaining bounded diagnostic context.</summary>
internal static class BoundedProcessOutputReader
{
    internal const int MaximumCapturedCharacters = 64 * 1024;
    internal const string TruncationMarker = "\n...[process output truncated]...\n";

    private const int PrefixLength = MaximumCapturedCharacters / 2;
    private const int ReadBufferLength = 4096;
    private const int TailLength = MaximumCapturedCharacters - PrefixLength;

    internal static async Task<string> ReadAsync(TextReader reader)
    {
        return (await DrainAsync(reader, CancellationToken.None).ConfigureAwait(false)).Text;
    }

    /// <summary>
    /// Windows redirected process pipes are synchronous handles: ReadAsync queues blocking reads on the thread pool.
    /// Drain them on dedicated threads so a busy pool cannot consume the exit grace before a queued read observes EOF.
    /// A caller owns its stream drains and bounds their thread count with invocation capacity, including detached
    /// reads. On Windows, stop interrupts the in-flight pipe read and retains captured text. Other platforms use
    /// the original ReadAsync path; work that does not honor stop still has bounded, detached invocation custody.
    /// </summary>
    internal static Task<BoundedProcessOutput> DrainProcessStreamAsync(TextReader reader, CancellationToken stopToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(reader);
            return Task.Factory.StartNew(
                () =>
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        return DrainAsync(reader, stopToken).GetAwaiter().GetResult();
                    }

                    using var readCancellation = new WindowsSynchronousReadCancellation();
                    return DrainAsync(reader, readCancellation, stopToken).GetAwaiter().GetResult();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }
#pragma warning disable CA1031 // Startup faults must use the same output-read classification as drain faults.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return Task.FromException<BoundedProcessOutput>(exception);
        }
    }

    /// <summary>
    /// Drains until end of stream or until <paramref name="stopToken"/> stops the drain. A stopped drain keeps the
    /// bounded text captured so far and reports that end of stream was not reached; it never throws for the stop.
    /// </summary>
    internal static Task<BoundedProcessOutput> DrainAsync(TextReader reader, CancellationToken stopToken)
    {
        return DrainAsync(reader, readCancellation: null, stopToken);
    }

    private static async Task<BoundedProcessOutput> DrainAsync(
        TextReader reader,
        WindowsSynchronousReadCancellation? readCancellation,
        CancellationToken stopToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        char[] readBuffer = ArrayPool<char>.Shared.Rent(ReadBufferLength);
        char[]? tail = null;
        var prefix = new StringBuilder();
        long totalCharacters = 0;
        int tailCount = 0;
        int tailWriteIndex = 0;
        bool reachedEndOfStream = false;
        try
        {
            while (true)
            {
                int read;
                try
                {
                    stopToken.ThrowIfCancellationRequested();
#pragma warning disable CA1849 // Dedicated process drains must not queue an async-over-sync pipe read on the pool.
                    read = readCancellation is not null && OperatingSystem.IsWindows()
                        ? readCancellation.Read(reader, readBuffer, ReadBufferLength, stopToken)
                        : await reader.ReadAsync(
                            readBuffer.AsMemory(0, ReadBufferLength),
                            stopToken).ConfigureAwait(false);
#pragma warning restore CA1849
                }
                catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
                {
                    break;
                }
                catch (IOException exception) when (stopToken.IsCancellationRequested &&
                    OperatingSystem.IsWindows() && (exception.HResult & 0xFFFF) == 995) // ERROR_OPERATION_ABORTED
                {
                    break;
                }

                if (read == 0)
                {
                    reachedEndOfStream = true;
                    break;
                }

                totalCharacters = totalCharacters > long.MaxValue - read
                    ? long.MaxValue
                    : totalCharacters + read;
                int offset = 0;
                if (prefix.Length < PrefixLength)
                {
                    int prefixCount = Math.Min(PrefixLength - prefix.Length, read);
                    _ = prefix.Append(readBuffer, 0, prefixCount);
                    offset = prefixCount;
                }

                if (offset < read)
                {
                    tail ??= ArrayPool<char>.Shared.Rent(TailLength);
                    AppendTail(
                        readBuffer.AsSpan(offset, read - offset),
                        tail.AsSpan(0, TailLength),
                        ref tailWriteIndex,
                        ref tailCount);
                }
            }

            string text = totalCharacters <= MaximumCapturedCharacters
                ? CreateCompleteOutput(prefix, tail, tailCount)
                : CreateTruncatedOutput(prefix, tail!, tailWriteIndex);
            return new BoundedProcessOutput(text, reachedEndOfStream);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(readBuffer, clearArray: true);
            if (tail is not null)
            {
                ArrayPool<char>.Shared.Return(tail, clearArray: true);
            }
        }
    }

    private static void AppendTail(
        ReadOnlySpan<char> source,
        Span<char> tail,
        ref int writeIndex,
        ref int count)
    {
        if (source.IsEmpty)
        {
            return;
        }

        if (source.Length >= tail.Length)
        {
            source[^tail.Length..].CopyTo(tail);
            writeIndex = 0;
            count = tail.Length;
            return;
        }

        int firstCount = Math.Min(source.Length, tail.Length - writeIndex);
        source[..firstCount].CopyTo(tail[writeIndex..]);
        source[firstCount..].CopyTo(tail);
        writeIndex = (writeIndex + source.Length) % tail.Length;
        count = Math.Min(tail.Length, count + source.Length);
    }

    private static string CreateCompleteOutput(StringBuilder prefix, char[]? tail, int tailCount)
    {
        return tailCount == 0
            ? prefix.ToString()
            : string.Concat(prefix.ToString(), new string(tail!, 0, tailCount));
    }

    private static string CreateTruncatedOutput(StringBuilder prefix, char[] tail, int tailWriteIndex)
    {
        if (prefix.Length > 0 && char.IsHighSurrogate(prefix[^1]))
        {
            prefix.Length--;
        }

        int retainedTailLength = MaximumCapturedCharacters - prefix.Length - TruncationMarker.Length;
        int retainedTailStart = (tailWriteIndex + TailLength - retainedTailLength) % TailLength;
        if (retainedTailLength > 0 && char.IsLowSurrogate(tail[retainedTailStart]))
        {
            retainedTailStart = (retainedTailStart + 1) % TailLength;
            retainedTailLength--;
        }

        if (retainedTailLength > 0)
        {
            int retainedTailEnd = (retainedTailStart + retainedTailLength - 1) % TailLength;
            if (char.IsHighSurrogate(tail[retainedTailEnd]))
            {
                retainedTailLength--;
            }
        }

        var result = new StringBuilder(MaximumCapturedCharacters);
        _ = result.Append(prefix);
        _ = result.Append(TruncationMarker);
        int firstCount = Math.Min(retainedTailLength, TailLength - retainedTailStart);
        _ = result.Append(tail, retainedTailStart, firstCount);
        _ = result.Append(tail, 0, retainedTailLength - firstCount);
        return result.ToString();
    }
}
