// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation.Provider;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nvt.Core.Avalonia.LogConsole;
using Nvt.Core.LogConsole;
using Xunit;
using static Nvt.Core.Avalonia.Tests.LogConsole.ConsoleAccessibilitySupport;

namespace Nvt.Core.Avalonia.Tests.LogConsole;

public sealed class ConsoleAutomationContentTests
{
    [AvaloniaTheory]
    [InlineData("message", false)]
    [InlineData("first\nsecond", false)]
    [InlineData("first\nsecond", true)]
    [InlineData("a long line that exceeds a narrow message column", false)]
    public void Reads_PresentedRow_DoNotReadContent(string text, bool expanded)
    {
        using var content = new ConsoleMeasureContent(text);
        using var scene = new ConsoleInteractionScene(count: 1, content: content);
        scene.Window.Width = 500;
        Present(scene, expanded);
        var reads = content.ReadCount;

        var peer = Peer(scene.Row(1));
        Read(peer);
        _ = peer.ExpandCollapseState;
        _ = peer.GetProvider<IExpandCollapseProvider>()?.ExpandCollapseState;
        peer.Refresh();

        Assert.True(reads > 0);
        Assert.Equal(reads, content.ReadCount);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reads_UnpresentedRow_OmitMessageWithoutReadingContent(bool expanded)
    {
        using var content = new ConsoleMeasureContent("first\nsecond");
        using var projection = Project(Row(content));
        var view = new ConsoleListView
        {
            Projection = projection,
            ViewState = new() { ExpandedIds = expanded ? [new(1)] : [] },
        };
        var window = ConsoleInteractionWindow.Create(view);
        try
        {
            var presenter = new ConsoleRowPresenter();
            presenter.Configure(projection.Rows[0], view);
            var reads = content.ReadCount;

            var peer = Peer(presenter);
            Read(peer);

            Assert.Equal("Info, 12:00:00.000, app", peer.GetName());
            Assert.Null(peer.GetProvider<IExpandCollapseProvider>());
            Assert.Equal(reads, content.ReadCount);
            presenter.Release();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expansion_UnchangedPeerAction_DoesNotReadContent(bool expanded)
    {
        using var content = new ConsoleMeasureContent("first\nsecond");
        using var projection = Project(Row(content));
        var view = new ConsoleListView
        {
            Projection = projection,
            ViewState = new() { ExpandedIds = expanded ? [new(1)] : [] },
        };
        var window = ConsoleInteractionWindow.Create(view);
        try
        {
            var presenter = ConsoleInteractionWindow.Host(view).Children.OfType<ConsoleRowPresenter>().First();
            var peer = Peer(presenter);
            var reads = content.ReadCount;

            RepeatExpansion(peer, expanded);

            Assert.Equal(reads, content.ReadCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ContentRead_RetainedHandle_IsRejectedByReadGuard()
    {
        using var content = new ConsoleMeasureContent("message");
        using var scene = new ConsoleInteractionScene(count: 1, content: content);
        var reads = content.ReadCount;

        scene.Row(1).Row!.TextContent.Read(0, new char[1]);

        Assert.Throws<global::Xunit.Sdk.EqualException>(() => Assert.Equal(reads, content.ReadCount));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Release_PresentedRow_DropsBoundedText(bool expanded)
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: new string('x', 100_000));
        Present(scene, expanded);
        var row = scene.Row(1);
        var text = Assert.IsType<ConsoleTextPresenter.PresentedText>(row.MessageText.Presentation);
        Assert.Equal(expanded ? ConsoleCopyText.MaximumCharacters : 1024, text.Text.Length);
        Assert.True(text.Truncated);

        row.Release();

        Assert.Null(row.MessageText.Presentation);
        Assert.False(row.MessageText.Truncated);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rebind_EqualRow_KeepsPublishedTextWithoutReadingContent(bool expanded)
    {
        using var content = new ConsoleMeasureContent("first\nsecond");
        using var projection = Project(Row(content));
        var view = new ConsoleListView
        {
            Projection = projection,
            ViewState = new() { ExpandedIds = expanded ? [new(1)] : [] },
        };
        var window = ConsoleInteractionWindow.Create(view);
        try
        {
            var presenter = ConsoleInteractionWindow.Host(view).Children.OfType<ConsoleRowPresenter>().First();
            var text = Assert.IsType<ConsoleTextPresenter.PresentedText>(presenter.MessageText.Presentation);
            var reads = content.ReadCount;
            using var next = Project(Row(content));

            view.Projection = next;
            ConsoleInteractionWindow.Pump(window);

            Assert.Same(text, presenter.MessageText.Presentation);
            Assert.Equal(reads, content.ReadCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Rebind_ExpandedFlagFlipped_DropsPublishedTextUntilNextLayout()
    {
        using var scene = new ConsoleInteractionScene(count: 1);
        var message = scene.Row(1).MessageText;
        var row = scene.Row(1).Row!;
        Assert.NotNull(message.Presentation);

        message.Configure(row, expanded: true);

        Assert.Null(message.Presentation);
    }

    [AvaloniaFact]
    public void HitLink_StateChangedBeforeLayout_ReturnsNoLink()
    {
        using var scene = new ConsoleInteractionScene(count: 1, message: "first\nsecond");
        var message = scene.Row(1).MessageText;
        var row = scene.Row(1).Row!;
        var links = new ConsoleLinkIndex([new ConsoleLinkSpan(0, 5, new LinkTarget(LinkKind.Url, "https://example.test"))],
            row.TextContent.Length);
        message.Configure(row, expanded: true, links);
        ConsoleInteractionWindow.Pump(scene.Window);
        message.Configure(row, expanded: false, links);

        var hit = message.HitLink(new Point(1, 1));

        Assert.Null(hit);
    }

    [AvaloniaFact]
    public void Present_ExpandedCapSplitsSurrogatePair_PublishesNoLoneHighSurrogate()
    {
        using var scene = new ConsoleInteractionScene(count: 1,
            message: new string('x', ConsoleCopyText.MaximumCharacters - 1) + "\U0001F600");
        Present(scene, expanded: true);

        var text = Assert.IsType<ConsoleTextPresenter.PresentedText>(scene.Row(1).MessageText.Presentation).Text;

        Assert.Equal(ConsoleCopyText.MaximumCharacters - 1, text.Length);
        Assert.False(char.IsHighSurrogate(text[^1]));
    }

    [AvaloniaFact]
    public void Present_ExpandedCapSplitsSurrogatePairBeforeMoreText_AddsNothingAfterTheCut()
    {
        using var scene = new ConsoleInteractionScene(count: 1,
            message: new string('x', ConsoleCopyText.MaximumCharacters - 1) + "\U0001F600" + new string('y', 6_000));
        Present(scene, expanded: true);

        var text = Assert.IsType<ConsoleTextPresenter.PresentedText>(scene.Row(1).MessageText.Presentation).Text;

        Assert.Equal(ConsoleCopyText.MaximumCharacters - 1, text.Length);
        Assert.Equal('x', text[^1]);
    }

    [AvaloniaFact]
    public void Rebind_OtherRow_DropsPublishedTextUntilNextLayout()
    {
        using var scene = new ConsoleInteractionScene(count: 2);
        var message = scene.Row(1).MessageText;
        var other = scene.Row(2).Row!;
        Assert.NotNull(message.Presentation);

        message.Configure(other, expanded: false);

        Assert.Null(message.Presentation);
    }

    [AvaloniaFact]
    public void Offsets_RebindToShorterRowBeforeLayout_ReturnFallbackWithoutReading()
    {
        using var content = new ConsoleMeasureContent(new string('a', 6_000));
        using var scene = new ConsoleInteractionScene(count: 2, content: content);
        var message = scene.Row(1).MessageText;
        var longRow = scene.Row(1).Row!;
        var shortRow = scene.Row(2).Row!;
        message.Configure(longRow, expanded: true);
        ConsoleInteractionWindow.Pump(scene.Window);
        var lastPixel = message.Segments[^1].Y;
        message.Configure(shortRow, expanded: true);
        var reads = content.ReadCount;

        var offset = message.TextOffsetAt(lastPixel);
        var pixel = message.PixelOffsetAt(longRow.TextContent.Length - 1, 7);

        Assert.Equal(0, offset);
        Assert.Equal(7, pixel);
        Assert.Equal(reads, content.ReadCount);
    }

    private static void Present(ConsoleInteractionScene scene, bool expanded)
    {
        if (expanded) scene.Key(Key.Enter);
        ConsoleInteractionWindow.Pump(scene.Window);
    }

    private static void RepeatExpansion(ConsoleRowAutomationPeer peer, bool expanded)
    {
        if (expanded) peer.Expand(); else peer.Collapse();
    }
}
