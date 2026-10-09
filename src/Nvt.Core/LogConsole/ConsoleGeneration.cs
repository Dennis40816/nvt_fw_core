// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.LogConsole;

// LogStore._gate protects this helper. Every delayed-work check uses this one generation.
// Lifecycle at this baseline has coalescing and undo helpers, but no reusable generation helper.
internal sealed class ConsoleGeneration
{
    internal long Value { get; private set; }
    internal void Advance() => Value = checked(Value + 1);
    internal bool IsCurrent(long value) => value == Value;
}
