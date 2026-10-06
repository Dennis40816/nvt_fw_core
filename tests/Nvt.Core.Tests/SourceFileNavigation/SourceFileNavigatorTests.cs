// Copyright (c) 2026 Dennis Liu. All rights reserved.
// Frozen NFU source: Dennis40816/nvt-event-buffer-replay, origin/0.2.0, commit 915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b
// src/Nvt.Replay.Avalonia/SourceFileNavigator.cs: TryStart and Open's default-open branch.
// tests/Nvt.Replay.Avalonia.Tests/SourceFileNavigatorTests.cs: routing policy tests remain in NFU.
// These BCL characterization tests use call-scoped starters and never start real processes.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.
#pragma warning disable CA1707 // Keep descriptive names consistent with the frozen NFU tests.

using System.ComponentModel;
using System.Diagnostics;
using Nvt.Core.SourceFileNavigation;
using Xunit;

namespace Nvt.Core.Tests.SourceFileNavigation;

public sealed class SourceFileNavigatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryStart_preserves_individual_arguments_and_normal_returns_are_successful(bool returnProcess)
    {
        const string executable = "tools/editor name.exe";
        string[] arguments = ["--flag", "source files/quoted \"name\".txt:37:1", "", "tab\targument", "資料.txt", "trailing\\"];
        using var process = returnProcess ? new Process() : null;
        var calls = 0;

        var opened = SourceFileNavigator.TryStart(executable, arguments, startInfo =>
        {
            calls++;
            Assert.Equal(executable, startInfo.FileName);
            Assert.False(startInfo.UseShellExecute);
            Assert.Equal(arguments, startInfo.ArgumentList);
            Assert.Equal(string.Empty, startInfo.Arguments);
            Assert.Equal(string.Empty, startInfo.WorkingDirectory);
            Assert.False(startInfo.CreateNoWindow);
            Assert.False(startInfo.RedirectStandardInput);
            Assert.False(startInfo.RedirectStandardOutput);
            Assert.False(startInfo.RedirectStandardError);
            return process;
        }, out var error);

        Assert.True(opened);
        Assert.Null(error);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void TryStart_accepts_an_empty_argument_list()
    {
        var calls = 0;

        var opened = SourceFileNavigator.TryStart("editor.exe", [], startInfo =>
        {
            calls++;
            Assert.Empty(startInfo.ArgumentList);
            Assert.False(startInfo.UseShellExecute);
            return null;
        }, out var error);

        Assert.True(opened);
        Assert.Null(error);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, "Synthetic failure.\r\n下一行。")]
    [InlineData(true, "Synthetic failure.\r\n下一行。")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public void TryStart_handles_only_the_frozen_exception_types_and_preserves_error_text(bool win32, string message)
    {
        Exception failure = win32 ? new Win32Exception(5, message) : new InvalidOperationException(message);
        var calls = 0;

        var opened = SourceFileNavigator.TryStart("editor.exe", ["source.txt"], _ =>
        {
            calls++;
            throw failure;
        }, out var error);

        Assert.False(opened);
        Assert.Equal(message, error);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("argument")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("unsupported")]
    public void TryStart_propagates_exceptions_outside_the_frozen_filter(string kind)
    {
        var failure = CreateUnhandledException(kind);
        var calls = 0;

        var error = Assert.Throws(failure.GetType(), () => SourceFileNavigator.TryStart("editor.exe", [], _ =>
        {
            calls++;
            throw failure;
        }, out _));

        Assert.Same(failure, error);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenDefault_preserves_the_path_uses_the_shell_and_never_claims_an_exact_line(bool returnProcess)
    {
        const string path = "relative folder/../missing source.csv";
        using var process = returnProcess ? new Process() : null;
        var calls = 0;

        var result = SourceFileNavigator.OpenDefault(path, startInfo =>
        {
            calls++;
            Assert.Equal(path, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
            Assert.Empty(startInfo.ArgumentList);
            Assert.Equal(string.Empty, startInfo.Arguments);
            Assert.Equal(string.Empty, startInfo.WorkingDirectory);
            Assert.False(startInfo.CreateNoWindow);
            Assert.False(startInfo.RedirectStandardInput);
            Assert.False(startInfo.RedirectStandardOutput);
            Assert.False(startInfo.RedirectStandardError);
            return process;
        });

        Assert.Equal(new SourceFileOpenResult(true, false, "default application"), result);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, "Synthetic failure.\r\n下一行。")]
    [InlineData(true, "Synthetic failure.\r\n下一行。")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public void OpenDefault_handles_only_the_frozen_exception_types_and_preserves_error_text(bool win32, string message)
    {
        Exception failure = win32 ? new Win32Exception(5, message) : new InvalidOperationException(message);
        var calls = 0;

        var result = SourceFileNavigator.OpenDefault("source.txt", _ =>
        {
            calls++;
            throw failure;
        });

        Assert.Equal(new SourceFileOpenResult(false, false, "default application", message), result);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("argument")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("unsupported")]
    public void OpenDefault_propagates_exceptions_outside_the_frozen_filter(string kind)
    {
        var failure = CreateUnhandledException(kind);
        var calls = 0;

        var error = Assert.Throws(failure.GetType(), () => SourceFileNavigator.OpenDefault("source.txt", _ =>
        {
            calls++;
            throw failure;
        }));

        Assert.Same(failure, error);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Nested_calls_use_their_own_starters_and_do_not_share_errors()
    {
        var outerCalls = 0;
        var innerCalls = 0;

        var opened = SourceFileNavigator.TryStart("editor.exe", ["source.txt"], startInfo =>
        {
            outerCalls++;
            Assert.False(startInfo.UseShellExecute);
            var innerResult = SourceFileNavigator.OpenDefault("other.txt", innerStartInfo =>
            {
                innerCalls++;
                Assert.True(innerStartInfo.UseShellExecute);
                Assert.Equal("other.txt", innerStartInfo.FileName);
                throw new InvalidOperationException("Inner failure.");
            });
            Assert.Equal(new SourceFileOpenResult(false, false, "default application", "Inner failure."), innerResult);
            return null;
        }, out var error);

        Assert.True(opened);
        Assert.Null(error);
        Assert.Equal(1, outerCalls);
        Assert.Equal(1, innerCalls);
    }

    private static Exception CreateUnhandledException(string kind) => kind switch
    {
        "argument" => new ArgumentException("Synthetic argument failure."),
        "io" => new IOException("Synthetic I/O failure."),
        "access" => new UnauthorizedAccessException("Synthetic access failure."),
        "unsupported" => new NotSupportedException("Synthetic unsupported operation."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown synthetic exception kind."),
    };
}
