// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleLinkCacheLifetimeTests
{
    [AvaloniaFact]
    public void ClearRetiresParsedTargetsWithoutAnotherLinkQuery()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "https://example.test/cache");
        fixture.Fence();
        using var controller = fixture.Controller();
        var target = ParseTarget(controller);
        controller.ClearCommand.Execute(null);
        fixture.Fence();
        ConsoleTestView.Pump();
        Collect();
        Assert.False(target.IsAlive);
        Assert.Empty(controller.Projection.Rows);
        Assert.NotNull(controller.LinkCacheIdentity);
        GC.KeepAlive(controller);
    }

    [AvaloniaFact]
    public void LiveSnapshotKeepsParsedTargetsInCache()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "https://example.test/cache");
        fixture.Fence();
        using var controller = fixture.Controller();
        var target = ParseTarget(controller);
        Collect();
        Assert.True(target.IsAlive);
        Assert.NotNull(controller.LinkCacheIdentity);
        GC.KeepAlive(controller);
    }

    [AvaloniaFact]
    public void DisposeReleasesLinkCacheAndParsedTargets()
    {
        using var fixture = new ConsoleTestStore();
        fixture.Store.Add(LogLevel.Info, "app", "https://example.test/cache");
        fixture.Fence();
        using var controller = fixture.Controller();
        var references = ParseCache(controller);
        controller.Dispose();
        controller.Dispose();
        Collect();
        Assert.Null(controller.LinkCacheIdentity);
        Assert.False(references.Cache.IsAlive);
        Assert.False(references.Target.IsAlive);
        GC.KeepAlive(controller);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ParseTarget(ConsoleController controller)
        => new(Assert.Single(controller.GetLinks(controller.Projection.Rows[0]).Spans).Target);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Cache, WeakReference Target) ParseCache(ConsoleController controller)
        => (new(controller.LinkCacheIdentity!), ParseTarget(controller));
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
