// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.

using System.Globalization;
using System.Text;
using Nvt.Core.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Contracts;

public sealed class UpdateCatalogVersionSnapshotTests
{
    // Generic assertions ported through Core's guarded factory and synthetic package names.
    [Fact]
    public void PackageIdentityDoesNotIncludeConfiguredSourcePath()
    {
        UpdateCatalogVersionSnapshot version = ContractFixture.CreateSnapshot(
            version: ManagedAppVersion.Parse("0.10.6"),
            packagePath: "packages/FixtureProduct-v0.10.6-test-runtime.zip");
        Assert.Equal("packages/FixtureProduct-v0.10.6-test-runtime.zip", version.PackagePath.Value);
        Assert.DoesNotContain("C:", version.Identity, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\", version.Identity, StringComparison.Ordinal);
        Assert.Contains(ContractFixture.PackageSha, version.Identity, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizedCandidateIdentityRemainsByteIdenticalAfterASourceMove()
    {
        var before = (SourceRoot: "C:/synthetic/source-before", Package: ContractFixture.CreateSnapshot());
        var after = (SourceRoot: "D:/synthetic/source-after", Package: ContractFixture.CreateSnapshot());
        var beforeCandidate = new VerifiedUpdateCandidate(
            before.Package.Version, before.Package.Identity, before.Package.ReleaseNotes);
        var afterCandidate = new VerifiedUpdateCandidate(
            after.Package.Version, after.Package.Identity, after.Package.ReleaseNotes);

        Assert.NotEqual(before.SourceRoot, after.SourceRoot);
        const string expected = "1.0.0|packages/FixtureProduct-v1.0.0-test-runtime.zip|42|" +
            ContractFixture.PackageSha + "|" + ContractFixture.ManifestSha;
        Assert.Equal(expected, before.Package.Identity);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(after.Package.Identity));
        Assert.Equal(Encoding.UTF8.GetBytes(beforeCandidate.AdmissionIdentity),
            Encoding.UTF8.GetBytes(afterCandidate.AdmissionIdentity));
        Assert.Equal(beforeCandidate, afterCandidate);
    }

    [Fact]
    public void SnapshotPreservesMetadataAndExcludesNotificationNotesAndPublicationFromIdentity()
    {
        UpdateCatalogVersionSnapshot first = ContractFixture.CreateSnapshot();
        var publishedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        UpdateCatalogVersionSnapshot second = ContractFixture.CreateSnapshot(
            publishedAt: publishedAt, releaseNotes: "", notificationPolicy: UpdateNotificationPolicy.ManualOnly);

        Assert.Equal(first.Version, second.Version);
        Assert.Equal(publishedAt, second.PublishedAt);
        Assert.Equal(first.PackagePath, second.PackagePath);
        Assert.Equal(42, second.PackageSize);
        Assert.Equal(ContractFixture.PackageSha, second.PackageSha256);
        Assert.Equal(ContractFixture.ManifestSha, second.ReleaseManifestSha256);
        Assert.Empty(second.ReleaseNotes);
        Assert.Equal(UpdateNotificationPolicy.ManualOnly, second.NotificationPolicy);
        Assert.Equal(first.Identity, second.Identity);
        Assert.DoesNotContain("manual-only", second.Identity, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFrozenContentIdentityComponentParticipates()
    {
        string identity = ContractFixture.CreateSnapshot().Identity;
        Assert.NotEqual(identity, ContractFixture.CreateSnapshot(version: ContractFixture.App101).Identity);
        Assert.NotEqual(identity, ContractFixture.CreateSnapshot(packagePath: "packages/moved-relative.zip").Identity);
        Assert.NotEqual(identity, ContractFixture.CreateSnapshot(packageSize: 43).Identity);
        Assert.NotEqual(identity, ContractFixture.CreateSnapshot(packageSha: new string('b', 64)).Identity);
        Assert.NotEqual(identity, ContractFixture.CreateSnapshot(manifestSha: new string('d', 64)).Identity);
    }

    [Fact]
    public void IdentityFormattingRemainsInvariantAcrossCultures()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        string expected = ContractFixture.CreateSnapshot().Identity;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal("1.0.0", ContractFixture.App100.ToString());
            Assert.Equal(expected, ContractFixture.CreateSnapshot().Identity);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../package.zip")]
    [InlineData("C:/package.zip")]
    [InlineData("/package.zip")]
    [InlineData("packages\\package.zip")]
    [InlineData("packages//package.zip")]
    [InlineData("packages/./package.zip")]
    [InlineData("packages/name./package.zip")]
    [InlineData("packages/name /package.zip")]
    [InlineData("packages/CON.zip")]
    [InlineData("packages/COM1.bin/package.zip")]
    [InlineData("packages/package.zip|alias")]
    public void SnapshotRejectsUnsafePathsBeforeComposingIdentity(string? path)
    {
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateSnapshot(packagePath: path!));
    }

    [Theory]
    [InlineData('\u007f')]
    [InlineData('\u0085')]
    [InlineData('\u009f')]
    public void SnapshotPreservesFrozenDelAndC1PathCharacters(char character)
    {
        string path = $"packages/a{character}.zip";
        UpdateCatalogVersionSnapshot snapshot = ContractFixture.CreateSnapshot(packagePath: path);

        Assert.Equal(path, snapshot.PackagePath.Value);
        Assert.Contains(path, snapshot.Identity, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotRejectsEveryFrozenC0PathCharacter()
    {
        for (char character = '\u0000'; character <= '\u001f'; character++)
        {
            string path = $"packages/a{character}.zip";
            Assert.Throws<ArgumentException>(() => ContractFixture.CreateSnapshot(packagePath: path));
        }
    }

    [Fact]
    public void SnapshotKeepsTheProductPackageAndReleaseNoteCeilings()
    {
        Assert.Equal(ContractFixture.MaximumPackageBytes,
            ContractFixture.CreateSnapshot(packageSize: ContractFixture.MaximumPackageBytes).PackageSize);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContractFixture.CreateSnapshot(packageSize: ContractFixture.MaximumPackageBytes + 1));

        string notesAtLimit = new('é', ContractFixture.MaximumReleaseNoteBytes / 2);
        Assert.Equal(notesAtLimit, ContractFixture.CreateSnapshot(releaseNotes: notesAtLimit).ReleaseNotes);
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateSnapshot(releaseNotes: notesAtLimit + "a"));
    }

    [Theory]
    [InlineData(0L, 1)]
    [InlineData(-1L, 1)]
    [InlineData(1L, 0)]
    [InlineData(1L, -1)]
    public void SnapshotRejectsNonpositiveCeilings(long maximumPackageBytes, int maximumReleaseNoteBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractFixture.CreateSnapshot(
            packageSize: 1,
            releaseNotes: "",
            maximumPackageBytes: maximumPackageBytes,
            maximumReleaseNoteBytes: maximumReleaseNoteBytes));
    }

    [Fact]
    public void SnapshotKeepsTheFrozenFiveHundredTwelveCharacterPathLimit()
    {
        string atLimit = "packages/" + new string('a', 499) + ".zip";
        Assert.Equal(512, atLimit.Length);
        Assert.Equal(atLimit, ContractFixture.CreateSnapshot(packagePath: atLimit).PackagePath.Value);
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateSnapshot(packagePath: atLimit[..^4] + "a.zip"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SnapshotRejectsNonpositivePackageSizes(long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractFixture.CreateSnapshot(packageSize: size));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void SnapshotRejectsNoncanonicalDigests(string? digest)
    {
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateSnapshot(packageSha: digest!));
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateSnapshot(manifestSha: digest!));
    }

    [Fact]
    public void SnapshotRequiresUtcPresentNotesAndKnownPolicy()
    {
        Assert.Throws<ArgumentException>(() => ContractFixture.CreateSnapshot(
            publishedAt: new DateTimeOffset(2026, 8, 21, 8, 0, 0, TimeSpan.FromHours(8))));
        Assert.Throws<ArgumentNullException>(() => ContractFixture.CreateSnapshot(releaseNotes: null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractFixture.CreateSnapshot(
            notificationPolicy: (UpdateNotificationPolicy)99));
        Assert.Throws<ArgumentException>(() => UpdateCatalogVersionSnapshot.Create(
            ContractFixture.MaximumPackageBytes, ContractFixture.MaximumReleaseNoteBytes,
            ContractFixture.App100, DateTimeOffset.MinValue, default, 42, ContractFixture.PackageSha,
            ContractFixture.ManifestSha, "", UpdateNotificationPolicy.Notify));
    }
}
