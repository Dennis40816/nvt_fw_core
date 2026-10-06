// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Characterizes the generic argument helpers.</summary>
public sealed class RuntimeQueryArgumentParserTests
{
    /// <summary>Integer arguments keep the source range checks and messages.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.Integers), MemberType = typeof(RuntimeQueryCommandCases))]
    public void IntArgumentsMatchFrozenRules(string? text, int min, int max, bool success, int expected, string? message)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "fr-FR", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var actual = RuntimeQueryArgumentParser.TryGetIntArg(Args(text), "value", min, max, out var value, out var error);
                Assert.Equal(success, actual);
                Assert.Equal(expected, value);
                AssertError(message, error);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Integer lists keep the source comma rules and messages.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.IntegerLists), MemberType = typeof(RuntimeQueryCommandCases))]
    public void IntListArgumentsMatchFrozenRules(string? text, bool success, int[] expected, string? message)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var actual = RuntimeQueryArgumentParser.TryGetIntListArg(Args(text), "value", -2, 2, out var values, out var error);
            Assert.Equal(success, actual);
            Assert.Equal(expected, values);
            AssertError(message, error);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Doubles parse with the invariant culture and keep the source messages.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.Doubles), MemberType = typeof(RuntimeQueryCommandCases))]
    public void DoubleArgumentsMatchFrozenRules(
        string? text, double min, double max, string culture, bool success, double expected, string? message)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var actual = RuntimeQueryArgumentParser.TryGetDoubleArg(Args(text), "value", min, max, out var value, out var error);
            Assert.Equal(success, actual);
            Assert.Equal(expected, value);
            AssertError(message, error);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>The string helper trims values and keeps the source messages.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.Strings), MemberType = typeof(RuntimeQueryCommandCases))]
    public void StringArgumentsMatchFrozenRules(string? text, bool success, string expected, string? message)
    {
        var actual = RuntimeQueryArgumentParser.TryGetStringArg(Args(text), "value", out var value, out var error);
        Assert.Equal(success, actual);
        Assert.Equal(expected, value);
        AssertError(message, error);
    }

    /// <summary>The boolean helper accepts every source spelling and keeps the source message.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.Booleans), MemberType = typeof(RuntimeQueryCommandCases))]
    public void BoolArgumentsMatchFrozenRules(string? text, bool success, bool expected, string? message)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var actual = RuntimeQueryArgumentParser.TryGetBoolArg(Args(text), "value", out var value, out var error);
            Assert.Equal(success, actual);
            Assert.Equal(expected, value);
            AssertError(message, error);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>A missing key or a null dictionary returns false with no error.</summary>
    [Theory]
    [MemberData(nameof(RuntimeQueryCommandCases.MissingArguments), MemberType = typeof(RuntimeQueryCommandCases))]
    public void MissingArgumentsReturnDefaultsWithoutErrors(IReadOnlyDictionary<string, string>? args)
    {
        Assert.False(RuntimeQueryArgumentParser.TryGetIntArg(args, "value", 1, 2, out var integer, out var intError));
        Assert.Equal(0, integer);
        Assert.Null(intError);
        Assert.False(RuntimeQueryArgumentParser.TryGetIntListArg(args, "value", 1, 2, out var integers, out var listError));
        Assert.Empty(integers);
        Assert.Null(listError);
        Assert.False(RuntimeQueryArgumentParser.TryGetDoubleArg(args, "value", 1, 2, out var number, out var doubleError));
        Assert.Equal(0d, number);
        Assert.Null(doubleError);
        Assert.False(RuntimeQueryArgumentParser.TryGetStringArg(args, "value", out var text, out var stringError));
        Assert.Equal("", text);
        Assert.Null(stringError);
        Assert.False(RuntimeQueryArgumentParser.TryGetBoolArg(args, "value", out var boolean, out var boolError));
        Assert.False(boolean);
        Assert.Null(boolError);
    }

    /// <summary>Key lookup uses the comparer of the supplied dictionary.</summary>
    [Fact]
    public void HelpersUseTheArgumentDictionaryComparer()
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["VALUE"] = "1" };
        Assert.True(RuntimeQueryArgumentParser.TryGetIntArg(args, "value", 1, 2, out _, out _));
        Assert.True(RuntimeQueryArgumentParser.TryGetIntListArg(args, "value", 1, 2, out _, out _));
        Assert.True(RuntimeQueryArgumentParser.TryGetDoubleArg(args, "value", 1, 2, out _, out _));
        Assert.True(RuntimeQueryArgumentParser.TryGetStringArg(args, "value", out _, out _));
        Assert.True(RuntimeQueryArgumentParser.TryGetBoolArg(args, "value", out _, out _));
    }

    /// <summary>Error messages name the key exactly as the caller supplied it.</summary>
    [Fact]
    public void HelpersRetainTheSuppliedKeyInErrorMessages()
    {
        var args = new Dictionary<string, string> { ["Limit"] = "invalid" };
        RuntimeQueryArgumentParser.TryGetIntArg(args, "Limit", 1, 2, out _, out var intError);
        AssertError("Argument '--Limit' must be an integer.", intError);
        RuntimeQueryArgumentParser.TryGetIntListArg(args, "Limit", 1, 2, out _, out var listError);
        AssertError("Argument '--Limit' must be a comma-separated integer list.", listError);
        RuntimeQueryArgumentParser.TryGetDoubleArg(args, "Limit", 1, 2, out _, out var doubleError);
        AssertError("Argument '--Limit' must be numeric.", doubleError);
        RuntimeQueryArgumentParser.TryGetBoolArg(args, "Limit", out _, out var boolError);
        AssertError("Argument '--Limit' must be true/false (or 1/0, on/off).", boolError);
    }

    private static Dictionary<string, string> Args(string? text) => new() { ["value"] = text! };

    private static void AssertError(string? message, RuntimeQueryResponseEnvelope? error)
    {
        if (message is null)
        {
            Assert.Null(error);
            return;
        }

        Assert.NotNull(error);
        Assert.False(error.Ok);
        Assert.Null(error.Data);
        Assert.Equal("INVALID_ARGUMENTS", error.Error?.Code);
        Assert.Equal(message, error.Error?.Message);
    }
}
