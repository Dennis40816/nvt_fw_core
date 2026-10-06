// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Windows;
using Nvt.Core.Tests.Files;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Windows;

/// <summary>Exercises held executable identity, PE admission and final native launch validation.</summary>
public sealed class StableManagedExecutableLaunchLeaseTests
{
    private static readonly WindowsStableTreeLimits Limits =
        WindowsStableTreeLimits.ForInstalledVersion(4097, 4096, 536870912, 4096);

    /// <summary>The descriptor-relative executable cannot be swapped until its application lease is released.</summary>
    [Fact]
    public async Task AcquiredApplicationLeaseDeniesExecutableSwapUntilReleased()
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] bytes = PortableExecutable();
        string executable = workspace.Write("version/" + ContractFixture.ApplicationPath, bytes);
        var acquired = await StableManagedExecutableLaunchLease.TryCreateAsync(Acquire(workspace.PathFor("version")),
            ContractFixture.Descriptor.ApplicationExecutableRelativePath, bytes.Length, Hash(bytes), 200000000,
            TestContext.Current.CancellationToken);
        using IManagedExecutableLaunchLease lease = Assert.IsAssignableFrom<IManagedExecutableLaunchLease>(acquired.Lease);
        Assert.True(acquired.IsAcquired, acquired.Issue.ToString());
        Assert.Equal(executable, lease.ExecutablePath);
        Assert.Equal(Path.GetDirectoryName(executable), lease.WorkingDirectory);
        Assert.Throws<IOException>(() => File.Move(executable, executable + ".displaced"));
        Assert.Throws<IOException>(() => File.WriteAllBytes(executable, PortableExecutable(marker: 9)));
        Assert.Throws<IOException>(() => Directory.Move(workspace.PathFor("version"), workspace.PathFor("replacement")));
        Assert.True(lease.TryValidateForStart());
        lease.Dispose();
        File.Move(executable, executable + ".displaced");
        File.Move(executable + ".displaced", executable);
        Assert.True(File.Exists(executable));
    }

    /// <summary>A child inserted after proof fails closed and releases all transferred custody.</summary>
    [Fact]
    public async Task AddedChildAfterManifestProofFailsClosedAndReleasesCustody()
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] bytes = PortableExecutable();
        string executable = workspace.Write("version/" + ContractFixture.LauncherPath, bytes);
        WindowsStablePathCustody custody = Acquire(workspace.PathFor("version"));
        string unexpected = workspace.PathFor("version/unexpected.dll");
        File.WriteAllText(unexpected, "foreign");
        var acquired = await StableManagedExecutableLaunchLease.TryCreateFromVerifiedTreeAsync(custody,
            ContractFixture.Descriptor.LauncherExecutableRelativePath, bytes.Length, Hash(bytes), 200000000,
            TestContext.Current.CancellationToken);
        Assert.Null(acquired.Lease);
        Assert.Equal(ManagedExecutableLaunchIssue.UnsafePath, acquired.Issue);
        File.Delete(unexpected);
        File.Move(executable, executable + ".released");
        Directory.Move(workspace.PathFor("version"), workspace.PathFor("released"));
    }

    /// <summary>Held ancestors block replacement during the admission-to-leaf-open interval.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AcquiredLauncherLeaseClosesAncestorAdmissionRace(int ancestorLevels)
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] bytes = PortableExecutable();
        string executable = workspace.Write("version/" + ContractFixture.LauncherPath, bytes);
        string ancestor = executable;
        for (int level = 0; level < ancestorLevels; level++)
        {
            ancestor = Path.GetDirectoryName(ancestor)!;
        }
        bool blocked = false;
        var acquired = await StableManagedExecutableLaunchLease.TryAcquireAsync(
            executable, bytes.Length, Hash(bytes), 200000000, () =>
            {
                try
                {
                    Directory.Move(ancestor, ancestor + ".displaced");
                }
                catch (IOException)
                {
                    blocked = true;
                }
            }, TestContext.Current.CancellationToken);
        using var lease = acquired.Lease;
        Assert.True(blocked);
        Assert.True(acquired.IsAcquired, acquired.Issue.ToString());
        Assert.True(lease!.TryValidateForStart());
    }

    /// <summary>A same-size replacement before acquisition must match the exact original hash.</summary>
    [Fact]
    public async Task SameLengthExecutableSwapFailsContentAdmission()
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] expected = PortableExecutable();
        string executable = workspace.Write("version/probe.exe", PortableExecutable(marker: 9));
        var acquired = await StableManagedExecutableLaunchLease.TryAcquireAsync(executable, expected.Length,
            Hash(expected), 200000000, TestContext.Current.CancellationToken);
        Assert.Null(acquired.Lease);
        Assert.Equal(ManagedExecutableLaunchIssue.Tampered, acquired.Issue);
        File.Move(executable, executable + ".released");
    }

    /// <summary>Full verified content and outer-launcher topology retain distinct admission responsibilities.</summary>
    [Fact]
    public async Task VerifiedTreeDoesNotRehashContentButOuterLauncherDoes()
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] bytes = PortableExecutable();
        string executable = workspace.Write("version/probe.exe", bytes);
        string other = workspace.Write("version/other.dll", [1]);
        File.WriteAllBytes(other, [9]);
        string anotherDigest = new string('a', 64);
        var fullyVerified = await StableManagedExecutableLaunchLease.TryCreateFromVerifiedTreeAsync(
            Acquire(workspace.PathFor("version")), "probe.exe", bytes.Length, anotherDigest, 200000000,
            TestContext.Current.CancellationToken);
        using (fullyVerified.Lease)
        {
            Assert.True(fullyVerified.IsAcquired);
            Assert.True(fullyVerified.Lease!.TryValidateForStart());
            Assert.Throws<IOException>(() => File.WriteAllBytes(other, [2]));
        }
        var outer = await StableManagedExecutableLaunchLease.TryCreateAsync(Acquire(workspace.PathFor("version")),
            "probe.exe", bytes.Length, anotherDigest, 200000000, TestContext.Current.CancellationToken);
        Assert.Null(outer.Lease);
        Assert.Equal(ManagedExecutableLaunchIssue.Tampered, outer.Issue);
        File.WriteAllBytes(executable, bytes);
    }

    /// <summary>Frozen DOS and PE predicates keep minimum-size and header-offset boundaries.</summary>
    [Theory]
    [InlineData(63, 64, false)]
    [InlineData(64, 64, false)]
    [InlineData(65, 64, false)]
    [InlineData(67, 64, false)]
    [InlineData(68, 64, true)]
    [InlineData(69, 64, true)]
    [InlineData(68, 63, false)]
    [InlineData(69, 65, true)]
    [InlineData(68, 65, false)]
    [InlineData(68, -1, false)]
    [InlineData(68, 2147483647, false)]
    public async Task PortableExecutableHeaderBoundaries(int length, int offset, bool valid)
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] bytes = PortableExecutable(length, offset);
        string executable = workspace.Write("probe.exe", bytes);
        var acquired = await StableManagedExecutableLaunchLease.TryAcquireAsync(executable, length, Hash(bytes),
            200000000, TestContext.Current.CancellationToken);
        using (acquired.Lease)
        {
            Assert.Equal(valid, acquired.IsAcquired);
            Assert.Equal(valid ? ManagedExecutableLaunchIssue.None : ManagedExecutableLaunchIssue.Tampered, acquired.Issue);
        }
        File.WriteAllBytes(executable, bytes);
    }

    /// <summary>Invalid signatures reject before accepting a digest, with no surviving handle.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(66)]
    [InlineData(67)]
    public async Task PortableExecutableSignatureChangesAreTampered(int index)
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] bytes = PortableExecutable();
        bytes[index] ^= 0xff;
        string executable = workspace.Write("probe.exe", bytes);
        var measured = await StableManagedExecutableLaunchLease.TryMeasureAsync(executable, 200000000,
            TestContext.Current.CancellationToken);
        Assert.False(measured.IsMeasured);
        Assert.Equal(ManagedExecutableLaunchIssue.Tampered, measured.Issue);
        File.Move(executable, executable + ".released");
    }

    /// <summary>Measured reads admit the frozen launcher ceiling and reject the immediately larger sparse PE.</summary>
    [Theory]
    [InlineData(199999999, true)]
    [InlineData(200000000, true)]
    [InlineData(200000001, false)]
    public async Task MeasuredExecutableCeilingIsInclusive(long length, bool accepted)
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        string executable = workspace.Write("probe.exe", PortableExecutable());
        using (FileStream file = new(executable, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            file.SetLength(length);
        }
        var measured = await StableManagedExecutableLaunchLease.TryMeasureAsync(executable, 200000000,
            TestContext.Current.CancellationToken);
        Assert.Equal(accepted, measured.IsMeasured);
        Assert.Equal(accepted ? length : 0, measured.Length);
        Assert.Equal(accepted ? 64 : 0, measured.Sha256.Length);
        File.Move(executable, executable + ".released");
    }

    /// <summary>Every measuring and acquisition entry point requires an explicit positive ceiling.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecutableLimitsMustBePositive(long maximum)
    {
        var token = TestContext.Current.CancellationToken;
        var first = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await StableManagedExecutableLaunchLease.TryAcquireAsync("missing", 68, new string('a', 64), maximum, token));
        Assert.Equal("maximumExecutableBytes", first.ParamName);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await StableManagedExecutableLaunchLease.TryAcquireMeasuredAsync("missing", maximum, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await StableManagedExecutableLaunchLease.TryMeasureAsync("missing", maximum, token));
    }

    /// <summary>Size and missing digest failures retain frozen precedence over path resolution.</summary>
    [Theory]
    [InlineData(0, "hash")]
    [InlineData(-1, "hash")]
    [InlineData(69, "hash")]
    [InlineData(68, "")]
    [InlineData(68, " ")]
    [InlineData(68, null)]
    public async Task InvalidExpectedIdentityFailsBeforePath(long size, string? hash)
    {
        var acquired = await StableManagedExecutableLaunchLease.TryAcquireAsync("relative", size, hash!, 68,
            TestContext.Current.CancellationToken);
        Assert.Null(acquired.Lease);
        Assert.Equal(ManagedExecutableLaunchIssue.Tampered, acquired.Issue);
    }

    /// <summary>Copy keeps original held content and uses no-replace output ownership.</summary>
    [Fact]
    public async Task HeldCopyPreservesContentAndRejectsExistingDestination()
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] bytes = PortableExecutable();
        string executable = workspace.Write("probe.exe", bytes);
        var acquired = await StableManagedExecutableLaunchLease.TryAcquireMeasuredAsync(executable, 68,
            TestContext.Current.CancellationToken);
        using StableManagedExecutableLaunchLease lease = Assert.IsType<StableManagedExecutableLaunchLease>(acquired.Lease);
        string destination = workspace.PathFor("copied.exe");
        await lease.CopyToAsync(destination, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, File.ReadAllBytes(destination));
        await Assert.ThrowsAsync<IOException>(async () =>
            await lease.CopyToAsync(destination, TestContext.Current.CancellationToken));
        Assert.True(lease.TryValidateForStart());
    }

    /// <summary>Cancellation after ownership transfer releases the complete tree and propagates.</summary>
    [Fact]
    public async Task CancelledLeaseCreationConsumesAndReleasesOwnedCustody()
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] bytes = PortableExecutable();
        string executable = workspace.Write("version/probe.exe", bytes);
        WindowsStablePathCustody custody = Acquire(workspace.PathFor("version"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await StableManagedExecutableLaunchLease.TryCreateAsync(custody, "probe.exe", 68, Hash(bytes),
                200000000, cancellation.Token));
        File.WriteAllBytes(executable, bytes);
        Directory.Move(workspace.PathFor("version"), workspace.PathFor("released"));
    }

    /// <summary>The copied framework-dependent shared probe starts while exact executable custody remains held.</summary>
    [Fact]
    public async Task SharedProbeStartsFromDescriptorPathWhileLeaseIsHeld()
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        string root = CopyProbe(workspace);
        var acquired = await AcquireProbe(root);
        using IManagedExecutableLaunchLease lease = acquired.Lease!;
        Assert.True(acquired.IsAcquired, acquired.Issue.ToString());
        string marker = workspace.PathFor("started.txt");
        using Process? process = StartAtValidation(lease, marker, beforeValidation: null);
        Assert.NotNull(process);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, process.ExitCode);
            string[] output = await File.ReadAllLinesAsync(marker, TestContext.Current.CancellationToken);
            Assert.Equal(["held-start", lease.WorkingDirectory], output);
            Assert.True(lease.TryValidateForStart());
            Assert.Throws<IOException>(() => File.Move(lease.ExecutablePath, lease.ExecutablePath + ".swapped"));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    /// <summary>A deterministic late-child gate rejects the same runnable probe before creating any child.</summary>
    [Fact]
    public async Task SharedProbeLateChildFailsFinalStartValidation()
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        string root = CopyProbe(workspace);
        var acquired = await AcquireProbe(root);
        using IManagedExecutableLaunchLease lease = acquired.Lease!;
        Assert.True(acquired.IsAcquired);
        string marker = workspace.PathFor("never-started.txt");
        using Process? process = StartAtValidation(lease, marker,
            () => File.WriteAllText(Path.Combine(root, "late-child.dll"), "foreign"));
        Assert.Null(process);
        Assert.False(lease.TryValidateForStart());
        Assert.False(File.Exists(marker));
        lease.Dispose();
        Directory.Move(root, root + ".released");
    }

    /// <summary>Transferred custody is released when either creation path receives a nonpositive ceiling.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public async Task TransferredCustodyRejectsNonpositiveExecutableLimit(long maximum, bool alreadyVerified)
    {
        RequireWindows();
        using var workspace = new TestWorkspace();
        byte[] bytes = PortableExecutable();
        string executable = workspace.Write("version/probe.exe", bytes);
        WindowsStablePathCustody custody = Acquire(workspace.PathFor("version"));
        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            if (alreadyVerified)
            {
                await StableManagedExecutableLaunchLease.TryCreateFromVerifiedTreeAsync(custody, "probe.exe",
                    68, Hash(bytes), maximum, TestContext.Current.CancellationToken);
            }
            else
            {
                await StableManagedExecutableLaunchLease.TryCreateAsync(custody, "probe.exe", 68, Hash(bytes),
                    maximum, TestContext.Current.CancellationToken);
            }
        });
        Assert.Equal("maximumExecutableBytes", error.ParamName);
        File.WriteAllBytes(executable, bytes);
        Directory.Move(workspace.PathFor("version"), workspace.PathFor("released"));
    }

    private static void RequireWindows() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");

    private static WindowsStablePathCustody Acquire(string root) =>
        Assert.IsType<WindowsStablePathCustody>(WindowsStablePathCustody.TryAcquireImmutableTree(
            root, Limits, TestContext.Current.CancellationToken).Custody);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] PortableExecutable(int length = 68, int offset = 64, byte marker = 2)
    {
        byte[] bytes = new byte[length];
        if (length >= 2)
        {
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
        }
        if (length > 2)
        {
            bytes[2] = marker;
        }
        if (length >= 64)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c), offset);
        }
        if (offset >= 64 && offset <= length - 4)
        {
            "PE\0\0"u8.CopyTo(bytes.AsSpan(offset));
        }
        return bytes;
    }

    private static string CopyProbe(TestWorkspace workspace)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "probe");
        string root = Directory.CreateDirectory(workspace.PathFor("version")).FullName;
        string target = Directory.CreateDirectory(Path.Combine(root, "launcher")).FullName;
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
        File.Move(Path.Combine(target, "Nvt.Core.TestProbe.exe"),
            Path.Combine(root, ContractFixture.Descriptor.LauncherExecutableRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        return root;
    }

    private static async ValueTask<ManagedExecutableLaunchLeaseResult> AcquireProbe(string root)
    {
        string path = Path.Combine(root, ContractFixture.LauncherPath.Replace('/', Path.DirectorySeparatorChar));
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        ManagedLauncherIdentity identity = ManagedLauncherIdentity.Create(ContractFixture.Descriptor, 200000000,
            ContractFixture.App100, "synthetic-admission", ContractFixture.ManifestSha, ContractFixture.App100,
            1, ContractFixture.LauncherPath, bytes.Length, Hash(bytes));
        return await StableManagedExecutableLaunchLease.TryCreateAsync(Acquire(root), identity.ExecutableRelativePath,
            identity.Size, identity.Sha256, 200000000, TestContext.Current.CancellationToken);
    }

    private static Process? StartAtValidation(IManagedExecutableLaunchLease lease, string marker, Action? beforeValidation)
    {
        beforeValidation?.Invoke();
        if (!lease.TryValidateForStart())
        {
            return null;
        }
        var info = new ProcessStartInfo
        {
            FileName = lease.ExecutablePath,
            WorkingDirectory = lease.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("--mode");
        info.ArgumentList.Add("arguments-environment");
        info.ArgumentList.Add("--marker");
        info.ArgumentList.Add(marker);
        info.ArgumentList.Add("--text");
        info.ArgumentList.Add("held-start");
        return Process.Start(info);
    }
}
