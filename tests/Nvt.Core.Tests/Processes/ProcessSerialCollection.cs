// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;
using System.Diagnostics.CodeAnalysis;

namespace Nvt.Core.Tests.Processes;

/// <summary>Serializes tests that exercise the process-wide launch gate.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "This type defines the shared xUnit process collection.")]
public sealed class ProcessSerialCollection
{
    /// <summary>The collection shared by process launch and lifetime tests.</summary>
    public const string Name = "ProcessSerial";
}
