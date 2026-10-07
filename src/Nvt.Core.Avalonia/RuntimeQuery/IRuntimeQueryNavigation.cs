// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>Supplies tool-owned pages and synchronous switch decisions on the UI thread.</summary>
public interface IRuntimeQueryNavigation
{
    /// <summary>The valid page names in the tool's order.</summary>
    IReadOnlyList<string> Pages { get; }

    /// <summary>The current page name.</summary>
    string CurrentPage { get; }

    /// <summary>Requests a switch to a known page. The current page returns Switched without changing state.</summary>
    /// <remarks>
    /// NeedsConfirmation must leave the page unchanged and must not open a dialog.
    /// Return Rejected when a dialog prevents navigation. Core only reports the decision.
    /// </remarks>
    RuntimeQueryPageResult SwitchPage(string name);
}

/// <summary>The tool's result for a page switch request.</summary>
public enum RuntimeQueryPageResult
{
    /// <summary>The page is current, including an unchanged current-page request.</summary>
    Switched,
    /// <summary>Confirmation is needed. Leave the page unchanged and open no dialog.</summary>
    NeedsConfirmation,
    /// <summary>The tool rejected navigation, for example while a dialog is open.</summary>
    Rejected
}
