// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Shell;

/// <summary>Tracks completed page navigation and rolls back failed activation.</summary>
/// <typeparam name="TPage">The host's page identity type.</typeparam>
/// <remarks>
/// Page identities use <see cref="EqualityComparer{TPage}.Default" />. The host owns navigation
/// guards, confirmation, page factories, and successful state refresh. Calls are synchronous
/// and are not synchronized; callbacks may reenter on the calling thread.
/// </remarks>
public sealed class NavigationHistory<TPage> where TPage : notnull
{
    private readonly List<TPage> _pageHistory;
    private readonly Func<TPage> _selectedPage;
    private readonly Action<TPage> _activate;
    private readonly Action _stateChanged;

    /// <summary>Initializes history with the host's Home page without activating it.</summary>
    /// <param name="home">The initial history entry.</param>
    /// <param name="selectedPage">Reads the host's current selection.</param>
    /// <param name="activate">Activates a page and performs the host's successful state refresh.</param>
    /// <param name="stateChanged">Refreshes host state after failed navigation restores history.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="selectedPage" />, <paramref name="activate" />, or
    /// <paramref name="stateChanged" /> is null, checked in that order.
    /// </exception>
    public NavigationHistory(
        TPage home,
        Func<TPage> selectedPage,
        Action<TPage> activate,
        Action stateChanged)
    {
        ArgumentNullException.ThrowIfNull(selectedPage);
        ArgumentNullException.ThrowIfNull(activate);
        ArgumentNullException.ThrowIfNull(stateChanged);

        _pageHistory = [home];
        _selectedPage = selectedPage;
        _activate = activate;
        _stateChanged = stateChanged;
    }

    /// <summary>Gets whether history contains an entry preceding its current entry.</summary>
    public bool CanGoBack => _pageHistory.Count > 1;

    /// <summary>Gets the preceding history entry for the host to capture before confirmation.</summary>
    /// <exception cref="InvalidOperationException">History contains no back entry.</exception>
    public TPage BackTarget => CanGoBack ? _pageHistory[^2] : throw new InvalidOperationException();

    /// <summary>Completes navigation to the host's captured target, including equal-target activation.</summary>
    /// <param name="target">The captured destination; it is not recomputed from history.</param>
    /// <param name="isBack">Whether to remove the latest history entry when a back entry exists.</param>
    /// <param name="afterActivation">An optional host action invoked after activation succeeds.</param>
    /// <remarks>
    /// History changes before activation. Forward completion appends the target only when the
    /// selected page differs; back completion removes one entry only when history has more than one.
    /// On activation or post-activation failure, the source is reactivated only if selection changed,
    /// then prior history is restored and <c>stateChanged</c> runs before the original failure is rethrown.
    /// A rollback activation failure interrupts that sequence, leaving the changed history intact.
    /// A rollback selection-read or state-refresh failure also propagates in place of the original failure.
    /// </remarks>
    public void CompleteNavigation(TPage target, bool isBack, Action? afterActivation = null)
    {
        TPage source = _selectedPage();
        TPage[] previousHistory = [.. _pageHistory];
        if (isBack && _pageHistory.Count > 1)
        {
            _pageHistory.RemoveAt(_pageHistory.Count - 1);
        }
        else if (!isBack && !EqualityComparer<TPage>.Default.Equals(_selectedPage(), target))
        {
            _pageHistory.Add(target);
        }

        try
        {
            _activate(target);
            afterActivation?.Invoke();
        }
        catch
        {
            if (!EqualityComparer<TPage>.Default.Equals(_selectedPage(), source))
            {
                _activate(source);
            }
            _pageHistory.Clear();
            _pageHistory.AddRange(previousHistory);
            _stateChanged();
            throw;
        }
    }
}
