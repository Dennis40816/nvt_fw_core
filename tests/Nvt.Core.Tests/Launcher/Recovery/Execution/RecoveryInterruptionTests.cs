// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Records the missing Core-linked executor hard-stop evidence without substituting injected cancellation.</summary>
public sealed class RecoveryInterruptionTests
{
    /// <summary>Every physical boundary needs the shared recovery-executor child mode and serialized restart evidence.</summary>
    [Theory]
    [MemberData(nameof(PhysicalBoundaries))]
    public void RealExecutorHardStopRequiresSharedChild(string shape, int deletion, string side) =>
        Assert.Skip($"Nvt.Core.LinkedProbe and Nvt.Core.TestProbe have no recovery-executor mode for {shape}, {side} deletion {deletion}. The host must provision that shared child mode; physical hard-stop evidence is pending.");

    /// <summary>Enumerates both sides of all 25/26 root and 26 staging deletions, including marker removal.</summary>
    public static IEnumerable<object[]> PhysicalBoundaries()
    {
        (string Shape, int Count)[] shapes = [("root", 25), ("root-with-launcher-state", 26), ("staging", 26)];
        foreach (var shape in shapes)
            foreach (int deletion in Enumerable.Range(1, shape.Count))
                foreach (string side in new[] { "before", "after" })
                    yield return [shape.Shape, deletion, side];
    }
}
