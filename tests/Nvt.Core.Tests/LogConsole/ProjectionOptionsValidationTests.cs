// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Invalid app presentation inputs fail before any row is projected.</summary>
public sealed class ProjectionOptionsValidationTests
{
    /// <summary>Each invalid input throws an argument exception that names the options.</summary>
    /// <param name="invalid">The invalid input case.</param>
    [Theory]
    [InlineData(InvalidOption.NullSource)]
    [InlineData(InvalidOption.NullSourceId)]
    [InlineData(InvalidOption.NullDisplayName)]
    [InlineData(InvalidOption.DefaultRegistry)]
    [InlineData(InvalidOption.NullTemplate)]
    [InlineData(InvalidOption.NullCulture)]
    [InlineData(InvalidOption.NullTimeZone)]
    public void InvalidPresentationInputsThrowArgumentExceptions(InvalidOption invalid)
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "message", DateTimeOffset.UnixEpoch);
        using var snapshot = LogStoreTests.Capture(store);
        var options = invalid switch
        {
            InvalidOption.NullSource => new ConsoleProjectionOptions { SourceRegistry = [null!] },
            InvalidOption.NullSourceId => new ConsoleProjectionOptions { SourceRegistry = [new ConsoleSource(null!, "x")] },
            InvalidOption.NullDisplayName => new ConsoleProjectionOptions { SourceRegistry = [new ConsoleSource("x", null!)] },
            InvalidOption.DefaultRegistry => new ConsoleProjectionOptions { SourceRegistry = default },
            InvalidOption.NullTemplate => new ConsoleProjectionOptions { RelativeTimeTemplate = null! },
            InvalidOption.NullCulture => new ConsoleProjectionOptions { Culture = null! },
            _ => new ConsoleProjectionOptions { AbsoluteTimeZone = null! },
        };
        Assert.ThrowsAny<ArgumentException>(() => ConsoleProjector.Project(snapshot, new ConsoleFilter(), new ConsoleViewState(), options));
    }

    /// <summary>The kinds of invalid presentation input.</summary>
    public enum InvalidOption
    {
        /// <summary>A null registry entry.</summary>
        NullSource,
        /// <summary>A source with a null ID.</summary>
        NullSourceId,
        /// <summary>A source with a null display name.</summary>
        NullDisplayName,
        /// <summary>An uninitialized registry array.</summary>
        DefaultRegistry,
        /// <summary>A null relative template.</summary>
        NullTemplate,
        /// <summary>A null culture.</summary>
        NullCulture,
        /// <summary>A null display zone.</summary>
        NullTimeZone,
    }
}
