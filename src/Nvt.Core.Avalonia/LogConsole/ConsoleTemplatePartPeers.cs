// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Nvt.Core.Avalonia.LogConsole;

/// <summary>The Jump to latest button of the list template; its automation parent is the list peer.</summary>
internal sealed class ConsoleJumpButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(ConsoleJumpButton owner) : ButtonAutomationPeer(owner)
    {
        protected override AutomationPeer? GetParentCore() => ConsoleTemplatePartParent.Of(owner);
    }
}

/// <summary>The retention notice of the list template; its automation parent is the list peer.</summary>
internal sealed class ConsoleRetentionText : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(ConsoleRetentionText owner) : TextBlockAutomationPeer(owner)
    {
        protected override AutomationPeer? GetParentCore() => ConsoleTemplatePartParent.Of(owner);
    }
}

internal static class ConsoleTemplatePartParent
{
    // The template containers have no automation role of their own, so a part reports the list as its parent.
    internal static AutomationPeer? Of(Control part)
        => part.FindAncestorOfType<ConsoleListView>() is { } view ? ControlAutomationPeer.CreatePeerForElement(view) : null;
}
