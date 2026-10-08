// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.MessageCenter;
using Xunit;

namespace Nvt.Core.Tests.MessageCenter;

/// <summary>Characterizes picker acceptance, generation-only completion, and callback fault scope without I/O.</summary>
public sealed class ExportWorkflowTests
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] FailedStatus = ["failed"];

    /// <summary>The frozen stale picker regression rejects a reopened context and accepts the next picker.</summary>
    [Fact]
    public async Task DiagnosticsPickerRejectsClosedAndReopenedContext()
    {
        var host = new ExportHost();
        var picker = new PickerGate();
        host.Picker = picker.PickAsync;
        Task exporting = host.PickAsync();
        await WaitAsync(picker.Entered.Task);
        host.Session.Close();
        host.Session.Open();
        picker.Result.SetResult("stale-diagnostics.json");
        await WaitAsync(exporting);

        Assert.Empty(host.Calls);
        Assert.Equal(string.Empty, host.Status);
        host.AssertTrace("picker:entered|picker:result|identity");

        host.Picker = static () => Task.FromResult<string?>("current-diagnostics.json");
        await host.PickAsync();

        host.AssertCall(0, "current-diagnostics.json", CancellationToken.None);
        Assert.Single(host.Calls);
        Assert.Equal("succeeded", host.Status);
        host.AssertTrace("picker:entered|picker:result|identity|picker:entered|picker:result|identity|export:entered|export:completed|succeeded");
    }

    /// <summary>The frozen picker failure stays visible through a null retry and a later successful retry clears it.</summary>
    [Fact]
    public async Task DiagnosticsPickerFailureIsVisibleAndRetryable()
    {
        var host = new ExportHost
        {
            Picker = static () => Task.FromException<string?>(new IOException("picker failed")),
        };
        await host.PickAsync();
        Assert.Equal("failed", host.Status);
        Assert.Empty(host.Calls);
        host.Picker = static () => Task.FromResult<string?>(null);
        await host.PickAsync();
        Assert.Equal("failed", host.Status);
        Assert.Empty(host.Calls);
        host.AssertTrace("picker:entered|picker:fault|identity|failed|picker:entered|picker:result");

        host.Picker = static () => Task.FromResult<string?>("retry.json");
        await host.PickAsync();

        host.AssertCall(0, "retry.json", CancellationToken.None);
        Assert.Single(host.Calls);
        Assert.Equal("succeeded", host.Status);
        host.AssertTrace("picker:entered|picker:fault|identity|failed|picker:entered|picker:result|picker:entered|picker:result|identity|export:entered|export:completed|succeeded");
    }

    /// <summary>The frozen ten-second gated write completes after close/reopen without publishing stale status.</summary>
    [Fact]
    public async Task DiagnosticsExportCompletionRejectsReopenedContext()
    {
        var host = new ExportHost();
        var write = new WriteGate();
        host.Export = write.ExportAsync;
        Task exporting = host.Workflow.ExportAsync("diagnostics.json", TestContext.Current.CancellationToken);
        await WaitAsync(write.Entered.Task);
        host.Session.Close();
        host.Session.Open();
        write.Release.SetResult();
        await WaitAsync(exporting);

        Assert.True(write.Completed);
        host.AssertCall(0, "diagnostics.json", TestContext.Current.CancellationToken);
        Assert.Single(host.Calls);
        Assert.Equal(string.Empty, host.Status);
        host.AssertTrace("export:entered|export:completed");
    }

    /// <summary>A closed session does not call either picker or identity and preserves prior status.</summary>
    [Fact]
    public async Task ClosedPickerStartIsSilent()
    {
        var host = new ExportHost(isOpen: false) { Status = "older failure" };

        await host.PickAsync();

        Assert.Empty(host.Calls);
        Assert.Equal("older failure", host.Status);
        host.AssertTrace(string.Empty);
    }

    /// <summary>Null, empty, and whitespace picker paths stop before identity and preserve prior status.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0\u2003")]
    public async Task BlankPickerResultsAreSilent(string? path)
    {
        var host = new ExportHost { Status = "older failure", Picker = () => Task.FromResult(path) };

        await host.PickAsync();

        Assert.Empty(host.Calls);
        Assert.Equal("older failure", host.Status);
        host.AssertTrace("picker:entered|picker:result");
    }

    /// <summary>Both a thrown cancellation and a canceled picker task retain the old failure without identity checks.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledPickerPreservesOldFailure(bool canceledTask)
    {
        var host = new ExportHost
        {
            Picker = static () => Task.FromException<string?>(new IOException("picker failed")),
        };
        await host.PickAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        host.Picker = () => canceledTask
            ? Task.FromCanceled<string?>(cancellation.Token)
            : throw new OperationCanceledException("picker canceled", cancellation.Token);

        await host.PickAsync();

        Assert.Empty(host.Calls);
        Assert.Equal("failed", host.Status);
        host.AssertTrace("picker:entered|picker:fault|identity|failed|picker:entered|picker:fault");
    }

    /// <summary>Every noncancellation picker fault, synchronous or asynchronous, publishes current failure.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task CurrentPickerFaultPublishesFailure(int kind, bool synchronous)
    {
        Exception failure = kind == 3 ? new InvalidOperationException("picker fault") : ExpectedFailure(kind);
        var host = new ExportHost
        {
            Picker = () => synchronous ? throw failure : Task.FromException<string?>(failure),
        };

        await host.PickAsync();

        Assert.Empty(host.Calls);
        Assert.Equal("failed", host.Status);
        host.AssertTrace("picker:entered|picker:fault|identity|failed");
    }

    /// <summary>Close, reopen, repeat open, and pane round trips reject both selected paths and picker faults.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public async Task ChangedPickerContextRejectsResultsAndFailures(int change, bool fails)
    {
        var host = new ExportHost { Status = "older failure" };
        var picker = new PickerGate();
        host.Picker = picker.PickAsync;
        Task exporting = host.PickAsync();
        await WaitAsync(picker.Entered.Task);
        ChangeContext(host.Session, change);
        if (fails) { picker.Result.SetException(new InvalidOperationException("stale picker fault")); }
        else { picker.Result.SetResult("stale.json"); }
        await WaitAsync(exporting);

        Assert.Empty(host.Calls);
        Assert.Equal("older failure", host.Status);
        host.AssertTrace(fails ? "picker:entered|picker:fault|identity" : "picker:entered|picker:result|identity");
    }

    /// <summary>Replacing the captured view rejects a delayed path or fault even when session generation still matches.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedViewIdentityRejectsPickerResultAndFailure(bool fails)
    {
        var host = new ExportHost { Status = "older failure" };
        long generation = host.Session.ExportContextGeneration;
        var picker = new PickerGate();
        host.Picker = picker.PickAsync;
        Task exporting = host.PickAsync();
        await WaitAsync(picker.Entered.Task);
        host.CurrentView = new object();
        if (fails) { picker.Result.SetException(new IOException("stale view picker fault")); }
        else { picker.Result.SetResult("stale-view.json"); }
        await WaitAsync(exporting);

        Assert.Equal(generation, host.Session.ExportContextGeneration);
        Assert.True(host.Session.IsExportContextCurrent(generation));
        Assert.Empty(host.Calls);
        Assert.Equal("older failure", host.Status);
        host.AssertTrace(fails ? "picker:entered|picker:fault|identity" : "picker:entered|picker:result|identity");
    }

    /// <summary>An open report pane still starts the picker but rejects its path and its failure publication.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickerStartsOnOtherPaneButCannotExportOrPublishFailure(bool fails)
    {
        var host = new ExportHost { Status = "older failure" };
        host.Session.SelectActivity(false);
        host.Picker = () => fails
            ? Task.FromException<string?>(new IOException("other pane picker fault"))
            : Task.FromResult<string?>("other-pane.json");

        await host.PickAsync();

        Assert.Empty(host.Calls);
        Assert.Equal("older failure", host.Status);
        host.AssertTrace(fails ? "picker:entered|picker:fault|identity" : "picker:entered|picker:result|identity");
    }

    /// <summary>Same-pane selection and host language/refresh status changes leave the pending picker generation intact.</summary>
    [Fact]
    public async Task SamePaneAndPresentationChangesDoNotInvalidatePicker()
    {
        var host = new ExportHost();
        long generation = host.Session.ExportContextGeneration;
        var picker = new PickerGate();
        host.Picker = picker.PickAsync;
        Task exporting = host.PickAsync();
        await WaitAsync(picker.Entered.Task);
        host.Session.SelectActivity(true, static () => throw new InvalidOperationException("same pane callback"));
        host.Status = "host language and refresh status";
        picker.Result.SetResult("current.json");
        await WaitAsync(exporting);

        Assert.Equal(generation, host.Session.ExportContextGeneration);
        Assert.Single(host.Calls);
        host.AssertCall(0, "current.json", CancellationToken.None);
        Assert.Equal("succeeded", host.Status);
        host.AssertTrace("picker:entered|picker:result|identity|export:entered|export:completed|succeeded");
    }

    /// <summary>Nonblank edge-character destinations pass to the delegate unchanged, including the minimal one-character path.</summary>
    [Theory]
    [InlineData("x")]
    [InlineData(" diagnostics.json ")]
    [InlineData("\u200b")]
    [InlineData("\0")]
    [InlineData("folder/合成😀?.json")]
    public async Task AcceptedPickerPreservesDestinationAndUsesNone(string path)
    {
        var host = new ExportHost { Picker = () => Task.FromResult<string?>(path) };

        await host.PickAsync();

        Assert.Single(host.Calls);
        host.AssertCall(0, path, CancellationToken.None);
        Assert.Equal("succeeded", host.Status);
        host.AssertTrace("picker:entered|picker:result|identity|export:entered|export:completed|succeeded");
    }

    /// <summary>Direct export does not validate or normalize destinations, leaving even null and blank handling to the host.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\0")]
    public async Task DirectExportPassesEdgeDestinationsUnchanged(string? path)
    {
        var host = new ExportHost();

        await host.Workflow.ExportAsync(path!, TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);
        host.AssertCall(0, path, TestContext.Current.CancellationToken);
        Assert.Equal("succeeded", host.Status);
        host.AssertTrace("export:entered|export:completed|succeeded");
    }

    /// <summary>Direct completion checks generation alone in every visibility and pane state, for success and failure.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task DirectCompletionRequiresOnlyUnchangedGeneration(bool isOpen, bool activitySelected, bool fails)
    {
        var host = new ExportHost(isOpen);
        host.Session.SelectActivity(activitySelected);
        long generation = host.Session.ExportContextGeneration;
        var write = new WriteGate { Failure = fails ? new IOException("write failed") : null };
        host.Export = write.ExportAsync;
        Task exporting = host.Workflow.ExportAsync("direct.json", TestContext.Current.CancellationToken);
        await WaitAsync(write.Entered.Task);
        Assert.Equal(string.Empty, host.Status);
        write.Release.SetResult();
        await WaitAsync(exporting);

        Assert.Equal(generation, host.Session.ExportContextGeneration);
        Assert.Equal(isOpen, host.Session.IsOpen);
        Assert.Equal(activitySelected, host.Session.IsActivitySelected);
        Assert.Single(host.Calls);
        host.AssertCall(0, "direct.json", TestContext.Current.CancellationToken);
        Assert.Equal(fails ? "failed" : "succeeded", host.Status);
        host.AssertTrace(fails ? "export:entered|export:fault|failed" : "export:entered|export:completed|succeeded");
    }

    /// <summary>All expected direct exceptions and their derived types publish failure for both synchronous and task faults.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public async Task ExpectedDirectFaultPublishesFailure(int kind, bool synchronous)
    {
        var host = new ExportHost();
        Exception failure = ExpectedFailure(kind);
        host.Export = _ => synchronous ? throw failure : Task.FromException(failure);

        await host.Workflow.ExportAsync("failed.json", TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);
        host.AssertCall(0, "failed.json", TestContext.Current.CancellationToken);
        Assert.Equal("failed", host.Status);
        host.AssertTrace("export:entered|export:fault|failed");
    }

    /// <summary>Stale write success and each expected write failure suppress status after every context-changing operation.</summary>
    [Theory]
    [InlineData(0, -1)]
    [InlineData(1, -1)]
    [InlineData(2, -1)]
    [InlineData(3, -1)]
    [InlineData(4, -1)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    public async Task ChangedWriteContextSuppressesSuccessAndExpectedFailure(int change, int faultKind)
    {
        var host = new ExportHost { Status = "newer status" };
        var write = new WriteGate { Failure = faultKind < 0 ? null : ExpectedFailure(faultKind) };
        host.Export = write.ExportAsync;
        Task exporting = host.Workflow.ExportAsync("stale-write.json", TestContext.Current.CancellationToken);
        await WaitAsync(write.Entered.Task);
        ChangeContext(host.Session, change);
        write.Release.SetResult();
        await WaitAsync(exporting);

        Assert.Single(host.Calls);
        host.AssertCall(0, "stale-write.json", TestContext.Current.CancellationToken);
        Assert.Equal("newer status", host.Status);
        host.AssertTrace(faultKind < 0 ? "export:entered|export:completed" : "export:entered|export:fault");
    }

    /// <summary>An accepted picker write continues with None after close and view replacement, suppressing stale status.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingAcceptedPickerDoesNotCancelRunningWrite(bool fails)
    {
        var host = new ExportHost();
        var write = new WriteGate { Failure = fails ? new IOException("late write fault") : null };
        host.Export = write.ExportAsync;
        Task exporting = host.PickAsync();
        await WaitAsync(write.Entered.Task);
        host.Session.Close();
        host.CurrentView = new object();
        write.Release.SetResult();
        await WaitAsync(exporting);

        Assert.Equal(!fails, write.Completed);
        Assert.Single(host.Calls);
        host.AssertCall(0, "selected.json", CancellationToken.None);
        Assert.False(host.Calls[0].CancellationToken.IsCancellationRequested);
        Assert.Equal(string.Empty, host.Status);
        host.AssertTrace(fails ? "picker:entered|picker:result|identity|export:entered|export:fault" : "picker:entered|picker:result|identity|export:entered|export:completed");
    }

    /// <summary>View replacement after picker acceptance does not add an identity check to direct completion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedWriteCompletionIgnoresLaterViewReplacement(bool fails)
    {
        var host = new ExportHost();
        var write = new WriteGate { Failure = fails ? new IOException("write fault") : null };
        host.Export = write.ExportAsync;
        Task exporting = host.PickAsync();
        await WaitAsync(write.Entered.Task);
        host.CurrentView = new object();
        write.Release.SetResult();
        await WaitAsync(exporting);

        Assert.Single(host.Calls);
        host.AssertCall(0, "selected.json", CancellationToken.None);
        Assert.Equal(fails ? "failed" : "succeeded", host.Status);
        host.AssertTrace(fails ? "picker:entered|picker:result|identity|export:entered|export:fault|failed" : "picker:entered|picker:result|identity|export:entered|export:completed|succeeded");
    }

    /// <summary>Unexpected export faults, including cancellation and wrapped I/O, propagate even when generation is stale.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task UnexpectedDirectFaultPropagatesUnchanged(int kind, bool stale)
    {
        Exception failure = kind switch
        {
            0 => new InvalidOperationException("unexpected export fault"),
            1 => new AggregateException(new IOException("wrapped write fault")),
            _ => new OperationCanceledException("write canceled", TestContext.Current.CancellationToken),
        };
        var host = new ExportHost { Status = "older status" };
        var write = new WriteGate { Failure = failure };
        host.Export = write.ExportAsync;
        Task exporting = host.Workflow.ExportAsync("unexpected.json", TestContext.Current.CancellationToken);
        await WaitAsync(write.Entered.Task);
        if (stale) { host.Session.Close(); host.Session.Open(); }
        write.Release.SetResult();

        Exception? thrown = await Record.ExceptionAsync(() => WaitAsync(exporting));

        Assert.Same(failure, thrown);
        Assert.Single(host.Calls);
        host.AssertCall(0, "unexpected.json", TestContext.Current.CancellationToken);
        Assert.Equal("older status", host.Status);
        host.AssertTrace("export:entered|export:fault");
    }

    /// <summary>A pre-canceled direct token is still passed to the exporter; the exporter owns cancellation decisions.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectTokenCancellationIsOwnedByExporter(bool exporterCancels)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var host = new ExportHost();
        host.Export = token => exporterCancels ? Task.FromCanceled(token) : Task.CompletedTask;

        Exception? thrown = await Record.ExceptionAsync(() => host.Workflow.ExportAsync("canceled.json", cancellation.Token));

        if (exporterCancels)
        {
            OperationCanceledException canceled = Assert.IsAssignableFrom<OperationCanceledException>(thrown);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Equal(string.Empty, host.Status);
            host.AssertTrace("export:entered|export:fault");
        }
        else
        {
            Assert.Null(thrown);
            Assert.Equal("succeeded", host.Status);
            host.AssertTrace("export:entered|export:completed|succeeded");
        }
        Assert.Single(host.Calls);
        host.AssertCall(0, "canceled.json", cancellation.Token);
    }

    /// <summary>The success callback remains inside the expected-exception catch, so expected faults invoke failure next.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExpectedSuccessCallbackFaultInvokesFailure(int kind)
    {
        var host = new ExportHost { Success = () => throw ExpectedFailure(kind) };

        await host.Workflow.ExportAsync("callback.json", TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);
        host.AssertCall(0, "callback.json", TestContext.Current.CancellationToken);
        Assert.Equal("failed", host.Status);
        host.AssertTrace("export:entered|export:completed|succeeded|failed");
    }

    /// <summary>A success callback that changes generation before its expected fault cannot publish a stale failure.</summary>
    [Fact]
    public async Task SuccessCallbackGenerationChangeSuppressesExpectedFailure()
    {
        var host = new ExportHost();
        host.Success = () => { host.Session.Close(); throw new IOException("callback fault"); };

        await host.Workflow.ExportAsync("callback.json", TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);
        host.AssertCall(0, "callback.json", TestContext.Current.CancellationToken);
        Assert.Equal("succeeded", host.Status);
        host.AssertTrace("export:entered|export:completed|succeeded");
    }

    /// <summary>Unexpected success callback faults and cancellation propagate without failure publication.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedSuccessCallbackFaultPropagates(bool canceled)
    {
        Exception failure = canceled
            ? new OperationCanceledException("callback canceled", TestContext.Current.CancellationToken)
            : new InvalidOperationException("callback fault");
        var host = new ExportHost { Success = () => throw failure };

        Exception? thrown = await Record.ExceptionAsync(() => host.Workflow.ExportAsync("callback.json", TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Single(host.Calls);
        host.AssertCall(0, "callback.json", TestContext.Current.CancellationToken);
        host.AssertTrace("export:entered|export:completed|succeeded");
    }

    /// <summary>Failure callback faults propagate exactly once outside both direct and picker catch bodies.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public async Task FailureCallbackFaultPropagatesWithoutRecatching(int kind, bool pickerFails)
    {
        Exception failure = kind switch
        {
            3 => new InvalidOperationException("failure callback fault"),
            4 => new OperationCanceledException("failure callback canceled", TestContext.Current.CancellationToken),
            _ => ExpectedFailure(kind),
        };
        var host = new ExportHost { Failure = () => throw failure };
        host.Export = static _ => Task.FromException(new IOException("write fault"));
        host.Picker = static () => Task.FromException<string?>(new IOException("picker fault"));

        Exception? thrown = await Record.ExceptionAsync(() => pickerFails
            ? host.PickAsync()
            : host.Workflow.ExportAsync("callback.json", TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        if (pickerFails) { Assert.Empty(host.Calls); }
        else { Assert.Single(host.Calls); host.AssertCall(0, "callback.json", TestContext.Current.CancellationToken); }
        host.AssertTrace(pickerFails ? "picker:entered|picker:fault|identity|failed" : "export:entered|export:fault|failed");
    }

    /// <summary>Identity faults propagate outside the picker catch, even when the captured generation has become stale.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IdentityCheckPrecedesSessionCheckAndItsFaultPropagates(bool pickerFails, bool stale)
    {
        var host = new ExportHost();
        var picker = new PickerGate();
        host.Picker = picker.PickAsync;
        var failure = new IOException("identity fault");
        host.Identity = () => throw failure;
        Task exporting = host.PickAsync();
        await WaitAsync(picker.Entered.Task);
        if (stale) { host.Session.Close(); }
        if (pickerFails) { picker.Result.SetException(new IOException("picker fault")); }
        else { picker.Result.SetResult("identity.json"); }

        Exception? thrown = await Record.ExceptionAsync(() => WaitAsync(exporting));

        Assert.Same(failure, thrown);
        Assert.Empty(host.Calls);
        Assert.Equal(string.Empty, host.Status);
        host.AssertTrace(pickerFails ? "picker:entered|picker:fault|identity" : "picker:entered|picker:result|identity");
    }

    /// <summary>Session admission uses the state after identity evaluation, preserving the short-circuit check order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdentityCallbackContextChangePreventsExportAndFailure(bool pickerFails)
    {
        var host = new ExportHost { Status = "older status" };
        host.Identity = () => { host.Session.Close(); return true; };
        host.Picker = () => pickerFails
            ? Task.FromException<string?>(new IOException("picker fault"))
            : Task.FromResult<string?>("identity.json");

        await host.PickAsync();

        Assert.False(host.Session.IsOpen);
        Assert.Empty(host.Calls);
        Assert.Equal("older status", host.Status);
        host.AssertTrace(pickerFails ? "picker:entered|picker:fault|identity" : "picker:entered|picker:result|identity");
    }

    /// <summary>Accepted export and callback faults remain outside the picker catch and propagate without duplicate failure.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AcceptedPickerPropagatesExportAndCallbackFaults(int kind)
    {
        Exception failure = kind switch
        {
            1 => new OperationCanceledException("accepted write canceled", TestContext.Current.CancellationToken),
            3 => new IOException("failure callback fault"),
            _ => new InvalidOperationException("accepted export fault"),
        };
        var host = new ExportHost();
        if (kind < 2) { host.Export = _ => Task.FromException(failure); }
        else if (kind == 2) { host.Success = () => throw failure; }
        else
        {
            host.Export = static _ => Task.FromException(new IOException("write fault"));
            host.Failure = () => throw failure;
        }

        Exception? thrown = await Record.ExceptionAsync(host.PickAsync);

        Assert.Same(failure, thrown);
        Assert.Single(host.Calls);
        host.AssertCall(0, "selected.json", CancellationToken.None);
        host.AssertTrace(kind switch
        {
            2 => "picker:entered|picker:result|identity|export:entered|export:completed|succeeded",
            3 => "picker:entered|picker:result|identity|export:entered|export:fault|failed",
            _ => "picker:entered|picker:result|identity|export:entered|export:fault",
        });
    }

    /// <summary>A failure callback fault after an expected success callback fault is propagated without being caught again.</summary>
    [Fact]
    public async Task FailureCallbackAfterSuccessCallbackFaultPropagates()
    {
        var failure = new IOException("failure callback fault");
        var host = new ExportHost
        {
            Success = static () => throw new IOException("success callback fault"),
            Failure = () => throw failure,
        };

        Exception? thrown = await Record.ExceptionAsync(() => host.Workflow.ExportAsync("callback.json", TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Single(host.Calls);
        host.AssertCall(0, "callback.json", TestContext.Current.CancellationToken);
        host.AssertTrace("export:entered|export:completed|succeeded|failed");
    }

    /// <summary>Generation is captured before invoking the exporter, not after its synchronous work.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectGenerationIsCapturedBeforeDelegateInvocation(bool fails)
    {
        var host = new ExportHost();
        host.Export = _ =>
        {
            host.Session.Close();
            return fails ? Task.FromException(new IOException("write fault")) : Task.CompletedTask;
        };

        await host.Workflow.ExportAsync("capture.json", TestContext.Current.CancellationToken);

        Assert.Single(host.Calls);
        host.AssertCall(0, "capture.json", TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, host.Status);
        host.AssertTrace(fails ? "export:entered|export:fault" : "export:entered|export:completed");
    }

    /// <summary>Generation is captured before the picker can change session state during invocation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickerGenerationIsCapturedBeforeDelegateInvocation(bool fails)
    {
        var host = new ExportHost();
        host.Picker = () =>
        {
            host.Session.Close();
            host.Session.Open();
            return fails ? Task.FromException<string?>(new IOException("picker fault")) : Task.FromResult<string?>("capture.json");
        };

        await host.PickAsync();

        Assert.Empty(host.Calls);
        Assert.Equal(string.Empty, host.Status);
        host.AssertTrace(fails ? "picker:entered|picker:fault|identity" : "picker:entered|picker:result|identity");
    }

    /// <summary>Overlapping same-generation writes publish each completion in completion order with complete call logs.</summary>
    [Fact]
    public async Task OverlappingWritesDoNotSupersedeOneAnother()
    {
        var host = new ExportHost();
        var first = new WriteGate { Failure = new IOException("first write fault") };
        var second = new WriteGate();
        int calls = 0;
        host.Export = token => ++calls == 1 ? first.ExportAsync(token) : second.ExportAsync(token);
        Task firstExport = host.Workflow.ExportAsync("first.json", TestContext.Current.CancellationToken);
        Task secondExport = host.Workflow.ExportAsync("second.json", TestContext.Current.CancellationToken);
        await WaitAsync(Task.WhenAll(first.Entered.Task, second.Entered.Task));
        second.Release.SetResult();
        await WaitAsync(secondExport);
        Assert.Equal("succeeded", host.Status);
        first.Release.SetResult();
        await WaitAsync(firstExport);

        Assert.Equal(2, host.Calls.Count);
        host.AssertCall(0, "first.json", TestContext.Current.CancellationToken);
        host.AssertCall(1, "second.json", TestContext.Current.CancellationToken);
        Assert.Equal("failed", host.Status);
        host.AssertTrace("export:entered|export:entered|export:completed|succeeded|export:fault|failed");
    }

    /// <summary>The workflow consumes zero, signed extremes, and the session's fixed maximum without incrementing generation.</summary>
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(long.MaxValue - 1)]
    [InlineData(long.MaxValue)]
    public async Task DirectExportDoesNotAdvanceGenerationAtBoundaries(long generation)
    {
        var host = new ExportHost(new MessageCenterSession(generation));

        await host.Workflow.ExportAsync("boundary.json", TestContext.Current.CancellationToken);

        Assert.Equal(generation, host.Session.ExportContextGeneration);
        Assert.Single(host.Calls);
        host.AssertCall(0, "boundary.json", TestContext.Current.CancellationToken);
        host.AssertTrace("export:entered|export:completed|succeeded");
    }

    /// <summary>A picker at the maximum generation is accepted without overflowing or changing the session.</summary>
    [Fact]
    public async Task PickerAtMaximumGenerationDoesNotAdvanceIt()
    {
        var session = new MessageCenterSession(long.MaxValue - 1);
        session.Open();
        var host = new ExportHost(session);

        await host.PickAsync();

        Assert.Equal(long.MaxValue, session.ExportContextGeneration);
        Assert.Single(host.Calls);
        host.AssertCall(0, "selected.json", CancellationToken.None);
        host.AssertTrace("picker:entered|picker:result|identity|export:entered|export:completed|succeeded");
    }

    /// <summary>Required constructor inputs are checked in signature order.</summary>
    [Theory]
    [InlineData(0, "session")]
    [InlineData(1, "export")]
    [InlineData(2, "succeeded")]
    [InlineData(3, "failed")]
    public void ConstructorRejectsNullInputsInOrder(int firstNull, string parameter)
    {
        ArgumentNullException thrown = Assert.Throws<ArgumentNullException>(() => new MessageCenterExportWorkflow(
            firstNull == 0 ? null! : new MessageCenterSession(),
            firstNull <= 1 ? null! : static (_, _) => Task.CompletedTask,
            firstNull <= 2 ? null! : static () => { },
            null!));

        Assert.Equal(parameter, thrown.ParamName);
    }

    /// <summary>Picker null validation precedes identity validation and closed-session rejection.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PickerRejectsNullDelegatesBeforeSessionChecks(bool isOpen, bool pickerPresent)
    {
        var host = new ExportHost(isOpen);

        ArgumentNullException thrown = await Assert.ThrowsAsync<ArgumentNullException>(() => host.Workflow.ExportWithPickerAsync(
            pickerPresent ? host.InvokePickerAsync : null!, null!));

        Assert.Equal(pickerPresent ? "isViewContextCurrent" : "pickPathAsync", thrown.ParamName);
        Assert.Empty(host.Calls);
        host.AssertTrace(string.Empty);
    }

    /// <summary>An exporter delegate that throws before returning a task stays inside the expected-failure scope.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task DirectlyThrowingExporterPublishesExpectedFailure(int kind)
    {
        var session = new MessageCenterSession();
        session.Open();
        var status = new List<string>();
        var workflow = new MessageCenterExportWorkflow(
            session,
            (_, _) => throw ExpectedFailure(kind),
            () => status.Add("succeeded"),
            () => status.Add("failed"));

        await workflow.ExportAsync("failed.json", TestContext.Current.CancellationToken);

        Assert.Equal(FailedStatus, status);
    }

    /// <summary>A directly thrown unexpected fault or cancellation from the exporter propagates without status.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectlyThrowingExporterPropagatesUnexpectedFaultAndCancellation(bool cancellation)
    {
        var session = new MessageCenterSession();
        session.Open();
        var status = new List<string>();
        Exception failure = cancellation ? new OperationCanceledException("synthetic cancellation") : new InvalidOperationException("synthetic fault");
        var workflow = new MessageCenterExportWorkflow(
            session,
            (_, _) => throw failure,
            () => status.Add("succeeded"),
            () => status.Add("failed"));

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => workflow.ExportAsync("failed.json", TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Empty(status);
    }

    /// <summary>A picker delegate that throws before returning a task publishes failure, except cancellation, which is silent.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task DirectlyThrowingPickerPublishesFailureOrStaysSilentForCancellation(int kind)
    {
        var session = new MessageCenterSession();
        session.Open();
        var status = new List<string>();
        var exports = new List<string>();
        Exception failure = kind switch
        {
            3 => new InvalidOperationException("synthetic picker fault"),
            4 => new OperationCanceledException("synthetic picker cancellation"),
            _ => ExpectedFailure(kind),
        };
        var workflow = new MessageCenterExportWorkflow(
            session,
            (path, _) => { exports.Add(path); return Task.CompletedTask; },
            () => status.Add("succeeded"),
            () => status.Add("failed"));

        await workflow.ExportWithPickerAsync(() => throw failure, static () => true);

        Assert.Empty(exports);
        Assert.Equal(kind == 4 ? [] : FailedStatus, status);
    }

    private static Task WaitAsync(Task operation) => operation.WaitAsync(GateTimeout, TestContext.Current.CancellationToken);

    private static Exception ExpectedFailure(int kind) => kind switch
    {
        0 => new IOException("synthetic I/O failure"),
        1 => new UnauthorizedAccessException("synthetic access failure"),
        2 => new ArgumentException("synthetic argument failure", nameof(kind)),
        3 => new DirectoryNotFoundException("synthetic directory failure"),
        4 => new ArgumentNullException(nameof(kind)),
        5 => new ArgumentOutOfRangeException(nameof(kind)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void ChangeContext(MessageCenterSession session, int change)
    {
        switch (change)
        {
            case 0: session.Close(); break;
            case 1: session.Close(); session.Open(); break;
            case 2: session.SelectActivity(false); break;
            case 3: session.SelectActivity(false); session.SelectActivity(true); break;
            case 4: session.Open(); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    private sealed record ExportCall(string? DestinationPath, CancellationToken CancellationToken);

    private sealed class ExportHost
    {
        private readonly object _capturedView = new();

        internal ExportHost(bool isOpen = true) : this(new MessageCenterSession())
        {
            if (isOpen) { Session.Open(); }
        }

        internal ExportHost(MessageCenterSession session)
        {
            Session = session;
            CurrentView = _capturedView;
            Identity = () => ReferenceEquals(CurrentView, _capturedView);
            Workflow = new MessageCenterExportWorkflow(session, ExportAsync, Succeed, Fail);
        }

        internal MessageCenterSession Session { get; }
        internal MessageCenterExportWorkflow Workflow { get; }
        internal List<ExportCall> Calls { get; } = [];
        internal List<string> Trace { get; } = [];
        internal string Status { get; set; } = string.Empty;
        internal object CurrentView { get; set; }
        internal Func<bool> Identity { get; set; }
        internal Func<Task<string?>> Picker { get; set; } = static () => Task.FromResult<string?>("selected.json");
        internal Func<CancellationToken, Task> Export { get; set; } = static _ => Task.CompletedTask;
        internal Action? Success { get; set; }
        internal Action? Failure { get; set; }

        internal Task PickAsync() => Workflow.ExportWithPickerAsync(InvokePickerAsync, IsViewCurrent);

        internal async Task<string?> InvokePickerAsync()
        {
            Trace.Add("picker:entered");
            try
            {
                string? path = await Picker();
                Trace.Add("picker:result");
                return path;
            }
            catch
            {
                Trace.Add("picker:fault");
                throw;
            }
        }

        internal void AssertCall(int index, string? path, CancellationToken cancellationToken)
        {
            Assert.Equal(path, Calls[index].DestinationPath);
            Assert.Equal(cancellationToken, Calls[index].CancellationToken);
        }

        internal void AssertTrace(string expected) => Assert.Equal(expected, string.Join('|', Trace));

        private async Task ExportAsync(string path, CancellationToken cancellationToken)
        {
            Calls.Add(new ExportCall(path, cancellationToken));
            Trace.Add("export:entered");
            try
            {
                await Export(cancellationToken);
                Trace.Add("export:completed");
            }
            catch
            {
                Trace.Add("export:fault");
                throw;
            }
        }

        private bool IsViewCurrent()
        {
            Trace.Add("identity");
            return Identity();
        }

        private void Succeed()
        {
            Trace.Add("succeeded");
            Status = "succeeded";
            Success?.Invoke();
        }

        private void Fail()
        {
            Trace.Add("failed");
            Status = "failed";
            Failure?.Invoke();
        }
    }

    private sealed class PickerGate
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<string?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task<string?> PickAsync()
        {
            Entered.SetResult();
            return await Result.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class WriteGate
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Completed { get; private set; }
        internal Exception? Failure { get; init; }

        internal async Task ExportAsync(CancellationToken cancellationToken)
        {
            Entered.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (Failure is not null) { throw Failure; }
            Completed = true;
        }
    }
}
