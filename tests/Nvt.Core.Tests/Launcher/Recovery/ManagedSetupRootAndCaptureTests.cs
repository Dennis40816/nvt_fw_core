// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Repository;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Checks exact root admission and executable capture through the production adapters.</summary>
public sealed class ManagedSetupRootAndCaptureTests
{
    /// <summary>Root observation distinguishes absence, existing directories and exact setup residue.</summary>
    [Fact]
    public async Task RootProbeClassifiesAbsentPresentAndExactResidue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Fixed-drive root admission is required.");
        using var fixture = new SetupFixture();
        var probe = new FileSystemManagedInstallationRootProbe();
        Assert.Equal(ManagedInstallationRootStatus.Absent, (await probe.ObserveAsync(fixture.Root, TestContext.Current.CancellationToken)).Status);
        Directory.CreateDirectory(fixture.Root);
        Assert.Equal(ManagedInstallationRootStatus.Present, (await probe.ObserveAsync(fixture.Root, TestContext.Current.CancellationToken)).Status);
        Directory.Delete(fixture.Root);
        SetupFixture.Write(fixture.MarkerPath, "foreign");
        Assert.Equal(ManagedInstallationRootStatus.Residue, (await probe.ObserveAsync(fixture.Root, TestContext.Current.CancellationToken)).Status);
        File.Delete(fixture.MarkerPath);
        Directory.CreateDirectory(fixture.StagingPath);
        Assert.Equal(ManagedInstallationRootStatus.Residue, (await probe.ObserveAsync(fixture.Root, TestContext.Current.CancellationToken)).Status);
        Directory.Delete(fixture.StagingPath);
        SetupFixture.Write(fixture.Root, "foreign");
        Assert.Equal(ManagedInstallationRootStatus.InvalidDestination, (await probe.ObserveAsync(fixture.Root, TestContext.Current.CancellationToken)).Status);
    }

    /// <summary>Unsafe and non-exact spellings never admit a managed destination.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData(@"\\?\C:\managed")]
    [InlineData(@"C:\managed:stream")]
    [InlineData(@"C:\managed\..\other")]
    public void RootAdmissionRejectsUnsafeSpelling(string? path)
    {
        Assert.Equal(ManagedInstallationRootStatus.InvalidDestination,
            FileSystemManagedInstallationRootProbe.AdmitRoot(path, out string normalized));
        Assert.Empty(normalized);
    }

    /// <summary>Held capture copies exact bytes and reports Launcher drift after review.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmbeddedCapturePreservesExactBytesAndRejectsLauncherDrift(bool changeLauncher)
    {
        SetupFixture.RequireNative();
        using var fixture = new SetupFixture();
        string launcher = Path.Combine(fixture.Parent, "launcher.exe");
        byte[] bytes = RepositoryFixture.PortableExecutable();
        TestFiles.WriteAllBytes(launcher, bytes);
        var source = new EmbeddedManagedDistributionPayloadSource(launcher,
            () => new MemoryStream("descriptor"u8.ToArray(), writable: false),
            () => new MemoryStream(new byte[] { 1, 2, 3 }, writable: false), ContractFixture.Descriptor,
            new DescriptorReader(), SetupFixture.Ceiling, 200_000_000);
        var inspected = await source.InspectAsync(TestContext.Current.CancellationToken);
        Assert.True(inspected.IsSuccess, inspected.Issue.ToString());
        if (changeLauncher) TestFiles.WriteAllBytes(launcher, [.. bytes, 0xA5]);
        var captured = await source.CaptureExactAsync(inspected.Identity!, TestContext.Current.CancellationToken);
        if (changeLauncher)
        {
            Assert.Null(captured.Capture);
            Assert.Equal(ManagedDistributionPayloadIssue.Changed, captured.Issue);
        }
        else
        {
            Assert.True(captured.IsSuccess, captured.Issue.ToString());
            using var capture = captured.Capture!;
            var content = Assert.IsAssignableFrom<IManagedDistributionPayloadContent>(capture);
            string launcherCopy = Path.Combine(fixture.Parent, "launcher-copy.exe");
            string bootstrapCopy = Path.Combine(fixture.Parent, "bootstrap-copy.exe");
            await content.CopyDistributionLauncherAsync(launcherCopy, TestContext.Current.CancellationToken);
            await content.CopyBootstrapAsync(bootstrapCopy, TestContext.Current.CancellationToken);
            Assert.Equal(bytes, await SetupFixture.ReadAsync(launcherCopy));
            Assert.Equal(new byte[] { 1, 2, 3 }, await SetupFixture.ReadAsync(bootstrapCopy));
        }
    }

    private sealed class DescriptorReader : IManagedSetupPayloadDescriptorReader
    {
        public ManagedSetupPayloadDescriptor ReadValidated(ReadOnlyMemory<byte> exactBytes) =>
            new("1.0.4", new string('c', 40), new(SetupFixture.Layout.BootstrapFileName,
                3, SetupFixture.Hash(new byte[] { 1, 2, 3 }), new string('c', 40)));
    }
}
