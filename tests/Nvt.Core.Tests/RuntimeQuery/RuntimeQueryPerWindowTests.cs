// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Nvt.Core.RuntimeQuery;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks per-window discovery and exact command-line output through live pipes.</summary>
[Collection(RuntimeQueryPipeTestGroup.Name)]
public sealed class RuntimeQueryPerWindowTests
{
    /// <summary>Invalid PID values in both forms, including missing values.</summary>
    public static IEnumerable<object[]> InvalidProcessIds =>
    [
        [new[] { "--pid" }],
        [new[] { "--pid", "--json-compact" }],
        [new[] { "--pid=" }],
        [new[] { "--pid", "0" }],
        [new[] { "--pid=0" }],
        [new[] { "--pid", "-1" }],
        [new[] { "--pid=-1" }],
        [new[] { "--pid", "text" }],
        [new[] { "--pid=text" }],
        [new[] { "--pid=2147483648" }],
        [new[] { "--pid= " }]
    ];

    /// <summary>PID errors always print the exact pretty error and return two.</summary>
    [Theory]
    [MemberData(nameof(InvalidProcessIds))]
    public void InvalidProcessIdPrintsExactErrorAndReturnsTwo(string[] options)
    {
        using var output = NewWriter();
        Assert.True(Handle(["query", "help", "--json-compact", .. options], RuntimeQueryTestValues.NewPipeName(), output, out var exitCode));
        Assert.Equal(2, exitCode);
        Assert.Equal(PrettyFailure("INVALID_ARGUMENTS", "--pid must be a positive process ID."), output.ToString());
    }

    /// <summary>The new entry preserves existing parser errors before discovery.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.ParseErrors), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public void ExistingParseErrorsKeepExactOutput(string[] args, string stdout)
    {
        using var output = NewWriter();
        Assert.True(Handle(args, RuntimeQueryTestValues.NewPipeName(), output, out var exitCode));
        Assert.Equal(2, exitCode);
        Assert.Equal(stdout, output.ToString());
    }

    /// <summary>Non-query arguments produce no output or discovery.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.UnhandledArguments), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public void NonQueryArgumentsRemainUnhandled(string[] args, string stdout)
    {
        using var output = NewWriter();
        Assert.False(Handle(args, RuntimeQueryTestValues.NewPipeName(), output, out var exitCode));
        Assert.Equal(0, exitCode);
        Assert.Equal(stdout, output.ToString());
    }

    /// <summary>Absent candidates use the caller's mapping with null detail and exit code one.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NoWindowPrintsMappedFailureAndReturnsOne(bool compact, bool requested)
    {
        using var output = NewWriter();
        var calls = 0;
        string[] args = ["query", "help", .. compact ? new[] { "--json-compact" } : [], .. requested ? new[] { "--pid=123" } : []];
        Assert.True(RuntimeQueryCommandLine.TryHandlePerWindowQueryCommand(args, RuntimeQueryTestValues.NewPipeName(), "1",
            RuntimeQueryCommandLineCases.SupportedCommands, (failure, detail) =>
            {
                calls++;
                Assert.Equal(RuntimeQueryFailure.ServerNotFound, failure);
                Assert.Null(detail);
                return new RuntimeQueryError("TEST_NOT_FOUND", "No matching window.");
            }, output, out var exitCode));
        Assert.Equal(1, calls);
        Assert.Equal(1, exitCode);
        Assert.Equal(compact
            ? "{\"ok\":false,\"data\":null,\"error\":{\"code\":\"TEST_NOT_FOUND\",\"message\":\"No matching window.\"}}" + Environment.NewLine
            : PrettyFailure("TEST_NOT_FOUND", "No matching window."), output.ToString());
    }

    /// <summary>Both PID forms select a real pipe and omit client options from the exact request frame.</summary>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, true)]
    public async Task ProcessIdSelectsPipeAndStaysOutOfRequest(bool equalsForm, bool compact, bool commandArgument, bool leadingZeros)
    {
        RequireWindows();
        var baseName = RuntimeQueryTestValues.NewPipeName();
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var pipeName = leadingZeros ? baseName + ".00" + pid : RuntimeQueryWindowPipes.BuildName(baseName, Environment.ProcessId);
        await using var peer = RuntimeQueryTestValues.CreatePeer(pipeName);
        using var timeout = NewTimeout();
        using var registration = timeout.Token.Register(() => peer.Dispose());
        var serverTask = Task.Run(async () =>
        {
            await peer.WaitForConnectionAsync(timeout.Token);
            var frame = await RuntimeQueryTestValues.ReadFrameAsync(peer, timeout.Token);
            await peer.WriteAsync(Encoding.UTF8.GetBytes(RuntimeQueryCommandLineCases.SuccessReply + Environment.NewLine), timeout.Token);
            return Encoding.UTF8.GetString(frame);
        }, timeout.Token);
        string[] args = ["query", "help", .. equalsForm ? new[] { "--PID=" + pid } : new[] { "--pid", pid },
            "--timeout-ms=5000", .. compact ? new[] { "--json-compact" } : [], .. commandArgument ? new[] { "--key=value" } : []];
        using var output = NewWriter();
        var exitCode = await Task.Run(() =>
        {
            Assert.True(Handle(args, baseName, output, out var result));
            return result;
        }, timeout.Token).WaitAsync(timeout.Token);
        Assert.Equal(0, exitCode);
        Assert.Equal(compact ? RuntimeQueryCommandLineCases.SuccessReply + Environment.NewLine : PrettySuccess(7), output.ToString());
        Assert.Equal("{\"version\":\"1\",\"command\":\"help\",\"args\":" +
            (commandArgument ? "{\"key\":\"value\"}" : "null") + "}" + Environment.NewLine,
            await serverTask.WaitAsync(timeout.Token));
    }

    /// <summary>Two live servers use real process IDs; the later child wins unless the client requests the parent.</summary>
    [Fact]
    public async Task DiscoveryAndCommandLineSelectLatestOrRequestedProcess()
    {
        RequireWindows();
        using var parent = Process.GetCurrentProcess();
        using var child = Process.Start(ProcessProbe.Create("silent-wait"))!;
        try
        {
            var parentStart = parent.StartTime.ToUniversalTime();
            var childStart = child.StartTime.ToUniversalTime();
            Assert.True(childStart > parentStart);
            var baseName = RuntimeQueryTestValues.NewPipeName();
            await using var first = RuntimeQueryTestValues.CreateServer(RuntimeQueryWindowPipes.BuildName(baseName, parent.Id),
                (_, _, _) => Task.FromResult(RuntimeQueryResponseEnvelope.Success(new { Value = 1 })));
            await using var second = RuntimeQueryTestValues.CreateServer(RuntimeQueryWindowPipes.BuildName(baseName, child.Id),
                (_, _, _) => Task.FromResult(RuntimeQueryResponseEnvelope.Success(new { Value = 2 })));
            using var timeout = NewTimeout();
            first.Start();
            second.Start();
            while (first.ActivePipe is null || second.ActivePipe is null)
            {
                await Task.Delay(10, timeout.Token);
            }

            var candidates = RuntimeQueryWindowPipes.DiscoverCandidates(baseName);
            Assert.Equal(new[] { (parent.Id, parentStart), (child.Id, childStart) }.OrderBy(candidate => candidate.Id),
                candidates.Select(candidate => (candidate.ProcessId, candidate.StartTime)).OrderBy(candidate => candidate.ProcessId));
            Assert.Equal(child.Id, RuntimeQueryWindowPipes.SelectProcessId(candidates.Select(candidate => (candidate.ProcessId, candidate.StartTime))));
            Assert.Equal(parent.Id, RuntimeQueryWindowPipes.SelectProcessId(candidates.Select(candidate => (candidate.ProcessId, candidate.StartTime)), parent.Id));
            foreach (var requested in new[] { false, true })
            {
                using var output = NewWriter();
                string[] args = ["query", "help", "--timeout-ms=5000",
                    .. requested ? new[] { "--pid", parent.Id.ToString(CultureInfo.InvariantCulture) } : []];
                var exitCode = await Task.Run(() =>
                {
                    Assert.True(Handle(args, baseName, output, out var result));
                    return result;
                }, timeout.Token).WaitAsync(timeout.Token);
                Assert.Equal(0, exitCode);
                Assert.Equal(PrettySuccess(requested ? 1 : 2), output.ToString());
            }

            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(timeout.Token).WaitAsync(timeout.Token);
            Assert.Equal(parent.Id, Assert.Single(RuntimeQueryWindowPipes.DiscoverCandidates(baseName)).ProcessId);
            using var missingOutput = NewWriter();
            Assert.True(Handle(["query", "help", "--pid=" + child.Id.ToString(CultureInfo.InvariantCulture)],
                baseName, missingOutput, out var missingExitCode));
            Assert.Equal(1, missingExitCode);
            Assert.Equal(PrettyFailure("ServerNotFound", "No matching window."), missingOutput.ToString());
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await child.WaitForExitAsync(cleanupTimeout.Token);
        }
    }

    /// <summary>Prefix collisions, malformed suffixes and nonexistent processes are not candidates.</summary>
    [Theory]
    [InlineData(".12x")]
    [InlineData(".")]
    [InlineData("x.1")]
    [InlineData(".0")]
    [InlineData(".-1")]
    [InlineData(".2147483647")]
    [InlineData(".self.x")]
    [InlineData(".+self")]
    [InlineData(". self")]
    public void DiscoveryIgnoresInvalidNames(string suffix)
    {
        RequireWindows();
        var baseName = RuntimeQueryTestValues.NewPipeName();
        suffix = suffix.Replace("self", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        using var peer = RuntimeQueryTestValues.CreatePeer(baseName + suffix);
        Assert.Empty(RuntimeQueryWindowPipes.DiscoverCandidates(baseName));
    }

    private static bool Handle(string[] args, string baseName, TextWriter output, out int exitCode) =>
        RuntimeQueryCommandLine.TryHandlePerWindowQueryCommand(args, baseName, "1", RuntimeQueryCommandLineCases.SupportedCommands,
            (failure, detail) => new RuntimeQueryError(failure.ToString(), detail ?? "No matching window."), output, out exitCode);

    private static StringWriter NewWriter() => new(CultureInfo.InvariantCulture) { NewLine = Environment.NewLine };

    private static CancellationTokenSource NewTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return timeout;
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Per-window pipe discovery requires Windows.");
        }
    }

    private static string PrettySuccess(int value) => string.Join(Environment.NewLine,
        "{", "  \"ok\": true,", "  \"data\": {", "    \"value\": " + value, "  },", "  \"error\": null", "}") + Environment.NewLine;

    private static string PrettyFailure(string code, string message) => string.Join(Environment.NewLine,
        "{", "  \"ok\": false,", "  \"data\": null,", "  \"error\": {", "    \"code\": \"" + code + "\",",
        "    \"message\": \"" + message + "\"", "  }", "}") + Environment.NewLine;
}
