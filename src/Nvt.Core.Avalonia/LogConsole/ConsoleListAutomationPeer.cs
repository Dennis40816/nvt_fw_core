// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace Nvt.Core.Avalonia.LogConsole;

internal sealed class ConsoleListAutomationPeer(ConsoleListView view) : ControlAutomationPeer(view), ISelectionProvider, IScrollProvider
{
    private bool IsAvailable => view.AutomationAvailable;
    public bool CanSelectMultiple => IsAvailable;
    public bool IsSelectionRequired => false;
    protected override AutomationControlType GetAutomationControlTypeCore()
        => IsAvailable ? AutomationControlType.List : AutomationControlType.Custom;
    protected override string GetNameCore() => IsAvailable ? ConsoleListText.Format(view, "Nvt.Console.List.Name") : string.Empty;
    protected override object? GetProviderCore(Type providerType)
        => IsAvailable && (providerType == typeof(ISelectionProvider) || providerType == typeof(IScrollProvider)) ? this : null;
    // Build on demand: rows are indirect visual children and template visibility can change independently.
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore()
    {
        if (!IsAvailable || view.ItemsHost is not { } host) return [];
        var children = host.Children.OfType<ConsoleRowPresenter>().OrderBy(row => row.AutomationInput?.Index)
            .Select(CreatePeerForElement).OfType<AutomationPeer>().ToList();
        if (view.TemplateParts is { } parts)
        {
            if (parts.Jump.IsEffectivelyVisible && CreatePeerForElement(parts.Jump) is { } jump) children.Add(jump);
            if (parts.Retention.IsEffectivelyVisible && CreatePeerForElement(parts.Retention) is { } retention) children.Add(retention);
        }
        return children;
    }
    internal void InvalidateRealizedChildren() => InvalidateChildren();

    public IReadOnlyList<AutomationPeer> GetSelection()
        => GetOrCreateChildrenCore().Where(peer => peer.GetProvider<ISelectionItemProvider>()?.IsSelected == true).ToArray();

    public bool HorizontallyScrollable => false;
    public double HorizontalScrollPercent => -1;
    public double HorizontalViewSize => IsAvailable ? 100 : 0;
    public bool VerticallyScrollable => IsAvailable && view.ItemsHost is { } host && host.Extent.Height > host.Viewport.Height;
    public double VerticalScrollPercent => VerticallyScrollable && view.ItemsHost is { } host
        ? 100 * host.Offset.Y / (host.Extent.Height - host.Viewport.Height) : -1;
    public double VerticalViewSize => IsAvailable && view.ItemsHost is { Extent.Height: > 0 } host
        ? Math.Min(100, 100 * host.Viewport.Height / host.Extent.Height) : 0;
    public void SetScrollPercent(double horizontalPercent, double verticalPercent)
    {
        if (!VerticallyScrollable || view.ItemsHost is not { } host || verticalPercent == -1) return;
        if (!double.IsFinite(verticalPercent) || verticalPercent < 0 || verticalPercent > 100) return;
        host.Offset = new(0, verticalPercent / 100 * (host.Extent.Height - host.Viewport.Height));
    }
    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        if (!VerticallyScrollable || view.ItemsHost is not { } host) return;
        var delta = verticalAmount switch
        {
            ScrollAmount.SmallDecrement => -host.ScrollSize.Height,
            ScrollAmount.SmallIncrement => host.ScrollSize.Height,
            ScrollAmount.LargeDecrement => -host.PageScrollSize.Height,
            ScrollAmount.LargeIncrement => host.PageScrollSize.Height,
            _ => 0,
        };
        host.Offset = new(0, host.Offset.Y + delta);
    }

    // UI-thread reads expose neutral values after disposal or detach.
    protected override string GetAcceleratorKeyCore() => IsAvailable ? (base.GetAcceleratorKeyCore() ?? string.Empty) : string.Empty;
    protected override string GetAccessKeyCore() => IsAvailable ? (base.GetAccessKeyCore() ?? string.Empty) : string.Empty;
    protected override string GetAutomationIdCore() => IsAvailable ? (base.GetAutomationIdCore() ?? string.Empty) : string.Empty;
    protected override Rect GetBoundingRectangleCore() => IsAvailable ? base.GetBoundingRectangleCore() : default;
    protected override string GetClassNameCore() => IsAvailable ? Owner.GetType().Name : string.Empty;
    protected override string GetHelpTextCore() => IsAvailable ? (base.GetHelpTextCore() ?? string.Empty) : string.Empty;
    protected override string GetPlaceholderTextCore() => string.Empty;
    protected override string GetItemTypeCore() => string.Empty;
    protected override string GetItemStatusCore() => string.Empty;
    protected override string GetLocalizedControlTypeCore() => IsAvailable ? base.GetLocalizedControlTypeCore() : string.Empty;
    protected override AutomationPeer? GetLabeledByCore() => IsAvailable ? base.GetLabeledByCore() : null;
    protected override AutomationLandmarkType? GetLandmarkTypeCore() => IsAvailable ? base.GetLandmarkTypeCore() : null;
    protected override int GetHeadingLevelCore() => IsAvailable ? base.GetHeadingLevelCore() : 0;
    protected override AutomationPeer? GetParentCore() => IsAvailable ? base.GetParentCore() : null;
    protected override bool HasKeyboardFocusCore() => IsAvailable && base.HasKeyboardFocusCore();
    protected override bool IsContentElementCore() => IsAvailable;
    protected override bool IsControlElementCore() => IsAvailable;
    protected override bool IsEnabledCore() => IsAvailable && base.IsEnabledCore();
    protected override bool IsKeyboardFocusableCore() => IsAvailable && base.IsKeyboardFocusableCore();
    protected override bool IsOffscreenCore() => !IsAvailable || base.IsOffscreenCore();
    protected override void SetFocusCore() { if (IsAvailable) base.SetFocusCore(); }
    protected override void BringIntoViewCore() { if (IsAvailable) base.BringIntoViewCore(); }
    protected override bool ShowContextMenuCore() => IsAvailable && base.ShowContextMenuCore();
}
