// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

/// <summary>Exercises physical held writes, topology races, non-replacing promotion and exact cleanup.</summary>
public sealed class RepositoryWriteCustodyTests
{
    /// <summary>Held verified members deny rewrites and a late child prevents exact promotion and cleanup.</summary>
    [Fact]
    public async Task InstallBlocksVerifiedFileRewriteAndRejectsLateChildBeforePromotion()
    {
        RepositoryFixture.RequireWindows();
        bool verifiedWriteBlocked = false;
        bool lateChildBlocked = true;
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            BeforePackagePromotion = staging =>
            {
                verifiedWriteBlocked = WriteWasBlocked(Path.Combine(staging, "README.txt"));
                lateChildBlocked = WriteWasBlocked(Path.Combine(staging, "late-child.txt"));
            },
        });
        var result = await fixture.InstallAsync();
        Assert.Equal(ManagedVersionInstallIssue.CleanupIncomplete, result.Issue);
        Assert.True(verifiedWriteBlocked);
        Assert.False(lateChildBlocked);
        Assert.False(Directory.Exists(fixture.VersionRoot));
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(Path.Combine(fixture.ManagedRoot, ".staging")));
    }

    /// <summary>A foreign child during cancellation survives and produces typed incomplete cleanup.</summary>
    [Fact]
    public async Task InstallReportsCleanupIncompleteWhenForeignChildPreventsExactCleanup()
    {
        RepositoryFixture.RequireWindows();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        string? foreign = null;
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            AfterPackageDirectoryCreated = directory =>
            {
                if (foreign is null)
                {
                    foreign = Path.Combine(directory, "foreign-child.txt");
                    File.WriteAllText(foreign, "foreign");
                    cancellation.Cancel();
                }
            },
        });
        var result = await fixture.Repository.InstallAsync(fixture.ManagedRoot, fixture.SourceRoot,
            fixture.Package.Candidate, cancellation.Token);
        Assert.NotNull(foreign);
        Assert.Equal(ManagedVersionInstallIssue.CleanupIncomplete, result.Issue);
        Assert.False(Directory.Exists(fixture.VersionRoot));
        Assert.Equal("foreign", await File.ReadAllTextAsync(foreign, TestContext.Current.CancellationToken));
    }

    /// <summary>Native rename cannot replace a destination created at the final topology gate.</summary>
    [Fact]
    public async Task InstallPromotionNeverReplacesLateDestination()
    {
        RepositoryFixture.RequireWindows();
        string? target = null;
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            BeforePackagePromotion = staging =>
            {
                target = Path.Combine(Directory.GetParent(Directory.GetParent(staging)!.FullName)!.FullName,
                    "versions", PackageFixture.Version.ToString());
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "owner.txt"), "destination-owner");
            },
        });
        var result = await fixture.InstallAsync();
        Assert.Equal(ManagedVersionInstallIssue.IdentityConflict, result.Issue);
        Assert.NotNull(target);
        Assert.Equal("destination-owner", await File.ReadAllTextAsync(Path.Combine(target, "owner.txt"),
            TestContext.Current.CancellationToken));
        Assert.Equal("owner.txt", Assert.Single(Directory.EnumerateFiles(target).Select(Path.GetFileName)));
        fixture.AssertEmptyStaging();
    }

    /// <summary>A nested link planted before relative child creation causes no write outside the held root.</summary>
    [Fact]
    public async Task InstallRejectsNestedJunctionRaceWithoutOutsideWriteOrPromotion()
    {
        RepositoryFixture.RequireWindows();
        string? outside = null;
        bool linkBlocked = true;
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            AfterPackageDirectoryCreated = directory =>
            {
                if (Path.GetFileName(directory) == "reference")
                {
                    try
                    {
                        Directory.CreateSymbolicLink(Path.Combine(directory, "nested"), outside!);
                        linkBlocked = false;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        Assert.Skip("Windows directory symbolic links are unavailable: " + exception.Message);
                    }
                }
            },
        });
        outside = Directory.CreateDirectory(fixture.PathFor("outside")).FullName;
        string sentinel = Path.Combine(outside, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "outside-owner", TestContext.Current.CancellationToken);
        var result = await fixture.InstallAsync();
        Assert.Equal(ManagedVersionInstallIssue.CleanupIncomplete, result.Issue);
        Assert.False(Directory.Exists(fixture.VersionRoot));
        Assert.False(linkBlocked);
        Assert.False(File.Exists(Path.Combine(outside, "data.bin")));
        Assert.Equal("outside-owner", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(Path.Combine(fixture.ManagedRoot, ".staging")));
    }

    /// <summary>A destination that physically accepts fewer bytes is rejected by the held reservation before promotion.</summary>
    [Fact]
    public async Task PhysicalShortWriteDuringExtractionFailsAndCleansExactStaging()
    {
        RepositoryFixture.RequireWindows();
        long physicallyWritten = 0;
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            WrapExtractionDestination = (path, stream) => path == "README.txt"
                ? new ShortWriteStream(stream, count => physicallyWritten += count) : stream,
        });
        var result = await fixture.InstallAsync();
        Assert.Equal(ManagedVersionInstallIssue.InvalidPayload, result.Issue);
        Assert.Equal(3, physicallyWritten);
        Assert.False(Directory.Exists(fixture.VersionRoot));
        fixture.AssertEmptyStaging();
    }

    /// <summary>Cancellation after exact staging proof removes the owned tree and propagates cancellation.</summary>
    [Fact]
    public async Task CancellationAfterVerifiedStagingCleansExactTree()
    {
        RepositoryFixture.RequireWindows();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            BeforePackagePromotion = _ => cancellation.Cancel(),
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Repository.InstallAsync(fixture.ManagedRoot, fixture.SourceRoot, fixture.Package.Candidate,
                cancellation.Token));
        Assert.False(Directory.Exists(fixture.VersionRoot));
        fixture.AssertEmptyStaging();
    }

    /// <summary>Cancellation after promotion rolls back the held final tree and preserves a reused staging name.</summary>
    [Fact]
    public async Task PromotedTreeCancellationPreservesForeignStagingReplacement()
    {
        RepositoryFixture.RequireWindows();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        string? stagingPath = null;
        string? foreign = null;
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            BeforePackagePromotion = staging => stagingPath = staging,
            AfterPromotion = _ =>
            {
                Directory.CreateDirectory(stagingPath!);
                foreign = Path.Combine(stagingPath!, "foreign.txt");
                File.WriteAllText(foreign, "foreign");
                cancellation.Cancel();
            },
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Repository.InstallAsync(fixture.ManagedRoot, fixture.SourceRoot, fixture.Package.Candidate,
                cancellation.Token));
        Assert.False(Directory.Exists(fixture.VersionRoot));
        Assert.NotNull(foreign);
        Assert.Equal("foreign", await File.ReadAllTextAsync(foreign, TestContext.Current.CancellationToken));
    }

    /// <summary>A captured plan retains the package file's sharing custody until extraction finishes.</summary>
    [Fact]
    public async Task TamperAfterPlanBeforeExtractionIsBlockedByHeldPackage()
    {
        RepositoryFixture.RequireWindows();
        string? packagePath = null;
        bool blocked = false;
        using var fixture = new RepositoryFixture(operations: new RepositoryOperations
        {
            BeforeExtraction = () => blocked = WriteWasBlocked(packagePath!),
        });
        packagePath = Path.Combine(fixture.SourceRoot, fixture.Package.Candidate.PackagePath.Value);
        var installed = await fixture.InstallAsync();
        Assert.True(blocked);
        Assert.True(installed.IsSuccess, installed.Issue.ToString());
    }

    /// <summary>Underreported physical expansion and ZIP64 overflow fail before staging exists.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedZipLengthsNeverMaterialize(bool zip64Overflow)
    {
        RepositoryFixture.RequireWindows();
        var package = PackageFixture.Create(mutatePackage: bytes => zip64Overflow
            ? PackageFixture.InflateSecondCentralEntryToLongMax(bytes)
            : PackageFixture.UnderreportEntry(bytes, "README.txt", 1));
        using var fixture = new RepositoryFixture(package);
        var result = await fixture.InstallAsync();
        Assert.Equal(zip64Overflow ? ManagedVersionInstallIssue.UnsafeArchive : ManagedVersionInstallIssue.InvalidPayload,
            result.Issue);
        Assert.False(Directory.Exists(fixture.VersionRoot));
        fixture.AssertEmptyStaging();
    }

    /// <summary>Borrowed adapter output is frozen before asynchronous extraction and admission writes.</summary>
    [Fact]
    public async Task BoundedAdmissionBytesAreCopiedBeforeExtraction()
    {
        RepositoryFixture.RequireWindows();
        byte[]? returnedBytes = null;
        var encoder = new AdmissionCodec();
        var codec = new AdmissionCodec
        {
            Encoder = admission =>
            {
                returnedBytes = encoder.Encode(admission).ToArray();
                return returnedBytes;
            },
        };
        using var fixture = new RepositoryFixture(codec: codec, operations: new RepositoryOperations
        {
            BeforeExtraction = () => Array.Fill(returnedBytes!, (byte)'x'),
        });
        var result = await fixture.InstallAsync();
        Assert.True(result.IsSuccess, result.Issue.ToString());
        Assert.Equal(1, (await fixture.InventoryAsync(result.Admission!)).Inventory!.HealthyCount);
    }

    private static bool WriteWasBlocked(string path)
    {
        try
        {
            File.WriteAllText(path, "attacker");
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}

internal sealed class ShortWriteStream(Stream physicalStream, Action<int> record) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => physicalStream.Length;
    public override long Position { get => physicalStream.Position; set => throw new NotSupportedException(); }
    public override void Flush() => physicalStream.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
    {
        physicalStream.Write(buffer, offset, count / 2);
        record(count / 2);
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await physicalStream.WriteAsync(buffer[..(buffer.Length / 2)], cancellationToken);
        record(buffer.Length / 2);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            physicalStream.Dispose();
        }
        base.Dispose(disposing);
    }
}
