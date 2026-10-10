// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Runs the CPU-bound generated scanner tests alone, so that their worker threads do not starve other tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScannerParallelIsolation
{
    /// <summary>The collection name.</summary>
    public const string Name = "ScannerParallel";
}
