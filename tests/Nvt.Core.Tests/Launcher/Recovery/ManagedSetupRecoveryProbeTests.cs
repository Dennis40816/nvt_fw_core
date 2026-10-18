// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Recovery;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Locks exact normalized marker projection and read-only custody.</summary>
public sealed partial class ManagedSetupRecoveryProbeTests
{
    /// <summary>A strict marker binds root, state, phase, and the exact owned-path set.</summary>
    [Fact]
    public async Task ProbeReturnsExactTypedTransactionOnlyForEveryBoundIdentity()
    {
        SetupFixture.RequireNative();
        using var temporary = new SetupFixture();
        string root = temporary.Root;
        string state = temporary.StatePath;
        ManagedSetupMarker marker = temporary.Marker();
        string path = FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(root);
        Nvt.Core.TestSupport.TestFiles.WriteAllBytes(path, temporary.Codec.Encode(marker).ToArray());
        SortedDictionary<string, string> before = await temporary.SnapshotAsync();

        ManagedSetupRecoveryFact fact = await temporary.Probe().ObserveAsync(
            root,
            state,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryFactKind.Exact, fact.Kind);
        ManagedSetupRecoveryTransaction transaction = Assert.IsType<ManagedSetupRecoveryTransaction>(
            fact.Transaction);
        Assert.Equal(marker.TransactionId, transaction.TransactionId);
        Assert.Equal(ManagedSetupRecoveryPhase.Staging, transaction.Phase);
        Assert.Equal(marker.OwnedPaths, transaction.OwnedPaths);
        Assert.Equal(marker.Candidate.EntryIdentity, transaction.Candidate.EntryIdentity);
        Assert.Equal(marker.ManagedRootIdentity, transaction.ManagedRootIdentity);
        Assert.Equal(marker.StatePathIdentity, transaction.StatePathIdentity);
        Assert.Equal(
            marker.Payload.LauncherSize,
            transaction.Payload.LauncherSize);
        Assert.Equal(
            marker.Payload.LauncherSha256,
            transaction.Payload.LauncherSha256);
        Assert.Equal(marker.Payload.DescriptorSize, transaction.Payload.DescriptorSize);
        Assert.Equal(marker.Payload.DescriptorSha256, transaction.Payload.DescriptorSha256);
        Assert.Equal(
            marker.Payload.BootstrapFileName,
            transaction.Payload.BootstrapFileName);
        Assert.Equal(marker.Payload.BootstrapSize, transaction.Payload.BootstrapSize);
        Assert.Equal(marker.Payload.BootstrapSha256, transaction.Payload.BootstrapSha256);
        Assert.Equal(marker.Candidate.RegistryRevision, transaction.Candidate.RegistryRevision);
        Assert.Equal(marker.Candidate.RegistryDigest, transaction.Candidate.RegistryDigest);
        Assert.Equal(marker.Candidate.CatalogSchemaVersion, transaction.Candidate.CatalogSchemaVersion);
        Assert.Equal(marker.Candidate.CatalogLatestVersion, transaction.Candidate.CatalogLatestVersion);
        Assert.Equal(marker.Candidate.CatalogDigest, transaction.Candidate.CatalogDigest);
        Assert.Equal(marker.Candidate.CatalogPath, transaction.Candidate.CatalogPath);
        Assert.Equal(marker.Candidate.RegistryId, transaction.Candidate.RegistryId);
        Assert.Equal(marker.Candidate.SourceRoot, transaction.Candidate.SourceRoot);
        Assert.Equal(marker.Candidate.SourceStatus, transaction.Candidate.SourceStatus);
        Assert.Equal(marker.Candidate.Version, transaction.Candidate.Version);
        Assert.Equal(marker.Candidate.PackagePath, transaction.Candidate.PackagePath);
        Assert.Equal(marker.Candidate.PackageSize, transaction.Candidate.PackageSize);
        Assert.Equal(marker.Candidate.PackageSha256, transaction.Candidate.PackageSha256);
        Assert.Equal(
            marker.Candidate.ReleaseManifestSha256,
            transaction.Candidate.ReleaseManifestSha256);
        Assert.Equal(before, await temporary.SnapshotAsync());
    }

    /// <summary>Held marker custody blocks same-name mutation, rename, and deletion.</summary>
    [Fact]
    public async Task ProbeCustodyBlocksSameNameWriteRenameAndDelete()
    {
        SetupFixture.RequireNative();
        using var temporary = new SetupFixture();
        string root = temporary.Root;
        string state = temporary.StatePath;
        string markerPath = FileSystemManagedInstallationRootProbe.GetTransactionMarkerPath(root);
        Nvt.Core.TestSupport.TestFiles.WriteAllBytes(markerPath, temporary.Codec.Encode(temporary.Marker()).ToArray());
        SortedDictionary<string, string> before = await temporary.SnapshotAsync();
        var blocked = new List<string>();
        var probe = new FileSystemManagedSetupRecoveryProbe(
            temporary.Codec, SetupFixture.Ceiling, SetupFixture.Ceiling,
            static (path, cancellationToken) => WindowsStablePathCustody.TryAcquireFile(
                path,
                cancellationToken: cancellationToken),
            _ =>
            {
                RecordBlocked("write", () => SetupFixture.Write(markerPath, "replacement"));
                RecordBlocked("rename", () => new FileInfo(markerPath).MoveTo(markerPath + ".moved"));
                RecordBlocked("delete", () => File.Delete(markerPath));
                return true;
            });

        ManagedSetupRecoveryFact fact = await probe.ObserveAsync(
            root,
            state,
            TestContext.Current.CancellationToken);

        Assert.Equal(ManagedSetupRecoveryFactKind.Exact, fact.Kind);
        Assert.Equal(["write", "rename", "delete"], blocked);
        Assert.Equal(before, await temporary.SnapshotAsync());
        return;

        void RecordBlocked(string operation, Action attack)
        {
            try
            {
                attack();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                blocked.Add(operation);
            }
        }
    }

}
