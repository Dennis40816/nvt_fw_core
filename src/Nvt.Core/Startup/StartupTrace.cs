// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;

namespace Nvt.Core.Startup;

/// <summary>Records startup milestones and optionally writes one trace file for the host application.</summary>
public sealed class StartupTrace
{
    private readonly List<StartupTracePoint>? _points;
    private readonly TimeProvider _timeProvider;
    private readonly Func<long> _allocatedBytesProvider;
    private readonly bool _isTimingEnabled;
    private readonly long _originTimestamp;
    private readonly long _originAllocatedBytes;
    private readonly DateTimeOffset _startedUtc;
    private readonly string? _outputPath;
    private bool _isComplete;

    private StartupTrace(
        string? outputPath,
        TimeProvider timeProvider,
        Func<long> allocatedBytesProvider,
        long originTimestamp,
        long originAllocatedBytes,
        DateTimeOffset startedUtc,
        bool isTimingEnabled)
    {
        _outputPath = outputPath;
        _timeProvider = timeProvider;
        _allocatedBytesProvider = allocatedBytesProvider;
        _originTimestamp = originTimestamp;
        _originAllocatedBytes = originAllocatedBytes;
        _startedUtc = startedUtc;
        _isTimingEnabled = isTimingEnabled;
        if (outputPath is not null)
        {
            _points = [new StartupTracePoint("managed-entry", 0, 0, 0)];
        }
    }

    /// <summary>Gets the shared recorder with both recording and timing disabled.</summary>
    public static StartupTrace Disabled { get; } =
        new(null, TimeProvider.System, static () => 0, 0, 0, default, false);

    /// <summary>Gets whether startup milestones are being recorded for file output.</summary>
    public bool IsEnabled => _points is not null;

    /// <summary>Gets the elapsed time since creation, or <see langword="null"/> when timing is disabled.</summary>
    public TimeSpan? ElapsedSinceManagedEntry => _isTimingEnabled
        ? _timeProvider.GetElapsedTime(_originTimestamp, _timeProvider.GetTimestamp())
        : null;

    /// <summary>Reads a host-selected output path variable and enables timing even without a path.</summary>
    /// <param name="outputPathEnvironmentVariable">The environment variable containing the output path.</param>
    /// <returns>A recorder with timing enabled and file output enabled only when a path is present.</returns>
    public static StartupTrace StartFromEnvironment(string outputPathEnvironmentVariable)
    {
        return Create(
            Environment.GetEnvironmentVariable(outputPathEnvironmentVariable),
            measureWithoutOutput: true);
    }

    /// <summary>Creates a recorder using optional timing and allocation providers.</summary>
    /// <param name="outputPath">The file destination; surrounding whitespace is trimmed and blank paths disable output.</param>
    /// <param name="timeProvider">The timing provider, or <see cref="TimeProvider.System"/> by default.</param>
    /// <param name="allocatedBytesProvider">The cumulative allocation provider, or imprecise total GC allocations by default.</param>
    /// <param name="measureWithoutOutput">Whether elapsed time is measured when no output path is present.</param>
    /// <returns>The shared disabled recorder, or a recorder that measures elapsed time.</returns>
    public static StartupTrace Create(
        string? outputPath,
        TimeProvider? timeProvider = null,
        Func<long>? allocatedBytesProvider = null,
        bool measureWithoutOutput = false)
    {
        string? normalizedPath = string.IsNullOrWhiteSpace(outputPath) ? null : outputPath.Trim();
        if (normalizedPath is null && !measureWithoutOutput)
        {
            return Disabled;
        }

        TimeProvider time = timeProvider ?? TimeProvider.System;
        Func<long> allocatedBytes = allocatedBytesProvider ?? GetAllocatedBytes;
        return new StartupTrace(
            normalizedPath,
            time,
            allocatedBytes,
            time.GetTimestamp(),
            allocatedBytes(),
            time.GetUtcNow(),
            true);
    }

    /// <summary>Records a milestone unless recording is disabled or completion has already been attempted.</summary>
    /// <param name="stage">The nonblank stage name.</param>
    /// <exception cref="ArgumentException">The stage is null, empty, or whitespace.</exception>
    public void Mark(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (_points is null || _isComplete)
        {
            return;
        }

        double elapsedMilliseconds = _timeProvider.GetElapsedTime(
            _originTimestamp,
            _timeProvider.GetTimestamp()).TotalMilliseconds;
        long allocatedBytes = Math.Max(0, _allocatedBytesProvider() - _originAllocatedBytes);
        long previousAllocatedBytes = _points[^1].AllocatedBytesSinceManagedEntry;
        _points.Add(new StartupTracePoint(
            stage,
            elapsedMilliseconds,
            allocatedBytes,
            Math.Max(0, allocatedBytes - previousAllocatedBytes)));
    }

    /// <summary>Records a final milestone and attempts to create the trace file once.</summary>
    /// <param name="finalStage">The nonblank final stage name.</param>
    /// <param name="schemaVersion">The schema name selected by the host application.</param>
    /// <param name="writeHostSections">An optional callback that writes root properties after the stages array.</param>
    /// <returns>Whether the file was written; disabled output, repeated completion, and expected write failures return false.</returns>
    /// <remarks>Completion remains final after a write failure. Unexpected callback exceptions propagate.</remarks>
    public bool Complete(
        string finalStage,
        string schemaVersion,
        Action<Utf8JsonWriter>? writeHostSections = null)
    {
        if (_points is null || _outputPath is null || _isComplete)
        {
            return false;
        }

        Mark(finalStage);
        _isComplete = true;
        return StartupTraceFileSink.TryWrite(
            _outputPath,
            schemaVersion,
            _startedUtc,
            _timeProvider.GetUtcNow(),
            _points,
            writeHostSections);
    }

    private static long GetAllocatedBytes()
    {
        return GC.GetTotalAllocatedBytes(precise: false);
    }
}
