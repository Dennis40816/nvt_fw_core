// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nvt.Core.Avalonia.RuntimeQuery;
using Nvt.Core.Avalonia.Threading;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

/// <summary>Characterizes real UI dispatch and ports the frozen UI handler failure test.</summary>
[Collection("RuntimeQuery")]
public sealed class RuntimeQueryUiThreadTests
{
    /// <summary>The UI handler receives unchanged inputs, including null requests and an already canceled token.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrapRunsOnUiThreadAndPassesInputsUnchanged(bool nullRequest)
    {
        var original = RuntimeQueryTestValues.DispatcherField.GetValue(null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = nullRequest ? null : new RuntimeQueryRequest("request-version", "probe",
            new Dictionary<string, string> { ["CaseKey"] = " unchanged " });
        var version = new string(" supplied-version ".ToCharArray());
        var expected = RuntimeQueryResponseEnvelope.Success(new object());
        var calls = 0;
        var handler = RuntimeQueryUiThread.Wrap(async (received, receivedVersion, token) =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            Assert.Same(request, received);
            Assert.Same(version, receivedVersion);
            Assert.Equal(cancellation.Token, token);
            Assert.True(token.IsCancellationRequested);
            calls++;
            await Task.Yield();
            return expected;
        }, (_, _) => throw new InvalidOperationException("The error mapper must not run."));
        try
        {
            UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
            var response = await Task.Run(() =>
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                return handler(request, version, cancellation.Token);
            }).WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            Assert.Same(expected, response);
            Assert.Equal(1, calls);
        }
        finally
        {
            RuntimeQueryTestValues.DispatcherField.SetValue(null, original);
        }
    }

    /// <summary>No registered dispatcher produces the mapped NFH error without calling the handler.</summary>
    [AvaloniaFact]
    public async Task WrapWhenDispatcherUnavailableReturnsMappedFailureWithoutRunningHandler()
    {
        var original = RuntimeQueryTestValues.DispatcherField.GetValue(null);
        var calls = 0;
        var mappings = 0;
        var expectedError = RuntimeQueryTestValues.NfhError(RuntimeQueryFailure.DispatcherUnavailable, null);
        var handler = RuntimeQueryUiThread.Wrap((_, _, _) =>
        {
            calls++;
            return Task.FromResult(RuntimeQueryResponseEnvelope.Success(null));
        }, (failure, detail) =>
        {
            mappings++;
            Assert.Equal(RuntimeQueryFailure.DispatcherUnavailable, failure);
            Assert.Null(detail);
            return expectedError;
        });
        try
        {
            RuntimeQueryTestValues.DispatcherField.SetValue(null, null);
            Assert.False(UiThread.TryGetRunningDispatcher(out _));
            var response = await handler(null, RuntimeQueryTestValues.Version, CancellationToken.None)
                .WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            Assert.False(response.Ok);
            Assert.Null(response.Data);
            var responseError = Assert.IsType<RuntimeQueryError>(response.Error);
            Assert.Same(expectedError, responseError);
            Assert.Equal("IPC_ERROR", responseError.Code);
            Assert.Equal("The UI dispatcher is unavailable.", responseError.Message);
            Assert.Equal(0, calls);
            Assert.Equal(1, mappings);
        }
        finally
        {
            RuntimeQueryTestValues.DispatcherField.SetValue(null, original);
        }
    }

    /// <summary>Synchronous and asynchronous handler exceptions escape with the same exception instance.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrapWhenHandlerThrowsLetsExceptionEscapeUnchanged(bool asynchronous)
    {
        var original = RuntimeQueryTestValues.DispatcherField.GetValue(null);
        var expected = new InvalidOperationException("synthetic handler failure");
        async Task<RuntimeQueryResponseEnvelope> ThrowAsync()
        {
            await Task.Yield();
            throw expected;
        }
        var handler = RuntimeQueryUiThread.Wrap((_, _, _) =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            return asynchronous ? ThrowAsync() : throw expected;
        }, (_, _) => throw new InvalidOperationException("The wrapper must not map handler errors."));
        try
        {
            UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
                handler(null, RuntimeQueryTestValues.Version, CancellationToken.None))
                .WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken));
            Assert.Same(expected, actual);
        }
        finally
        {
            RuntimeQueryTestValues.DispatcherField.SetValue(null, original);
        }
    }

    /// <summary>Ports the source's throwing-handler pipe test through the real UI dispatcher.</summary>
    [AvaloniaFact]
    public async Task SendRequestWhenRuntimeQueryThrowsReturnsIpcErrorEnvelope()
    {
        var original = RuntimeQueryTestValues.DispatcherField.GetValue(null);
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var expected = new InvalidOperationException("NullValue must be in [0,65535].");
        Exception? reportedException = null;
        var handler = RuntimeQueryUiThread.Wrap((_, _, _) =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            throw expected;
        }, RuntimeQueryTestValues.NfhError);
        var host = new RuntimeQueryHost(() => RuntimeQueryTestValues.CreateServer(pipeName, handler,
            (kind, exception) =>
            {
                if (kind == RuntimeQueryDiagnostic.HandlerFailed)
                {
                    reportedException = exception;
                }
            }));
        try
        {
            UiThread.RegisterRunningDispatcher(Dispatcher.UIThread);
            host.Start();
            var response = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
                new RuntimeQueryRequest(RuntimeQueryTestValues.Version, "notch-validation",
                    new Dictionary<string, string> { ["regular-id"] = "100" }),
                3000, RuntimeQueryTestValues.NfhError))
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(RuntimeQueryResponseEnvelope.Failure("IPC_ERROR", "NullValue must be in [0,65535]."), response);
            Assert.Same(expected, reportedException);
        }
        finally
        {
            try
            {
                await host.StopAsync().WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
            }
            finally
            {
                RuntimeQueryTestValues.DispatcherField.SetValue(null, original);
            }
        }
    }
}
