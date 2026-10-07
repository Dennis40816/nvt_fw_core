// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Concurrent;
using System.Text;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Observes foreground permission through a per-call seam without changing the real foreground.</summary>
[Collection(RuntimeQueryPipeTestGroup.Name)]
public sealed class RuntimeQueryForegroundTests
{
    /// <summary>Only focus grants permission, before any request bytes, using the connected server's real process ID.</summary>
    [Theory]
    [InlineData("focus", true, false, true)]
    [InlineData("focus", false, false, true)]
    [InlineData("focus", false, true, true)]
    [InlineData(" FOCUS ", true, true, true)]
    [InlineData("help", true, false, false)]
    [InlineData("refocus", true, true, false)]
    public async Task PermissionRunsOnlyForFocusBeforeRequest(string command, bool permissionResult, bool perWindowName, bool expectedCall)
    {
        var baseName = RuntimeQueryTestValues.NewPipeName();
        // A misleading suffix proves that the native lookup supplies the ID, rather than the name.
        var pipeName = perWindowName ? RuntimeQueryWindowPipes.BuildName(baseName, int.MaxValue) : baseName;
        await using var peer = RuntimeQueryTestValues.CreatePeer(pipeName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var registration = timeout.Token.Register(() => peer.Dispose());
        var calls = new ConcurrentQueue<uint>();
        var serverTask = Task.Run(async () =>
        {
            await peer.WaitForConnectionAsync(timeout.Token);
            var frame = await RuntimeQueryTestValues.ReadFrameAsync(peer, timeout.Token);
            Assert.Equal(OperatingSystem.IsWindows() && expectedCall ? 1 : 0, calls.Count);
            await peer.WriteAsync(Encoding.UTF8.GetBytes("{\"ok\":true,\"data\":null,\"error\":null}" + Environment.NewLine), timeout.Token);
            return frame;
        }, timeout.Token);
        var reply = await Task.Run(() => RuntimeQueryIpcClient.SendRequest(pipeName,
            new RuntimeQueryRequest("1", command, null), 3000, RuntimeQueryTestValues.NfhError, processId =>
            {
                calls.Enqueue(processId);
                return permissionResult;
            }), timeout.Token).WaitAsync(timeout.Token);
        Assert.Equal(RuntimeQueryResponseEnvelope.Success(null), reply);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"version\":\"1\",\"command\":\"" + command + "\",\"args\":null}" + Environment.NewLine),
            await serverTask.WaitAsync(timeout.Token));
        if (OperatingSystem.IsWindows() && expectedCall)
        {
            Assert.Equal((uint)Environment.ProcessId, Assert.Single(calls));
        }
        else
        {
            Assert.Empty(calls);
        }
    }
}
