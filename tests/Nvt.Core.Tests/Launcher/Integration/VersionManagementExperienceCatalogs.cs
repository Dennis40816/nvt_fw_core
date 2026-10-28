// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;

namespace Nvt.Core.Tests.Launcher.Integration;

internal static partial class VersionManagementExperienceTestSupport
{
    internal const string CatalogContentDigest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    internal static UpdateCatalogSnapshot Catalog(string version) => UpdateCatalogSnapshot.Create(
        IntegrationTestData.Limits, [Row(version)]);

    internal static UpdateCatalogVersionSnapshot Row(string version, string? notes = null,
        UpdateNotificationPolicy policy = UpdateNotificationPolicy.Notify) => UpdateCatalogVersionSnapshot.Create(
            134_217_728, 65_536, ManagedAppVersion.Parse(version), IntegrationTestData.PublishedAt,
            new($"packages/Fixture-v{version}-synthetic.zip"), 42, Hash, Hash, notes ?? $"Release {version}", policy);

    internal static UpdateCatalogSnapshot CatalogV2(params (string Version, string Policy)[] versions) =>
        UpdateCatalogSnapshot.Create(IntegrationTestData.Limits,
            [.. versions.Select(row => Row(row.Version, policy: row.Policy == "notify"
                ? UpdateNotificationPolicy.Notify : UpdateNotificationPolicy.ManualOnly))]);

    internal static UpdateCatalogSnapshot CatalogWithOlderReleaseNote(string olderNote) =>
        UpdateCatalogSnapshot.Create(IntegrationTestData.Limits, [Row("0.10.6"), Row("0.10.5", olderNote)]);

    internal static UpdateCatalogSnapshot CatalogWithVersionCount(int count) => UpdateCatalogSnapshot.Create(
        IntegrationTestData.Limits, [.. Enumerable.Range(1, count).Select(index => Row($"0.10.{index}"))]);

    internal static string SourcePath(string name) => Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(Path.Combine("registry-sources", name)));

    internal static UpdateSourceRegistryLoadResult Registry(long revision, string digest,
        params (string Path, UpdateSourceRegistryEntryStatus Status)[] entries) =>
        RegistryWithPublication("0.10.6", 1, CatalogContentDigest, revision, digest, entries);

    internal static UpdateSourceRegistryLoadResult RegistryWithPublication(
        string latestVersion, int catalogSchemaVersion, string catalogDigest, long revision, string digest,
        params (string Path, UpdateSourceRegistryEntryStatus Status)[] entries) => new(
            UpdateSourceRegistrySnapshot.Create(IntegrationTestData.Limits, IntegrationTestData.Descriptor(),
                "fixture-registry", revision, IntegrationTestData.PublishedAt,
                UpdateCatalogPublicationAssertion.Create(latestVersion, catalogSchemaVersion, catalogDigest), digest,
                [.. entries.Select(entry => UpdateSourceRegistryEntry.Create(
                    Path.Combine(entry.Path, "update-catalog.v1.json"), entry.Status))]), UpdateSourceRegistryLoadIssue.None);
}
