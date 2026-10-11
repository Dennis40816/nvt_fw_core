// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace Nvt.Core.Avalonia.LogConsole;

internal sealed class ConsoleRowAutomationPeer : ControlAutomationPeer, ISelectionItemProvider, IExpandCollapseProvider
{
    private readonly ConsoleRowPresenter _presenter;
    // UI thread only. Previous delivered values exist solely to suppress duplicate automation notifications.
    private Observation _observed;
    internal ConsoleRowAutomationPeer(ConsoleRowPresenter presenter) : base(presenter)
    {
        _presenter = presenter;
        _observed = Observe();
    }
    private ConsoleRowPresenter.RowInput? Input => _presenter.AutomationInput;
    private bool IsAvailable => Input is not null;
    internal int PositionInSet => Input is { } input ? input.Index + 1 : 0;
    internal int SizeOfSet => Input?.View.Projection?.Rows.Length ?? 0;
    public bool IsSelected => Input is { } input && ConsoleRowSelection.Contains(input.Row, input.View.ViewState);
    public ISelectionProvider? SelectionContainer => Input is { } input
        ? CreatePeerForElement(input.View)?.GetProvider<ISelectionProvider>() : null;
    private bool CanExpand => Input is not null && _presenter.MessageText.Truncated;
    public ExpandCollapseState ExpandCollapseState => Input is { } input && CanExpand
        ? input.View.ViewState.ExpandedIds.Contains(input.Row.Id) ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed
        : ExpandCollapseState.LeafNode;
    public bool ShowsMenu => false;
    protected override AutomationControlType GetAutomationControlTypeCore()
        => IsAvailable ? AutomationControlType.ListItem : AutomationControlType.Custom;
    protected override string GetNameCore() => Input is { } input
        ? ConsoleRowName.Build(input, _presenter.MessageText.Presentation?.Text ?? string.Empty) : string.Empty;
    protected override AutomationPeer? GetParentCore() => Input is { } input ? CreatePeerForElement(input.View) : null;
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];
    protected override object? GetProviderCore(Type providerType)
        => IsAvailable && (providerType == typeof(ISelectionItemProvider)
            || providerType == typeof(IExpandCollapseProvider) && CanExpand) ? this : null;
    public void Select() => Interact(ConsoleSelectionAction.Replace);
    public void AddToSelection() => Interact(ConsoleSelectionAction.Add);
    public void RemoveFromSelection() => Interact(ConsoleSelectionAction.Remove);
    public void Expand() { if (CanExpand && ExpandCollapseState != ExpandCollapseState.Expanded) Interact(ConsoleSelectionAction.Replace, true); }
    public void Collapse() { if (CanExpand && ExpandCollapseState != ExpandCollapseState.Collapsed) Interact(ConsoleSelectionAction.Replace, false); }
    private void Interact(ConsoleSelectionAction selection, bool? expanded = null)
    {
        if (Input is { } input) input.View.RowInteraction?.Automate(input.Row, selection, expanded);
    }
    private Observation Observe() => new(GetNameCore(), IsSelected, ExpandCollapseState);
    internal void Refresh()
    {
        var previous = _observed;
        var current = Observe();
        _observed = current;
        if (!string.Equals(previous.Name, current.Name, StringComparison.Ordinal))
            RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, previous.Name, current.Name);
        if (previous.Selected != current.Selected)
            RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, previous.Selected, current.Selected);
        if (previous.Expanded != current.Expanded)
            RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, previous.Expanded, current.Expanded);
    }
    private sealed record Observation(string Name, bool Selected, ExpandCollapseState Expanded);

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
