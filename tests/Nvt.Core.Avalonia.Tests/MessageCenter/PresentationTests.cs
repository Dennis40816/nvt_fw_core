// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.MessageCenter;
using Nvt.Core.MessageCenter;
using Xunit;
using static Nvt.Core.Avalonia.Tests.MessageCenter.PresentationTestValues;

namespace Nvt.Core.Avalonia.Tests.MessageCenter;

/// <summary>Characterizes commands, passive projections, notification order, and host seams.</summary>
public sealed class PresentationTests
{
    /// <summary>Initial modal, filter, disclosure, progress, and export state match the frozen source.</summary>
    [Fact]
    public void InitialStateUsesOneClosedActivitySession()
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        Assert.False(vm.IsOpen);
        Assert.True(vm.IsSystemInformationSelected);
        Assert.False(vm.IsRunReportsSelected);
        Assert.Equal(0, vm.ExportContextGeneration);
        Assert.False(vm.IsExportContextCurrent(0));
        Assert.Equal(MessageActivityFilter.Important, vm.SelectedActivityFilter);
        Assert.True(vm.IsImportantActivitySelected);
        Assert.False(vm.IsWarningActivitySelected);
        Assert.False(vm.IsErrorActivitySelected);
        Assert.False(vm.IsDebugActivityExpanded);
        Assert.False(vm.IsRefreshInProgress);
        Assert.False(vm.HasExportFailure);
        Assert.Equal(string.Empty, vm.ExportStatus);
        Assert.Equal("A:show-debug", vm.DebugActivityActionLabel);
        Assert.Equal("A:refresh", vm.RefreshActionLabel);
        Assert.False(vm.HasActivityItems);
        Assert.True(vm.HasNoActivityItems);
        Assert.Equal(1, fixture.Provider.Captures);
        Assert.Empty(fixture.Provider.Projections);
        fixture.AssertTrace(string.Empty);
    }

    /// <summary>Every modal command preserves its complete action/property trace, including repetitions.</summary>
    [Fact]
    public void ModalCommandsPreserveCompleteTransitionOrder()
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        vm.ReportExportFailure();
        fixture.Trace.Clear();
        vm.OpenCommand.Execute(null);
        fixture.AssertTrace(FailureResetTrace + "|interaction:Opened|" + ActivityTrace + "|changing:IsOpen|changed:IsOpen");
        Assert.True(vm.IsOpen);
        Assert.Equal(1, vm.ExportContextGeneration);
        fixture.Trace.Clear();
        vm.OpenCommand.Execute(null);
        fixture.AssertTrace("interaction:Opened|" + ActivityTrace);
        Assert.Equal(2, vm.ExportContextGeneration);
        fixture.Trace.Clear();
        vm.ShowRunReportsCommand.Execute(null);
        fixture.AssertTrace(PaneTrace);
        Assert.True(vm.IsRunReportsSelected);
        Assert.True(vm.IsOpen);
        Assert.Equal(3, vm.ExportContextGeneration);
        fixture.Trace.Clear();
        vm.ShowRunReportsCommand.Execute(null);
        fixture.AssertTrace(string.Empty);
        Assert.Equal(3, vm.ExportContextGeneration);
        vm.ShowSystemInformationCommand.Execute(null);
        fixture.AssertTrace(PaneTrace);
        Assert.True(vm.IsSystemInformationSelected);
        Assert.Equal(4, vm.ExportContextGeneration);
        fixture.Trace.Clear();
        vm.ShowSystemInformationCommand.Execute(null);
        fixture.AssertTrace(string.Empty);
        vm.CloseCommand.Execute(null);
        fixture.AssertTrace("close-report|changing:IsOpen|changed:IsOpen");
        Assert.False(vm.IsOpen);
        Assert.Equal(5, vm.ExportContextGeneration);
        fixture.Trace.Clear();
        vm.CloseCommand.Execute(null);
        fixture.AssertTrace("close-report");
        Assert.Equal(6, vm.ExportContextGeneration);
        fixture.Trace.Clear();
        vm.OpenRunReportsCommand.Execute(null);
        fixture.AssertTrace("close-report|" + PaneTrace + "|interaction:Opened|" + ActivityTrace + "|changing:IsOpen|changed:IsOpen");
        Assert.True(vm.IsOpen);
        Assert.True(vm.IsRunReportsSelected);
        Assert.Equal(8, vm.ExportContextGeneration);
        fixture.Trace.Clear();
        vm.OpenRunReportsCommand.Execute(null);
        fixture.AssertTrace("close-report|interaction:Opened|" + ActivityTrace);
        Assert.Equal(9, vm.ExportContextGeneration);
        fixture.Trace.Clear();
        vm.CloseCommand.Execute(null);
        fixture.AssertTrace("close-report|changing:IsOpen|changed:IsOpen");
        Assert.True(vm.IsRunReportsSelected);
    }

    /// <summary>Changing observers see advanced generation and old visibility or selection before commit.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PreCommitObserversSeeAdvancedGenerationAndOldState(int operation)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        if (operation == 1) { vm.OpenCommand.Execute(null); }
        bool oldVisibility = vm.IsOpen;
        bool oldPane = vm.IsSystemInformationSelected;
        long generation = vm.ExportContextGeneration;
        int observations = 0;
        vm.PropertyChanging += (_, args) =>
        {
            observations++;
            Assert.True(operation == 2 ? args.PropertyName is "IsSystemInformationSelected" or "IsRunReportsSelected" : args.PropertyName == "IsOpen");
            Assert.Equal(generation + 1, vm.ExportContextGeneration);
            Assert.Equal(oldVisibility, vm.IsOpen);
            Assert.Equal(oldPane, vm.IsSystemInformationSelected);
        };
        ModalCommand(vm, operation).Execute(null);
        Assert.Equal(operation == 2 ? 2 : 1, observations);
        Assert.Equal(operation == 0, vm.IsOpen);
        Assert.Equal(operation != 2, vm.IsSystemInformationSelected);
    }

    /// <summary>A throwing pre-commit observer prevents commit but retains the generation advance.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ThrowingPreCommitObserverPreventsCommit(int operation)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        if (operation == 1) { vm.OpenCommand.Execute(null); }
        bool oldVisibility = vm.IsOpen;
        bool oldPane = vm.IsSystemInformationSelected;
        long generation = vm.ExportContextGeneration;
        fixture.Trace.Clear();
        var failure = new InvalidOperationException("Synthetic pre-commit failure");
        vm.PropertyChanging += (_, _) => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => ModalCommand(vm, operation).Execute(null)));
        Assert.Equal(generation + 1, vm.ExportContextGeneration);
        Assert.Equal(oldVisibility, vm.IsOpen);
        Assert.Equal(oldPane, vm.IsSystemInformationSelected);
        fixture.AssertTrace(operation switch
        {
            0 => "interaction:Opened|" + ActivityTrace + "|changing:IsOpen",
            1 => "close-report|changing:IsOpen",
            _ => "changing:IsSystemInformationSelected",
        });
    }

    /// <summary>Open reset precedes interaction and visibility; interaction faults remain unguarded.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingOpenInteractionKeepsOldVisibilityAfterReset(bool alreadyOpen)
    {
        var failure = new InvalidOperationException("Synthetic interaction failure");
        Facade? vm = null;
        bool shouldThrow = false;
        var fixture = new Fixture(interaction: value =>
        {
            if (value != MessageCenterInteraction.Opened || !shouldThrow) { return; }
            Assert.Equal(alreadyOpen, vm!.IsOpen);
            Assert.False(vm.HasExportFailure);
            Assert.Equal(string.Empty, vm.ExportStatus);
            throw failure;
        });
        vm = fixture.ViewModel;
        if (alreadyOpen) { vm.OpenCommand.Execute(null); }
        vm.ReportExportFailure();
        long generation = vm.ExportContextGeneration;
        fixture.Trace.Clear();
        shouldThrow = true;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => vm.OpenCommand.Execute(null)));
        Assert.Equal(generation + 1, vm.ExportContextGeneration);
        Assert.Equal(alreadyOpen, vm.IsOpen);
        fixture.AssertTrace(FailureResetTrace + "|interaction:Opened");
    }

    /// <summary>Close-report faults preserve prior modal state at the correct generation point.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ThrowingCloseReportPreservesStateAndOperationOrder(bool open, bool openReports)
    {
        var failure = new InvalidOperationException("Synthetic report close failure");
        Facade? vm = null;
        long generation = 0;
        var fixture = new Fixture(close: () =>
        {
            Assert.Equal(generation + (openReports ? 0 : 1), vm!.ExportContextGeneration);
            Assert.Equal(open, vm.IsOpen);
            Assert.True(vm.IsSystemInformationSelected);
            throw failure;
        });
        vm = fixture.ViewModel;
        if (open) { vm.OpenCommand.Execute(null); }
        generation = vm.ExportContextGeneration;
        fixture.Trace.Clear();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            (openReports ? vm.OpenRunReportsCommand : vm.CloseCommand).Execute(null)));
        Assert.Equal(generation + (openReports ? 0 : 1), vm.ExportContextGeneration);
        Assert.Equal(open, vm.IsOpen);
        Assert.True(vm.IsSystemInformationSelected);
        fixture.AssertTrace("close-report");
    }

    /// <summary>Advancing commands honor the fixed maximum before callbacks and commits.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void GenerationBoundaryPreservesSessionOverflowOrder(int operation)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        SetGeneration(vm, long.MaxValue - 2);
        ModalCommand(vm, operation).Execute(null);
        Assert.Equal(long.MaxValue - 1, vm.ExportContextGeneration);
        ModalCommand(vm, operation).Execute(null);
        Assert.Equal(long.MaxValue, vm.ExportContextGeneration);
        bool oldVisibility = vm.IsOpen;
        bool oldPane = vm.IsSystemInformationSelected;
        fixture.Trace.Clear();
        Assert.Throws<OverflowException>(() => ModalCommand(vm, operation).Execute(null));
        fixture.AssertTrace(string.Empty);
        Assert.Equal(long.MaxValue, vm.ExportContextGeneration);
        Assert.Equal(oldVisibility, vm.IsOpen);
        Assert.Equal(oldPane, vm.IsSystemInformationSelected);
    }

    /// <summary>Opening reports closes the report before pane or visibility generation overflow.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenReportsBoundaryKeepsPartialTransitionOrder(bool alreadyReports)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        if (alreadyReports) { vm.ShowRunReportsCommand.Execute(null); }
        SetGeneration(vm, long.MaxValue - 1);
        fixture.Trace.Clear();
        if (alreadyReports)
        {
            vm.OpenRunReportsCommand.Execute(null);
            Assert.True(vm.IsOpen);
            fixture.AssertTrace("close-report|interaction:Opened|" + ActivityTrace + "|changing:IsOpen|changed:IsOpen");
        }
        else
        {
            Assert.Throws<OverflowException>(() => vm.OpenRunReportsCommand.Execute(null));
            Assert.False(vm.IsOpen);
            fixture.AssertTrace("close-report|" + PaneTrace);
        }
        Assert.True(vm.IsRunReportsSelected);
        Assert.Equal(long.MaxValue, vm.ExportContextGeneration);
        fixture.Trace.Clear();
        Assert.Throws<OverflowException>(() => vm.OpenRunReportsCommand.Execute(null));
        fixture.AssertTrace("close-report");
    }

    /// <summary>Same-pane commands remain silent at the generation maximum for both panes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SamePaneAtMaximumDoesNotAdvance(bool activity)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        if (!activity) { vm.ShowRunReportsCommand.Execute(null); }
        SetGeneration(vm, long.MaxValue);
        fixture.Trace.Clear();
        (activity ? vm.ShowSystemInformationCommand : vm.ShowRunReportsCommand).Execute(null);
        Assert.Equal(long.MaxValue, vm.ExportContextGeneration);
        Assert.Equal(activity, vm.IsSystemInformationSelected);
        fixture.AssertTrace(string.Empty);
    }

    /// <summary>Passive counts pass unchanged at zero, negative, positive and signed extremes without projection.</summary>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void PassiveCountsDoNotCaptureOrProject(int count)
    {
        var fixture = new Fixture();
        fixture.Provider.SetCounts(count, count);
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Success);
        Facade vm = fixture.ViewModel;
        Assert.Equal(count, vm.ActiveBadgeCount);
        Assert.Equal(count > 0, vm.HasActiveDiagnostics);
        Assert.Equal(count <= 0, vm.HasNoActiveDiagnostics);
        Assert.Equal($"A:activity:{count}", vm.SessionActivitySummary);
        Assert.Equal($"A:name:{count}", vm.MessageCenterAccessibleName);
        Assert.Equal($"A:diagnostics:{count}", vm.SystemStatusAnnouncement);
        Assert.Equal(0, fixture.Provider.Captures);
        Assert.Empty(fixture.Provider.Projections);
        vm.SetProgress(true);
        Assert.Equal("A:refreshing", vm.SystemStatusAnnouncement);
        Assert.Equal("A:refreshing", vm.RefreshActionLabel);
        Assert.Equal(0, fixture.Provider.Captures);
    }

    /// <summary>Filter/disclosure combinations project admitted metadata with correct severity flags.</summary>
    [Theory]
    [InlineData(0, false, "4,3,2,1")]
    [InlineData(0, true, "6,5,4,3,2,1")]
    [InlineData(1, false, "3")]
    [InlineData(1, true, "5,3")]
    [InlineData(2, false, "4")]
    [InlineData(2, true, "6,4")]
    public void FiltersProjectOnlyDisclosedMatchingMetadata(int filter, bool debug, string expected)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Information);
        fixture.Provider.Add(2, MessageActivityImportance.Important, MessageActivitySeverity.Success);
        fixture.Provider.Add(3, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        fixture.Provider.Add(4, MessageActivityImportance.Important, MessageActivitySeverity.Error);
        fixture.Provider.Add(5, MessageActivityImportance.Debug, MessageActivitySeverity.Warning);
        fixture.Provider.Add(6, MessageActivityImportance.Debug, MessageActivitySeverity.Error);
        if (filter == 1) { vm.ShowWarningActivityCommand.Execute(null); }
        if (filter == 2) { vm.ShowErrorActivityCommand.Execute(null); }
        if (debug) { vm.ToggleDebugActivityCommand.Execute(null); }
        Assert.Equal((MessageActivityFilter)filter, vm.SelectedActivityFilter);
        Assert.Equal(filter == 0, vm.IsImportantActivitySelected);
        Assert.Equal(filter == 1, vm.IsWarningActivitySelected);
        Assert.Equal(filter == 2, vm.IsErrorActivitySelected);
        Assert.Equal(debug ? "A:hide-debug" : "A:show-debug", vm.DebugActivityActionLabel);
        IReadOnlyList<MessageCenterActivityItem> rows = vm.ActivityItems;
        Assert.Equal(expected, string.Join(',', fixture.Provider.Projections));
        Assert.Equal(1, fixture.Provider.Captures);
        Assert.All(rows, row =>
        {
            Assert.Equal(row.Severity == MessageActivitySeverity.Information, row.IsInformation);
            Assert.Equal(row.Severity == MessageActivitySeverity.Success, row.IsSuccess);
            Assert.Equal(row.Severity == MessageActivitySeverity.Warning, row.IsWarning);
            Assert.Equal(row.Severity == MessageActivitySeverity.Error, row.IsError);
        });
        Assert.Equal(6, fixture.Provider.Entries.Count);
    }

    /// <summary>Filter/disclosure notifications follow Toolkit order and suppress same-value filter changes.</summary>
    [Fact]
    public void FilterAndDisclosureCommandsKeepCompleteNotificationOrder()
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        const string filterTrace = "changing:SelectedActivityFilter|changing:ActivityItems|changing:HasActivityItems|changing:HasNoActivityItems|changing:IsImportantActivitySelected|changing:IsWarningActivitySelected|changing:IsErrorActivitySelected|changed:SelectedActivityFilter|changed:ActivityItems|changed:HasActivityItems|changed:HasNoActivityItems|changed:IsImportantActivitySelected|changed:IsWarningActivitySelected|changed:IsErrorActivitySelected";
        const string debugTrace = "changing:IsDebugActivityExpanded|changing:ActivityItems|changing:HasActivityItems|changing:HasNoActivityItems|changing:DebugActivityActionLabel|changed:IsDebugActivityExpanded|changed:ActivityItems|changed:HasActivityItems|changed:HasNoActivityItems|changed:DebugActivityActionLabel";
        vm.ShowImportantActivityCommand.Execute(null);
        fixture.AssertTrace(string.Empty);
        vm.ShowWarningActivityCommand.Execute(null);
        fixture.AssertTrace(filterTrace);
        fixture.Trace.Clear();
        vm.ShowWarningActivityCommand.Execute(null);
        fixture.AssertTrace(string.Empty);
        vm.ShowErrorActivityCommand.Execute(null);
        fixture.AssertTrace(filterTrace);
        fixture.Trace.Clear();
        vm.ShowImportantActivityCommand.Execute(null);
        fixture.AssertTrace(filterTrace);
        fixture.Trace.Clear();
        vm.ToggleDebugActivityCommand.Execute(null);
        fixture.AssertTrace(debugTrace);
        fixture.Trace.Clear();
        vm.ToggleDebugActivityCommand.Execute(null);
        fixture.AssertTrace(debugTrace);
        Assert.False(vm.IsDebugActivityExpanded);
        Assert.Equal(0, vm.ExportContextGeneration);
        Assert.Equal(0, fixture.Provider.Captures);
    }

    /// <summary>Language change reprojects host strings, resets status in order, and retains generation.</summary>
    [Fact]
    public void ActivityDisclosureAndLanguageReprojectionPreserveResetOrder()
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        fixture.Provider.SetCounts(2, 7);
        fixture.Provider.Add(1, MessageActivityImportance.Debug, MessageActivitySeverity.Information);
        fixture.Provider.Add(2, MessageActivityImportance.Important, MessageActivitySeverity.Success);
        Assert.Equal(new MessageCenterActivityItem("A:time:2", "A:title:2", "A:detail:2", "A:category:2", "A:status:2", MessageActivitySeverity.Success), Assert.Single(vm.ActivityItems));
        vm.ToggleDebugActivityCommand.Execute(null);
        Assert.Equal(2, vm.ActivityItems.Count);
        vm.OpenCommand.Execute(null);
        vm.ReportExportFailure();
        long generation = vm.ExportContextGeneration;
        fixture.Text = B;
        fixture.Trace.Clear();
        vm.ApplyLanguageChanged();
        fixture.AssertTrace(LanguageTrace + "|" + FailureResetTrace);
        Assert.Same(B, vm.Text);
        Assert.False(vm.HasExportFailure);
        Assert.Equal(string.Empty, vm.ExportStatus);
        Assert.Equal(generation, vm.ExportContextGeneration);
        Assert.True(vm.IsExportContextCurrent(generation));
        IReadOnlyList<MessageCenterActivityItem> rows = vm.ActivityItems;
        Assert.Equal(new MessageCenterActivityItem("B:time:2", "B:title:2", "B:detail:2", "B:category:2", "B:status:2", MessageActivitySeverity.Success), rows[0]);
        Assert.Equal(new MessageCenterActivityItem("B:time:1", "B:title:1", "B:detail:1", "B:category:1", "B:status:1", MessageActivitySeverity.Information), rows[1]);
        Assert.Equal("B:activity:7", vm.SessionActivitySummary);
        Assert.Equal("B:name:2", vm.MessageCenterAccessibleName);
        Assert.Equal("B:diagnostics:2", vm.SystemStatusAnnouncement);
        Assert.Equal("B:refresh", vm.RefreshActionLabel);
        Assert.Equal("B:hide-debug", vm.DebugActivityActionLabel);
        fixture.Trace.Clear();
        vm.ApplyLanguageChanged();
        fixture.AssertTrace(LanguageTrace);
    }

    /// <summary>Committed activity/diagnostic observers are independently isolated at each original site.</summary>
    [Fact]
    public void CommittedNotificationSitesContinueAfterEachObserverFault()
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        vm.PropertyChanged += static (_, _) => throw new InvalidOperationException("Synthetic committed observer failure");
        vm.NotifyActivityChanged();
        fixture.AssertTrace(ActivityTrace);
        fixture.Trace.Clear();
        vm.NotifyDiagnosticsOutsideScope();
        fixture.AssertTrace(DiagnosticTrace);
        Assert.Equal(0, fixture.Provider.Captures);
        Assert.Equal(0, vm.ExportContextGeneration);
    }

    /// <summary>Unguarded language/export observers abort subsequent work at the original site.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnguardedObserversPropagateWithoutFinishingTransition(bool language)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        vm.SetStatus("existing");
        var failure = new InvalidOperationException("Synthetic unguarded observer failure");
        vm.PropertyChanged += (_, _) => throw failure;
        fixture.Trace.Clear();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
        {
            if (language) { vm.ApplyLanguageChanged(); }
            else { vm.ReportExportFailure(); }
        }));
        Assert.Equal("existing", vm.ExportStatus);
        Assert.Equal(!language, vm.HasExportFailure);
        fixture.AssertTrace(language ? "changed:Text" : "interaction:ExportFailed|changing:HasExportFailure|changed:HasExportFailure");
    }

    /// <summary>Post-commit modal observer faults propagate while retaining the new committed state.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PostCommitModalObserverFailureRetainsCommittedState(int operation)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        if (operation == 1) { vm.OpenCommand.Execute(null); }
        var failure = new InvalidOperationException("Synthetic committed modal failure");
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is "IsOpen" or "IsSystemInformationSelected") { throw failure; }
        };
        fixture.Trace.Clear();
        long generation = vm.ExportContextGeneration;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => ModalCommand(vm, operation).Execute(null)));
        Assert.Equal(generation + 1, vm.ExportContextGeneration);
        Assert.Equal(operation == 0, vm.IsOpen);
        Assert.Equal(operation != 2, vm.IsSystemInformationSelected);
        fixture.AssertTrace(operation switch
        {
            0 => "interaction:Opened|" + ActivityTrace + "|changing:IsOpen|changed:IsOpen",
            1 => "close-report|changing:IsOpen|changed:IsOpen",
            _ => "changing:IsSystemInformationSelected|changing:IsRunReportsSelected|changed:IsSystemInformationSelected",
        });
    }

    /// <summary>A facade inserts product progress names through the inherited virtual notification seam.</summary>
    [Fact]
    public void FacadeProgressSeamKeepsProductNamesBeforeGenericDependents()
    {
        var fixture = new Fixture(productProgressNotifications: true);
        const string expected = "changing:IsRefreshInProgress|changing:HostAvailability|changing:HostAvailabilityText|changing:SystemStatusAnnouncement|changing:RefreshActionLabel|changed:IsRefreshInProgress|changed:HostAvailability|changed:HostAvailabilityText|changed:SystemStatusAnnouncement|changed:RefreshActionLabel";
        fixture.ViewModel.SetProgress(true);
        fixture.AssertTrace(expected);
        fixture.Trace.Clear();
        fixture.ViewModel.SetProgress(false);
        fixture.AssertTrace(expected);
    }

    /// <summary>Protected setters remain discoverable and writable through a derived binding facade.</summary>
    [Fact]
    public void InheritedPropertiesRetainReflectionSetters()
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        typeof(Facade).GetProperty(nameof(MessageCenterViewModel.ExportStatus))!.SetValue(vm, "revised-status");
        typeof(Facade).GetProperty(nameof(MessageCenterViewModel.HasExportFailure))!.SetValue(vm, true);
        typeof(Facade).GetProperty(nameof(MessageCenterViewModel.IsRefreshInProgress))!.SetValue(vm, true);
        Assert.Equal("revised-status", vm.ExportStatus);
        Assert.True(vm.HasExportFailure);
        Assert.True(vm.IsRefreshInProgress);
        fixture.AssertTrace("changing:ExportStatus|changed:ExportStatus|changing:HasExportFailure|changed:HasExportFailure|" + ProgressTrace);
    }

    /// <summary>Gated UI-thread refresh records interaction first; the host owns progress and reset timing.</summary>
    [AvaloniaFact]
    public async Task RefreshCommandPublishesProgressUntilHostReloadCompletes()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var entered = Gate();
        var release = Gate();
        var fixture = new Fixture(refresh: (vm, token) => vm.RefreshWithProgressAsync(async refreshToken =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            entered.SetResult();
            await release.Task.WaitAsync(refreshToken);
            Assert.True(Dispatcher.UIThread.CheckAccess());
        }, token));
        Facade vm = fixture.ViewModel;
        vm.ReportExportFailure();
        fixture.Trace.Clear();
        long generation = vm.ExportContextGeneration;
        Task refreshing = vm.RefreshCommand.ExecuteAsync(null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(vm.RefreshCommand.IsRunning);
            Assert.False(vm.RefreshCommand.CanExecute(null));
            Assert.True(vm.IsRefreshInProgress);
            Assert.Equal("A:refreshing", vm.RefreshActionLabel);
            Assert.Equal("A:refreshing", vm.SystemStatusAnnouncement);
            Assert.True(vm.HasExportFailure);
            fixture.AssertTrace("interaction:RefreshRequested|" + ActivityTrace + "|" + ProgressTrace);
        }
        finally { release.TrySetResult(); }
        await refreshing;
        Assert.False(vm.IsRefreshInProgress);
        Assert.False(vm.RefreshCommand.IsRunning);
        Assert.True(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.HasExportFailure);
        Assert.Equal(string.Empty, vm.ExportStatus);
        Assert.Equal("A:refresh", vm.RefreshActionLabel);
        Assert.Equal(generation, vm.ExportContextGeneration);
        fixture.AssertTrace("interaction:RefreshRequested|" + ActivityTrace + "|" + ProgressTrace + "|" + FailureResetTrace + "|" + DiagnosticTrace + "|" + ProgressTrace);
    }

    /// <summary>Committed host progress/reset isolation preserves success, operation faults, and cancellation.</summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RefreshObserverIsolationPreservesSuccessFailureAndCancellation(int outcome)
    {
        var entered = Gate();
        var release = Gate();
        var failure = new InvalidOperationException("Synthetic refresh failure");
        var fixture = new Fixture(refresh: (vm, token) => vm.RefreshWithProgressAsync(async refreshToken =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(refreshToken);
            if (outcome == 1) { throw failure; }
            if (outcome == 2) { throw new OperationCanceledException(refreshToken); }
        }, token));
        Facade vm = fixture.ViewModel;
        vm.ReportExportFailure();
        fixture.Trace.Clear();
        vm.PropertyChanged += static (_, _) => throw new InvalidOperationException("Synthetic notification fault");
        Task refresh = vm.RefreshCommand.ExecuteAsync(null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(vm.IsRefreshInProgress);
        }
        finally { release.TrySetResult(); }
        Exception? observed = await Record.ExceptionAsync(() => refresh);
        if (outcome == 0) { Assert.Null(observed); }
        if (outcome == 1) { Assert.Same(failure, observed); }
        if (outcome == 2) { Assert.IsType<OperationCanceledException>(observed); }
        Assert.False(vm.IsRefreshInProgress);
        Assert.Equal("A:export-failed", vm.ExportStatus);
        // On success, the failure-flag observer interrupts the grouped host reset before status.
        Assert.Equal(outcome != 0, vm.HasExportFailure);
        Assert.Equal("A:refresh", vm.RefreshActionLabel);
        string expected = "interaction:RefreshRequested|" + ActivityTrace + "|changing:IsRefreshInProgress|changing:SystemStatusAnnouncement|changing:RefreshActionLabel|changed:IsRefreshInProgress";
        if (outcome == 0) { expected += "|changing:HasExportFailure|changed:HasExportFailure|" + DiagnosticTrace; }
        expected += "|changing:IsRefreshInProgress|changing:SystemStatusAnnouncement|changing:RefreshActionLabel|changed:IsRefreshInProgress";
        fixture.AssertTrace(expected);
    }

    /// <summary>Toolkit command cancellation forwards its owner token and unwinds host progress.</summary>
    [AvaloniaFact]
    public async Task RefreshCommandCancellationForwardsOwnerToken()
    {
        var entered = Gate();
        CancellationToken observedToken = default;
        var fixture = new Fixture(refresh: (vm, token) => vm.RefreshWithProgressAsync(async refreshToken =>
        {
            observedToken = refreshToken;
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, refreshToken);
        }, token));
        Task refreshing = fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.ViewModel.RefreshCommand.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refreshing);
        Assert.True(observedToken.IsCancellationRequested);
        Assert.False(fixture.ViewModel.IsRefreshInProgress);
    }

    /// <summary>Interaction faults prevent refresh and are never isolated as observer failures.</summary>
    [Fact]
    public async Task RefreshInteractionFaultPreventsHostWork()
    {
        int refreshes = 0;
        var failure = new InvalidOperationException("Synthetic interaction failure");
        var fixture = new Fixture(refresh: (_, _) => { refreshes++; return Task.CompletedTask; }, interaction: _ => throw failure);
        Assert.Same(failure, await Record.ExceptionAsync(() => fixture.ViewModel.RefreshCommand.ExecuteAsync(null)));
        Assert.Equal(0, refreshes);
        fixture.AssertTrace("interaction:RefreshRequested");
    }

    /// <summary>Only inner refresh joins existing coordination; each command still records its interaction.</summary>
    [AvaloniaFact]
    public async Task SuppliedRefreshJoinsExistingInnerCoordinator()
    {
        var entered = Gate();
        var release = Gate();
        int reloads = 0;
        var coordinator = new MessageCenterRefreshCoordinator(async (_, token) =>
        {
            reloads++;
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        Task existing = coordinator.RefreshAsync(true, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var fixture = new Fixture(refresh: (_, token) => coordinator.RefreshAsync(true, token));
        Task command = fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(command.IsCompleted);
        Assert.Equal(1, reloads);
        fixture.AssertTrace("interaction:RefreshRequested|" + ActivityTrace);
        release.SetResult();
        await Task.WhenAll(existing, command);
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, reloads);
        fixture.AssertTrace("interaction:RefreshRequested|" + ActivityTrace + "|interaction:RefreshRequested|" + ActivityTrace);
    }

    /// <summary>Export preserves status order and passes destination/token unchanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportStatusPreservesInteractionSetterAndActivityOrder(bool fails)
    {
        string? path = null;
        CancellationToken token = default;
        var fixture = new Fixture(export: (destination, cancellationToken) =>
        {
            path = destination;
            token = cancellationToken;
            return fails ? Task.FromException(new IOException("Synthetic write failure")) : Task.CompletedTask;
        });
        Facade vm = fixture.ViewModel;
        if (!fails) { vm.ReportExportFailure(); }
        fixture.Trace.Clear();
        await vm.ExportAsync(" unchanged destination ", TestContext.Current.CancellationToken);
        Assert.Equal(" unchanged destination ", path);
        Assert.Equal(TestContext.Current.CancellationToken, token);
        Assert.Equal(fails, vm.HasExportFailure);
        Assert.Equal(fails ? "A:export-failed" : "A:exported", vm.ExportStatus);
        fixture.AssertTrace((fails ? "interaction:ExportFailed|" : "interaction:ExportSucceeded|") + FailureResetTrace + "|" + ActivityTrace);
        Assert.Equal(0, vm.ExportContextGeneration);
    }

    /// <summary>Picker failure remains visible through null/canceled retries and clears after success.</summary>
    [AvaloniaFact]
    public async Task DiagnosticsPickerFailureIsVisibleAndRetryable()
    {
        int writes = 0;
        var fixture = new Fixture(export: (_, token) =>
        {
            Assert.Equal(CancellationToken.None, token);
            writes++;
            return Task.CompletedTask;
        });
        Facade vm = fixture.ViewModel;
        vm.OpenCommand.Execute(null);
        await vm.ExportWithPickerAsync(() => Task.FromException<string?>(new IOException("Synthetic picker failure")), static () => true);
        Assert.True(vm.HasExportFailure);
        Assert.Equal("A:export-failed", vm.ExportStatus);
        await vm.ExportWithPickerAsync(() => Task.FromResult<string?>(null), static () => true);
        await vm.ExportWithPickerAsync(() => Task.FromException<string?>(new OperationCanceledException()), static () => true);
        Assert.Equal("A:export-failed", vm.ExportStatus);
        Assert.Equal(0, writes);
        await vm.ExportWithPickerAsync(() => Task.FromResult<string?>("current.json"), static () => true);
        Assert.Equal(1, writes);
        Assert.False(vm.HasExportFailure);
        Assert.Equal("A:exported", vm.ExportStatus);
    }

    /// <summary>Closed/reopened, changed-pane, and replaced-view picker contexts cannot export.</summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DiagnosticsPickerRejectsStaleContext(int invalidation)
    {
        int writes = 0;
        var fixture = new Fixture(export: (_, _) => { writes++; return Task.CompletedTask; });
        Facade vm = fixture.ViewModel;
        vm.OpenCommand.Execute(null);
        var picker = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task exporting = vm.ExportWithPickerAsync(() => picker.Task, () => invalidation != 2);
        if (invalidation == 0) { vm.CloseCommand.Execute(null); vm.OpenCommand.Execute(null); }
        if (invalidation == 1) { vm.ShowRunReportsCommand.Execute(null); vm.ShowSystemInformationCommand.Execute(null); }
        fixture.Trace.Clear();
        picker.SetResult("stale.json");
        await exporting;
        Assert.Equal(0, writes);
        Assert.Equal(string.Empty, vm.ExportStatus);
        fixture.AssertTrace(string.Empty);
        await vm.ExportWithPickerAsync(() => Task.FromResult<string?>("current.json"), static () => true);
        Assert.Equal(1, writes);
        Assert.Equal("A:exported", vm.ExportStatus);
    }

    /// <summary>A write completes after close/reopen without publishing stale success or failure.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiagnosticsExportCompletionRejectsReopenedContext(bool fails)
    {
        var entered = Gate();
        var release = Gate();
        bool completed = false;
        var fixture = new Fixture(export: async (_, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            completed = true;
            if (fails) { throw new IOException("Synthetic stale failure"); }
        });
        Facade vm = fixture.ViewModel;
        vm.OpenCommand.Execute(null);
        Task exporting = vm.ExportAsync("diagnostics.json", TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            vm.CloseCommand.Execute(null);
            vm.OpenCommand.Execute(null);
            fixture.Trace.Clear();
        }
        finally { release.TrySetResult(); }
        await exporting;
        Assert.True(completed);
        Assert.False(vm.HasExportFailure);
        Assert.Equal(string.Empty, vm.ExportStatus);
        fixture.AssertTrace(string.Empty);
    }

    /// <summary>Success interaction faults retain the workflow's expected-exception catch scope.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportInteractionFaultScopeRemainsUnchanged(bool expected)
    {
        Exception failure = expected ? new IOException("Synthetic interaction I/O failure") : new InvalidOperationException("Synthetic interaction failure");
        var fixture = new Fixture(interaction: interaction =>
        {
            if (interaction == MessageCenterInteraction.ExportSucceeded) { throw failure; }
        });
        Exception? result = await Record.ExceptionAsync(() => fixture.ViewModel.ExportAsync("destination", TestContext.Current.CancellationToken));
        if (expected)
        {
            Assert.Null(result);
            Assert.True(fixture.ViewModel.HasExportFailure);
            fixture.AssertTrace("interaction:ExportSucceeded|interaction:ExportFailed|" + FailureResetTrace + "|" + ActivityTrace);
        }
        else
        {
            Assert.Same(failure, result);
            fixture.AssertTrace("interaction:ExportSucceeded");
            Assert.False(fixture.ViewModel.HasExportFailure);
            Assert.Equal(string.Empty, fixture.ViewModel.ExportStatus);
        }
    }

    /// <summary>Failure interaction faults propagate before status mutation on direct and picker paths.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportFailureInteractionIsUnguarded(bool picker)
    {
        var failure = new InvalidOperationException("Synthetic failure interaction fault");
        var fixture = new Fixture(export: static (_, _) => Task.FromException(new IOException("Synthetic export failure")),
            interaction: value => { if (value == MessageCenterInteraction.ExportFailed) { throw failure; } });
        fixture.ViewModel.OpenCommand.Execute(null);
        fixture.Trace.Clear();
        Exception? observed = await Record.ExceptionAsync(() => picker
            ? fixture.ViewModel.ExportWithPickerAsync(() => Task.FromException<string?>(new IOException("Synthetic picker failure")), static () => true)
            : fixture.ViewModel.ExportAsync("destination", TestContext.Current.CancellationToken));
        Assert.Same(failure, observed);
        Assert.False(fixture.ViewModel.HasExportFailure);
        Assert.Equal(string.Empty, fixture.ViewModel.ExportStatus);
        fixture.AssertTrace("interaction:ExportFailed");
    }

    /// <summary>Refresh/export use their supplied owners without appending to unrelated report history.</summary>
    [Fact]
    public async Task RefreshAndExportPreserveUnrelatedHistory()
    {
        var history = new List<string> { "existing-report" };
        var fixture = new Fixture();
        fixture.ViewModel.OpenRunReportsCommand.Execute(null);
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        await fixture.ViewModel.ExportAsync("destination", TestContext.Current.CancellationToken);
        Assert.Equal("existing-report", Assert.Single(history));
        Assert.True(fixture.ViewModel.IsRunReportsSelected);
        Assert.True(fixture.ViewModel.IsOpen);
        Assert.Equal(0, fixture.Provider.Captures);
    }

    /// <summary>Constructor checks preserve frozen text-first order before passive provider and operation inputs.</summary>
    [Theory]
    [InlineData(0, "textProvider")]
    [InlineData(1, "provider")]
    [InlineData(2, "refresh")]
    [InlineData(3, "export")]
    [InlineData(4, "closeReport")]
    [InlineData(5, "interaction")]
    public void ConstructorNullGuardsPreserveTextFirstOrder(int argument, string expected)
    {
        var provider = new PassiveProvider(() => A);
        ArgumentNullException failure = Assert.Throws<ArgumentNullException>(() => new MessageCenterViewModel(
            argument <= 1 ? null! : provider,
            argument <= 0 ? null! : () => A,
            argument <= 2 ? null! : static _ => Task.CompletedTask,
            argument <= 3 ? null! : static (_, _) => Task.CompletedTask,
            argument <= 4 ? null! : static () => { },
            null!));
        Assert.Equal(expected, failure.ParamName);
    }

    /// <summary>The inherited seam supports complete frozen interleaving for language and diagnostic batches.</summary>
    [Fact]
    public void FacadeInsertsProductNotificationsInFrozenBatchOrder()
    {
        var fixture = new Fixture();
        fixture.ViewModel.NotifyHostLanguageChanged();
        fixture.AssertTrace("changed:Text|changed:HostRows|changed:HostCatalog|changed:HostEnvironment|changed:MessageCenterAccessibleName|changed:SystemStatusAnnouncement|changed:HostAvailabilityText|changed:RefreshActionLabel|changed:ActivityItems|changed:SessionActivitySummary|changed:DebugActivityActionLabel");
        fixture.Trace.Clear();
        fixture.ViewModel.NotifyHostDiagnosticsChanged();
        fixture.AssertTrace(HostDiagnosticTrace);
        fixture.Trace.Clear();
        fixture.ViewModel.NotifyActivityChanged();
        fixture.AssertTrace(ActivityTrace);
    }

    /// <summary>Host forwards and diagnostic tail sites retain committed state after observer faults.</summary>
    [Theory]
    [InlineData("HostCurrent")]
    [InlineData(nameof(MessageCenterViewModel.HasNoActiveDiagnostics))]
    [InlineData("HostRows")]
    [InlineData("HostBlocker")]
    [InlineData("HostAvailability")]
    [InlineData("HostAvailabilityText")]
    [InlineData("HostCatalog")]
    [InlineData("HostEnvironment")]
    public void HostDiagnosticObserverFaultsRetainCommittedStateAndCompleteTrace(string property)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        fixture.Provider.SetCounts(2, 1);
        fixture.Provider.Add(7, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        vm.OpenCommand.Execute(null);
        vm.ReportExportFailure();
        long generation = vm.ExportContextGeneration;
        fixture.Trace.Clear();
        int faults = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == property)
            {
                faults++;
                throw new InvalidOperationException("Synthetic host diagnostic observer failure");
            }
        };
        // The fault must still stop later subscribers at its own site, rather than isolating each observer.
        vm.PropertyChanged += (_, args) => fixture.Trace.Add($"observed:{args.PropertyName}");

        vm.NotifyHostDiagnosticsChanged();

        string expected = string.Join('|', HostDiagnosticTrace.Split('|').Select(notification =>
            notification == $"changed:{property}"
                ? notification
                : notification + "|" + notification.Replace("changed:", "observed:", StringComparison.Ordinal)));
        fixture.AssertTrace(expected);
        Assert.Equal(1, faults);
        Assert.Equal(2, vm.ActiveBadgeCount);
        Assert.True(vm.HasActiveDiagnostics);
        Assert.False(vm.HasNoActiveDiagnostics);
        Assert.Equal("A:name:2", vm.MessageCenterAccessibleName);
        Assert.Equal("A:diagnostics:2", vm.SystemStatusAnnouncement);
        Assert.True(vm.IsOpen);
        Assert.True(vm.IsSystemInformationSelected);
        Assert.Equal(generation, vm.ExportContextGeneration);
        Assert.True(vm.IsExportContextCurrent(generation));
        Assert.True(vm.HasExportFailure);
        Assert.Equal("A:export-failed", vm.ExportStatus);
        Assert.Equal("A:activity:1", vm.SessionActivitySummary);
        Assert.Equal(0, fixture.Provider.Captures);
        Assert.Equal(new MessageCenterActivityItem("A:time:7", "A:title:7", "A:detail:7", "A:category:7", "A:status:7", MessageActivitySeverity.Warning), Assert.Single(vm.ActivityItems));
        Assert.Equal(7, Assert.Single(fixture.Provider.Entries).Sequence);
    }

    /// <summary>A badge observer fault leaves the earlier HostCurrent site and every later site in place.</summary>
    [Fact]
    public void BadgeObserverFaultKeepsHostCurrentAndLaterSites()
    {
        var fixture = new Fixture();
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MessageCenterViewModel.ActiveBadgeCount))
            {
                throw new InvalidOperationException("Synthetic primary diagnostic observer failure");
            }
        };

        fixture.ViewModel.NotifyHostDiagnosticsChanged();

        fixture.AssertTrace("changed:HostCurrent|changed:ActiveBadgeCount|changed:HasActiveDiagnostics|changed:HasNoActiveDiagnostics|changed:HostRows|changed:HostBlocker|changed:HostAvailability|changed:HostAvailabilityText|changed:HostCatalog|changed:HostEnvironment|changed:MessageCenterAccessibleName|changed:SystemStatusAnnouncement|" + ActivityTrace);
    }

    /// <summary>Dependent pane-changing observer failures prevent commit after both changing notifications.</summary>
    [Fact]
    public void ThrowingDependentPaneObserverPreventsCommit()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("Synthetic dependent pane failure");
        fixture.ViewModel.PropertyChanging += (_, args) =>
        {
            if (args.PropertyName == nameof(MessageCenterViewModel.IsRunReportsSelected)) { throw failure; }
        };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => fixture.ViewModel.ShowRunReportsCommand.Execute(null)));
        Assert.Equal(1, fixture.ViewModel.ExportContextGeneration);
        Assert.True(fixture.ViewModel.IsSystemInformationSelected);
        fixture.AssertTrace("changing:IsSystemInformationSelected|changing:IsRunReportsSelected");
    }

    /// <summary>Every successful report-close callback sees the exact old state and operation generation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseReportObservesOldStateBeforeCommit(bool openReports)
    {
        Facade? vm = null;
        var fixture = new Fixture(close: () =>
        {
            Assert.True(vm!.IsOpen);
            Assert.True(vm.IsSystemInformationSelected);
            Assert.Equal(openReports ? 1 : 2, vm.ExportContextGeneration);
        });
        vm = fixture.ViewModel;
        vm.OpenCommand.Execute(null);
        fixture.Trace.Clear();
        (openReports ? vm.OpenRunReportsCommand : vm.CloseCommand).Execute(null);
        fixture.AssertTrace(openReports
            ? "close-report|" + PaneTrace + "|interaction:Opened|" + ActivityTrace
            : "close-report|changing:IsOpen|changed:IsOpen");
        Assert.Equal(openReports, vm.IsOpen);
        Assert.Equal(openReports, vm.IsRunReportsSelected);
    }

    /// <summary>Null and empty reflected status retain the source's conditional language-reset behavior.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("status")]
    public void LanguageResetPreservesEmptyStatusPredicate(string? status)
    {
        var fixture = new Fixture();
        typeof(Facade).GetProperty(nameof(MessageCenterViewModel.ExportStatus))!.SetValue(fixture.ViewModel, status);
        fixture.ViewModel.SetFailure(true);
        fixture.Trace.Clear();
        fixture.ViewModel.ApplyLanguageChanged();
        Assert.False(fixture.ViewModel.HasExportFailure);
        Assert.Equal(status is null ? null : string.Empty, fixture.ViewModel.ExportStatus);
        fixture.AssertTrace(LanguageTrace + "|changing:HasExportFailure|changed:HasExportFailure" +
            (string.IsNullOrEmpty(status) ? string.Empty : "|changing:ExportStatus|changed:ExportStatus"));
    }

    /// <summary>Only disclosed entries evaluate undefined filters; the presentation adds no new predicate.</summary>
    [Fact]
    public void UndefinedFilterIsDeferredUntilDisclosedMetadata()
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        typeof(MessageCenterViewModel).GetProperty(nameof(MessageCenterViewModel.SelectedActivityFilter))!.SetValue(vm, (MessageActivityFilter)(-1));
        Assert.False(vm.IsImportantActivitySelected);
        Assert.False(vm.IsWarningActivitySelected);
        Assert.False(vm.IsErrorActivitySelected);
        Assert.Empty(vm.ActivityItems);
        fixture.Provider.Add(1, MessageActivityImportance.Debug, MessageActivitySeverity.Information);
        Assert.Empty(vm.ActivityItems);
        Assert.False(vm.HasActivityItems);
        Assert.True(vm.HasNoActivityItems);
        Assert.Empty(fixture.Provider.Projections);
        vm.ToggleDebugActivityCommand.Execute(null);
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() => vm.ActivityItems);
        Assert.Null(failure.ParamName);
        Assert.Empty(fixture.Provider.Projections);
    }

    /// <summary>Opening reports reaches one below and exactly the maximum before the next open overflows.</summary>
    [Fact]
    public void OpenReportsAcceptsBelowAndExactGenerationMaximum()
    {
        var fixture = new Fixture();
        SetGeneration(fixture.ViewModel, long.MaxValue - 3);
        fixture.ViewModel.OpenRunReportsCommand.Execute(null);
        Assert.Equal(long.MaxValue - 1, fixture.ViewModel.ExportContextGeneration);
        Assert.True(fixture.ViewModel.IsOpen);
        fixture.Trace.Clear();
        fixture.ViewModel.OpenRunReportsCommand.Execute(null);
        Assert.Equal(long.MaxValue, fixture.ViewModel.ExportContextGeneration);
        fixture.AssertTrace("close-report|interaction:Opened|" + ActivityTrace);
        fixture.Trace.Clear();
        Assert.Throws<OverflowException>(() => fixture.ViewModel.OpenRunReportsCommand.Execute(null));
        fixture.AssertTrace("close-report");
    }

    /// <summary>Open reset observer faults precede interaction and leave independently observable partial reset state.</summary>
    [Theory]
    [InlineData("HasExportFailure")]
    [InlineData("ExportStatus")]
    public void OpenResetObserverFaultPreventsInteractionAndVisibility(string property)
    {
        var fixture = new Fixture();
        Facade vm = fixture.ViewModel;
        vm.ReportExportFailure();
        fixture.Trace.Clear();
        var failure = new InvalidOperationException("Synthetic reset observer failure");
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == property) { throw failure; } };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => vm.OpenCommand.Execute(null)));
        Assert.Equal(1, vm.ExportContextGeneration);
        Assert.False(vm.IsOpen);
        Assert.False(vm.HasExportFailure);
        Assert.Equal(property == "HasExportFailure" ? "A:export-failed" : string.Empty, vm.ExportStatus);
        fixture.AssertTrace("changing:HasExportFailure|changed:HasExportFailure" +
            (property == "HasExportFailure" ? string.Empty : "|changing:ExportStatus|changed:ExportStatus"));
    }

    /// <summary>Open contains committed activity observer faults while still committing modal visibility.</summary>
    [Fact]
    public void OpenPreservesSiteSpecificActivityObserverIsolation()
    {
        var fixture = new Fixture();
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MessageCenterViewModel.IsOpen))
            {
                throw new InvalidOperationException("Synthetic activity observer failure");
            }
        };
        fixture.ViewModel.OpenCommand.Execute(null);
        Assert.True(fixture.ViewModel.IsOpen);
        fixture.AssertTrace("interaction:Opened|" + ActivityTrace + "|changing:IsOpen|changed:IsOpen");
    }

    /// <summary>Exporter cancellation and unexpected faults propagate without synthesizing failure status.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportCancellationAndUnexpectedFaultsPropagate(bool canceled)
    {
        Exception failure = canceled
            ? new OperationCanceledException(TestContext.Current.CancellationToken)
            : new InvalidOperationException("Synthetic exporter fault");
        var fixture = new Fixture(export: (_, _) => Task.FromException(failure));
        Assert.Same(failure, await Record.ExceptionAsync(() => fixture.ViewModel.ExportAsync("destination", TestContext.Current.CancellationToken)));
        Assert.Equal(string.Empty, fixture.ViewModel.ExportStatus);
        Assert.False(fixture.ViewModel.HasExportFailure);
        fixture.AssertTrace(string.Empty);
    }

    /// <summary>Provider captures and host projections remain unguarded passive getter operations.</summary>
    [Fact]
    public void ProjectionFaultPropagatesWithoutStatusOrHistoryChanges()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("Synthetic projection failure");
        fixture.Provider.Entries.Add(new(1, MessageActivityImportance.Important, MessageActivitySeverity.Error,
            () => throw failure));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => fixture.ViewModel.ActivityItems));
        Assert.Equal(string.Empty, fixture.ViewModel.ExportStatus);
        Assert.False(fixture.ViewModel.HasExportFailure);
        Assert.Equal(1, fixture.Provider.Captures);
        fixture.AssertTrace(string.Empty);
    }
}



