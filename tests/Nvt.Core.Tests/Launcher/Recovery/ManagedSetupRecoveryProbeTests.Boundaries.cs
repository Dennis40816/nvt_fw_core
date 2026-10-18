// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

public sealed partial class ManagedSetupRecoveryProbeTests
{
    /// <summary>Foreign root, state and owned paths remain read-only mismatches.</summary>
    [Theory]
    [InlineData("root")]
    [InlineData("state")]
    [InlineData("paths")]
    public async Task ForeignMarkersNeverAuthorizeRecovery(string field)
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var marker = fixture.Marker();
        marker = field switch
        {
            "root" => marker with { ManagedRootIdentity = fixture.Root + "-foreign" },
            "state" => marker with { StatePathIdentity = fixture.StatePath + "-foreign" },
            "paths" => marker with { OwnedPaths = ["managed", "foreign.json"] },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        TestFiles.WriteAllBytes(fixture.MarkerPath, fixture.Codec.Encode(marker).ToArray());
        var before = await fixture.SnapshotAsync();
        var result = await fixture.Probe().ObserveAsync(fixture.Root, fixture.StatePath, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryFactKind.IdentityMismatch, result.Kind);
        Assert.Null(result.Transaction);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    /// <summary>Empty, malformed, exact and oversized raw markers retain their typed observation outcome.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(65_535, true)]
    [InlineData(65_536, true)]
    [InlineData(65_537, false)]
    public async Task ProbePreservesMarkerLengthBoundaries(int length, bool exact)
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var codec = new SyntheticMarkerCodec { PadTo = length };
        TestFiles.WriteAllBytes(fixture.MarkerPath, length == 0 ? [] : codec.Encode(fixture.Marker()).ToArray());
        var before = await fixture.SnapshotAsync();
        var fact = await fixture.Probe().ObserveAsync(fixture.Root, fixture.StatePath, TestContext.Current.CancellationToken);
        Assert.Equal(exact ? ManagedSetupRecoveryFactKind.Exact : ManagedSetupRecoveryFactKind.Malformed, fact.Kind);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    /// <summary>Custody failures retain their exact typed categories without creating any entry.</summary>
    [Theory]
    [InlineData((int)WindowsStableCustodyIssue.AccessDenied, ManagedSetupRecoveryFactKind.AccessDenied)]
    [InlineData((int)WindowsStableCustodyIssue.Changed, ManagedSetupRecoveryFactKind.Changed)]
    [InlineData((int)WindowsStableCustodyIssue.Contended, ManagedSetupRecoveryFactKind.Unavailable)]
    [InlineData((int)WindowsStableCustodyIssue.Unavailable, ManagedSetupRecoveryFactKind.Unavailable)]
    public async Task FailedObservationNeverMutates(int issue, ManagedSetupRecoveryFactKind expected)
    {
        using var fixture = new SetupFixture();
        var before = await fixture.SnapshotAsync();
        var probe = new FileSystemManagedSetupRecoveryProbe(fixture.Codec, SetupFixture.Ceiling, SetupFixture.Ceiling,
            (_, _) => WindowsStableCustodyResult.Failure((WindowsStableCustodyIssue)issue), _ => true);
        var fact = await probe.ObserveAsync(fixture.Root, fixture.StatePath, TestContext.Current.CancellationToken);
        Assert.Equal(expected, fact.Kind);
        Assert.Null(fact.Transaction);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }
}
