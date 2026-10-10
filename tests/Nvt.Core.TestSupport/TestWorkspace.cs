// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.ExceptionServices;

namespace Nvt.Core.TestSupport;

/// <summary>Owns a short, unique temporary directory for synthetic test fixtures and bounded cleanup.</summary>
public sealed class TestWorkspace : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan _retryBudget = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan _retryInterval = TimeSpan.FromMilliseconds(50);
    private const int MaximumAttempts = 10;
    private readonly Lock _gate = new();
    private readonly Action<string> _delete;
    private readonly Action<TimeSpan> _waitForRetry;
    private readonly Func<TimeSpan>? _retryElapsed;
    private bool _disposed;
    private ExceptionDispatchInfo? _cleanupFailure;

    internal TestWorkspace(string rootPath, Action<string> delete, Action<TimeSpan> waitForRetry,
        Func<TimeSpan>? retryElapsed = null)
    {
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!RootPath.StartsWith(temp, PathComparison) ||
            string.Equals(Path.TrimEndingDirectorySeparator(RootPath), Path.TrimEndingDirectorySeparator(temp), PathComparison))
        {
            throw new ArgumentException("The workspace must be a directory below the temporary directory.", nameof(rootPath));
        }
        _delete = delete;
        _waitForRetry = waitForRetry;
        _retryElapsed = retryElapsed;
    }

    /// <summary>Creates a unique directory under the OS temporary directory, with a short <c>nvt-</c> name.</summary>
    public static TestWorkspace Create()
    {
        string root = Directory.CreateTempSubdirectory("nvt-").FullName;
        try
        {
            return new TestWorkspace(root, static path => Directory.Delete(path, recursive: true), WaitForRetry);
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    /// <summary>Gets the absolute path of the owned temporary directory.</summary>
    public string RootPath { get; }

    /// <summary>Resolves a relative fixture path below the workspace without creating files or directories.</summary>
    /// <remarks>
    /// Both slash forms are separators. Rooted, drive/UNC, colon-containing, empty and parent-segment paths are rejected.
    /// Validation is lexical; fixtures must not introduce symlinks or junctions to data outside the workspace.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The path is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The path is empty, rooted, invalid or contains a parent segment.</exception>
    public string GetPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath[0] is '/' or '\\' || relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Use a relative path below the workspace.", nameof(relativePath));
        }
        string normalized = relativePath.Replace('\\', '/');
        foreach (string segment in normalized.Split('/'))
        {
            // Windows trims trailing dots/spaces; reject ambiguous parent spellings on every OS.
            if (segment == ".." || segment.TrimEnd(' ') == ".." ||
                (segment.Length > 0 && segment.Trim(' ', '.').Length == 0 && segment != "."))
            {
                throw new ArgumentException("Parent segments are not allowed.", nameof(relativePath));
            }
        }
        string resolved = Path.GetFullPath(Path.Combine(RootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = RootPath + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, PathComparison) && !string.Equals(resolved, RootPath, PathComparison))
        {
            throw new ArgumentException("The path escapes the workspace.", nameof(relativePath));
        }
        return resolved;
    }

    private void DisposeCore()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                _cleanupFailure?.Throw();
                return;
            }
            long started = TimeProvider.System.GetTimestamp();
            Exception? failure = null;
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                try
                {
                    _delete(RootPath);
                    _disposed = true;
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    _disposed = true;
                    return;
                }
                catch (IOException exception)
                {
                    failure = exception;
                }
                catch (UnauthorizedAccessException exception)
                {
                    failure = exception;
                }
                TimeSpan remaining = _retryBudget - (_retryElapsed?.Invoke() ?? TimeProvider.System.GetElapsedTime(started));
                if (attempt == MaximumAttempts - 1 || remaining <= TimeSpan.Zero)
                {
                    break;
                }
                _waitForRetry(remaining < _retryInterval ? remaining : _retryInterval);
                if ((_retryElapsed?.Invoke() ?? TimeProvider.System.GetElapsedTime(started)) >= _retryBudget)
                {
                    break;
                }
            }
            _cleanupFailure = ExceptionDispatchInfo.Capture(
                new IOException($"Could not clean up test workspace. Retained path: {RootPath}", failure));
            _disposed = true;
            _cleanupFailure.Throw();
        }
    }

    /// <summary>Deletes the directory, retrying sharing/access failures for at most ten attempts within 500 ms.</summary>
    /// <remarks>
    /// Disposal is idempotent, including failure: later calls report the same retained-path exception.
    /// The retry budget bounds retries and waits; an OS filesystem call already in progress cannot be interrupted.
    /// </remarks>
    /// <exception cref="IOException">Cleanup failed; the exception identifies the retained directory.</exception>
    public void Dispose() => DisposeCore();

    /// <summary>Performs the same synchronous, bounded cleanup as <see cref="Dispose"/>.</summary>
    /// <exception cref="IOException">Cleanup failed; the exception identifies the retained directory.</exception>
    public ValueTask DisposeAsync()
    {
        DisposeCore();
        return ValueTask.CompletedTask;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // Production cleanup waits in real time (at most 50 ms per attempt); tests drive the internal hook instead.
    private static void WaitForRetry(TimeSpan duration)
    {
        using var signal = new ManualResetEventSlim();
        _ = signal.WaitHandle.WaitOne(duration);
    }
}
