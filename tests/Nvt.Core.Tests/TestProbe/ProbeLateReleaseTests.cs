// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.TestProbe;
using Xunit;

namespace Nvt.Core.Tests.TestProbe;

/// <summary>Release callbacks tolerate the wait handle's completed lifetime.</summary>
public sealed class ProbeLateReleaseTests
{
    /// <summary>A release event after disposal has nothing left to signal and must not throw.</summary>
    [Fact]
    public void ReleaseAfterWaitHandleDisposalDoesNotThrow()
    {
        using var released = new ManualResetEvent(initialState: false);
        released.Dispose();

        Assert.Null(Record.Exception(() => ReleaseSignal.Set(released)));
    }
}
