// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Posted assertion failures are delivered on the joining test thread.</summary>
public sealed class SingleThreadPumpTests
{
    /// <summary>The first posted exception is preserved and later callbacks still run before shutdown.</summary>
    /// <param name="dispose">Whether disposal supplies the join.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PostedAssertionFailureIsRethrownOnJoinOrDispose(bool dispose)
    {
        using var waitCancellation = StoreRegressionSupport.CreateWaitCancellation();
        var expected = new Xunit.Sdk.XunitException("posted assertion failed");
        var pump = new SingleThreadPump(waitCancellation.Token);
        var continued = false;
        pump.Schedule(() => throw expected);
        pump.Schedule(() => throw new InvalidOperationException("later callback failed"));
        pump.Schedule(() => continued = true);
        try
        {
            var actual = Assert.Throws<Xunit.Sdk.XunitException>(() =>
            {
                if (dispose) pump.Dispose(); else pump.Join();
            });
            Assert.Same(expected, actual);
            Assert.True(continued);
        }
        finally
        {
            try { pump.Dispose(); }
            catch (Xunit.Sdk.XunitException exception) { Assert.Same(expected, exception); }
        }
    }
}
