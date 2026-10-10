// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>Protects the intentionally small test-support public contract.</summary>
public sealed class PublicApiTests
{
    /// <summary>Only the planned types and their declared API are exported; disposal implements the two interfaces.</summary>
    [Fact]
    public void PublicSurfaceMatchesThePlan()
    {
        Type clock = typeof(ManualTimeProvider);
        Type workspace = typeof(TestWorkspace);
        Type signal = typeof(SignalWait);
        Type child = typeof(ChildProcessFixture);
        Type perf = typeof(RelativePerf);
        Type block = typeof(TaskBlock);
        Type files = typeof(TestFiles);
        Type[] expectedTypes = [child, clock, perf, signal, block, files, workspace];
        Assert.Equal(expectedTypes, clock.Assembly.GetExportedTypes().OrderBy(type => type.Name, StringComparer.Ordinal));
        Assert.True(clock.IsSealed);
        Assert.True(workspace.IsSealed);
        Assert.True(signal.IsSealed);
        Assert.True(child.IsSealed);
        Assert.True(perf.IsAbstract && perf.IsSealed);
        Assert.True(block.IsAbstract && block.IsSealed);
        Assert.True(files.IsAbstract && files.IsSealed);
        Assert.Equal(typeof(TimeProvider), clock.BaseType);
        Assert.Equal(typeof(DateTimeOffset), Assert.Single(Assert.Single(clock.GetConstructors()).GetParameters()).ParameterType);
        Assert.Empty(workspace.GetConstructors());
        Assert.Empty(child.GetConstructors());
        string[] clockMethods = ["Advance"];
        string[] workspaceMethods = ["Create", "Dispose", "DisposeAsync", "GetPath", "get_RootPath"];
        string[] signalMethods = ["Set", "WaitAsync", "WaitAsync", "get_IsSet"];
        string[] childMethods = ["Dispose", "DisposeAsync", "KillTree", "Start", "WaitForExitAsync", "WaitForOutputAsync",
            "get_HasExited", "get_Output", "get_OutputTruncated", "get_ProcessId", "get_WatchdogExpired"];
        Assert.Equal(clockMethods, NewPublicMethods(clock));
        Assert.Equal(workspaceMethods, NewPublicMethods(workspace));
        Assert.Equal(signalMethods, NewPublicMethods(signal));
        Assert.Equal(childMethods, NewPublicMethods(child));
        Assert.Equal(["CalibrationUnit", "InUnits", "MedianAllocatedBytes", "MedianTime", "ScaleRatio"], NewPublicMethods(perf));
        Assert.Equal(["UntilComplete", "UntilComplete"], NewPublicMethods(block));
        Assert.Equal(["ReadLinesAsync", "WriteAllBytes"], NewPublicMethods(files));
        Assert.True(typeof(IDisposable).IsAssignableFrom(workspace));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(workspace));
        Assert.True(typeof(IDisposable).IsAssignableFrom(child));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(child));
    }

    private static IEnumerable<string> NewPublicMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.GetBaseDefinition().DeclaringType == type)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal);
}
