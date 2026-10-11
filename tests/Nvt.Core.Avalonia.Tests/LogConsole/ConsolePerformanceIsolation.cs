// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

/// <summary>Runs the scroll performance tests alone, so parallel test classes do not load the machine while they measure.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsolePerformanceIsolation
{
    /// <summary>The collection name.</summary>
    public const string Name = nameof(ConsolePerformanceIsolation);
}
