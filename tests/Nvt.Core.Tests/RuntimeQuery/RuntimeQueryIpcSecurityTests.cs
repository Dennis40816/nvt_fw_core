// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Pipe access rules, client access denial and server name conflicts.</summary>
public sealed class RuntimeQueryIpcSecurityTests
{
    /// <summary>Each live Windows pipe allows only the user SID and denies network logons.</summary>
    [Fact]
    public async Task ServerPipeAllowsOnlyCurrentUserAndDeniesNetwork()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows pipe access rules require Windows.");
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var server = RuntimeQueryTestValues.CreateServer(pipeName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        server.Start();
        NamedPipeServerStream? previousPipe = null;
        for (var instance = 0; instance < 2; instance++)
        {
            await using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(RuntimeQueryTestValues.ClientTimeoutMs, timeout.Token);
            var pipe = await WaitForActivePipeAsync(server, previousPipe, timeout.Token);
            var security = pipe.GetAccessControl();
            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<PipeAccessRule>().ToArray();
            Assert.True(security.AreAccessRulesProtected);
            Assert.Equal(identity.Owner, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.Equal(2, rules.Length);
            var allow = rules[1];
            Assert.Equal(AccessControlType.Allow, allow.AccessControlType);
            Assert.Equal(identity.User, allow.IdentityReference);
            Assert.Equal(PipeAccessRights.FullControl, allow.PipeAccessRights);
            Assert.False(allow.IsInherited);
            var deny = rules[0];
            Assert.Equal(AccessControlType.Deny, deny.AccessControlType);
            Assert.Equal(networkSid, deny.IdentityReference);
            Assert.Equal(PipeAccessRights.FullControl, deny.PipeAccessRights);
            Assert.False(deny.IsInherited);

            await client.WriteAsync(Encoding.UTF8.GetBytes("{\"version\":\"1\",\"command\":\"probe\",\"args\":null}\n"), timeout.Token);
            Assert.Equal(Encoding.UTF8.GetBytes("{\"ok\":true,\"data\":null,\"error\":null}" + Environment.NewLine),
                await RuntimeQueryTestValues.ReadFrameAsync(client, timeout.Token));
            previousPipe = pipe;
        }

        await server.DisposeAsync().AsTask().WaitAsync(
            TimeSpan.FromMilliseconds(RuntimeQueryTestValues.ShutdownBoundMs), TestContext.Current.CancellationToken);
    }

    /// <summary>The runtime rejects CurrentUserOnly together with an explicit Windows ACL.</summary>
    [Fact]
    public void ExplicitPipeSecurityWithCurrentUserOnlyIsRejected()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Explicit pipe security requires Windows.");
            return;
        }

        var security = new PipeSecurity();
        Assert.Throws<ArgumentException>("pipeSecurity", () =>
        {
            if (OperatingSystem.IsWindows())
            {
                using var pipe = NamedPipeServerStreamAcl.Create(
                    RuntimeQueryTestValues.NewPipeName(), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 0, 0, security);
            }
        });
    }

    /// <summary>Another user's server raises access denial, which uses the existing client error mapping.</summary>
    [Fact]
    public void UnauthorizedAccessUsesClientErrorAndPreservesDetail()
    {
        var exception = new UnauthorizedAccessException("synthetic server ownership mismatch");
        var calls = 0;
        var response = RuntimeQueryIpcClient.Failure((failure, detail) =>
        {
            calls++;
            Assert.Equal(RuntimeQueryFailure.ClientError, failure);
            Assert.Equal(exception.Message, detail);
            return new RuntimeQueryError("SYNTHETIC_CLIENT_ERROR", detail!);
        }, exception);

        Assert.Equal(1, calls);
        Assert.Equal(RuntimeQueryResponseEnvelope.Failure("SYNTHETIC_CLIENT_ERROR", exception.Message), response);
    }

    /// <summary>A name conflict reports one creation failure while the first server remains usable.</summary>
    [Fact]
    public async Task SecondServerReportsPipeCreationFailureAndStopsWithinBounds()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var events = new ConcurrentQueue<RuntimeQueryDiagnostic>();
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = RuntimeQueryTestValues.CreateServer(pipeName);
        await using var second = RuntimeQueryTestValues.CreateServer(pipeName, diagnostic: (kind, exception) =>
        {
            events.Enqueue(kind);
            if (kind == RuntimeQueryDiagnostic.PipeCreationFailed)
            {
                failed.TrySetResult(exception!);
            }
            else if (kind == RuntimeQueryDiagnostic.Stopped)
            {
                stopped.TrySetResult();
            }
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        first.Start();
        _ = await WaitForActivePipeAsync(first, null, timeout.Token);
        second.Start();
        var exception = await failed.Task.WaitAsync(timeout.Token);
        Assert.True(exception is IOException or UnauthorizedAccessException);
        await stopped.Task.WaitAsync(timeout.Token);

        for (var request = 0; request < 2; request++)
        {
            var response = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
                new RuntimeQueryRequest("1", "probe", null), RuntimeQueryTestValues.ClientTimeoutMs,
                RuntimeQueryTestValues.NfhError), timeout.Token).WaitAsync(timeout.Token);
            Assert.Equal(RuntimeQueryResponseEnvelope.Success(null), response);
        }

        await second.DisposeAsync().AsTask().WaitAsync(
            TimeSpan.FromMilliseconds(RuntimeQueryTestValues.ShutdownBoundMs), TestContext.Current.CancellationToken);
        await first.DisposeAsync().AsTask().WaitAsync(
            TimeSpan.FromMilliseconds(RuntimeQueryTestValues.ShutdownBoundMs), TestContext.Current.CancellationToken);
        Assert.Equal(1, events.Count(kind => kind == RuntimeQueryDiagnostic.PipeCreationFailed));
        Assert.Equal(1, events.Count(kind => kind == RuntimeQueryDiagnostic.Stopped));
        Assert.DoesNotContain(RuntimeQueryDiagnostic.ShutdownFailed, events);
        Assert.DoesNotContain(RuntimeQueryDiagnostic.ShutdownTimedOut, events);
    }

    private static async Task<NamedPipeServerStream> WaitForActivePipeAsync(
        RuntimeQueryIpcServer server, NamedPipeServerStream? previousPipe, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pipe = server.ActivePipe;
            if (pipe is not null && !ReferenceEquals(pipe, previousPipe))
            {
                return pipe;
            }

            await Task.Delay(10, cancellationToken);
        }
    }
}
