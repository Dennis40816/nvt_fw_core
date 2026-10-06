// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Processes;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>Characterizes launch validation, argument ownership, result equality, and failure contracts.</summary>
public sealed class ExternalProcessContractTests
{
    private static readonly string[] OriginalArguments = ["first", "two words", ""];
    private static readonly string[] CleanupNames =
        ["Complete", "TerminationUnconfirmed", "OutputStreamHeldOpen", "OutputReadFailed"];
    private static readonly int[] CleanupValues = [0, 1, 2, 3];

    /// <summary>Null paths retain the source's null exception type and parameter name.</summary>
    [Theory]
    [InlineData(true, "executablePath")]
    [InlineData(false, "workingDirectory")]
    public void StartInfoRejectsNullPaths(bool executable, string expectedParameter)
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new ExternalProcessStartInfo(executable ? null! : "tool", executable ? "work" : null!,
                [], TimeSpan.FromTicks(1)));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    /// <summary>Empty and whitespace paths retain the source's argument exception type and parameter name.</summary>
    [Theory]
    [InlineData("", true, "executablePath")]
    [InlineData(" ", true, "executablePath")]
    [InlineData("\t\r\n", true, "executablePath")]
    [InlineData("", false, "workingDirectory")]
    [InlineData(" ", false, "workingDirectory")]
    [InlineData("\t\r\n", false, "workingDirectory")]
    public void StartInfoRejectsBlankPaths(string value, bool executable, string expectedParameter)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new ExternalProcessStartInfo(executable ? value : "tool", executable ? "work" : value,
                [], TimeSpan.FromTicks(1)));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    /// <summary>A null argument sequence is rejected before timeout validation.</summary>
    [Fact]
    public void StartInfoRejectsNullArguments()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new ExternalProcessStartInfo("tool", "work", null!, TimeSpan.Zero));

        Assert.Equal("arguments", exception.ParamName);
    }

    /// <summary>Zero and negative timeouts preserve the exact message, parameter, and actual value.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void StartInfoRejectsNonpositiveTimeout(long ticks)
    {
        TimeSpan timeout = TimeSpan.FromTicks(ticks);
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExternalProcessStartInfo("tool", "work", [], timeout));

        Assert.Equal("timeout", exception.ParamName);
        Assert.Equal(timeout, exception.ActualValue);
        Assert.StartsWith("Timeout must be positive.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>One tick and the largest representable positive timeout are accepted unchanged.</summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void StartInfoAcceptsPositiveTimeout(long ticks)
    {
        TimeSpan timeout = TimeSpan.FromTicks(ticks);
        var request = new ExternalProcessStartInfo("tool", "work", [], timeout);

        Assert.Equal(timeout, request.Timeout);
        Assert.Empty(request.Arguments);
    }

    /// <summary>Executable, working directory, arguments, and timeout fail in their frozen order.</summary>
    [Fact]
    public void StartInfoPreservesValidationOrder()
    {
        ArgumentNullException executable = Assert.Throws<ArgumentNullException>(() =>
            new ExternalProcessStartInfo(null!, null!, null!, TimeSpan.Zero));
        ArgumentNullException directory = Assert.Throws<ArgumentNullException>(() =>
            new ExternalProcessStartInfo("tool", null!, null!, TimeSpan.Zero));
        ArgumentNullException arguments = Assert.Throws<ArgumentNullException>(() =>
            new ExternalProcessStartInfo("tool", "work", null!, TimeSpan.Zero));

        Assert.Equal("executablePath", executable.ParamName);
        Assert.Equal("workingDirectory", directory.ParamName);
        Assert.Equal("arguments", arguments.ParamName);

        var enumerationFailure = new InvalidOperationException("Synthetic enumeration failure.");
        IEnumerable<string> ThrowingArguments()
        {
            yield return "first";
            throw enumerationFailure;
        }

        ArgumentOutOfRangeException timeout = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExternalProcessStartInfo("tool", "work", ThrowingArguments(), TimeSpan.Zero));
        Assert.Equal("timeout", timeout.ParamName);
        Assert.Same(enumerationFailure, Assert.Throws<InvalidOperationException>(() =>
            new ExternalProcessStartInfo("tool", "work", ThrowingArguments(), TimeSpan.FromTicks(1))));
    }

    /// <summary>The request owns a new argument array independent of subsequent source-list changes.</summary>
    [Fact]
    public void StartInfoCopiesArguments()
    {
        var arguments = new List<string> { "first", "two words", "" };
        var request = new ExternalProcessStartInfo(" tool ", " work ", arguments, TimeSpan.FromSeconds(1));
        arguments[0] = "changed";
        arguments.Add("later");
        arguments.Clear();

        Assert.Equal(OriginalArguments, request.Arguments);
        Assert.Equal(" tool ", request.ExecutablePath);
        Assert.Equal(" work ", request.WorkingDirectory);

        string[] sourceArray = ["original"];
        var arrayRequest = new ExternalProcessStartInfo("tool", "work", sourceArray, TimeSpan.FromTicks(1));
        sourceArray[0] = "changed";
        Assert.NotSame(sourceArray, arrayRequest.Arguments);
        Assert.Equal("original", Assert.Single(arrayRequest.Arguments));
    }

    /// <summary>The source does not impose a path, argument-count, or argument-character ceiling.</summary>
    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    public void StartInfoPreservesUnboundedNonblankValues(int length)
    {
        string value = new('P', length);
        string[] arguments = [value, "", null!];
        var request = new ExternalProcessStartInfo(value, value, arguments, TimeSpan.FromTicks(1));

        Assert.Equal(value, request.ExecutablePath);
        Assert.Equal(value, request.WorkingDirectory);
        Assert.Equal(arguments, request.Arguments);
    }

    /// <summary>Cleanup defaults to complete and participates in record equality and copying.</summary>
    [Fact]
    public void ResultDefaultsToCompleteAndUsesValueEquality()
    {
        var first = new ExternalProcessResult(0, false, "out", "err");
        var second = new ExternalProcessResult(0, false, "out", "err");

        Assert.Equal(ExternalProcessCleanup.Complete, first.Cleanup);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first, first with { });
        Assert.NotEqual(first, first with { Cleanup = ExternalProcessCleanup.OutputStreamHeldOpen });
        Assert.Equal(first with { Cleanup = ExternalProcessCleanup.OutputReadFailed },
            second with { Cleanup = ExternalProcessCleanup.OutputReadFailed });
    }

    /// <summary>Every positional result component participates in equality.</summary>
    [Theory]
    [InlineData(1, false, "out", "err")]
    [InlineData(0, true, "out", "err")]
    [InlineData(0, false, "changed", "err")]
    [InlineData(0, false, "out", "changed")]
    public void ResultEqualityIncludesAllObservedValues(int exitCode, bool timedOut, string output, string error)
    {
        Assert.NotEqual(new ExternalProcessResult(0, false, "out", "err"),
            new ExternalProcessResult(exitCode, timedOut, output, error));
    }

    /// <summary>Capacity facts and the complete refusal message are retained without new validation.</summary>
    [Theory]
    [InlineData(7, 8)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    [InlineData(1, 1)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void CapacityExceptionPreservesPropertiesAndExactMessage(int inUseInvocations, int limit)
    {
        var exception = new ExternalProcessCleanupCapacityException(inUseInvocations, limit);

        Assert.Equal(inUseInvocations, exception.InUseInvocations);
        Assert.Equal(limit, exception.Limit);
        Assert.Equal(
            $"The external process runner is at its limit of {limit} invocations that are running or still cleaning up " +
            $"({inUseInvocations} in use when the reservation was refused); a new run is refused. Restart the application.",
            exception.Message);
        Assert.Null(exception.InnerException);
    }

    /// <summary>A start failure names only the inner exception type and retains the same exception instance.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartFailurePreservesTypeTextAndInnerException(bool ioFailure)
    {
        Exception inner = ioFailure ? new IOException("Synthetic I/O detail.") : new InvalidOperationException("Synthetic detail.");
        var exception = new ExternalProcessStartFailedException(inner);

        Assert.Equal($"The external process could not be started ({inner.GetType().Name}).", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    /// <summary>A null start exception retains the frozen dereference failure instead of new validation.</summary>
    [Fact]
    public void StartFailureWithNullExceptionPreservesSourceFailure()
    {
        _ = Assert.Throws<NullReferenceException>(() => new ExternalProcessStartFailedException(null!));
    }

    /// <summary>Cleanup names and underlying values preserve the exact source order.</summary>
    [Fact]
    public void CleanupEnumPreservesExactNamesAndOrder()
    {
        Assert.Equal(CleanupNames, Enum.GetNames<ExternalProcessCleanup>());
        Assert.Equal(CleanupValues, Enum.GetValues<ExternalProcessCleanup>().Select(value => (int)value));
    }
}
