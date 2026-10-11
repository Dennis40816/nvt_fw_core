// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.MessageCenter;
using Nvt.Core.MessageCenter;
using Xunit;
using static Nvt.Core.Avalonia.Tests.MessageCenter.PresentationTestValues;

namespace Nvt.Core.Avalonia.Tests.MessageCenter;

public sealed partial class PresentationTests
{
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
}
