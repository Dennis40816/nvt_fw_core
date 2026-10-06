// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Processes;

/// <summary>Absolute Stopwatch timestamps for one invocation's cleanup phase.</summary>
internal readonly record struct CleanupSchedule(long HeldOutputGraceAt, long ReaderStopAt, long DeadlineAt);
