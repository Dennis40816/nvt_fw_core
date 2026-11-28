// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.LinkedProbe;

/// <summary>Checks that the recovery modes participate in the shared dispatch and required-input protocol.</summary>
public sealed class RecoveryModeInputTests
{
    /// <summary>Missing recovery context returns the shared input error before loading any scenario.</summary>
    [Theory]
    [InlineData("setup-materializer")]
    [InlineData("recovery-executor")]
    public async Task RecoveryModesRequireStatePath(string mode)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Recovery modes require Windows.");
        string executable = Path.Combine(AppContext.BaseDirectory, "linked-probe", "Nvt.Core.LinkedProbe.exe");
        await using var child = ChildProcessFixture.Start(executable, ["--mode", mode]);
        Assert.Equal(64, await child.WaitForExitAsync(TestContext.Current.CancellationToken));
        await child.WaitForOutputAsync("Missing input: state-path", TestContext.Current.CancellationToken);
    }
}
