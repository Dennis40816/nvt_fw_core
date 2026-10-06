// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nvt.Core.Startup;

internal static class StartupTraceFileSink
{
    internal static bool TryWrite(
        string outputPath,
        string schemaVersion,
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        IReadOnlyList<StartupTracePoint> points,
        Action<Utf8JsonWriter>? writeHostSections)
    {
        try
        {
            using var stream = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", schemaVersion);
            writer.WriteNumber("processId", Environment.ProcessId);
            writer.WriteString("runtime", RuntimeInformation.FrameworkDescription);
            writer.WriteString("osArchitecture", RuntimeInformation.OSArchitecture.ToString());
            writer.WriteString("processArchitecture", RuntimeInformation.ProcessArchitecture.ToString());
            writer.WriteString("startedUtc", startedUtc);
            writer.WriteString("completedUtc", completedUtc);
            writer.WriteStartArray("stages");

            double previousElapsed = 0;
            foreach (StartupTracePoint point in points)
            {
                writer.WriteStartObject();
                writer.WriteString("name", point.Stage);
                writer.WriteNumber("elapsedMilliseconds", point.ElapsedMilliseconds);
                writer.WriteNumber("deltaMilliseconds", point.ElapsedMilliseconds - previousElapsed);
                writer.WriteNumber("allocatedBytesSinceManagedEntry", point.AllocatedBytesSinceManagedEntry);
                writer.WriteNumber("allocationDeltaBytes", point.AllocationDeltaBytes);
                writer.WriteEndObject();
                previousElapsed = point.ElapsedMilliseconds;
            }

            writer.WriteEndArray();
            writeHostSections?.Invoke(writer);
            writer.WriteEndObject();
            writer.Flush();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            NotSupportedException or ArgumentException)
        {
            Trace.TraceWarning("Startup trace was not written: {0}", exception.Message);
            return false;
        }
    }
}
