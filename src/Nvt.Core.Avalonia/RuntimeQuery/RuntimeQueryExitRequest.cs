// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>The confirmation supplied with a generic exit request.</summary>
/// <param name="Confirmed">Whether the confirm argument parsed as true. A missing or blank argument means false.</param>
public sealed record RuntimeQueryExitRequest(bool Confirmed);
