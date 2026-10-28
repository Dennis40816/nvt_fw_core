// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;

namespace Nvt.Core.Launcher.Recovery;

public sealed partial class FileSystemManagedSetupRecoveryExecution
{
    private static async ValueTask<ExecutionTreeObservation> ObserveExactTreeAsync(
        string path,
        WindowsStableTreeLimits limits,
        CancellationToken cancellationToken)
    {
        WindowsStableCustodyResult acquired =
            WindowsStablePathCustody.TryAcquireImmutableTreeOrExactMissing(
                path,
                limits,
                cancellationToken);
        if (acquired.IsExactChildMissing)
        {
            return new(null, string.Empty, "absent", Issue: null);
        }
        if (!acquired.IsAcquired)
        {
            acquired.Custody?.Dispose();
            return new(null, null, null, MapExecutionIssue(acquired.Issue));
        }
        using WindowsStablePathCustody custody = acquired.Custody!;
        if (!custody.RevalidateClosedTree() ||
            !custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot))
        {
            return new(
                null,
                null,
                null,
                ManagedSetupRecoveryExecutionPortOutcome.SourceChanged);
        }
        string inventory = await SecureSnapshotProofAsync(
            custody,
            snapshot!,
            cancellationToken).ConfigureAwait(false);
        return custody.RevalidateClosedTree()
            ? new(snapshot, inventory, ProofDigest(inventory), Issue: null)
            : new(null, null, null, ManagedSetupRecoveryExecutionPortOutcome.SourceChanged);
    }

    private static string ObserveRawOrMissingIdentity(
        string path,
        CancellationToken cancellationToken)
    {
        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireFile(
            path,
            cancellationToken: cancellationToken);
        if (acquired.IsExactChildMissing)
        {
            return "missing";
        }
        if (!acquired.IsAcquired)
        {
            acquired.Custody?.Dispose();
            return "unavailable";
        }
        using WindowsStablePathCustody custody = acquired.Custody!;
        using FileStream stream = custody.OpenReadOnlyFile(Path.GetFileName(path));
        string digest = LowerHex(SHA256.HashData(stream));
        return custody.RevalidateClosedTree() &&
            custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot)
                ? string.Concat(TreeDigest(snapshot!), ":", digest)
                : "changed";
    }

    private WindowsStableTreeLimits RecoveryTreeLimits(
        ManagedSetupRecoveryPayloadIdentity payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new(
            checked(_payloadTreeLimits.MaximumFiles + 3),
            checked(_payloadTreeLimits.MaximumDirectories + 3),
            checked(
                _payloadTreeLimits.MaximumBytes +
                payload.LauncherSize +
                payload.BootstrapSize +
                (1024 * 1024)));
    }

    private bool HasAdmittedExecutableSizes(ManagedSetupRecoveryPayloadIdentity payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        long maximum = _limits.MaximumExecutableBytes;
        return payload.LauncherSize > 0 && payload.LauncherSize <= maximum &&
            payload.BootstrapSize > 0 && payload.BootstrapSize <= maximum;
    }

    private static WindowsStableTreeLimits RecoveryTreeLimits(FileSystemRecoveryToken token)
    {
        return new(token.MaximumFiles, token.MaximumDirectories, token.MaximumBytes);
    }

    private static string[] SplitLines(string value)
    {
        return string.IsNullOrEmpty(value)
            ? []
            : value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static ManagedSetupRecoveryExecutionPortOutcome MapExecutionIssue(
        WindowsStableCustodyIssue issue)
    {
        return issue switch
        {
            WindowsStableCustodyIssue.AccessDenied =>
                ManagedSetupRecoveryExecutionPortOutcome.PermissionDenied,
            WindowsStableCustodyIssue.Changed =>
                ManagedSetupRecoveryExecutionPortOutcome.SourceChanged,
            WindowsStableCustodyIssue.InvalidPath or WindowsStableCustodyIssue.ReparsePoint =>
                ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired,
            WindowsStableCustodyIssue.Contended or WindowsStableCustodyIssue.Unavailable =>
                ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable,
            WindowsStableCustodyIssue.None => throw new InvalidOperationException(
                "Successful custody cannot map to a recovery failure."),
            _ => throw new InvalidOperationException("Stable custody returned an undefined issue."),
        };
    }

    private static ManagedSetupRecoveryExecutionPortOutcome MapExecutionIssue(
        ManagedSetupRecoveryEvidenceIssue issue)
    {
        return issue switch
        {
            ManagedSetupRecoveryEvidenceIssue.PermissionDenied =>
                ManagedSetupRecoveryExecutionPortOutcome.PermissionDenied,
            ManagedSetupRecoveryEvidenceIssue.StateUnavailable =>
                ManagedSetupRecoveryExecutionPortOutcome.StateUnavailable,
            ManagedSetupRecoveryEvidenceIssue.SourceChanged =>
                ManagedSetupRecoveryExecutionPortOutcome.SourceChanged,
            ManagedSetupRecoveryEvidenceIssue.Invalid =>
                ManagedSetupRecoveryExecutionPortOutcome.ManualInterventionRequired,
            ManagedSetupRecoveryEvidenceIssue.None => throw new InvalidOperationException(
                "Successful recovery evidence cannot map to an execution failure."),
            _ => throw new InvalidOperationException("Recovery evidence returned an undefined issue."),
        };
    }

    private static ManagedSetupRecoveryEvidenceIssue MapEvidenceIssue(
        WindowsStableCustodyIssue issue)
    {
        return issue switch
        {
            WindowsStableCustodyIssue.AccessDenied =>
                ManagedSetupRecoveryEvidenceIssue.PermissionDenied,
            WindowsStableCustodyIssue.Changed => ManagedSetupRecoveryEvidenceIssue.SourceChanged,
            WindowsStableCustodyIssue.InvalidPath or WindowsStableCustodyIssue.ReparsePoint =>
                ManagedSetupRecoveryEvidenceIssue.Invalid,
            WindowsStableCustodyIssue.Contended or WindowsStableCustodyIssue.Unavailable =>
                ManagedSetupRecoveryEvidenceIssue.StateUnavailable,
            WindowsStableCustodyIssue.None => throw new InvalidOperationException(
                "Successful custody omitted its owner."),
            _ => throw new InvalidOperationException("Stable custody returned an undefined issue."),
        };
    }

    private static ManagedSetupRecoveryEvidenceIssue MapEvidenceIssue(InstalledLauncherIssue issue)
    {
        return issue switch
        {
            InstalledLauncherIssue.Unavailable => ManagedSetupRecoveryEvidenceIssue.StateUnavailable,
            InstalledLauncherIssue.Tampered or InstalledLauncherIssue.InvalidManifest =>
                ManagedSetupRecoveryEvidenceIssue.SourceChanged,
            InstalledLauncherIssue.ProtocolMismatch or InstalledLauncherIssue.UnsafePath =>
                ManagedSetupRecoveryEvidenceIssue.Invalid,
            InstalledLauncherIssue.None => throw new InvalidOperationException(
                "Verified Launcher omitted its identity."),
            _ => throw new InvalidOperationException("Launcher verification returned an undefined issue."),
        };
    }

    private static string TransactionDigest(ManagedSetupRecoveryTransaction transaction)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, transaction.TransactionId);
        Append(hash, transaction.ManagedRootIdentity);
        Append(hash, transaction.StatePathIdentity);
        Append(hash, transaction.Phase.ToString());
        foreach (string path in transaction.OwnedPaths)
        {
            Append(hash, path);
        }
        Append(hash, transaction.Payload.LauncherSize);
        Append(hash, transaction.Payload.LauncherSha256);
        Append(hash, transaction.Payload.DescriptorSize);
        Append(hash, transaction.Payload.DescriptorSha256);
        Append(hash, transaction.Payload.BootstrapFileName);
        Append(hash, transaction.Payload.BootstrapSize);
        Append(hash, transaction.Payload.BootstrapSha256);
        Append(hash, transaction.Candidate.RegistryRevision);
        Append(hash, transaction.Candidate.RegistryDigest);
        Append(hash, transaction.Candidate.CatalogSchemaVersion);
        Append(hash, transaction.Candidate.CatalogLatestVersion);
        Append(hash, transaction.Candidate.CatalogDigest);
        Append(hash, transaction.Candidate.CatalogPath);
        Append(hash, transaction.Candidate.RegistryId);
        Append(hash, transaction.Candidate.SourceRoot);
        Append(hash, transaction.Candidate.SourceStatus);
        Append(hash, transaction.Candidate.Version);
        Append(hash, transaction.Candidate.PackagePath);
        Append(hash, transaction.Candidate.PackageSize);
        Append(hash, transaction.Candidate.PackageSha256);
        Append(hash, transaction.Candidate.ReleaseManifestSha256);
        Append(hash, transaction.Candidate.EntryIdentity);
        return LowerHex(hash.GetHashAndReset());
    }

    private static string TreeDigest(WindowsStableOwnedTreeSnapshot snapshot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((string path, WindowsStablePathIdentity identity) in snapshot.Files
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            Append(hash, "F");
            Append(hash, path);
            Append(hash, identity.VolumeSerialNumber);
            Append(hash, identity.FileIdLow);
            Append(hash, identity.FileIdHigh);
        }
        foreach ((string path, WindowsStablePathIdentity identity) in snapshot.Directories
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            Append(hash, "D");
            Append(hash, path);
            Append(hash, identity.VolumeSerialNumber);
            Append(hash, identity.FileIdLow);
            Append(hash, identity.FileIdHigh);
        }
        return LowerHex(hash.GetHashAndReset());
    }

    private static async ValueTask<string> SecureSnapshotProofAsync(
        WindowsStablePathCustody custody,
        WindowsStableOwnedTreeSnapshot snapshot,
        CancellationToken cancellationToken,
        Action<long>? afterHashedFile = null)
    {
        var lines = new List<string>(snapshot.Files.Count + snapshot.Directories.Count);
        foreach ((string path, WindowsStablePathIdentity identity) in snapshot.Files
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            using FileStream stream = custody.OpenReadOnlyFile(path);
            stream.Position = 0;
            long length = stream.Length;
            string sha256 = LowerHex(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            afterHashedFile?.Invoke(length);
            lines.Add(string.Join('|',
                "F",
                path,
                identity.VolumeSerialNumber.ToString("x16", CultureInfo.InvariantCulture),
                identity.FileIdLow.ToString("x16", CultureInfo.InvariantCulture),
                identity.FileIdHigh.ToString("x16", CultureInfo.InvariantCulture),
                length.ToString(CultureInfo.InvariantCulture),
                sha256));
        }
        lines.AddRange(snapshot.Directories.Select(static pair =>
            ProofLine('D', pair.Key, pair.Value)));
        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines);
    }

    private static string ProofDigest(string proof)
    {
        return LowerHex(SHA256.HashData(Encoding.UTF8.GetBytes(proof)));
    }

    private static string ProofLine(
        char kind,
        string path,
        WindowsStablePathIdentity identity)
    {
        return string.Join('|',
            kind,
            path,
            identity.VolumeSerialNumber.ToString("x16", CultureInfo.InvariantCulture),
            identity.FileIdLow.ToString("x16", CultureInfo.InvariantCulture),
            identity.FileIdHigh.ToString("x16", CultureInfo.InvariantCulture));
    }

    private static string TreeSteps(
        WindowsStableOwnedTreeSnapshot snapshot,
        ManagedAppVersion version)
    {
        return string.Join('\n', BuildTreeSteps(snapshot.Files.Keys, snapshot.Directories.Keys, version));
    }

    private static string[] BuildTreeSteps(
        IEnumerable<string> files,
        IEnumerable<string> directories,
        ManagedAppVersion version)
    {
        string[] filePaths = [.. files];
        string[] directoryPaths = [.. directories];
        string versionRoot = string.Join(
            '/',
            FileSystemManagedVersionRepository.VersionsDirectoryName,
            version.ToString());
        string manifest = string.Concat(versionRoot, "/RELEASE-MANIFEST.json");
        string[] fixedSkeleton =
        [
            versionRoot,
            FileSystemManagedVersionRepository.VersionsDirectoryName,
            string.Empty,
        ];
        IEnumerable<string> ordinaryFiles = filePaths
            .Where(path => !RepositoryPathSafety.PathComparer.Equals(path, manifest))
            .OrderByDescending(PathDepth)
            .ThenBy(static path => path, StringComparer.Ordinal)
            .Select(static path => string.Concat("F|", path));
        IEnumerable<string> nonterminalDirectories = directoryPaths
            .Except(fixedSkeleton, RepositoryPathSafety.PathComparer)
            .OrderByDescending(PathDepth)
            .ThenBy(static path => path, StringComparer.Ordinal)
            .Select(static path => string.Concat("D|", path));
        IEnumerable<string> manifestStep = filePaths
            .Where(path => RepositoryPathSafety.PathComparer.Equals(path, manifest))
            .Select(static path => string.Concat("F|", path));
        IEnumerable<string> skeletonSteps = directoryPaths
            .Where(path => fixedSkeleton.Contains(path, RepositoryPathSafety.PathComparer))
            .OrderByDescending(PathDepth)
            .ThenBy(static path => path, StringComparer.Ordinal)
            .Select(static path => string.Concat("D|", path));
        return [.. ordinaryFiles
            .Concat(nonterminalDirectories)
            .Concat(manifestStep)
            .Concat(skeletonSteps)];
    }

    private static int PathDepth(string path)
    {
        return string.IsNullOrEmpty(path) ? 0 : path.Count(static value => value == '/') + 1;
    }

    private static string LauncherStateDigest(LauncherBootstrapStateLoadResult result)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, result.Issue.ToString());
        if (result.State is { } state)
        {
            Append(hash, state.ManagedRootIdentity);
            Append(hash, state.Active);
            Append(hash, state.LastKnownGood);
            Append(hash, state.Pending?.Candidate);
            Append(hash, state.Pending?.PreviousActive);
            Append(hash, state.Pending?.PreviousLastKnownGood);
            Append(hash, state.Pending?.Phase.ToString() ?? string.Empty);
            Append(hash, state.Failed);
        }
        return LowerHex(hash.GetHashAndReset());
    }

    private static string LauncherDigest(ManagedLauncherIdentity? launcher)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, launcher);
        return LowerHex(hash.GetHashAndReset());
    }

    private static async ValueTask<StateFileObservation<T>> ObserveStateFileAsync<T>(
        string path,
        Func<CancellationToken, ValueTask<T>> load,
        Func<T, bool> isMissing,
        CancellationToken cancellationToken)
        where T : class
    {
        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireFile(
            path,
            cancellationToken: cancellationToken);
        if (!acquired.IsAcquired)
        {
            acquired.Custody?.Dispose();
            if (!acquired.IsExactChildMissing)
            {
                return new(null, null, MapEvidenceIssue(acquired.Issue));
            }
            T missing = await load(cancellationToken).ConfigureAwait(false);
            return isMissing(missing)
                ? new(missing, "missing", Issue: null)
                : new(null, null, ManagedSetupRecoveryEvidenceIssue.SourceChanged);
        }

        using WindowsStablePathCustody custody = acquired.Custody!;
        T value = await load(cancellationToken).ConfigureAwait(false);
        if (isMissing(value) ||
            !custody.RevalidateClosedTree() ||
            !custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot))
        {
            return new(null, null, ManagedSetupRecoveryEvidenceIssue.SourceChanged);
        }
        using FileStream stream = custody.OpenReadOnlyFile(Path.GetFileName(path));
        string bytesDigest = LowerHex(
            await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        return new(
            value,
            string.Concat(TreeDigest(snapshot!), ":", bytesDigest),
            Issue: null);
    }

    private static async ValueTask<StateFileObservation<RawFileEvidence>> ObserveRawFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireFile(
            path,
            cancellationToken: cancellationToken);
        if (!acquired.IsAcquired)
        {
            acquired.Custody?.Dispose();
            return new(
                null,
                null,
                acquired.IsExactChildMissing
                    ? ManagedSetupRecoveryEvidenceIssue.SourceChanged
                    : MapEvidenceIssue(acquired.Issue));
        }
        using WindowsStablePathCustody custody = acquired.Custody!;
        using FileStream stream = custody.OpenReadOnlyFile(Path.GetFileName(path));
        string digest = LowerHex(
            await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        return custody.RevalidateClosedTree() &&
            custody.TryCreateOwnedSnapshot(out WindowsStableOwnedTreeSnapshot? snapshot)
                ? new(new(digest), string.Concat(TreeDigest(snapshot!), ":", digest), Issue: null)
                : new(null, null, ManagedSetupRecoveryEvidenceIssue.SourceChanged);
    }

    private static void Append(IncrementalHash hash, ManagedLauncherIdentity? launcher)
    {
        if (launcher is null)
        {
            Append(hash, string.Empty);
            return;
        }
        Append(hash, launcher.OwnerAppVersion.ToString());
        Append(hash, launcher.OwnerAdmissionIdentity);
        Append(hash, launcher.OwnerReleaseManifestSha256);
        Append(hash, launcher.LauncherVersion.ToString());
        Append(hash, launcher.ProtocolVersion);
        Append(hash, launcher.ExecutableRelativePath);
        Append(hash, launcher.Size);
        Append(hash, launcher.Sha256);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void Append(IncrementalHash hash, long value)
    {
        Append(hash, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Append(IncrementalHash hash, int value)
    {
        Append(hash, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Append(IncrementalHash hash, ulong value)
    {
        Append(hash, value.ToString(CultureInfo.InvariantCulture));
    }

    private sealed record FileSystemRecoveryToken(
        string StatePath,
        string ManagedRoot,
        string TransactionId,
        ManagedSetupRecoveryPhase Phase,
        string Version,
        string TransactionSha256,
        string TreeRoot,
        string TreeSha256,
        string TreeInventory,
        string TreeSteps,
        string StagingInventory,
        string StagingSha256,
        int MaximumFiles,
        int MaximumDirectories,
        long MaximumBytes,
        string MarkerIdentity,
        string ApplicationStateIdentity,
        string LauncherStateIdentity,
        string LauncherStateSha256,
        string LauncherSha256) : ManagedSetupRecoveryExecutionToken;

    private sealed record PrefixObservation(
        bool IsExact,
        WindowsStableOwnedTreeSnapshot? Snapshot,
        string? TreeDigest,
        string? TreeInventory,
        string? StagingInventory,
        string? StagingDigest,
        ManagedSetupRecoveryEvidenceIssue? Issue);

    private sealed record StagingObservation(
        bool IsEmptyOrAbsent,
        string? Inventory,
        string? Digest,
        ManagedSetupRecoveryEvidenceIssue? Issue);

    private sealed record StateFileObservation<T>(
        T? Value,
        string? IdentityDigest,
        ManagedSetupRecoveryEvidenceIssue? Issue)
        where T : class;

    private sealed record RawFileEvidence(string Sha256);

    private sealed record ExecutionTreeObservation(
        WindowsStableOwnedTreeSnapshot? Snapshot,
        string? Inventory,
        string? Digest,
        ManagedSetupRecoveryExecutionPortOutcome? Issue);

    private sealed record PreparedRollbackTree(
        WindowsStablePathCustody? Custody,
        ManagedSetupRecoveryExecutionPortOutcome? Issue);

    private sealed class RecoveryMutationProgress
    {
        internal bool Mutated => DeleteCount != 0;
        // Owned by the single asynchronous execution flow; never shared between attempts.
        internal int DeleteCount { get; set; }
    }
}
