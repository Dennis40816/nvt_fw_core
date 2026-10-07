// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Pipes;
using Nvt.Core.Avalonia.RuntimeQuery;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

/// <summary>Ports the frozen host shutdown tests and characterizes restart and concurrent starts.</summary>
[Collection("RuntimeQuery")]
public sealed class RuntimeQueryHostTests
{
    /// <summary>Stopping before any start completes at once and does not call the factory.</summary>
    [Fact]
    public async Task StopAsyncWhenHostNotStartedCompletes()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var calls = 0;
        var host = new RuntimeQueryHost(() =>
        {
            calls++;
            return RuntimeQueryTestValues.CreateServer(pipeName);
        });

        var stopTask = host.StopAsync();
        Assert.True(stopTask.IsCompletedSuccessfully);
        await stopTask.WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
        Assert.Equal(0, calls);
    }

    /// <summary>Ports the source's 1500 ms shutdown bound after start.</summary>
    [Fact]
    public async Task StopAsyncWhenHostStartedCompletesWithinTimeout()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var host = new RuntimeQueryHost(() => RuntimeQueryTestValues.CreateServer(pipeName));
        try
        {
            host.Start();
            await host.StopAsync().WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
        }
        finally
        {
            await host.StopAsync().WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Ports the source's 1500 ms shutdown bound with a connected idle client.</summary>
    [Fact]
    public async Task StopAsyncWhenClientConnectedWithoutRequestCompletesWithinTimeout()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var host = new RuntimeQueryHost(() => RuntimeQueryTestValues.CreateServer(pipeName));
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            host.Start();
            await client.ConnectAsync(1500, TestContext.Current.CancellationToken);
            await host.StopAsync().WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
        }
        finally
        {
            await host.StopAsync().WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Two starts create and start only one server.</summary>
    [Fact]
    public async Task StartWhenCalledTwiceCreatesOneServer()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var calls = 0;
        var starts = 0;
        var host = new RuntimeQueryHost(() =>
        {
            calls++;
            return RuntimeQueryTestValues.CreateServer(pipeName, diagnostic: (kind, _) =>
            {
                if (kind == RuntimeQueryDiagnostic.Started)
                {
                    Interlocked.Increment(ref starts);
                }
            });
        });
        try
        {
            host.Start();
            host.Start();
            Assert.Equal(1, calls);
            Assert.Equal(1, starts);
        }
        finally
        {
            await host.StopAsync().WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Starting after a completed stop creates a fresh server on the same test pipe.</summary>
    [Fact]
    public async Task StartAfterStopCreatesNewServer()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var servers = new List<RuntimeQueryIpcServer>();
        var host = new RuntimeQueryHost(() =>
        {
            var server = RuntimeQueryTestValues.CreateServer(pipeName);
            servers.Add(server);
            return server;
        });
        try
        {
            host.Start();
            await host.StopAsync().WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
            host.Start();
            Assert.Equal(2, servers.Count);
            Assert.NotSame(servers[0], servers[1]);
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(1500, TestContext.Current.CancellationToken);
        }
        finally
        {
            await host.StopAsync().WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Concurrent start calls share one factory invocation.</summary>
    [Fact]
    public async Task StartWhenCalledConcurrentlyCreatesOneServer()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var calls = 0;
        var host = new RuntimeQueryHost(() =>
        {
            Interlocked.Increment(ref calls);
            return RuntimeQueryTestValues.CreateServer(pipeName);
        });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await release.Task.WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            host.Start();
        })).ToArray();
        try
        {
            release.SetResult();
            await Task.WhenAll(starts).WaitAsync(RuntimeQueryTestValues.WaitBound, TestContext.Current.CancellationToken);
            Assert.Equal(1, calls);
        }
        finally
        {
            await host.StopAsync().WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A second stop during the first one returns the same task, so no caller continues early.</summary>
    [Fact]
    public async Task StopAsyncWhenCalledTwiceReturnsTheSameStop()
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        var host = new RuntimeQueryHost(() => RuntimeQueryTestValues.CreateServer(pipeName));
        host.Start();
        var first = host.StopAsync();
        var second = host.StopAsync();
        Assert.Same(first, second);
        await first.WaitAsync(RuntimeQueryTestValues.ShutdownBound, TestContext.Current.CancellationToken);
    }
}
