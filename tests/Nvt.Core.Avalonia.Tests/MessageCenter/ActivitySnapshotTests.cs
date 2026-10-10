// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Avalonia.MessageCenter;
using Nvt.Core.MessageCenter;
using Xunit;
using static Nvt.Core.Avalonia.Tests.MessageCenter.PresentationTestValues;

namespace Nvt.Core.Avalonia.Tests.MessageCenter;

/// <summary>Pins lazy activity revisions, coherent dependent getters, and invalidation timing.</summary>
public sealed class ActivitySnapshotTests
{
    /// <summary>Any of the three bindings can fill the cache; subsequent reads reuse rows and projections.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ActivityGettersShareOneLazyProjection(int firstGetter)
    {
        var fixture = new Fixture();
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        Facade vm = fixture.ViewModel;
        Assert.Equal(0, fixture.Provider.Captures);
        switch (firstGetter)
        {
            case 0: Assert.Single(vm.ActivityItems); break;
            case 1: Assert.True(vm.HasActivityItems); break;
            case 2: Assert.False(vm.HasNoActivityItems); break;
        }
        IReadOnlyList<MessageCenterActivityItem> rows = vm.ActivityItems;
        for (int read = 0; read < 10; read++)
        {
            Assert.Same(rows, vm.ActivityItems);
            Assert.True(vm.HasActivityItems);
            Assert.False(vm.HasNoActivityItems);
        }
        Assert.Equal(1, fixture.Provider.Captures);
        Assert.Equal(1, Assert.Single(fixture.Provider.Projections));
        fixture.AssertTrace(string.Empty);
    }

    /// <summary>Changing provider history cannot split row/presence bindings before the next data signal.</summary>
    [Fact]
    public void ProviderChangesRemainInvisibleUntilRevisionSignal()
    {
        var fixture = new Fixture();
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        Facade vm = fixture.ViewModel;
        IReadOnlyList<MessageCenterActivityItem> rows = vm.ActivityItems;
        fixture.Provider.Entries.Clear();
        Assert.True(vm.HasActivityItems);
        Assert.False(vm.HasNoActivityItems);
        Assert.Same(rows, vm.ActivityItems);
        Assert.Equal("A:title:1", Assert.Single(rows).Title);
        Assert.Equal(1, fixture.Provider.Captures);

        vm.NotifyActivityChanged();
        Assert.Equal(1, fixture.Provider.Captures);
        Assert.True(vm.HasNoActivityItems);
        Assert.False(vm.HasActivityItems);
        Assert.Empty(vm.ActivityItems);
        Assert.Equal(2, fixture.Provider.Captures);

        fixture.Provider.Add(2, MessageActivityImportance.Important, MessageActivitySeverity.Error);
        Assert.Empty(vm.ActivityItems);
        Assert.False(vm.HasActivityItems);
        Assert.Equal(2, fixture.Provider.Captures);
        vm.NotifyDiagnosticsOutsideScope();
        Assert.Equal("A:title:2", Assert.Single(vm.ActivityItems).Title);
        Assert.True(vm.HasActivityItems);
        Assert.False(vm.HasNoActivityItems);
        Assert.Equal(3, fixture.Provider.Captures);
    }

    /// <summary>Filter, debug disclosure, activity, and diagnostics each start one lazy revision.</summary>
    [Theory]
    [InlineData(0, "A:title:1")]
    [InlineData(1, "A:title:3,A:title:2,A:title:1")]
    [InlineData(2, "A:title:3,A:title:1")]
    [InlineData(3, "A:title:3,A:title:1")]
    public void RevisionSignalsInvalidateWithoutEagerCapture(int signal, string expectedTitles)
    {
        var fixture = new Fixture();
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        Facade vm = fixture.ViewModel;
        IReadOnlyList<MessageCenterActivityItem> previous = vm.ActivityItems;
        fixture.Provider.Add(2, MessageActivityImportance.Debug, MessageActivitySeverity.Warning);
        fixture.Provider.Add(3, MessageActivityImportance.Important, MessageActivitySeverity.Error);
        switch (signal)
        {
            case 0: vm.ShowWarningActivityCommand.Execute(null); break;
            case 1: vm.ToggleDebugActivityCommand.Execute(null); break;
            case 2: vm.NotifyActivityChanged(); break;
            case 3: vm.NotifyDiagnosticsOutsideScope(); break;
        }
        Assert.Equal(1, fixture.Provider.Captures);
        Assert.Single(fixture.Provider.Projections);
        IReadOnlyList<MessageCenterActivityItem> rows = vm.ActivityItems;
        Assert.NotSame(previous, rows);
        Assert.Equal(expectedTitles, string.Join(',', rows.Select(row => row.Title)));
        Assert.True(vm.HasActivityItems);
        Assert.False(vm.HasNoActivityItems);
        Assert.Same(rows, vm.ActivityItems);
        Assert.Equal(2, fixture.Provider.Captures);
        Assert.Equal(1 + rows.Count, fixture.Provider.Projections.Count);
    }

    /// <summary>Changing observers retain old state; the first changed observer sees the new revision.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedSettersInvalidateAfterCommitBeforeChangedObservers(bool debug)
    {
        var fixture = new Fixture();
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        fixture.Provider.Add(2, MessageActivityImportance.Important, MessageActivitySeverity.Error);
        fixture.Provider.Add(3, MessageActivityImportance.Debug, MessageActivitySeverity.Warning);
        Facade vm = fixture.ViewModel;
        IReadOnlyList<MessageCenterActivityItem> previous = vm.ActivityItems;
        string property = debug ? nameof(vm.IsDebugActivityExpanded) : nameof(vm.SelectedActivityFilter);
        var observed = new List<IReadOnlyList<MessageCenterActivityItem>>();
        vm.PropertyChanging += (_, _) =>
        {
            Assert.Same(previous, vm.ActivityItems);
            Assert.Equal(1, fixture.Provider.Captures);
        };
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == property || args.PropertyName == nameof(vm.ActivityItems))
            {
                IReadOnlyList<MessageCenterActivityItem> rows = vm.ActivityItems;
                Assert.NotSame(previous, rows);
                Assert.Equal(debug ? 3 : 1, rows.Count);
                Assert.True(vm.HasActivityItems);
                Assert.False(vm.HasNoActivityItems);
                observed.Add(rows);
            }
        };
        if (debug) { vm.ToggleDebugActivityCommand.Execute(null); }
        else { vm.ShowWarningActivityCommand.Execute(null); }
        Assert.Equal(2, observed.Count);
        Assert.Same(observed[0], observed[1]);
        Assert.Equal(2, fixture.Provider.Captures);
    }

    /// <summary>Diagnostic observers and the later activity notifications share one committed revision.</summary>
    [Fact]
    public void DiagnosticBatchInvalidatesBeforeItsFirstObserverOnlyOnce()
    {
        var fixture = new Fixture();
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        Facade vm = fixture.ViewModel;
        Assert.Single(vm.ActivityItems);
        fixture.Provider.Entries.Clear();
        var observed = new List<IReadOnlyList<MessageCenterActivityItem>>();
        vm.PropertyChanged += (_, _) =>
        {
            Assert.Empty(vm.ActivityItems);
            Assert.False(vm.HasActivityItems);
            Assert.True(vm.HasNoActivityItems);
            observed.Add(vm.ActivityItems);
        };
        vm.NotifyDiagnosticsOutsideScope();
        fixture.AssertTrace(DiagnosticTrace);
        Assert.Equal(DiagnosticTrace.Split('|').Length, observed.Count);
        Assert.All(observed, rows => Assert.Same(observed[0], rows));
        Assert.Equal(2, fixture.Provider.Captures);
    }

    /// <summary>Language changes discard materialized host strings and recapture lazily, even before first read.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LanguageChangeStartsLazyRevisionForTextDependentRows(bool readBeforeChange)
    {
        var fixture = new Fixture();
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        Facade vm = fixture.ViewModel;
        if (readBeforeChange) { Assert.Equal("A:title:1", Assert.Single(vm.ActivityItems).Title); }
        int captures = fixture.Provider.Captures;
        fixture.Text = B;
        vm.ApplyLanguageChanged();
        fixture.AssertTrace(LanguageTrace);
        Assert.Equal(captures, fixture.Provider.Captures);
        Assert.Equal(0, vm.ExportContextGeneration);
        IReadOnlyList<MessageCenterActivityItem> rows = vm.ActivityItems;
        Assert.Equal(new MessageCenterActivityItem("B:time:1", "B:title:1", "B:detail:1",
            "B:category:1", "B:status:1", MessageActivitySeverity.Warning), Assert.Single(rows));
        Assert.True(vm.HasActivityItems);
        Assert.False(vm.HasNoActivityItems);
        Assert.Same(rows, vm.ActivityItems);
        Assert.Equal(captures + 1, fixture.Provider.Captures);
    }

    /// <summary>Site isolation still reaches later bindings with fresh rows, with or without an early read.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ObserverFaultDoesNotKeepStaleProjection(bool diagnostics, bool readBeforeFault)
    {
        var fixture = new Fixture();
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        Facade vm = fixture.ViewModel;
        Assert.Single(vm.ActivityItems);
        fixture.Provider.Entries.Clear();
        string faultProperty = diagnostics ? nameof(vm.ActiveBadgeCount) : nameof(vm.ActivityItems);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == faultProperty)
            {
                if (readBeforeFault) { Assert.Empty(vm.ActivityItems); }
                throw new InvalidOperationException("Synthetic committed observer failure");
            }
        };
        var observed = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            Assert.Empty(vm.ActivityItems);
            Assert.False(vm.HasActivityItems);
            Assert.True(vm.HasNoActivityItems);
            observed.Add(args.PropertyName!);
        };
        if (diagnostics) { vm.NotifyDiagnosticsOutsideScope(); }
        else { vm.NotifyActivityChanged(); }
        string trace = diagnostics ? DiagnosticTrace : ActivityTrace;
        fixture.AssertTrace(trace);
        Assert.Equal(trace.Split('|').Skip(1).Select(name => name["changed:".Length..]), observed);
        Assert.Empty(vm.ActivityItems);
        Assert.Equal(2, fixture.Provider.Captures);
    }

    /// <summary>An unguarded language observer fault cannot preserve rows containing the previous language.</summary>
    [Fact]
    public void LanguageObserverFaultStillInvalidatesBeforeNotifications()
    {
        var fixture = new Fixture();
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        Facade vm = fixture.ViewModel;
        Assert.Equal("A:title:1", Assert.Single(vm.ActivityItems).Title);
        fixture.Text = B;
        var failure = new InvalidOperationException("Synthetic language observer failure");
        vm.PropertyChanged += (_, _) => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(vm.ApplyLanguageChanged));
        Assert.Equal(1, fixture.Provider.Captures);
        Assert.Equal("B:title:1", Assert.Single(vm.ActivityItems).Title);
        Assert.True(vm.HasActivityItems);
        Assert.False(vm.HasNoActivityItems);
        Assert.Equal(2, fixture.Provider.Captures);
        fixture.AssertTrace("changed:Text");
    }

    /// <summary>Same-value filters, pane/visibility, progress and status changes do not affect activity rows.</summary>
    [Fact]
    public void UnrelatedStateAndSameValueFilterRetainProjection()
    {
        var fixture = new Fixture();
        fixture.Provider.Add(1, MessageActivityImportance.Important, MessageActivitySeverity.Warning);
        Facade vm = fixture.ViewModel;
        IReadOnlyList<MessageCenterActivityItem> rows = vm.ActivityItems;
        fixture.Provider.Entries.Clear();
        vm.ShowImportantActivityCommand.Execute(null);
        vm.SetProgress(true);
        vm.SetStatus("host status");
        vm.SetFailure(true);
        vm.ShowRunReportsCommand.Execute(null);
        vm.ShowSystemInformationCommand.Execute(null);
        vm.CloseCommand.Execute(null);
        Assert.Same(rows, vm.ActivityItems);
        Assert.True(vm.HasActivityItems);
        Assert.False(vm.HasNoActivityItems);
        Assert.Equal(1, fixture.Provider.Captures);
        Assert.Single(fixture.Provider.Projections);
    }

    /// <summary>Projection failures propagate unchanged and cannot cause another capture before a data signal.</summary>
    [Fact]
    public void ProjectionFaultIsSharedUntilNextRevision()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("Synthetic projection failure");
        fixture.Provider.Entries.Add(new(1, MessageActivityImportance.Important, MessageActivitySeverity.Error,
            () => throw failure));
        Facade vm = fixture.ViewModel;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => vm.ActivityItems));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => vm.HasActivityItems));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => vm.HasNoActivityItems));
        fixture.Provider.Entries.Clear();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => vm.ActivityItems));
        Assert.Equal(1, fixture.Provider.Captures);
        Assert.Equal(string.Empty, vm.ExportStatus);
        Assert.False(vm.HasExportFailure);
        vm.NotifyActivityChanged();
        Assert.Empty(vm.ActivityItems);
        Assert.Equal(2, fixture.Provider.Captures);
    }
}
