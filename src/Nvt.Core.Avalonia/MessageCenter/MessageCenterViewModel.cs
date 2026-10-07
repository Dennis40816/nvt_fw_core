// Copyright (c) 2026 Dennis Liu. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nvt.Core.MessageCenter;

namespace Nvt.Core.Avalonia.MessageCenter;

/// <summary>Presents passive activity and application-supplied refresh and export operations.</summary>
/// <remarks>
/// All mutable presentation state, the session, commands, and their continuations are UI-thread-only.
/// The host owns refresh progress/reset timing through the protected setters and uses
/// MessageCenterRefreshCoordinator for its inner refresh. The inherited virtual notification seam
/// lets a typed facade insert application notifications in their original order.
/// </remarks>
public partial class MessageCenterViewModel : ObservableObject
{
    private readonly IMessageCenterProvider _provider;
    private readonly Func<IMessageCenterText> _textProvider;
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly Action _closeReport;
    private readonly Action<MessageCenterInteraction> _interaction;
    // Mutable session contents are UI-thread-only; visibility and generation have no second owner.
    private readonly MessageCenterSession _session = new();
    private readonly MessageCenterExportWorkflow _exportWorkflow;

    /// <summary>Creates a presentation with application-owned data, text, operations, and activity recording.</summary>
    /// <param name="provider">Supplies passive counts and admitted activity metadata.</param>
    /// <param name="textProvider">Supplies the current application text without caching.</param>
    /// <param name="refresh">Runs explicit refresh, including host progress and successful-reset timing.</param>
    /// <param name="export">Exports to the unchanged destination with the supplied token.</param>
    /// <param name="closeReport">Closes the application's report before modal close or report opening.</param>
    /// <param name="interaction">Records application activity before presentation notifications.</param>
    /// <exception cref="ArgumentNullException">A required provider or delegate is null.</exception>
    public MessageCenterViewModel(
        IMessageCenterProvider provider,
        Func<IMessageCenterText> textProvider,
        Func<CancellationToken, Task> refresh,
        Func<string, CancellationToken, Task> export,
        Action closeReport,
        Action<MessageCenterInteraction> interaction)
    {
        _textProvider = textProvider ?? throw new ArgumentNullException(nameof(textProvider));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        ArgumentNullException.ThrowIfNull(export);
        _closeReport = closeReport ?? throw new ArgumentNullException(nameof(closeReport));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _exportWorkflow = new MessageCenterExportWorkflow(_session, export, ReportExportSuccess, ReportExportFailure);
        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(Close);
        OpenRunReportsCommand = new RelayCommand(() =>
        {
            _closeReport();
            SelectSystemInformation(false);
            Open();
        });
        ShowRunReportsCommand = new RelayCommand(() => SelectSystemInformation(false));
        ShowSystemInformationCommand = new RelayCommand(() => SelectSystemInformation(true));
        ShowImportantActivityCommand = new RelayCommand(() => SelectedActivityFilter = MessageActivityFilter.Important);
        ShowWarningActivityCommand = new RelayCommand(() => SelectedActivityFilter = MessageActivityFilter.Warnings);
        ShowErrorActivityCommand = new RelayCommand(() => SelectedActivityFilter = MessageActivityFilter.Errors);
        ToggleDebugActivityCommand = new RelayCommand(() => IsDebugActivityExpanded = !IsDebugActivityExpanded);
        RefreshCommand = new AsyncRelayCommand(RefreshExplicitAsync);
    }

    /// <summary>Gets the current application text.</summary>
    public IMessageCenterText Text => _textProvider();

    /// <summary>Gets committed modal visibility from the session.</summary>
    public bool IsOpen => _session.IsOpen;

    /// <summary>Gets whether the activity pane is selected.</summary>
    public bool IsSystemInformationSelected => _session.IsActivitySelected;

    /// <summary>Gets whether the application's report pane is selected.</summary>
    public bool IsRunReportsSelected => !IsSystemInformationSelected;

    /// <summary>Gets the severity filter. Its generated backing state is UI-thread-only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityItems))]
    [NotifyPropertyChangedFor(nameof(HasActivityItems))]
    [NotifyPropertyChangedFor(nameof(HasNoActivityItems))]
    [NotifyPropertyChangedFor(nameof(IsImportantActivitySelected))]
    [NotifyPropertyChangedFor(nameof(IsWarningActivitySelected))]
    [NotifyPropertyChangedFor(nameof(IsErrorActivitySelected))]
    public partial MessageActivityFilter SelectedActivityFilter { get; private set; }

    /// <summary>Gets debug disclosure. Its generated backing state is UI-thread-only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityItems))]
    [NotifyPropertyChangedFor(nameof(HasActivityItems))]
    [NotifyPropertyChangedFor(nameof(HasNoActivityItems))]
    [NotifyPropertyChangedFor(nameof(DebugActivityActionLabel))]
    public partial bool IsDebugActivityExpanded { get; private set; }

    /// <summary>Gets whether all disclosed severities are selected.</summary>
    public bool IsImportantActivitySelected => SelectedActivityFilter == MessageActivityFilter.Important;

    /// <summary>Gets whether warnings are selected.</summary>
    public bool IsWarningActivitySelected => SelectedActivityFilter == MessageActivityFilter.Warnings;

    /// <summary>Gets whether errors are selected.</summary>
    public bool IsErrorActivitySelected => SelectedActivityFilter == MessageActivityFilter.Errors;

    /// <summary>Captures, filters, orders, and projects rows through the current host callback.</summary>
    public IReadOnlyList<MessageCenterActivityItem> ActivityItems => MessageCenterActivityFilter.Apply(
        _provider.CaptureActivity(), SelectedActivityFilter, IsDebugActivityExpanded);

    /// <summary>Gets whether the current projection contains rows.</summary>
    public bool HasActivityItems => ActivityItems.Count > 0;

    /// <summary>Gets whether the current projection contains no rows.</summary>
    public bool HasNoActivityItems => !HasActivityItems;

    /// <summary>Gets the passive active diagnostic count without capturing activity.</summary>
    public int ActiveBadgeCount => _provider.ActiveDiagnosticCount;

    /// <summary>Gets whether the passive active diagnostic count is positive.</summary>
    public bool HasActiveDiagnostics => ActiveBadgeCount > 0;

    /// <summary>Gets whether the passive active diagnostic count is not positive.</summary>
    public bool HasNoActiveDiagnostics => !HasActiveDiagnostics;

    /// <summary>Gets the formatted unfiltered passive activity count.</summary>
    public string SessionActivitySummary => Text.FormatSessionActivitySummary(_provider.ActivityCount);

    /// <summary>Gets the action label for the opposite disclosure state.</summary>
    public string DebugActivityActionLabel => IsDebugActivityExpanded
        ? Text.HideDebugActivityLabel : Text.ShowDebugActivityLabel;

    /// <summary>Gets the accessible name from the passive badge count.</summary>
    public string MessageCenterAccessibleName => Text.FormatMessageCenterAccessibleName(ActiveBadgeCount);

    /// <summary>Gets the progress announcement or the formatted active count.</summary>
    public string SystemStatusAnnouncement => IsRefreshInProgress
        ? Text.RefreshingDiagnosticsLabel : Text.FormatSystemDiagnosticAnnouncement(ActiveBadgeCount);

    /// <summary>Gets the visible idle or progress action label.</summary>
    public string RefreshActionLabel => IsRefreshInProgress
        ? Text.RefreshingDiagnosticsLabel : Text.RefreshDiagnosticsLabel;

    /// <summary>Gets or sets host-owned refresh progress; generated backing state is UI-thread-only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemStatusAnnouncement))]
    [NotifyPropertyChangedFor(nameof(RefreshActionLabel))]
    public partial bool IsRefreshInProgress { get; protected set; }

    // These properties remain independently writable to preserve ordered callbacks and reflection setters.
    /// <summary>Gets or sets export status; generated backing state is UI-thread-only.</summary>
    [ObservableProperty]
    public partial string ExportStatus { get; protected set; } = string.Empty;

    /// <summary>Gets or sets failure styling; generated backing state is UI-thread-only.</summary>
    [ObservableProperty]
    public partial bool HasExportFailure { get; protected set; }

    /// <summary>Gets the session generation used by the export workflow.</summary>
    public long ExportContextGeneration => _session.ExportContextGeneration;

    /// <summary>Checks generation, open visibility, and activity selection in the session's order.</summary>
    /// <param name="generation">The captured export generation.</param>
    public bool IsExportContextCurrent(long generation) => _session.IsExportContextCurrent(generation);

    /// <summary>Opens the modal after reset and activity recording.</summary>
    public IRelayCommand OpenCommand { get; }

    /// <summary>Closes the report before committing closed modal visibility.</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>Closes the report, selects reports, then opens the modal.</summary>
    public IRelayCommand OpenRunReportsCommand { get; }

    /// <summary>Selects the application's report pane.</summary>
    public IRelayCommand ShowRunReportsCommand { get; }

    /// <summary>Selects the activity pane.</summary>
    public IRelayCommand ShowSystemInformationCommand { get; }

    /// <summary>Selects every disclosed severity.</summary>
    public IRelayCommand ShowImportantActivityCommand { get; }

    /// <summary>Selects warning activity.</summary>
    public IRelayCommand ShowWarningActivityCommand { get; }

    /// <summary>Selects error activity.</summary>
    public IRelayCommand ShowErrorActivityCommand { get; }

    /// <summary>Toggles debug disclosure.</summary>
    public IRelayCommand ToggleDebugActivityCommand { get; }

    /// <summary>Records the refresh interaction before invoking the explicit host delegate.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Delegates export and generation-sensitive status publication to the shared workflow.</summary>
    /// <param name="destinationPath">The unchanged destination supplied to the exporter.</param>
    /// <param name="cancellationToken">The unchanged exporter token.</param>
    public Task ExportAsync(string destinationPath, CancellationToken cancellationToken) =>
        _exportWorkflow.ExportAsync(destinationPath, cancellationToken);

    /// <summary>Delegates picker acceptance and failure handling to the shared workflow.</summary>
    /// <param name="pickPathAsync">The application's picker.</param>
    /// <param name="isViewContextCurrent">Checks the view identity captured by the application.</param>
    public Task ExportWithPickerAsync(Func<Task<string?>> pickPathAsync, Func<bool> isViewContextCurrent) =>
        _exportWorkflow.ExportWithPickerAsync(pickPathAsync, isViewContextCurrent);

    /// <summary>Records export failure, then changes styling and status and notifies activity.</summary>
    /// <remarks>Interaction, text, and setter observer failures propagate at their original sites.</remarks>
    public void ReportExportFailure()
    {
        _interaction(MessageCenterInteraction.ExportFailed);
        HasExportFailure = true;
        ExportStatus = Text.DiagnosticsExportFailedLabel;
        NotifyActivityChanged();
    }

    /// <summary>Notifies current text and projections, then clears export failure and status.</summary>
    /// <remarks>Does not advance generation. Notifications are unguarded, preserving failure order.</remarks>
    public void ApplyLanguageChanged()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(MessageCenterAccessibleName));
        OnPropertyChanged(nameof(SystemStatusAnnouncement));
        OnPropertyChanged(nameof(RefreshActionLabel));
        OnPropertyChanged(nameof(ActivityItems));
        OnPropertyChanged(nameof(SessionActivitySummary));
        OnPropertyChanged(nameof(DebugActivityActionLabel));
        HasExportFailure = false;
        if (!string.IsNullOrEmpty(ExportStatus))
        {
            ExportStatus = string.Empty;
        }
    }

    /// <summary>Notifies each committed activity projection independently, isolating observer faults.</summary>
    public void NotifyActivityChanged()
    {
        Observe(() => OnPropertyChanged(nameof(ActivityItems)));
        Observe(() => OnPropertyChanged(nameof(HasActivityItems)));
        Observe(() => OnPropertyChanged(nameof(HasNoActivityItems)));
        Observe(() => OnPropertyChanged(nameof(SessionActivitySummary)));
    }

    /// <summary>Notifies committed diagnostic counts and announcements, then activity projections.</summary>
    public void NotifyDiagnosticsChanged()
    {
        Observe(() => OnPropertyChanged(nameof(ActiveBadgeCount)));
        Observe(() => OnPropertyChanged(nameof(HasActiveDiagnostics)));
        Observe(() => OnPropertyChanged(nameof(HasNoActiveDiagnostics)));
        Observe(() => OnPropertyChanged(nameof(MessageCenterAccessibleName)));
        Observe(() => OnPropertyChanged(nameof(SystemStatusAnnouncement)));
        Observe(NotifyActivityChanged);
    }

    private void ReportExportSuccess()
    {
        _interaction(MessageCenterInteraction.ExportSucceeded);
        HasExportFailure = false;
        ExportStatus = Text.DiagnosticsExportedLabel;
        NotifyActivityChanged();
    }

    private void Open()
    {
        bool changing = false;
        _session.Open(() =>
        {
            HasExportFailure = false;
            ExportStatus = string.Empty;
            _interaction(MessageCenterInteraction.Opened);
            NotifyActivityChanged();
            changing = !IsOpen;
            if (changing) { OnPropertyChanging(nameof(IsOpen)); }
        });
        if (changing) { OnPropertyChanged(nameof(IsOpen)); }
    }

    private void Close()
    {
        bool changing = false;
        _session.Close(() =>
        {
            _closeReport();
            changing = IsOpen;
            if (changing) { OnPropertyChanging(nameof(IsOpen)); }
        });
        if (changing) { OnPropertyChanged(nameof(IsOpen)); }
    }

    private void SelectSystemInformation(bool selected)
    {
        if (IsSystemInformationSelected == selected) { return; }
        _session.SelectActivity(selected, () =>
        {
            OnPropertyChanging(nameof(IsSystemInformationSelected));
            OnPropertyChanging(nameof(IsRunReportsSelected));
        });
        OnPropertyChanged(nameof(IsSystemInformationSelected));
        OnPropertyChanged(nameof(IsRunReportsSelected));
    }

    private async Task RefreshExplicitAsync(CancellationToken cancellationToken)
    {
        _interaction(MessageCenterInteraction.RefreshRequested);
        NotifyActivityChanged();
        await _refresh(cancellationToken);
    }

    private static void Observe(Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Presentation observer failed: {0}", exception);
        }
    }
}


