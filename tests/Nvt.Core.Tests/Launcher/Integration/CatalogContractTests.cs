// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.IntegrationTestData;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Normalized catalog construction, identity, ordering and frozen exception contracts.</summary>
public sealed class CatalogContractTests
{
    /// <summary>Schema validation precedes the catalog digest check.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ContentIdentityChecksSchemaBeforeDigest(int schema)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new UpdateCatalogContentIdentity(schema, "invalid"));
        Assert.Equal("schemaVersion", exception.ParamName);
    }

    /// <summary>Invalid digest values keep the frozen message and parameter.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("gaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ContentIdentityRejectsInvalidDigest(string? digest)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new UpdateCatalogContentIdentity(1, digest!));
        Assert.Equal("sha256", exception.ParamName);
        Assert.StartsWith("Catalog SHA-256 is invalid.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Valid exact wire identities preserve every value.</summary>
    [Fact]
    public void ContentIdentityPreservesValues()
    {
        var identity = new UpdateCatalogContentIdentity(1, Hash);
        Assert.Equal(1, identity.SchemaVersion);
        Assert.Equal(Hash, identity.Sha256);
        Assert.Equal(identity, new UpdateCatalogContentIdentity(1, Hash));
    }

    /// <summary>The snapshot owns a read-only copy and never mutates the adapter's order.</summary>
    [Fact]
    public void CatalogOwnsRowsAndOrdersNewestFirst()
    {
        UpdateCatalogVersionSnapshot older = Package(1);
        UpdateCatalogVersionSnapshot newer = Package(2);
        UpdateCatalogVersionSnapshot[] input = [older, newer];
        UpdateCatalogSnapshot catalog = UpdateCatalogSnapshot.Create(Limits, input);
        Assert.Same(older, input[0]);
        input[1] = older;
        Assert.Equal(new[] { newer, older }, catalog.Versions);
        Assert.Throws<NotSupportedException>(() => ((IList<UpdateCatalogVersionSnapshot>)catalog.Versions)[0] = older);
    }

    /// <summary>Null rows and duplicate versions are rejected at normalized admission.</summary>
    [Fact]
    public void CatalogRejectsMissingAndDuplicateRows()
    {
        UpdateCatalogVersionSnapshot row = Package();
        Assert.Throws<ArgumentNullException>(() => UpdateCatalogSnapshot.Create(null!, [row]));
        Assert.Throws<ArgumentNullException>(() => UpdateCatalogSnapshot.Create(Limits, null!));
        Assert.Throws<ArgumentException>(() => UpdateCatalogSnapshot.Create(Limits, [null!]));
        Assert.Throws<ArgumentException>(() => UpdateCatalogSnapshot.Create(Limits, [row, row]));
    }

    /// <summary>Explicit search keeps manual rows while automatic search picks the newest eligible notify row.</summary>
    [Fact]
    public void SearchPreservesStrictNewnessAndNotificationPolicy()
    {
        UpdateCatalogVersionSnapshot manual = Package(3, policy: UpdateNotificationPolicy.ManualOnly);
        UpdateCatalogVersionSnapshot notify = Package(2);
        UpdateCatalogSnapshot catalog = UpdateCatalogSnapshot.Create(Limits, [notify, manual, Package(1)]);
        Assert.Same(manual, catalog.FindNewestNewerThan(ManagedAppVersion.Parse("1.0.1")));
        Assert.Same(notify, catalog.FindNewestNotifyNewerThan(ManagedAppVersion.Parse("1.0.1")));
        Assert.Null(catalog.FindNewestNewerThan(manual.Version));
        Assert.Null(catalog.FindNewestNotifyNewerThan(notify.Version));
    }

    /// <summary>The existing location-independent identity retains invariant size and the exact field order.</summary>
    [Fact]
    public void PackageIdentityKeepsFrozenComposition()
    {
        UpdateCatalogVersionSnapshot package = Package(bytes: 134_217_728, notes: "é");
        string expected = "1.0.1|pppppppppppp|" + 134_217_728L.ToString(CultureInfo.InvariantCulture) + "|" + Hash + "|" + Hash;
        Assert.Equal(expected, package.Identity);
        Assert.Equal(expected, Package(bytes: 134_217_728, notes: "Metadata correction",
            policy: UpdateNotificationPolicy.ManualOnly).Identity);
    }

    /// <summary>Both the snapshot and no-issue category are required; exact wire identity remains optional.</summary>
    [Fact]
    public void CatalogLoadResultRetainsFrozenSuccessPredicate()
    {
        UpdateCatalogSnapshot snapshot = UpdateCatalogSnapshot.Create(Limits, [Package()]);
        Assert.True(new UpdateCatalogLoadResult(snapshot, UpdateCatalogLoadIssue.None).IsSuccess);
        Assert.False(new UpdateCatalogLoadResult(null, UpdateCatalogLoadIssue.None).IsSuccess);
        Assert.False(new UpdateCatalogLoadResult(snapshot, UpdateCatalogLoadIssue.InvalidManifest).IsSuccess);
        Assert.False(new UpdateCatalogLoadResult(snapshot, (UpdateCatalogLoadIssue)999).IsSuccess);
    }
}
