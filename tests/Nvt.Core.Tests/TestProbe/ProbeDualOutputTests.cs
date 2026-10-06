// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.

using System.Diagnostics;
using Xunit;

namespace Nvt.Core.Tests.TestProbe;

[Collection(nameof(ProbeEnvironmentGroup))]
public sealed class ProbeDualOutputTests
{
    private const int DefaultCount = 131_072;

    [Fact]
    public async Task DualOutputExitWritesBothStreamsAndReturnsZero()
    {
        await using var workspace = new ProbeWorkspace();
        var process = workspace.Start(["--mode", "dual-output-exit"]);
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await ProbeWorkspace.ExitAsync(process, 0);
        Assert.Equal(new string('A', DefaultCount) + "OUT-END", await output.WaitAsync(ProbeWorkspace.Bound, TestContext.Current.CancellationToken));
        Assert.Equal(new string('B', DefaultCount) + "ERR-END", await error.WaitAsync(ProbeWorkspace.Bound, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(workspace.Root));
    }

    [Fact]
    public async Task DualOutputInputsAcceptBothForms()
    {
        await using var workspace = new ProbeWorkspace();
        var process = workspace.Start(["--mode", "dual-output-exit", "--out-char", "x", "--out-count", "3", "--out-suffix", "end"], info =>
        {
            info.Environment["CORE_TEST_PROBE_ERR_CHAR"] = "y";
            info.Environment["CORE_TEST_PROBE_ERR_COUNT"] = "0";
            info.Environment["CORE_TEST_PROBE_ERR_SUFFIX"] = "only";
        });
        await ProbeWorkspace.ExitAsync(process, 0);
        // Compare bytes, because the redirected readers would remove a byte order mark.
        Assert.Equal("xxxend"u8.ToArray(), await ProbeWorkspace.ReadAsync(process.StandardOutput.BaseStream));
        Assert.Equal("only"u8.ToArray(), await ProbeWorkspace.ReadAsync(process.StandardError.BaseStream));
    }

    [Fact]
    public async Task DualOutputWaitWritesBothStreamsThenWaitsWithoutMoreOutput()
    {
        await using var workspace = new ProbeWorkspace();
        var process = workspace.Start(["--mode", "dual-output-wait"]);
        string expectedOutput = new string('O', DefaultCount) + "OUT-PARTIAL-END";
        string expectedError = new string('E', DefaultCount) + "ERR-PARTIAL-END";
        Task<string> output = ReadLengthAsync(process.StandardOutput, expectedOutput.Length);
        Task<string> error = ReadLengthAsync(process.StandardError, expectedError.Length);
        Assert.Equal(expectedOutput, await output);
        Assert.Equal(expectedError, await error);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.False(process.HasExited);
        await ProbeWorkspace.KillAsync(process);
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
    }

    [Fact]
    public async Task DualOutputWaitReturnsZeroAfterTheRequestedWait()
    {
        await using var workspace = new ProbeWorkspace();
        var clock = Stopwatch.StartNew();
        var process = workspace.Start(["--mode", "dual-output-wait", "--out-count", "1", "--err-count", "1", "--wait-ms", "2000"]);
        Assert.Equal("OOUT-PARTIAL-END", await ReadLengthAsync(process.StandardOutput, 16));
        Assert.Equal("EERR-PARTIAL-END", await ReadLengthAsync(process.StandardError, 16));
        Assert.False(process.HasExited);
        await ProbeWorkspace.ExitAsync(process, 0, 10);
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(2));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardError));
    }

    [Theory]
    [InlineData("dual-output-exit", "out-char", "AB")]
    [InlineData("dual-output-exit", "err-char", "é")]
    [InlineData("dual-output-exit", "out-char", "")]
    [InlineData("dual-output-exit", "out-suffix", "end ✓")]
    [InlineData("dual-output-exit", "err-suffix", "é")]
    [InlineData("dual-output-exit", "out-count", "abc")]
    [InlineData("dual-output-exit", "err-count", "-1")]
    [InlineData("dual-output-wait", "wait-ms", "0")]
    [InlineData("dual-output-wait", "wait-ms", "-5")]
    public async Task InvalidDualOutputInputsReturnUsageCodeWithoutOutput(string mode, string input, string value)
    {
        await using var workspace = new ProbeWorkspace();
        var process = workspace.Start(["--mode", mode, "--" + input, value]);
        await ProbeWorkspace.ExitAsync(process, 64);
        Assert.Empty(await ProbeWorkspace.ReadAsync(process.StandardOutput));
        string error = await ProbeWorkspace.ReadAsync(process.StandardError);
        Assert.Contains(input, error, StringComparison.Ordinal);
        Assert.Single(error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task<string> ReadLengthAsync(StreamReader reader, int length)
    {
        char[] buffer = new char[length];
        int read = await reader.ReadBlockAsync(buffer, TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(ProbeWorkspace.Bound, TestContext.Current.CancellationToken);
        return new string(buffer, 0, read);
    }
}
