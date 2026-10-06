// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.MessageCenter;

/// <summary>Tracks modal visibility, pane selection, and export-context generations.</summary>
/// <remarks>Callbacks observe the advanced generation and the old committed state.</remarks>
public sealed class MessageCenterSession
{
    /// <summary>Creates a closed session with activity selected and generation zero.</summary>
    public MessageCenterSession()
    {
    }

    // Internal construction permits checked-overflow characterization without a public generation setter.
    internal MessageCenterSession(long exportContextGeneration)
    {
        ExportContextGeneration = exportContextGeneration;
    }

    /// <summary>Gets whether the modal is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets whether the activity pane is selected.</summary>
    public bool IsActivitySelected { get; private set; } = true;

    /// <summary>Gets the generation used to reject stale export contexts.</summary>
    public long ExportContextGeneration { get; private set; }

    /// <summary>Advances generation, invokes the callback, and commits open visibility after success.</summary>
    /// <param name="beforeOpen">Optional host work before visibility is committed, even if already open.</param>
    /// <remarks>A callback failure leaves visibility unchanged but retains the generation advance.</remarks>
    /// <exception cref="OverflowException">The generation cannot be incremented.</exception>
    public void Open(Action? beforeOpen = null)
    {
        ExportContextGeneration = checked(ExportContextGeneration + 1);
        beforeOpen?.Invoke();
        IsOpen = true;
    }

    /// <summary>Advances generation, invokes the callback, and commits closed visibility after success.</summary>
    /// <param name="beforeClose">Optional host work before visibility is committed, even if already closed.</param>
    /// <remarks>A callback failure leaves visibility unchanged but retains the generation advance.</remarks>
    /// <exception cref="OverflowException">The generation cannot be incremented.</exception>
    public void Close(Action? beforeClose = null)
    {
        ExportContextGeneration = checked(ExportContextGeneration + 1);
        beforeClose?.Invoke();
        IsOpen = false;
    }

    /// <summary>Changes pane selection after advancing generation and successfully invoking the callback.</summary>
    /// <param name="selected">Whether to select activity.</param>
    /// <param name="beforeSelect">Optional host work at the pre-commit property-changing point.</param>
    /// <remarks>
    /// Selecting the current pane does nothing and does not invoke the callback. A callback failure
    /// leaves selection unchanged but retains the generation advance.
    /// </remarks>
    /// <exception cref="OverflowException">The generation cannot be incremented for a pane change.</exception>
    public void SelectActivity(bool selected, Action? beforeSelect = null)
    {
        if (IsActivitySelected == selected)
        {
            return;
        }

        ExportContextGeneration = checked(ExportContextGeneration + 1);
        beforeSelect?.Invoke();
        IsActivitySelected = selected;
    }

    /// <summary>Checks generation equality, open visibility, and activity selection in that order.</summary>
    /// <param name="generation">The generation captured by the host export operation.</param>
    /// <returns>Whether the captured generation still identifies the open activity context.</returns>
    public bool IsExportContextCurrent(long generation)
    {
        return generation == ExportContextGeneration && IsOpen && IsActivitySelected;
    }
}
