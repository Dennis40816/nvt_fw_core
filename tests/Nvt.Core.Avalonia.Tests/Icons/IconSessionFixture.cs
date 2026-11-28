// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Headless;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Icons;

/// <summary>Starts one headless icon session for the whole icon collection and disposes it once at the end.</summary>
public sealed class IconSessionFixture : IAsyncLifetime
{
    private HeadlessUnitTestSession? session;

    /// <summary>Gets the running session.</summary>
    public HeadlessUnitTestSession Session =>
        this.session ?? throw new InvalidOperationException("The icon session is not started.");

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        this.session = HeadlessUnitTestSession.StartNew(typeof(IconsTestApplication));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.session is { } started)
        {
            this.session = null;
            await DisposeSessionAsync(started);
        }
    }

    private static async ValueTask DisposeSessionAsync(HeadlessUnitTestSession session)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch (NullReferenceException exception) when (exception.StackTrace?.TrimStart().StartsWith(
            "at Avalonia.Headless.HeadlessUnitTestSession.DisposeAsync()", StringComparison.Ordinal) == true)
        {
            // Avalonia.Headless 12.1.1 can capture a null dispatch task before StartNew assigns Task.Run's result.
        }
    }
}
