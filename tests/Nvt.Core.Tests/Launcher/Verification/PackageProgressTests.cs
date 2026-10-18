// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Verification;

/// <summary>Checks package progress against physical reads and the admitted payload counters.</summary>
public sealed class PackageProgressTests
{
    /// <summary>The bounded package hash finishes before payload verification begins, with exact stage-local totals.</summary>
    [Fact]
    public async Task PlanReportsMeasuredReadThenPayloadVerification()
    {
        PackageFixture fixture = CreatePackage();
        using var package = new ObservedPackageStream(fixture.PackageBytes);
        var events = new List<(string Stage, long Done, long Total)>();

        ManagedPackagePlanResult result = await fixture.Verifier().WithProgress(
                (done, total) => events.Add(("read", done, total)),
                (done, total) => events.Add(("verify", done, total)))
            .CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan plan = Assert.IsType<ManagedPackagePlan>(result.Plan);

        Assert.True(result.IsSuccess, result.Issue.ToString());
        long packageBytes = fixture.PackageBytes.LongLength;
        long payloadBytes = fixture.Files.Sum(pair => pair.Value.LongLength);
        var expected = new List<(string Stage, long Done, long Total)> { ("read", 0, packageBytes) };
        for (long done = 0; done < packageBytes;)
        {
            done += Math.Min(64 * 1024, packageBytes - done);
            expected.Add(("read", done, packageBytes));
        }
        expected.Add(("verify", 0, payloadBytes));
        long verified = 0;
        foreach (byte[] bytes in fixture.Files.Values)
        {
            for (long done = 0; done < bytes.LongLength;)
            {
                long count = Math.Min(64 * 1024, bytes.LongLength - done);
                done += count;
                verified += count;
                expected.Add(("verify", verified, payloadBytes));
            }
        }
        Assert.Equal(expected, events);
        Assert.Equal(packageBytes, package.BytesRead);
        Assert.Equal(1, package.LastRequested);
        Assert.True(package.HashProbeCompletedBeforeZipRead);
        plan.Dispose();
        Assert.False(package.WasDisposed);
        Assert.True(package.CanRead);
    }

    /// <summary>Null and active callbacks preserve the existing stream reads, exact plan facts, and borrowed ownership.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalCallbacksPreservePlanAndReadBehavior(bool observeProgress)
    {
        PackageFixture fixture = CreatePackage();
        using var baseline = new ObservedPackageStream(fixture.PackageBytes);
        using var observed = new ObservedPackageStream(fixture.PackageBytes);
        var events = new List<(long Done, long Total)>();
        Action<long, long>? progress = observeProgress ? (done, total) => events.Add((done, total)) : null;
        ManagedPackagePlanResult expected = await fixture.Verifier().CreatePlanAsync(baseline, fixture.Candidate,
            TestContext.Current.CancellationToken);
        using ManagedPackagePlan expectedPlan = Assert.IsType<ManagedPackagePlan>(expected.Plan);

        ManagedPackagePlanResult actual = await fixture.Verifier().WithProgress(progress, progress)
            .CreatePlanAsync(observed, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan actualPlan = Assert.IsType<ManagedPackagePlan>(actual.Plan);

        Assert.Equal(expected.Issue, actual.Issue);
        Assert.Equal(expectedPlan.Candidate, actualPlan.Candidate);
        Assert.Equal(expectedPlan.MemberPaths, actualPlan.MemberPaths);
        Assert.Equal(expectedPlan.Manifest.Files, actualPlan.Manifest.Files);
        Assert.Equal(expectedPlan.ManifestBytes.ToArray(), actualPlan.ManifestBytes.ToArray());
        Assert.Equal(expectedPlan.ChecksumBytes.ToArray(), actualPlan.ChecksumBytes.ToArray());
        Assert.Equal(expectedPlan.ExpandedBytes, actualPlan.ExpandedBytes);
        Assert.Equal(baseline.AsyncReads, observed.AsyncReads);
        Assert.Equal(baseline.SyncReads, observed.SyncReads);
        Assert.Equal(baseline.BytesRead, observed.BytesRead);
        Assert.Equal(baseline.LastRequested, observed.LastRequested);
        Assert.Equal(baseline.MaximumRequested, observed.MaximumRequested);
        Assert.Equal(baseline.LastToken, observed.LastToken);
        Assert.Equal(baseline.HashProbeCompletedBeforeZipRead, observed.HashProbeCompletedBeforeZipRead);
        Assert.False(observed.WasDisposed);
        Assert.Equal(observeProgress, events.Count > 0);
    }

    /// <summary>A changed package reports its complete bounded read and never enters payload verification.</summary>
    [Fact]
    public async Task MismatchedPackageDoesNotReportPayloadWork()
    {
        PackageFixture fixture = CreatePackage();
        byte[] bytes = fixture.PackageBytes.ToArray();
        bytes[^1] ^= 1;
        await using var package = new MemoryStream(bytes);
        var reads = new List<(long Done, long Total)>();
        var verification = new List<(long Done, long Total)>();

        ManagedPackagePlanResult result = await fixture.Verifier().WithProgress(
                (done, total) => reads.Add((done, total)),
                (done, total) => verification.Add((done, total)))
            .CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);

        Assert.Null(result.Plan);
        Assert.Equal(ManagedVersionInstallIssue.PackageMismatch, result.Issue);
        Assert.Equal((0L, bytes.LongLength), reads[0]);
        Assert.Equal((bytes.LongLength, bytes.LongLength), reads[^1]);
        Assert.Empty(verification);
        Assert.True(package.CanRead);
    }

    /// <summary>Progress leaves the exact Files short-read, growth, final-length, and final-position checks intact.</summary>
    [Theory]
    [InlineData(FileChangeKind.ShortRead)]
    [InlineData(FileChangeKind.Growth)]
    [InlineData(FileChangeKind.Shrinkage)]
    [InlineData(FileChangeKind.PositionChanged)]
    public async Task ProgressPreservesChangedStreamFailures(FileChangeKind change)
    {
        PackageFixture fixture = CreatePackage();
        byte[] bytes = change switch
        {
            FileChangeKind.ShortRead => fixture.PackageBytes[..^1],
            FileChangeKind.Growth => [.. fixture.PackageBytes, (byte)1],
            _ => fixture.PackageBytes,
        };
        using var package = new ObservedPackageStream(bytes)
        {
            ObservedLength = fixture.PackageBytes.LongLength,
            FinalLength = change == FileChangeKind.Shrinkage ? fixture.PackageBytes.LongLength - 1 : null,
            WrongFinalPosition = change == FileChangeKind.PositionChanged,
        };
        var reads = new List<(long Done, long Total)>();
        bool verificationStarted = false;

        FileChangedDuringReadException failure = await Assert.ThrowsAsync<FileChangedDuringReadException>(async () =>
            await fixture.Verifier().WithProgress(
                    (done, total) => reads.Add((done, total)),
                    (_, _) => verificationStarted = true)
                .CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken,
                    propagateReadFailures: true));

        Assert.Equal(change, failure.ChangeKind);
        Assert.False(verificationStarted);
        Assert.NotEmpty(reads);
        Assert.All(reads, item =>
        {
            Assert.Equal(fixture.PackageBytes.LongLength, item.Total);
            Assert.InRange(item.Done, 0, item.Total);
        });
        Assert.False(package.WasDisposed);
    }

    private static PackageFixture CreatePackage() => PackageFixture.Create(new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        [ContractFixture.ApplicationPath] = [0x4d, 0x5a, 1],
        ["large.bin"] = new byte[3 * 64 * 1024 + 17],
        ["README.txt"] = "readme"u8.ToArray(),
    });
}
