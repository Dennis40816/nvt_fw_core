// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.MessageCenter;
using Xunit;

namespace Nvt.Core.Tests.MessageCenter;

public sealed partial class ExportWorkflowTests
{
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
}
