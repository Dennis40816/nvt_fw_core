// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Nvt.Core.Startup;
using Xunit;

namespace Nvt.Core.Tests.Startup;

/// <summary>Verifies startup timing, ordered JSON output, and safe file creation using synthetic data.</summary>
public sealed class StartupTraceTests
{
    /// <summary>Writes deterministic ordered milestones, allocation values, and host sections.</summary>
    [Fact]
    public async Task EnabledTraceWritesOrderedDeterministicStages()
    {
        using var workspace = new TestWorkspace();
        string outputPath = workspace.PathFor("startup.json");
        long origin = 1234;
        DateTimeOffset started = new(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);
        var time = new FakeTimeProvider(
            [origin, origin + Stopwatch.Frequency, origin + (2 * Stopwatch.Frequency)],
            [started, started.AddSeconds(3)]);
        var allocations = new Queue<long>([100, 260, 500]);
        StartupTrace trace = StartupTrace.Create("  " + outputPath + "  ", time, allocations.Dequeue);

        trace.Mark("options.ready");
        bool written = trace.Complete("window.opened", "test-trace-v1", WriteHostStages);

        Assert.True(trace.IsEnabled);
        Assert.True(written);
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        JsonElement root = document.RootElement;
        Assert.Equal("test-trace-v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(Environment.ProcessId, root.GetProperty("processId").GetInt32());
        Assert.Equal(RuntimeInformation.FrameworkDescription, root.GetProperty("runtime").GetString());
        Assert.Equal(RuntimeInformation.OSArchitecture.ToString(), root.GetProperty("osArchitecture").GetString());
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString(), root.GetProperty("processArchitecture").GetString());
        JsonElement[] stages = [.. root.GetProperty("stages").EnumerateArray()];
        Assert.Equal(["managed-entry", "options.ready", "window.opened"],
            stages.Select(stage => stage.GetProperty("name").GetString()));
        Assert.Equal([0d, 1000d, 2000d],
            stages.Select(stage => stage.GetProperty("elapsedMilliseconds").GetDouble()));
        Assert.Equal([0d, 1000d, 1000d],
            stages.Select(stage => stage.GetProperty("deltaMilliseconds").GetDouble()));
        Assert.Equal([0L, 160L, 400L],
            stages.Select(stage => stage.GetProperty("allocatedBytesSinceManagedEntry").GetInt64()));
        Assert.Equal([0L, 160L, 240L],
            stages.Select(stage => stage.GetProperty("allocationDeltaBytes").GetInt64()));
        JsonElement[] hostStages = [.. root.GetProperty("hostStages").EnumerateArray()];
        Assert.Equal(["host-first", "host-second"],
            hostStages.Select(stage => stage.GetProperty("id").GetString()));
        Assert.Equal("Succeeded", hostStages[0].GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, hostStages[0].GetProperty("completedWork").ValueKind);
        Assert.Equal(JsonValueKind.Null, hostStages[0].GetProperty("totalWork").ValueKind);
        Assert.Equal(5, hostStages[1].GetProperty("completedWork").GetInt64());
        Assert.Equal(5, hostStages[1].GetProperty("totalWork").GetInt64());
        Assert.Equal(started, root.GetProperty("startedUtc").GetDateTimeOffset());
        Assert.Equal(started.AddSeconds(3), root.GetProperty("completedUtc").GetDateTimeOffset());
    }

    /// <summary>An existing destination remains byte-for-byte unchanged after completion is attempted.</summary>
    [Fact]
    public async Task TraceNeverOverwritesAnExistingFileOrBlocksStartup()
    {
        using var workspace = new TestWorkspace();
        string outputPath = workspace.PathFor("startup.json");
        await File.WriteAllTextAsync(outputPath, "owner data", Encoding.UTF8, TestContext.Current.CancellationToken);
        byte[] original = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        StartupTrace trace = StartupTrace.Create(outputPath);

        Assert.False(trace.Complete("window.opened", "test-trace-v1"));

        Assert.Equal("owner data", await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken));
        Assert.Equal(original, await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
    }

    /// <summary>A blank path disables recording and timing without reading any supplied provider.</summary>
    [Fact]
    public void BlankOutputPathKeepsTracingDisabled()
    {
        StartupTrace trace = StartupTrace.Create(
            "  ",
            new ThrowingTimeProvider(),
            static () => throw new InvalidOperationException("The allocation provider should remain idle."));

        trace.Mark("ignored");

        Assert.Same(StartupTrace.Disabled, trace);
        Assert.False(trace.IsEnabled);
        Assert.Null(trace.ElapsedSinceManagedEntry);
        Assert.False(trace.Complete("ignored", "test-trace-v1"));
    }

    /// <summary>A launch without output measures elapsed time while marks and completion remain inactive.</summary>
    [Fact]
    public void NormalLaunchKeepsOneMonotonicDurationWithoutTraceOutput()
    {
        long origin = 4321;
        var time = new FakeTimeProvider(
            [origin, origin + (3 * Stopwatch.Frequency / 2)],
            [DateTimeOffset.UnixEpoch]);
        var allocations = new Queue<long>([0]);
        StartupTrace trace = StartupTrace.Create(null, time, allocations.Dequeue, measureWithoutOutput: true);

        trace.Mark("ignored");
        TimeSpan elapsed = Assert.IsType<TimeSpan>(trace.ElapsedSinceManagedEntry);

        Assert.False(trace.IsEnabled);
        Assert.Equal(1500, elapsed.TotalMilliseconds);
        Assert.False(trace.Complete("ignored", "test-trace-v1"));
    }

    /// <summary>Later marks and completion calls leave a completed trace file unchanged and read no providers.</summary>
    [Fact]
    public async Task CompletionIgnoresLaterMarksAndLeavesFileUnchanged()
    {
        using var workspace = new TestWorkspace();
        string outputPath = workspace.PathFor("startup.json");
        var time = new FakeTimeProvider(
            [0, Stopwatch.Frequency],
            [DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1)]);
        var allocations = new Queue<long>([100, 200]);
        StartupTrace trace = StartupTrace.Create(outputPath, time, allocations.Dequeue);
        Assert.True(trace.Complete("ready", "test-trace-v1"));
        byte[] original = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);

        trace.Mark("ignored");
        Assert.False(trace.Complete("ignored", "other-test-schema",
            static _ => throw new InvalidOperationException("The host callback should remain idle.")));

        byte[] current = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.Equal(original, current);
        using JsonDocument document = JsonDocument.Parse(current);
        Assert.Equal(["managed-entry", "ready"], document.RootElement.GetProperty("stages")
            .EnumerateArray().Select(stage => stage.GetProperty("name").GetString()));
    }

    /// <summary>The host-selected environment variable supplies the output destination.</summary>
    [Fact]
    public async Task StartFromEnvironmentReadsNamedVariable()
    {
        using var workspace = new TestWorkspace();
        string outputPath = workspace.PathFor("environment.json");
        string variableName = "STARTUP_TRACE_TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(variableName, "  " + outputPath + "  ");
            StartupTrace trace = StartupTrace.StartFromEnvironment(variableName);

            Assert.True(trace.IsEnabled);
            Assert.IsType<TimeSpan>(trace.ElapsedSinceManagedEntry);
            Assert.True(trace.Complete("ready", "test-trace-v1"));
            using JsonDocument document = JsonDocument.Parse(
                await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
            Assert.Equal("test-trace-v1", document.RootElement.GetProperty("schemaVersion").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    /// <summary>An unset host-selected environment variable disables recording while retaining timing.</summary>
    [Fact]
    public void StartFromEnvironmentWithoutVariableKeepsTimingEnabled()
    {
        string variableName = "STARTUP_TRACE_TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(variableName, null);
            StartupTrace trace = StartupTrace.StartFromEnvironment(variableName);

            trace.Mark("ignored");

            Assert.False(trace.IsEnabled);
            Assert.NotSame(StartupTrace.Disabled, trace);
            Assert.True(Assert.IsType<TimeSpan>(trace.ElapsedSinceManagedEntry) >= TimeSpan.Zero);
            Assert.False(trace.Complete("ignored", "test-trace-v1"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    /// <summary>A missing destination directory returns false without creating a directory or permitting a retry.</summary>
    [Fact]
    public void MissingParentDirectoryDoesNotBlockStartup()
    {
        using var workspace = new TestWorkspace();
        string parentPath = workspace.PathFor("missing");
        string outputPath = Path.Combine(parentPath, "startup.json");
        var time = new FakeTimeProvider([0, Stopwatch.Frequency],
            [DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1)]);
        var allocations = new Queue<long>([0, 100]);
        StartupTrace trace = StartupTrace.Create(outputPath, time, allocations.Dequeue);

        Assert.False(trace.Complete("ready", "test-trace-v1",
            static _ => throw new InvalidOperationException("The host callback should remain idle.")));

        trace.Mark("ignored");
        Assert.False(trace.Complete("ignored", "test-trace-v1"));
        Assert.False(Directory.Exists(parentPath));
        Assert.False(File.Exists(outputPath));
    }

    /// <summary>Root and stage properties retain their required order and host sections follow the stages array.</summary>
    [Fact]
    public async Task HostSectionsFollowStagesAndProduceValidJson()
    {
        using var workspace = new TestWorkspace();
        string outputPath = workspace.PathFor("startup.json");
        StartupTrace trace = StartupTrace.Create(outputPath);
        int callbackCalls = 0;

        Assert.True(trace.Complete("ready", "test-trace-v1", writer =>
        {
            callbackCalls++;
            WriteHostStages(writer);
            writer.WriteString("hostStatus", "ready");
        }));

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        JsonElement root = document.RootElement;
        Assert.Equal(1, callbackCalls);
        Assert.Equal(["schemaVersion", "processId", "runtime", "osArchitecture", "processArchitecture",
            "startedUtc", "completedUtc", "stages", "hostStages", "hostStatus"],
            root.EnumerateObject().Select(property => property.Name));
        foreach (JsonElement stage in root.GetProperty("stages").EnumerateArray())
        {
            Assert.Equal(["name", "elapsedMilliseconds", "deltaMilliseconds",
                "allocatedBytesSinceManagedEntry", "allocationDeltaBytes"],
                stage.EnumerateObject().Select(property => property.Name));
        }
        Assert.Equal(2, root.GetProperty("hostStages").GetArrayLength());
        Assert.Equal("ready", root.GetProperty("hostStatus").GetString());
    }

    /// <summary>Three recorded stages match hand-computed durations and allocations after the zero origin point.</summary>
    [Fact]
    public async Task ThreeStagesMatchHandComputedDurationsAndAllocations()
    {
        using var workspace = new TestWorkspace();
        string outputPath = workspace.PathFor("startup.json");
        long origin = 5000;
        var time = new FakeTimeProvider(
            [origin, origin + (Stopwatch.Frequency / 4), origin + (3 * Stopwatch.Frequency / 4),
                origin + (7 * Stopwatch.Frequency / 4)],
            [DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(2)]);
        var allocations = new Queue<long>([100, 260, 500, 600]);
        StartupTrace trace = StartupTrace.Create(outputPath, time, allocations.Dequeue);

        trace.Mark("first");
        trace.Mark("second");
        Assert.True(trace.Complete("third", "test-trace-v1"));

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        JsonElement[] stages = [.. document.RootElement.GetProperty("stages").EnumerateArray()];
        Assert.Equal([0d, 250d, 750d, 1750d],
            stages.Select(stage => stage.GetProperty("elapsedMilliseconds").GetDouble()));
        Assert.Equal([0d, 250d, 500d, 1000d],
            stages.Select(stage => stage.GetProperty("deltaMilliseconds").GetDouble()));
        Assert.Equal([0L, 160L, 400L, 500L],
            stages.Select(stage => stage.GetProperty("allocatedBytesSinceManagedEntry").GetInt64()));
        Assert.Equal([0L, 160L, 240L, 100L],
            stages.Select(stage => stage.GetProperty("allocationDeltaBytes").GetInt64()));
    }

    /// <summary>Creation reads timestamp, allocations, then UTC; completion records its stage before UTC.</summary>
    [Fact]
    public void ProvidersAreReadInRequiredOrder()
    {
        using var workspace = new TestWorkspace();
        var reads = new List<string>();
        var time = new FakeTimeProvider([0, Stopwatch.Frequency, 2 * Stopwatch.Frequency],
            [DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(2)], reads.Add);
        var allocations = new Queue<long>([100, 200, 300]);
        StartupTrace trace = StartupTrace.Create(workspace.PathFor("startup.json"), time, () =>
        {
            reads.Add("allocatedBytes");
            return allocations.Dequeue();
        });

        trace.Mark("first");
        Assert.True(trace.Complete("ready", "test-trace-v1"));

        Assert.Equal(["timestamp", "allocatedBytes", "utc", "timestamp", "allocatedBytes",
            "timestamp", "allocatedBytes", "utc"], reads);
    }

    /// <summary>Allocation totals below the origin and negative allocation deltas are clamped to zero.</summary>
    [Fact]
    public async Task DecreasingAllocationsAreClampedToZero()
    {
        using var workspace = new TestWorkspace();
        string outputPath = workspace.PathFor("startup.json");
        var time = new FakeTimeProvider([0, Stopwatch.Frequency, 2 * Stopwatch.Frequency, 3 * Stopwatch.Frequency],
            [DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(3)]);
        var allocations = new Queue<long>([100, 90, 150, 120]);
        StartupTrace trace = StartupTrace.Create(outputPath, time, allocations.Dequeue);

        trace.Mark("below-origin");
        trace.Mark("higher");
        Assert.True(trace.Complete("lower", "test-trace-v1"));

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        JsonElement[] stages = [.. document.RootElement.GetProperty("stages").EnumerateArray()];
        Assert.Equal([0L, 0L, 50L, 20L],
            stages.Select(stage => stage.GetProperty("allocatedBytesSinceManagedEntry").GetInt64()));
        Assert.Equal([0L, 0L, 50L, 0L],
            stages.Select(stage => stage.GetProperty("allocationDeltaBytes").GetInt64()));
    }

    /// <summary>Unexpected host callback exceptions propagate and leave completion final.</summary>
    [Fact]
    public void HostSectionExceptionsPropagateAndCompletionRemainsFinal()
    {
        using var workspace = new TestWorkspace();
        var time = new FakeTimeProvider([0, Stopwatch.Frequency],
            [DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1)]);
        var allocations = new Queue<long>([0, 100]);
        StartupTrace trace = StartupTrace.Create(workspace.PathFor("startup.json"), time, allocations.Dequeue);
        var expected = new InvalidOperationException("Synthetic host section failure.");

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
            trace.Complete("ready", "test-trace-v1", _ => throw expected));

        Assert.Same(expected, actual);
        trace.Mark("ignored");
        Assert.False(trace.Complete("ignored", "test-trace-v1"));
    }

    /// <summary>Blank stage names are rejected even when recording is disabled or already complete.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MarkRejectsBlankStagesEvenWhenRecordingIsInactive(string? stage)
    {
        using var workspace = new TestWorkspace();
        StartupTrace trace = StartupTrace.Create(workspace.PathFor("startup.json"));
        Assert.ThrowsAny<ArgumentException>(() => trace.Mark(stage!));
        Assert.True(trace.Complete("ready", "test-trace-v1"));

        Assert.ThrowsAny<ArgumentException>(() => StartupTrace.Disabled.Mark(stage!));
        Assert.ThrowsAny<ArgumentException>(() => trace.Mark(stage!));
    }

    private static void WriteHostStages(Utf8JsonWriter writer)
    {
        writer.WriteStartArray("hostStages");
        writer.WriteStartObject();
        writer.WriteString("id", "host-first");
        writer.WriteString("state", "Succeeded");
        writer.WriteNull("completedWork");
        writer.WriteNull("totalWork");
        writer.WriteEndObject();
        writer.WriteStartObject();
        writer.WriteString("id", "host-second");
        writer.WriteString("state", "Succeeded");
        writer.WriteNumber("completedWork", 5);
        writer.WriteNumber("totalWork", 5);
        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly Queue<long> _timestamps;
        private readonly Queue<DateTimeOffset> _utcValues;
        private readonly Action<string>? _onRead;

        internal FakeTimeProvider(
            IEnumerable<long> timestamps,
            IEnumerable<DateTimeOffset> utcValues,
            Action<string>? onRead = null)
        {
            _timestamps = new(timestamps);
            _utcValues = new(utcValues);
            _onRead = onRead;
        }

        /// <summary>Gets the frequency used by the synthetic timestamps.</summary>
        public override long TimestampFrequency => Stopwatch.Frequency;

        /// <summary>Returns the next queued timestamp.</summary>
        public override long GetTimestamp()
        {
            _onRead?.Invoke("timestamp");
            return _timestamps.Dequeue();
        }

        /// <summary>Returns the next queued UTC time.</summary>
        public override DateTimeOffset GetUtcNow()
        {
            _onRead?.Invoke("utc");
            return _utcValues.Dequeue();
        }
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        /// <summary>Rejects frequency reads when timing should remain disabled.</summary>
        public override long TimestampFrequency => throw new InvalidOperationException("The frequency should remain idle.");

        /// <summary>Rejects time zone reads when timing should remain disabled.</summary>
        public override TimeZoneInfo LocalTimeZone => throw new InvalidOperationException("The time zone should remain idle.");

        /// <summary>Rejects timestamp reads when timing should remain disabled.</summary>
        public override long GetTimestamp() => throw new InvalidOperationException("The timestamp should remain idle.");

        /// <summary>Rejects UTC reads when timing should remain disabled.</summary>
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("The UTC clock should remain idle.");

        /// <summary>Rejects timer creation when timing should remain disabled.</summary>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            throw new InvalidOperationException("The timer should remain idle.");
        }
    }
}
