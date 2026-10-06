// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Nvt.Core.Processes;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>Pins platform-independent native preparation values without changing the internal starter API.</summary>
public sealed class WindowsContainedProcessStarterContractTests
{
    /// <summary>Quotes empty arguments, spaces, tabs, quotes, and zero/one/two backslashes exactly.</summary>
    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("\t", "\"\t\"")]
    [InlineData("quote\"inside", "\"quote\\\"inside\"")]
    [InlineData("trail\\", "trail\\")]
    [InlineData("trail\\\\", "trail\\\\")]
    [InlineData("two words\\", "\"two words\\\\\"")]
    [InlineData("two words\\\\", "\"two words\\\\\\\\\"")]
    [InlineData("\"", "\"\\\"\"")]
    [InlineData("\\\"", "\"\\\\\\\"\"")]
    [InlineData("\\\\\"", "\"\\\\\\\\\\\"\"")]
    [InlineData("\n", "\n")]
    [InlineData("第一 個", "\"第一 個\"")]
    public void CommandLineQuotingRetainsFrozenCharacterBoundaries(string argument, string quoted)
    {
        var info = new ProcessStartInfo { FileName = @"C:\worker.exe" };
        info.ArgumentList.Add(argument);
        Assert.Equal(@"C:\worker.exe " + quoted, CommandLine(info));
    }

    /// <summary>Executable quoting uses the same rules and raw Arguments are appended unchanged.</summary>
    [Theory]
    [InlineData(null, "\"C:\\worker folder\\worker.exe\"")]
    [InlineData("", "\"C:\\worker folder\\worker.exe\"")]
    [InlineData(" ", "\"C:\\worker folder\\worker.exe\"  ")]
    [InlineData("\"raw words\" \\tail", "\"C:\\worker folder\\worker.exe\" \"raw words\" \\tail")]
    public void RawArgumentsRemainExact(string? arguments, string expected)
    {
        var info = new ProcessStartInfo { FileName = @"C:\worker folder\worker.exe", Arguments = arguments };
        Assert.Equal(expected, CommandLine(info));
    }

    /// <summary>An argument list takes precedence in command construction while public native validation rejects mixing.</summary>
    [Fact]
    public void PreparationRetainsArgumentListPrecedence()
    {
        var info = new ProcessStartInfo { FileName = @"C:\worker.exe", Arguments = "unused raw" };
        info.ArgumentList.Add("first");
        info.ArgumentList.Add("");
        info.ArgumentList.Add("second");
        Assert.Equal("C:\\worker.exe first \"\" second", CommandLine(info));
    }

    /// <summary>Environment entries use stable ordinal case-insensitive ordering and skip null values only.</summary>
    [Fact]
    public void EnvironmentBlockRetainsSortAndNullPolicy()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["z"] = "last",
            ["b"] = "",
            ["aLpha"] = "環境 值 ✓",
            ["A"] = "first",
            ["omitted"] = null,
            ["a"] = "same folded key",
        };
        Assert.Equal("A=first\0a=same folded key\0aLpha=環境 值 ✓\0b=\0z=last\0\0",
            EnvironmentBlock(environment));
    }

    /// <summary>Zero, one, and two environment entries retain the frozen terminator behavior.</summary>
    [Theory]
    [InlineData(0, "\0")]
    [InlineData(1, "a=0\0\0")]
    [InlineData(2, "a=0\0b=1\0\0")]
    public void EnvironmentBlockCountBoundariesRetainTerminators(int count, string expected)
    {
        var environment = new Dictionary<string, string?>();
        for (int index = 0; index < count; index++)
        {
            environment.Add(((char)('a' + index)).ToString(), index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        Assert.Equal(expected, EnvironmentBlock(environment));
    }

    /// <summary>No environment key/value normalization or hidden length ceiling is added.</summary>
    [Fact]
    public void EnvironmentBlockPreservesSourceCharacters()
    {
        var environment = new Dictionary<string, string?> { [" a "] = "x=y\0z" };
        Assert.Equal(" a =x=y\0z\0\0", EnvironmentBlock(environment));
    }

    /// <summary>The fixed confirmation bound remains exactly five seconds rather than a product parameter.</summary>
    [Fact]
    public void TerminationConfirmationRetainsFiveSecondMechanismBound()
    {
        FieldInfo field = typeof(WindowsContainedProcessStarter).GetField(
            "TerminationConfirmationMilliseconds", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.True(field.IsLiteral);
        Assert.Equal(5_000u, Assert.IsType<uint>(field.GetRawConstantValue()));
    }

    private static string CommandLine(ProcessStartInfo info) =>
        Invoke("CreateCommandLine", info);

    private static string EnvironmentBlock(IReadOnlyDictionary<string, string?> environment) =>
        Invoke("CreateEnvironmentBlock", environment);

    private static string Invoke(string method, object value)
    {
        MethodInfo member = typeof(WindowsContainedProcessStarter).GetMethod(
            method, BindingFlags.Static | BindingFlags.NonPublic)!;
        return Assert.IsType<string>(member.Invoke(null, [value]));
    }
}
