// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

/// <summary>Checks held-root installation progress and compatibility with existing callers.</summary>
public sealed class RepositoryProgressTests
{
    /// <summary>Each measured stage starts at zero, completes its own total, and precedes the next stage.</summary>
    [Fact]
    public async Task HeldRootReportsMeasuredStagesInOrder()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture(CreatePackage());
        var progress = new InstallationProgress();

        ManagedVersionInstallResult result = await MaterializeAsync(fixture, progress);

        Assert.True(result.IsSuccess, result.Issue.ToString());
        Assert.Equal(new[]
        {
            ManagedFirstInstallationProgressStage.ReadingPackage,
            ManagedFirstInstallationProgressStage.VerifyingPackage,
            ManagedFirstInstallationProgressStage.InstallingPackage,
            ManagedFirstInstallationProgressStage.VerifyingInstallation,
        }, progress.Events.Where((item, index) => index == 0 ||
            progress.Events[index - 1].Stage != item.Stage).Select(item => item.Stage));
        long payloadBytes = fixture.Package.Files.Sum(pair => pair.Value.LongLength);
        AssertStage(progress, ManagedFirstInstallationProgressStage.ReadingPackage,
            fixture.Package.PackageBytes.LongLength);
        AssertStage(progress, ManagedFirstInstallationProgressStage.VerifyingPackage, payloadBytes);
        AssertStage(progress, ManagedFirstInstallationProgressStage.InstallingPackage,
            fixture.Package.ExpandedBytes);
        AssertStage(progress, ManagedFirstInstallationProgressStage.VerifyingInstallation, payloadBytes);
        fixture.AssertEmptyStaging();
    }

    /// <summary>One reporting repository restarts its stage counters on every call and leaves its source silent.</summary>
    [Fact]
    public async Task ReportingRepositoryResetsStageCountersOnEveryCall()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture(CreatePackage());
        var progress = new InstallationProgress();
        FileSystemManagedVersionRepository reporting = fixture.Repository.WithInstallationProgress(progress);
        Assert.NotSame(fixture.Repository, reporting);
        Assert.Same(reporting, reporting.WithInstallationProgress(null));

        Assert.True((await MaterializeWithAsync(fixture, reporting)).IsSuccess);
        int firstRun = progress.Events.Count;
        Assert.True((await MaterializeWithAsync(fixture, reporting)).IsSuccess);

        // The second call finds the installed payload. It still reads and verifies the package, then verifies the install.
        ManagedFirstInstallationProgress[] second = [.. progress.Events.Skip(firstRun)];
        Assert.Equal(new[]
        {
            ManagedFirstInstallationProgressStage.ReadingPackage,
            ManagedFirstInstallationProgressStage.VerifyingPackage,
            ManagedFirstInstallationProgressStage.VerifyingInstallation,
        }, second.Select(item => item.Stage).Distinct());
        foreach (ManagedFirstInstallationProgressStage stage in second.Select(item => item.Stage).Distinct())
        {
            ManagedFirstInstallationProgress[] events = [.. second.Where(item => item.Stage == stage)];
            Assert.Equal(0, events[0].CompletedWork);
            Assert.Equal(events[0].TotalWork, events[^1].CompletedWork);
        }

        progress.Events.Clear();
        Assert.True((await MaterializeWithAsync(fixture, fixture.Repository)).IsSuccess);
        Assert.Empty(progress.Events);
    }

    /// <summary>Explicit null and active progress preserve the public caller's result and every installed byte.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalProgressPreservesExistingInstallation(bool observeProgress)
    {
        RepositoryFixture.RequireWindows();
        PackageFixture package = CreatePackage();
        using var baseline = new RepositoryFixture(package);
        using var observed = new RepositoryFixture(package);
        ManagedVersionInstallResult expected = await baseline.InstallAsync();

        ManagedVersionInstallResult actual = await MaterializeAsync(observed,
            observeProgress ? new InstallationProgress() : null);

        Assert.True(expected.IsSuccess, expected.Issue.ToString());
        Assert.Equal(expected, actual);
        string[] expectedPaths = RelativeFiles(baseline.VersionRoot);
        Assert.Equal(expectedPaths, RelativeFiles(observed.VersionRoot));
        foreach (string path in expectedPaths)
        {
            BoundedReadResult expectedFile = await BoundedFileReader.ReadFileAsync(
                Path.Combine(baseline.VersionRoot, path), allowedRoots: null,
                baseline.Limits.MaximumExpandedBytes, FileCaptureMode.CaptureBytes,
                TestContext.Current.CancellationToken);
            BoundedReadResult actualFile = await BoundedFileReader.ReadFileAsync(
                Path.Combine(observed.VersionRoot, path), allowedRoots: null,
                observed.Limits.MaximumExpandedBytes, FileCaptureMode.CaptureBytes,
                TestContext.Current.CancellationToken);
            Assert.Equal(expectedFile.Bytes, actualFile.Bytes);
        }
        baseline.AssertEmptyStaging();
        observed.AssertEmptyStaging();
    }

    /// <summary>A matching existing installation reports verification without inventing extraction work.</summary>
    [Fact]
    public async Task ExistingPayloadReportsVerificationWithoutInstallation()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        Assert.True((await fixture.InstallAsync()).IsSuccess);
        var progress = new InstallationProgress();

        ManagedVersionInstallResult result = await MaterializeAsync(fixture, progress);

        Assert.True(result.IsSuccess, result.Issue.ToString());
        Assert.True(result.WasAlreadyInstalled);
        Assert.Equal(new[]
        {
            ManagedFirstInstallationProgressStage.ReadingPackage,
            ManagedFirstInstallationProgressStage.VerifyingPackage,
            ManagedFirstInstallationProgressStage.VerifyingInstallation,
        }, progress.Events.Where((item, index) => index == 0 ||
            progress.Events[index - 1].Stage != item.Stage).Select(item => item.Stage));
        fixture.AssertEmptyStaging();
    }

    /// <summary>A changed package ends at the measured read and reports no verification or installation stage.</summary>
    [Fact]
    public async Task PackageMismatchDoesNotReportLaterStages()
    {
        RepositoryFixture.RequireWindows();
        using var fixture = new RepositoryFixture();
        byte[] bytes = fixture.Package.PackageBytes.ToArray();
        bytes[^1] ^= 1;
        await using (var stream = new FileStream(Path.Combine(fixture.SourceRoot,
            Assert.IsType<string>(fixture.Package.Candidate.PackagePath.Value)),
            FileMode.Open, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(bytes, TestContext.Current.CancellationToken);
        }
        var progress = new InstallationProgress();

        ManagedVersionInstallResult result = await MaterializeAsync(fixture, progress);

        Assert.Equal(ManagedVersionInstallIssue.PackageMismatch, result.Issue);
        AssertStage(progress, ManagedFirstInstallationProgressStage.ReadingPackage, bytes.LongLength);
        Assert.All(progress.Events, item =>
            Assert.Equal(ManagedFirstInstallationProgressStage.ReadingPackage, item.Stage));
        Assert.False(Directory.Exists(fixture.VersionRoot));
        fixture.AssertEmptyStaging();
    }

    /// <summary>Installed verification measures declared payload bytes and filters duplicate percentages without native custody.</summary>
    [Fact]
    public async Task InstalledProofReportsDeclaredPayloadCounters()
    {
        using var fixture = new RepositoryFixture(CreatePackage());
        ManagedVersionAdmission admission = await fixture.SeedInstalledAsync();
        var proof = new InstalledPayloadProof(fixture.Repository, ContractFixture.Descriptor,
            fixture.Package.Policy, fixture.Limits, fixture.Package.Verifier());
        var progress = new InstallationProgress();

        var damage = await proof.VerifyAsync(fixture.VersionRoot, admission,
            TestContext.Current.CancellationToken, MeasuredFirstInstallationStageProgress.Create(progress,
                ManagedFirstInstallationProgressStage.VerifyingInstallation));

        Assert.Null(damage);
        AssertStage(progress, ManagedFirstInstallationProgressStage.VerifyingInstallation,
            fixture.Package.Files.Sum(pair => pair.Value.LongLength));
        Assert.True(progress.Events.Count < fixture.Package.Files.Count);
    }

    private static ValueTask<ManagedVersionInstallResult> MaterializeAsync(RepositoryFixture fixture,
        IProgress<ManagedFirstInstallationProgress>? progress) =>
        MaterializeWithAsync(fixture, fixture.Repository.WithInstallationProgress(progress));

    private static async ValueTask<ManagedVersionInstallResult> MaterializeWithAsync(RepositoryFixture fixture,
        FileSystemManagedVersionRepository repository)
    {
        WindowsStableCustodyIssue issue = WindowsStableRelativeWriteRoot.TryAcquire(fixture.ManagedRoot,
            out WindowsStableRelativeWriteRoot? writeRoot);
        using (writeRoot)
        {
            Assert.Equal(WindowsStableCustodyIssue.None, issue);
            Assert.NotNull(writeRoot);
            return await repository
                .MaterializeVerifiedPayloadWithinHeldRootAsync(writeRoot, fixture.SourceRoot,
                    fixture.Package.Candidate, requireNew: false, TestContext.Current.CancellationToken);
        }
    }

    private static void AssertStage(InstallationProgress progress, ManagedFirstInstallationProgressStage stage,
        long total)
    {
        ManagedFirstInstallationProgress[] events = [.. progress.Events.Where(item => item.Stage == stage)];
        Assert.InRange(events.Length, 2, 101);
        Assert.Equal(0, events[0].CompletedWork);
        Assert.Equal(total, events[^1].CompletedWork);
        Assert.Equal(0, events[0].Percent);
        Assert.Equal(100, events[^1].Percent);
        Assert.All(events, item => Assert.Equal(total, item.TotalWork));
        Assert.All(events.Zip(events.Skip(1)), pair =>
            Assert.True(pair.First.Percent < pair.Second.Percent));
    }

    private static string[] RelativeFiles(string root) => [.. Directory.EnumerateFiles(root, "*",
        SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path)).Order(StringComparer.Ordinal)];

    private static PackageFixture CreatePackage()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ContractFixture.ApplicationPath] = RepositoryFixture.PortableExecutable(),
            ["large.bin"] = new byte[3 * 64 * 1024 + 17],
        };
        for (int index = 0; index < 128; index++)
        {
            files.Add($"small/{index}.bin", [1]);
        }
        return PackageFixture.Create(files);
    }

    private sealed class InstallationProgress : IProgress<ManagedFirstInstallationProgress>
    {
        internal List<ManagedFirstInstallationProgress> Events { get; } = [];
        public void Report(ManagedFirstInstallationProgress value) => Events.Add(value);
    }
}
