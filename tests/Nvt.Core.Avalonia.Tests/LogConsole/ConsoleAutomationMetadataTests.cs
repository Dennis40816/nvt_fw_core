// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.LogConsole;
using Xunit;
using static Nvt.Core.Avalonia.Tests.LogConsole.ConsoleAccessibilitySupport;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleAutomationMetadataTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void Traverse_WithoutPeers_WritesNoAutomationMetadata()
    {
        using var scene = new ConsoleScrollScene(10_000);
        scene.Pause();
        using var writes = new MetadataWrites(scene.Host, false);

        scene.Traverse();

        output.WriteLine($"10,000-row traversal without peers: {writes.Count} automation property writes");
        Assert.Equal(0, writes.Count);
        Assert.All(writes.Rows, row => Assert.Null(ControlAutomationPeer.FromElement(row)));
    }

    [AvaloniaFact]
    public void Traverse_WithPeers_UpdatesAutomationMetadata()
    {
        using var scene = new ConsoleScrollScene(10_000);
        scene.Pause();
        using var writes = new MetadataWrites(scene.Host, true);
        writes.Reset();

        scene.Traverse();

        output.WriteLine($"10,000-row traversal with peers: {writes.Count} automation property writes");
        var realized = scene.Host.Children.OfType<ConsoleRowPresenter>().ToArray();
        Assert.True(writes.Count >= realized.Length);
        Assert.All(realized, row =>
        {
            var peer = Assert.IsType<ConsoleRowAutomationPeer>(ControlAutomationPeer.FromElement(row));
            Assert.Equal(peer.PositionInSet, AutomationProperties.GetPositionInSet(row));
            Assert.Equal(10_000, AutomationProperties.GetSizeOfSet(row));
        });
    }

    [AvaloniaFact]
    public void Release_WithPeer_ClearsAutomationMetadata()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        var row = scene.Row(1);
        var peer = Peer(row);
        Assert.Equal(1, AutomationProperties.GetPositionInSet(row));
        Assert.Equal(1, AutomationProperties.GetSizeOfSet(row));

        row.Release();

        Assert.False(row.IsSet(AutomationProperties.PositionInSetProperty));
        Assert.False(row.IsSet(AutomationProperties.SizeOfSetProperty));
        Assert.Equal(-1, AutomationProperties.GetPositionInSet(row));
        Assert.Equal(-1, AutomationProperties.GetSizeOfSet(row));
        Assert.Equal(0, peer.PositionInSet);
        Assert.Equal(0, peer.SizeOfSet);
    }

    private sealed class MetadataWrites : IDisposable
    {
        // Headless UI thread only; subscribe before newly realized rows receive their input.
        private readonly ConsoleItemsHost _host;
        private readonly bool _createPeers;
        internal HashSet<ConsoleRowPresenter> Rows { get; } = [];
        internal int Count { get; private set; }
        internal MetadataWrites(ConsoleItemsHost host, bool createPeers)
        {
            _host = host;
            _createPeers = createPeers;
            host.Children.CollectionChanged += ChildrenChanged;
            foreach (var row in host.Children.OfType<ConsoleRowPresenter>()) Observe(row);
        }
        internal void Reset() => Count = 0;
        private void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.NewItems is null) return;
            foreach (var row in args.NewItems.OfType<ConsoleRowPresenter>()) Observe(row);
        }
        private void Observe(ConsoleRowPresenter row)
        {
            if (!Rows.Add(row)) return;
            row.PropertyChanged += Changed;
            if (_createPeers) _ = Peer(row);
        }
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == AutomationProperties.PositionInSetProperty || args.Property == AutomationProperties.SizeOfSetProperty)
                Count++;
        }
        public void Dispose()
        {
            _host.Children.CollectionChanged -= ChildrenChanged;
            foreach (var row in Rows) row.PropertyChanged -= Changed;
        }
    }
}
