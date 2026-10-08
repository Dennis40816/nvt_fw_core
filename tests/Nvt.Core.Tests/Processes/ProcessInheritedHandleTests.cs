// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Nvt.Core.Processes;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>Characterizes inherited-handle validation, decimal boundaries, and value equality.</summary>
public sealed class ProcessInheritedHandleTests
{
    /// <summary>The default handle advertises its nullable environment name without validating construction.</summary>
    [Fact]
    public void DefaultHandleHasNoEnvironmentName()
    {
        ProcessInheritedHandle binding = default;
        Assert.Null(binding.EnvironmentVariable);
        Assert.Equal(IntPtr.Zero, binding.Handle);
        var property = typeof(ProcessInheritedHandle).GetProperty(nameof(ProcessInheritedHandle.EnvironmentVariable));
        Assert.NotNull(property);
        Assert.Equal(System.Reflection.NullabilityState.Nullable,
            new System.Reflection.NullabilityInfoContext().Create(property).ReadState);
    }

    /// <summary>Blank environment names fail before the nonpositive-handle predicate.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void BlankEnvironmentNameIsRejectedFirst(string? name)
    {
        ArgumentException error = Assert.ThrowsAny<ArgumentException>(() =>
            new ProcessInheritedHandle(name!, IntPtr.Zero));
        Assert.Equal("environmentVariable", error.ParamName);
        Assert.IsType(name is null ? typeof(ArgumentNullException) : typeof(ArgumentException), error);
    }

    /// <summary>The equals predicate precedes the handle predicate and retains its exact message.</summary>
    [Theory]
    [InlineData("=")]
    [InlineData("=name")]
    [InlineData("name=")]
    [InlineData("na=me")]
    public void EqualsInEnvironmentNameIsRejectedSecond(string name)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new ProcessInheritedHandle(name, IntPtr.Zero));
        Assert.Equal("environmentVariable", error.ParamName);
        Assert.Equal(new ArgumentException("Environment variable names cannot contain '='.",
            error.ParamName).Message, error.Message);
    }

    /// <summary>No extra normalization or environment-name policy is introduced.</summary>
    [Theory]
    [InlineData("a")]
    [InlineData(" name ")]
    [InlineData("a b")]
    [InlineData("環境")]
    [InlineData("a\0b")]
    public void NonblankNamesWithoutEqualsRemainExact(string name)
    {
        var binding = new ProcessInheritedHandle(name, new IntPtr(1));
        Assert.Equal(name, binding.EnvironmentVariable);
        Assert.Equal(new IntPtr(1), binding.Handle);
    }

    /// <summary>Checks zero, negative one, and positive one around the exclusive zero boundary.</summary>
    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void HandleBoundaryRetainsExclusiveZeroPredicate(long value)
    {
        if (value <= 0)
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ProcessInheritedHandle("HANDLE", new IntPtr(value)));
            Assert.Equal("handle", error.ParamName);
            Assert.Equal(new ArgumentOutOfRangeException(error.ParamName).Message, error.Message);
        }
        else
        {
            Assert.Equal(new IntPtr(value), new ProcessInheritedHandle("HANDLE", new IntPtr(value)).Handle);
        }
    }

    /// <summary>The signed pointer extremes retain the frozen positivity predicate.</summary>
    [Fact]
    public void PointerExtremesRetainPositivityPredicate()
    {
        long minimum = IntPtr.Size == 8 ? long.MinValue : int.MinValue;
        long maximum = IntPtr.Size == 8 ? long.MaxValue : int.MaxValue;
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessInheritedHandle("HANDLE", new IntPtr(minimum)));
        Assert.Equal(new IntPtr(maximum), new ProcessInheritedHandle("HANDLE", new IntPtr(maximum)).Handle);
    }

    /// <summary>Unsigned decimal syntax accepts digits without changing the bound name.</summary>
    [Theory]
    [InlineData("1", 1)]
    [InlineData("123", 123)]
    [InlineData("000123", 123)]
    public void ParseAcceptsUnsignedDecimalDigits(string text, long expected)
    {
        Assert.Equal(new ProcessInheritedHandle("HANDLE", new IntPtr(expected)),
            ProcessInheritedHandle.Parse("HANDLE", text));
    }

    /// <summary>Parse failures preserve the handle parameter and exact positive-decimal message.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("0x10")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,000")]
    [InlineData("١")]
    [InlineData("9223372036854775808")]
    public void ParseRejectsOtherSyntaxBeforeNameValidation(string? text)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            ProcessInheritedHandle.Parse("", text!));
        Assert.Equal("handle", error.ParamName);
        Assert.Equal(new ArgumentException("Inherited handle must be a positive decimal value.",
            error.ParamName).Message, error.Message);
    }

    /// <summary>Zero parses successfully before the constructor checks name and then handle.</summary>
    [Fact]
    public void ParsedZeroRetainsConstructorValidationOrder()
    {
        Assert.Equal("environmentVariable", Assert.Throws<ArgumentException>(() =>
            ProcessInheritedHandle.Parse("", "0")).ParamName);
        Assert.Equal("environmentVariable", Assert.Throws<ArgumentException>(() =>
            ProcessInheritedHandle.Parse("=", "0")).ParamName);
        Assert.Equal("handle", Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProcessInheritedHandle.Parse("HANDLE", "0")).ParamName);
    }

    /// <summary>Checks the signed decimal parser ceiling immediately below, at, and above its boundary.</summary>
    [Fact]
    public void ParseRetainsSigned64BitCeiling()
    {
        Assert.SkipUnless(IntPtr.Size == 8, "Signed 64-bit handle acceptance requires a 64-bit process.");
        Assert.Equal(new IntPtr(long.MaxValue - 1),
            ProcessInheritedHandle.Parse("HANDLE", "9223372036854775806").Handle);
        Assert.Equal(new IntPtr(long.MaxValue),
            ProcessInheritedHandle.Parse("HANDLE", "9223372036854775807").Handle);
        Assert.Equal("handle", Assert.Throws<ArgumentException>(() =>
            ProcessInheritedHandle.Parse("HANDLE", "9223372036854775808")).ParamName);
    }

    /// <summary>The ambient culture does not change decimal parsing.</summary>
    [Fact]
    public void ParseUsesInvariantCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal(new IntPtr(123), ProcessInheritedHandle.Parse("HANDLE", "123").Handle);
            _ = Assert.Throws<ArgumentException>(() => ProcessInheritedHandle.Parse("HANDLE", "١٢٣"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Both record components participate in case-sensitive equality and default remains a raw record.</summary>
    [Fact]
    public void RecordEqualityIncludesExactNameAndHandle()
    {
        var value = new ProcessInheritedHandle("HANDLE", new IntPtr(123));
        var equal = new ProcessInheritedHandle("HANDLE", new IntPtr(123));
        Assert.Equal(value, equal);
        Assert.True(value == equal);
        Assert.Equal(value.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(value, new ProcessInheritedHandle("handle", new IntPtr(123)));
        Assert.NotEqual(value, new ProcessInheritedHandle("HANDLE", new IntPtr(124)));
        ProcessInheritedHandle empty = default;
        Assert.Null(empty.EnvironmentVariable);
        Assert.Equal(IntPtr.Zero, empty.Handle);
    }
}
