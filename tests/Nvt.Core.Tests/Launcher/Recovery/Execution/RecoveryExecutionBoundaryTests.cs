// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery.Execution;

/// <summary>Checks explicit positive product ceilings and the fixed empty-tree recovery mechanism.</summary>
public sealed class RecoveryExecutionBoundaryTests
{
    /// <summary>Every copied product ceiling is checked again by the concrete recovery entry point.</summary>
    [Theory]
    [InlineData("entries", 0)]
    [InlineData("entries", -1)]
    [InlineData("package", 0)]
    [InlineData("package", -1)]
    [InlineData("expanded", 0)]
    [InlineData("expanded", -1)]
    [InlineData("manifest", 0)]
    [InlineData("manifest", -1)]
    [InlineData("admission", 0)]
    [InlineData("admission", -1)]
    [InlineData("executable", 0)]
    [InlineData("executable", -1)]
    [InlineData("directories", 0)]
    [InlineData("directories", -1)]
    public async Task EveryPositiveCeilingRejectsZeroAndNegativeAtConsumption(string kind, long value)
    {
        using var fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync();
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.CreateExecutor(limits: ReplaceLimit(kind, value)));
    }

    /// <summary>Every frozen configurable ceiling retains its neighboring positive values.</summary>
    [Theory]
    [MemberData(nameof(CeilingNeighbors))]
    public async Task FrozenCeilingsRetainAdjacentValues(string kind, long value, bool accepted)
    {
        using var fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync();
        var error = Record.Exception(() => fixture.CreateExecutor(limits: ReplaceLimit(kind, value)));
        if (accepted) Assert.Null(error);
        else Assert.IsType<ArgumentOutOfRangeException>(error);
    }

    /// <summary>The seven product ceilings use their exact frozen values and one unit on each side.</summary>
    public static IEnumerable<object[]> CeilingNeighbors()
    {
        (string Kind, long Value)[] bounds = [("entries", 4096), ("package", 134_217_728),
            ("expanded", 536_870_912), ("manifest", 1_048_576), ("admission", 4096),
            ("executable", 200_000_000), ("directories", 4096)];
        foreach (var bound in bounds)
            foreach (int offset in new[] { -1, 0, 1 })
                yield return [bound.Kind, bound.Value + offset, bound.Kind != "executable" || offset <= 0];
    }

    /// <summary>The frozen executable ceiling is inclusive and checked before any limit arithmetic.</summary>
    [Theory]
    [InlineData(199_999_999, true)]
    [InlineData(200_000_000, true)]
    [InlineData(200_000_001, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(long.MaxValue, false)]
    public async Task PayloadExecutableCeilingIsInclusive(long size, bool accepted)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync(launcherSize: size, bootstrapSize: size);
        var evidence = await fixture.Executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(accepted ? ManagedSetupRecoveryEvidenceIssue.None : ManagedSetupRecoveryEvidenceIssue.Invalid, evidence.Issue);
    }

    /// <summary>A smaller explicit product executable ceiling is never silently widened to the fixed maximum.</summary>
    [Theory]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public async Task CallerExecutableCeilingIsInclusive(long size, bool accepted)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateEarlyStagingAsync(launcherSize: size, bootstrapSize: size);
        var executor = fixture.CreateExecutor(limits: PackageFixture.FrozenLimits with { MaximumExecutableBytes = 10 });
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(accepted ? ManagedSetupRecoveryEvidenceIssue.None : ManagedSetupRecoveryEvidenceIssue.Invalid, evidence.Issue);
    }

    /// <summary>The three fixed skeleton ceilings admit zero files/bytes and no directory beyond the original bound.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void RecoverySkeletonHasExactZeroFileAndByteBounds(int directories)
    {
        var limits = WindowsStableTreeLimits.ForRecoverySkeleton(directories);
        limits.Validate();
        Assert.True(limits.Allows(0, directories, 0));
        Assert.Equal(directories > 0, limits.Allows(0, directories - 1, 0));
        Assert.False(limits.Allows(0, directories + 1, 0));
        Assert.False(limits.Allows(1, directories, 0));
        Assert.False(limits.Allows(0, directories, 1));
        Assert.False(limits.Allows(-1, directories, 0));
        Assert.False(limits.Allows(0, directories, -1));
    }

    /// <summary>Ordinary tree ceilings retain strict positivity; the recovery factory does not change that contract.</summary>
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(-1, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, -1)]
    public void OrdinaryTreeLimitsRejectNonPositiveBounds(int files, int directories, long bytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsStableTreeLimits(files, directories, bytes));

    /// <summary>Default structs and negative skeleton bounds cannot authorize an acquisition.</summary>
    [Fact]
    public void InvalidMechanismBoundsFailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => default(WindowsStableTreeLimits).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsStableTreeLimits.ForRecoverySkeleton(-1));
    }

    /// <summary>The retained authority's relative-path mechanism keeps its frozen inclusive character bound.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(511, true)]
    [InlineData(512, true)]
    [InlineData(513, false)]
    public void RelativePathCharacterBoundIsInclusive(int characters, bool accepted) =>
        Assert.Equal(accepted, ContractValidation.IsSafeRelativePath(new string('a', characters)));

    /// <summary>Installed reservations preserve exact file, directory and byte limits independently.</summary>
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void InstalledProofBoundsAreInclusive(int offset, bool accepted)
    {
        var limits = WindowsStableTreeLimits.ForInstalledVersion(4097, 4096, 536_870_912, 4096);
        Assert.Equal(accepted, limits.Allows(4097 + offset, 4096, 536_875_008));
        Assert.Equal(accepted, limits.Allows(4097, 4096 + offset, 536_875_008));
        Assert.Equal(accepted, limits.Allows(4097, 4096, 536_875_008 + offset));
    }

    /// <summary>The concrete probe consumes exact measured file, directory and byte ceilings without widening them.</summary>
    [Theory]
    [InlineData("files", -1)]
    [InlineData("files", 0)]
    [InlineData("files", 1)]
    [InlineData("directories", -1)]
    [InlineData("directories", 0)]
    [InlineData("directories", 1)]
    [InlineData("bytes", -1)]
    [InlineData("bytes", 0)]
    [InlineData("bytes", 1)]
    public async Task ConcreteTreeProofConsumesInclusiveCeilings(string kind, int offset)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        int files = Directory.EnumerateFiles(fixture.ManagedRoot, "*", SearchOption.AllDirectories).Count();
        // CaptureTree budgets descendant directories; the held root is outside that count.
        int directories = Directory.EnumerateDirectories(fixture.ManagedRoot, "*", SearchOption.AllDirectories).Count();
        Assert.Equal(9, directories);
        var limits = kind switch
        {
            "files" => PackageFixture.FrozenLimits with { MaximumArchiveEntries = files - 4 + offset },
            "directories" => PackageFixture.FrozenLimits with { MaximumInstalledDirectories = directories - 2 + offset },
            "bytes" => PackageFixture.FrozenLimits with
            {
                MaximumExpandedBytes = fixture.Package.ExpandedBytes + offset,
                MaximumAdmissionBytes = checked((int)new FileInfo(Path.Combine(fixture.VersionRoot,
                    FileSystemManagedVersionRepository.AdmissionFileName)).Length),
            },
            _ => throw new ArgumentException("Unknown synthetic tree limit.", nameof(kind)),
        };
        var executor = fixture.CreateExecutor(limits: limits);
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(offset < 0 ? ManagedSetupRecoveryEvidenceIssue.StateUnavailable
            : ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        Assert.Equal(offset >= 0, evidence.ExecutionToken is not null);
        Assert.True(File.Exists(fixture.StatePath));
        Assert.True(File.Exists(fixture.MarkerPath));
    }

    /// <summary>A retained authority document obeys the exact caller byte ceiling before decoding.</summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task RetainedManifestByteCeilingIsInclusive(int offset, bool accepted)
    {
        RecoveryEvidenceFixture.RequireNative();
        using var fixture = await RecoveryEvidenceFixture.CreateAsync();
        await InterruptAfterAsync(fixture, 11);
        var executor = fixture.CreateExecutor(limits: PackageFixture.FrozenLimits with
        {
            MaximumManifestBytes = fixture.Package.ManifestBytes.Length + offset,
        });
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(accepted ? ManagedSetupRecoveryEvidenceIssue.None : ManagedSetupRecoveryEvidenceIssue.Invalid, evidence.Issue);
    }

    /// <summary>Public failure observations cannot claim successful evidence.</summary>
    [Fact]
    public void FailureConstructorCannotAuthorizeDeletion() => Assert.Throws<ArgumentException>(
        () => new ManagedSetupRecoveryEvidenceObservation(ManagedSetupRecoveryEvidenceIssue.None));

    internal static async Task InterruptAfterAsync(RecoveryEvidenceFixture fixture, int boundary)
    {
        using var cancellation = new CancellationTokenSource();
        var executor = fixture.CreateExecutor(count => { if (count == boundary) cancellation.Cancel(); });
        var evidence = await executor.ObserveAsync(fixture.Transaction, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryEvidenceIssue.None, evidence.Issue);
        using var lease = await fixture.ApplicationStore.TryAcquireWriteLeaseAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(ManagedSetupRecoveryExecutionPortOutcome.RecoveryRequired, await executor.ExecuteAsync(
            new(ManagedSetupRecoveryAction.RemoveIncompleteInstallation, evidence.ExecutionToken!), lease, cancellation.Token));
    }

    private static PackageVerificationLimits ReplaceLimit(string kind, long value) => kind switch
    {
        "entries" => PackageFixture.FrozenLimits with { MaximumArchiveEntries = checked((int)value) },
        "package" => PackageFixture.FrozenLimits with { MaximumPackageBytes = value },
        "expanded" => PackageFixture.FrozenLimits with { MaximumExpandedBytes = value },
        "manifest" => PackageFixture.FrozenLimits with { MaximumManifestBytes = checked((int)value) },
        "admission" => PackageFixture.FrozenLimits with { MaximumAdmissionBytes = checked((int)value) },
        "executable" => PackageFixture.FrozenLimits with { MaximumExecutableBytes = value },
        "directories" => PackageFixture.FrozenLimits with { MaximumInstalledDirectories = checked((int)value) },
        _ => throw new ArgumentException("Unknown synthetic limit.", nameof(kind)),
    };
}
