// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.IntegrationTestData;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Inclusive frozen ceilings and re-admission of receipts made with wider ceilings.</summary>
public sealed class AdmissionBoundaryTests
{
    /// <summary>Every mandatory ceiling rejects both zero and negative values.</summary>
    [Theory]
    [InlineData("maximumCatalogEntries", 0)]
    [InlineData("maximumCatalogEntries", -1)]
    [InlineData("maximumPackageBytes", 0)]
    [InlineData("maximumPackageBytes", -1)]
    [InlineData("maximumReleaseNoteBytes", 0)]
    [InlineData("maximumReleaseNoteBytes", -1)]
    [InlineData("maximumRelativePathCharacters", 0)]
    [InlineData("maximumRelativePathCharacters", -1)]
    [InlineData("maximumRegistryEntries", 0)]
    [InlineData("maximumRegistryEntries", -1)]
    [InlineData("maximumRegistryIdCharacters", 0)]
    [InlineData("maximumRegistryIdCharacters", -1)]
    public void NonpositiveLimitsAreRejected(string parameter, int value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateLimits(parameter, value));
        Assert.Equal(parameter, exception.ParamName);
    }

    /// <summary>All six minimum positive parameters remain legal.</summary>
    [Fact]
    public void MinimumPositiveLimitsAreAdmitted()
    {
        var limits = new VersionManagementLimits(1, 1, 1, 1, 1, 1);
        Assert.Equal(1, limits.MaximumCatalogEntries);
        Assert.Equal(1, limits.MaximumPackageBytes);
        Assert.Equal(1, limits.MaximumReleaseNoteBytes);
        Assert.Equal(1, limits.MaximumRelativePathCharacters);
        Assert.Equal(1, limits.MaximumRegistryEntries);
        Assert.Equal(1, limits.MaximumRegistryIdCharacters);
    }

    /// <summary>The shared path grammar cannot be widened through configuration.</summary>
    [Fact]
    public void PathConfigurationAboveSharedMaximumIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VersionManagementLimits(128, 1, 1, 513, 16, 128));
    }

    /// <summary>The catalog count ceiling includes one below and the exact maximum.</summary>
    [Theory]
    [InlineData(127)]
    [InlineData(128)]
    public void CatalogEntryCeilingIsInclusive(int count)
    {
        UpdateCatalogSnapshot catalog = UpdateCatalogSnapshot.Create(Limits, Packages(count));
        Assert.Equal(count, catalog.Versions.Count);
        Assert.Equal(ManagedAppVersion.Parse($"1.0.{count}"), catalog.Versions[0].Version);
    }

    /// <summary>Empty and excessive catalogs cannot enter Core.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(129)]
    public void InvalidCatalogCountIsRejected(int count)
    {
        Assert.Throws<ArgumentException>(() => UpdateCatalogSnapshot.Create(Limits, Packages(count)));
    }

    /// <summary>The declared byte ceiling includes its preceding value.</summary>
    [Theory]
    [InlineData(134_217_727L)]
    [InlineData(134_217_728L)]
    public void PackageByteCeilingIsInclusive(long bytes)
    {
        UpdateCatalogVersionSnapshot row = Package(bytes: bytes);
        Assert.Same(row, Assert.Single(UpdateCatalogSnapshot.Create(Limits, [row]).Versions));
    }

    /// <summary>A receipt made with a wider package limit is rechecked on catalog admission.</summary>
    [Fact]
    public void ExcessivePackageBytesAreRejectedOnReadmission()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            UpdateCatalogSnapshot.Create(Limits, [Package(bytes: 134_217_729)]));
        Assert.Equal("packageSize", exception.ParamName);
    }

    /// <summary>Declared package size must be positive at its existing admission owner.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositivePackageSizeIsRejected(long bytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Package(bytes: bytes));
    }

    /// <summary>UTF-8 note bytes, including empty text, are measured independently of characters.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(65_535)]
    [InlineData(65_536)]
    public void ReleaseNoteByteCeilingIsInclusive(int bytes)
    {
        string notes = Notes(bytes);
        UpdateCatalogVersionSnapshot row = Assert.Single(UpdateCatalogSnapshot.Create(Limits, [Package(notes: notes)]).Versions);
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(row.ReleaseNotes));
        Assert.Equal(notes, row.ReleaseNotes);
    }

    /// <summary>A wider release-note receipt is rejected using bytes and the existing literal message.</summary>
    [Fact]
    public void ExcessiveMultibyteReleaseNotesAreRejectedOnReadmission()
    {
        string notes = Notes(65_537);
        Assert.True(notes.Length < Limits.MaximumReleaseNoteBytes);
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            UpdateCatalogSnapshot.Create(Limits, [Package(notes: notes)]));
        Assert.StartsWith("Release notes exceed the byte ceiling.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Both the preceding and exact shared path lengths are legal.</summary>
    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    public void RelativePathCharacterCeilingIsInclusive(int characters)
    {
        Assert.Equal(characters, Assert.Single(UpdateCatalogSnapshot.Create(
            Limits, [Package(pathCharacters: characters)]).Versions).PackagePath.Value!.Length);
    }

    /// <summary>The shared path owner rejects one character above its fixed maximum.</summary>
    [Fact]
    public void ExcessiveRelativePathIsRejectedBySharedOwner()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Package(pathCharacters: 513));
        Assert.StartsWith("Package path is unsafe.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Stricter caller limits recheck all catalog ceilings.</summary>
    [Theory]
    [InlineData("maximumCatalogEntries", 1)]
    [InlineData("maximumPackageBytes", 41)]
    [InlineData("maximumReleaseNoteBytes", 1)]
    [InlineData("maximumRelativePathCharacters", 11)]
    public void CatalogCannotReuseWiderReceipts(string parameter, int maximum)
    {
        UpdateCatalogSnapshot wide = UpdateCatalogSnapshot.Create(Limits, [Package(1, notes: "é"), Package(2)]);
        Assert.ThrowsAny<ArgumentException>(() => UpdateCatalogSnapshot.Create(CreateLimits(parameter, maximum), wide.Versions));
    }

    /// <summary>The registry includes the preceding and exact entry ceilings.</summary>
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void RegistryEntryCeilingIsInclusive(int count)
    {
        Assert.Equal(count, Snapshot(entries: Entries(count)).Entries.Count);
    }

    /// <summary>Empty and excessive registry publications retain the consistency failure.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void RegistryEntryCountOutsideBoundsIsRejected(int count)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Snapshot(entries: Entries(count)));
        Assert.StartsWith("Update-source registry snapshot is inconsistent.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The explicit identity ceiling includes the preceding and exact lengths.</summary>
    [Theory]
    [InlineData(127)]
    [InlineData(128)]
    public void RegistryIdCeilingIsInclusive(int characters)
    {
        Assert.Equal(characters, Snapshot(registryId: new string('r', characters)).RegistryId.Length);
    }

    /// <summary>A matching product descriptor does not bypass the identity ceiling.</summary>
    [Fact]
    public void ExcessiveRegistryIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(registryId: new string('r', 129)));
    }

    /// <summary>Registry publications made with wider limits are rechecked by the consuming factory.</summary>
    [Theory]
    [InlineData("maximumRegistryEntries", 15)]
    [InlineData("maximumRegistryIdCharacters", 127)]
    public void RegistryCannotReuseWiderReceipts(string parameter, int maximum)
    {
        UpdateSourceRegistrySnapshot wide = Snapshot(registryId: new string('r', 128), entries: Entries(16));
        Assert.Throws<ArgumentException>(() => UpdateSourceRegistrySnapshot.Create(CreateLimits(parameter, maximum),
            Descriptor(wide.RegistryId), wide.RegistryId, wide.RegistryRevision, wide.PublishedAtUtc,
            wide.CatalogPublication, wide.ContentDigest, wide.Entries));
    }

    private static VersionManagementLimits CreateLimits(string parameter, int value) => new(
        parameter == "maximumCatalogEntries" ? value : 128,
        parameter == "maximumPackageBytes" ? value : 134_217_728,
        parameter == "maximumReleaseNoteBytes" ? value : 65_536,
        parameter == "maximumRelativePathCharacters" ? value : 512,
        parameter == "maximumRegistryEntries" ? value : 16,
        parameter == "maximumRegistryIdCharacters" ? value : 128);

    private static UpdateCatalogVersionSnapshot[] Packages(int count) =>
        [.. Enumerable.Range(1, count).Select(index => Package(index))];

    private static UpdateSourceRegistryEntry[] Entries(int count) => [.. Enumerable.Range(0, count)
        .Select(index => Entry(index, index == 0 ? UpdateSourceRegistryEntryStatus.Latest : UpdateSourceRegistryEntryStatus.Available))];

    private static string Notes(int bytes) => new string('é', bytes / 2) + new string('a', bytes % 2);
}
