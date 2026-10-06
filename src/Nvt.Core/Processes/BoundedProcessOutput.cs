// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Processes;

/// <summary>Bounded diagnostic text from one redirected stream and whether its end was observed.</summary>
internal readonly record struct BoundedProcessOutput(string Text, bool ReachedEndOfStream);
