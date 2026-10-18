// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Checks the reusable closed-root proof and read-only lifetime adapter.</summary>
public sealed class ManagedSetupVerifierTests
{
    /// <summary>Content, seed and exact top-level inventory drift fail before repository inventory.</summary>
    [Theory]
    [InlineData("none", true)]
    [InlineData("launcher", false)]
    [InlineData("bootstrap", false)]
    [InlineData("seed", false)]
    [InlineData("extra-file", false)]
    [InlineData("extra-directory", false)]
    public async Task ClosedRootRejectsContentAndTopologyDrift(string drift, bool expected)
    {
        using var fixture = new SetupFixture();
        Directory.CreateDirectory(fixture.Root);
        using var payload = new TestPayloadCapture(SetupFixture.PayloadIdentity());
        await payload.CopyDistributionLauncherAsync(Path.Combine(fixture.Root, SetupFixture.Layout.DistributionLauncherFileName), TestContext.Current.CancellationToken);
        await payload.CopyBootstrapAsync(Path.Combine(fixture.Root, SetupFixture.Layout.BootstrapFileName), TestContext.Current.CancellationToken);
        await fixture.Repository.InstallAsync(fixture.Root, fixture.Candidate.Identity.SourceRoot, fixture.Candidate.Package, TestContext.Current.CancellationToken);
        await fixture.SeedStore(Path.Combine(fixture.Root, SetupFixture.Layout.SeedFileName)).SaveAsync(fixture.Seed, TestContext.Current.CancellationToken);
        switch (drift)
        {
            case "launcher": SetupFixture.Write(Path.Combine(fixture.Root, SetupFixture.Layout.DistributionLauncherFileName), "changed"); break;
            case "bootstrap": SetupFixture.Write(Path.Combine(fixture.Root, SetupFixture.Layout.BootstrapFileName), "changed"); break;
            case "seed": SetupFixture.Write(Path.Combine(fixture.Root, SetupFixture.Layout.SeedFileName), "changed"); break;
            case "extra-file": SetupFixture.Write(Path.Combine(fixture.Root, "foreign.txt"), "owner"); break;
            case "extra-directory": Directory.CreateDirectory(Path.Combine(fixture.Root, "foreign")); break;
        }
        var verifier = new ManagedSetupClosedRootVerifier(fixture.Repository, SetupFixture.Layout, fixture.SeedStore);
        Assert.Equal(expected, await verifier.VerifyAsync(fixture.Root, SetupFixture.PayloadIdentity(), fixture.Admission, TestContext.Current.CancellationToken));
        Assert.Equal(expected ? 1 : 0, fixture.Repository.InventoryCount);
    }

    /// <summary>Absent lifetime authority is observed without creating its lease or ancestry.</summary>
    [Theory]
    [InlineData(ManagedProcessLifetimeKind.Bootstrap)]
    [InlineData(ManagedProcessLifetimeKind.Application)]
    [InlineData(ManagedProcessLifetimeKind.Launcher)]
    public async Task LifetimeProbeReportsExitedWithoutCreatingAuthority(ManagedProcessLifetimeKind kind)
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        var before = await fixture.SnapshotAsync();
        var probe = new FileSystemManagedProcessLifetimeProbe(ContractFixture.ProtocolNames, @"Local\CoreFixture.L10");
        Assert.Equal(ManagedProcessLifetimeStatus.Exited, await probe.ObserveAsync(fixture.StatePath, kind, TestContext.Current.CancellationToken));
        Assert.Equal(before, await fixture.SnapshotAsync());
        string missing = Path.Combine(fixture.Parent, "missing", "state.json");
        Assert.Equal(ManagedProcessLifetimeStatus.Unavailable, await probe.ObserveAsync(missing, kind, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.GetDirectoryName(missing)));
    }
}
