// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Startup;

internal sealed record StartupTracePoint(
    string Stage,
    double ElapsedMilliseconds,
    long AllocatedBytesSinceManagedEntry,
    long AllocationDeltaBytes);
