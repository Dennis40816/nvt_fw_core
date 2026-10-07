// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Reflection;
using System.Text;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>All 40 frozen command-line cases and four additional boundaries.</summary>
[Collection(RuntimeQueryPipeTestGroup.Name)]
public sealed class RuntimeQueryCommandLineTests
{
    /// <summary>Parse failures keep pretty output and exit code two.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.ParseErrors), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public void ParseErrorPrintsExactPrettyJsonAndReturnsTwo(string[] args, string stdout)
    {
        AssertOutput(args, handled: true, exitCode: 2, stdout);
    }

    /// <summary>Option forms preserve exact request keys, values and stdout.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.OptionForms), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public async Task OptionFormsSendExactArgumentsAndPrintExactSuccess(string[] args, string request, string stdout)
    {
        await AssertReplyAsync(args, RuntimeQueryCommandLineCases.SuccessReply, 0, stdout, request);
    }

    /// <summary>The default timeout and both inclusive limits match the source parser.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.Timeouts), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public void TimeoutDefaultAndInclusiveLimitsAreParsed(string[] args, int expected)
    {
        // Keep the source's private-parser check; a 1 ms round trip would race the pipe connection.
        var method = typeof(RuntimeQueryCommandLine).GetMethod("TryParseCommandLine", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] parameters = [args, RuntimeQueryCommandLineCases.SupportedCommands, null, null, null, null, null];

        Assert.True((bool)method.Invoke(null, parameters)!);
        Assert.Equal("help", parameters[2]);
        Assert.Empty(Assert.IsType<Dictionary<string, string>>(parameters[3]));
        Assert.Equal(expected, parameters[4]);
        Assert.Equal(true, parameters[5]);
        Assert.Null(parameters[6]);
    }

    /// <summary>Output switches affect stdout and stay out of the request.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.OutputModes), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public async Task OutputModePrintsExactJsonAndOmitsCommonOptionsFromRequest(string[] args, string request, string stdout)
    {
        await AssertReplyAsync(args, RuntimeQueryCommandLineCases.SuccessReply, 0, stdout, request);
    }

    /// <summary>The query verb ignores case.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.QueryVerbs), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public void QueryVerbIgnoresLetterCase(string[] args, string stdout)
    {
        AssertOutput(args, handled: true, exitCode: 2, stdout);
    }

    /// <summary>Other first arguments return unhandled with no output.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.UnhandledArguments), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public void NonQueryArgumentsAreNotHandledAndPrintNothing(string[] args, string stdout)
    {
        AssertOutput(args, handled: false, exitCode: 0, stdout);
    }

    /// <summary>An absent unique pipe uses the source tool's client error text.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.NoRunningInstance), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public void NoRunningInstancePrintsExactFailureAndReturnsOne(string[] args, string stdout)
    {
        AssertOutput(args, handled: true, exitCode: 1, stdout, RuntimeQueryTestValues.NewPipeName());
    }

    /// <summary>A failed response keeps compact output and exit code one.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.FailedResponse), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public async Task FailedResponsePrintsExactCompactJsonAndReturnsOne(string[] args, string reply, string stdout)
    {
        await AssertReplyAsync(args, reply, 1, stdout);
    }

    /// <summary>The caller can assign the returned exit code exactly as the source Program does.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandLineCases.ProgramParseError), MemberType = typeof(RuntimeQueryCommandLineCases))]
    public void ProgramQueryParseErrorSetsProcessExitCodeAndPrintsExactJson(string[] args, string stdout)
    {
        var originalExitCode = Environment.ExitCode;
        using var writer = NewWriter();
        try
        {
            // Reproduce Program's caller-owned exit assignment without the product UI startup.
            if (Handle(args, RuntimeQueryCommandLineCases.PipeName, writer, out var exitCode))
            {
                Environment.ExitCode = exitCode;
            }

            Assert.Equal(2, Environment.ExitCode);
            Assert.Equal(stdout, writer.ToString());
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
        }
    }

    /// <summary>Fixed-name mode forwards PID options as ordinary command arguments, including invalid process IDs.</summary>
    [Theory]
    [InlineData("--pid", "123", "123")]
    [InlineData("--pid=123", null, "123")]
    [InlineData("--pid", null, "true")]
    [InlineData("--pid=0", null, "0")]
    [InlineData("--pid=-1", null, "-1")]
    [InlineData("--pid=text", null, "text")]
    public async Task FixedNameModeKeepsPidAsCommandArgument(string option, string? value, string expected)
    {
        string[] args = value is null ? ["query", "help", option, "--json-compact"] : ["query", "help", option, value, "--json-compact"];
        await AssertReplyAsync(args, RuntimeQueryCommandLineCases.SuccessReply, 0,
            RuntimeQueryCommandLineCases.SuccessReply + Environment.NewLine,
            "{\"version\":\"1\",\"command\":\"help\",\"args\":{\"pid\":\"" + expected + "\"}}" + Environment.NewLine);
    }

    private static void AssertOutput(string[] args, bool handled, int exitCode, string stdout, string? pipeName = null)
    {
        using var writer = NewWriter();
        var result = Handle(args, pipeName ?? RuntimeQueryCommandLineCases.PipeName, writer, out var actualExitCode);

        Assert.Equal(handled, result);
        Assert.Equal(exitCode, actualExitCode);
        Assert.Equal(stdout, writer.ToString());
    }

    private static async Task AssertReplyAsync(string[] args, string reply, int exitCode, string stdout, string? request = null)
    {
        var pipeName = RuntimeQueryTestValues.NewPipeName();
        await using var peer = RuntimeQueryTestValues.CreatePeer(pipeName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var registration = timeout.Token.Register(() => peer.Dispose());
        var serverTask = Task.Run(async () =>
        {
            await peer.WaitForConnectionAsync(timeout.Token);
            var frame = await RuntimeQueryTestValues.ReadFrameAsync(peer, timeout.Token);
            await peer.WriteAsync(Encoding.UTF8.GetBytes(reply + Environment.NewLine), timeout.Token);
            return Encoding.UTF8.GetString(frame);
        }, timeout.Token);

        using var writer = NewWriter();
        var result = await Task.Run(() =>
        {
            var handled = Handle(args, pipeName, writer, out var actualExitCode);
            return (Handled: handled, ExitCode: actualExitCode);
        }, timeout.Token).WaitAsync(timeout.Token);
        var frame = await serverTask.WaitAsync(timeout.Token);

        Assert.True(result.Handled);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(stdout, writer.ToString());
        if (request is not null)
        {
            Assert.Equal(request, frame);
        }
    }

    private static bool Handle(string[] args, string pipeName, TextWriter output, out int exitCode) =>
        RuntimeQueryCommandLine.TryHandleQueryCommand(args, pipeName, RuntimeQueryCommandLineCases.Version,
            RuntimeQueryCommandLineCases.SupportedCommands, RuntimeQueryTestValues.NfhError, output, out exitCode);

    private static StringWriter NewWriter() => new(CultureInfo.InvariantCulture) { NewLine = Environment.NewLine };
}
