// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.Launcher.Windows;
using Nvt.Core.Files;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nvt.Core.Launcher.Recovery;

/// <summary>Infrastructure-only byte custody used by the fresh-root materializer.</summary>
internal interface IManagedDistributionPayloadContent
{
    /// <summary>Copies the already captured distribution Launcher bytes.</summary>
    ValueTask CopyDistributionLauncherAsync(
        string destination,
        CancellationToken cancellationToken);

    /// <summary>Copies the already captured immutable Root Bootstrap bytes.</summary>
    ValueTask CopyBootstrapAsync(string destination, CancellationToken cancellationToken);
}

/// <summary>Admits the running distribution Launcher and its embedded closed payload.</summary>
public sealed class EmbeddedManagedDistributionPayloadSource : IManagedDistributionPayloadSource
{
    private readonly int _maximumDescriptorBytes;
    private readonly long _maximumExecutableBytes;
    private readonly ProductDescriptor _descriptor;
    private readonly IManagedSetupPayloadDescriptorReader _descriptorReader;
    private readonly Func<Stream?> _openBootstrap;
    private readonly Func<Stream?> _openDescriptor;
    private readonly string _launcherPath;

    /// <summary>Creates a source over exact, independently reopenable embedded resources.</summary>
    public EmbeddedManagedDistributionPayloadSource(
        string launcherPath,
        Func<Stream?> openDescriptor,
        Func<Stream?> openBootstrap,
        ProductDescriptor descriptor, IManagedSetupPayloadDescriptorReader descriptorReader,
        int maximumDescriptorBytes, long maximumExecutableBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDescriptorBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumDescriptorBytes, Array.MaxLength - 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExecutableBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumExecutableBytes, ManagedImmutableBootstrapIdentity.MaximumExecutableBytes);
        _maximumDescriptorBytes = maximumDescriptorBytes;
        _maximumExecutableBytes = maximumExecutableBytes;
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _descriptorReader = descriptorReader ?? throw new ArgumentNullException(nameof(descriptorReader));
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherPath);
        _launcherPath = Path.GetFullPath(launcherPath);
        _openDescriptor = openDescriptor ?? throw new ArgumentNullException(nameof(openDescriptor));
        _openBootstrap = openBootstrap ?? throw new ArgumentNullException(nameof(openBootstrap));
    }

    /// <inheritdoc />
    public async ValueTask<ManagedDistributionPayloadEntryAdmissionResult> AdmitEntryAsync(
        CancellationToken cancellationToken)
    {
        EntryProjectionResult admitted = await AdmitEntryCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        return admitted.IsSuccess
            ? new(
                admitted.Descriptor!.LauncherVersion,
                admitted.Bootstrap,
                ManagedDistributionPayloadIssue.None)
            : new(default, null, admitted.Issue);
    }

    /// <inheritdoc />
    public async ValueTask<ManagedDistributionPayloadInspectionResult> InspectAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EntryProjectionResult admitted = await AdmitEntryCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!admitted.IsSuccess)
        {
            return new(null, admitted.Issue);
        }
        BootstrapReadResult bootstrap = await ReadBootstrapExactAsync(
            admitted.Bootstrap!,
            captureBytes: false,
            cancellationToken).ConfigureAwait(false);
        if (!bootstrap.IsSuccess)
        {
            return new(null, bootstrap.Issue);
        }

        StableManagedExecutableMeasurementResult launcher = await StableManagedExecutableLaunchLease
            .TryMeasureAsync(_launcherPath, _maximumExecutableBytes, cancellationToken).ConfigureAwait(false);
        return launcher.IsMeasured
            ? new(
                CreateIdentity(admitted.Descriptor!, launcher.Length, launcher.Sha256),
                ManagedDistributionPayloadIssue.None)
            : new(null, MapMeasurementIssue(launcher.Issue));
    }

    /// <inheritdoc />
    public async ValueTask<ManagedDistributionPayloadCaptureResult> CaptureExactAsync(
        ManagedDistributionPayloadIdentity expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        EntryProjectionResult admitted = await AdmitEntryCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!admitted.IsSuccess)
        {
            return new(
                null,
                admitted.Issue == ManagedDistributionPayloadIssue.Invalid
                    ? ManagedDistributionPayloadIssue.Changed
                    : admitted.Issue);
        }
        ManagedDistributionPayloadIdentity embedded = CreateIdentity(
            admitted.Descriptor!,
            expected.LauncherSize,
            expected.LauncherSha256);
        if (embedded != expected)
        {
            return new(null, ManagedDistributionPayloadIssue.Changed);
        }

        BootstrapReadResult bootstrap = await ReadBootstrapExactAsync(
            admitted.Bootstrap!,
            captureBytes: true,
            cancellationToken).ConfigureAwait(false);
        if (!bootstrap.IsSuccess)
        {
            return new(
                null,
                bootstrap.Issue == ManagedDistributionPayloadIssue.Invalid
                    ? ManagedDistributionPayloadIssue.Changed
                    : bootstrap.Issue);
        }

        ManagedExecutableLaunchLeaseResult acquired = await StableManagedExecutableLaunchLease
            .TryAcquireAsync(
                _launcherPath,
                expected.LauncherSize,
                expected.LauncherSha256,
                _maximumExecutableBytes,
                cancellationToken).ConfigureAwait(false);
        if (!acquired.IsAcquired)
        {
            return new(null, acquired.Issue switch
            {
                ManagedExecutableLaunchIssue.Tampered => ManagedDistributionPayloadIssue.Changed,
                ManagedExecutableLaunchIssue.UnsafePath => ManagedDistributionPayloadIssue.Invalid,
                ManagedExecutableLaunchIssue.Unavailable => ManagedDistributionPayloadIssue.Unavailable,
                ManagedExecutableLaunchIssue.None => throw new InvalidOperationException(
                    "A successful Launcher capture did not return custody."),
                _ => throw new InvalidOperationException(
                    "Launcher custody returned an undefined issue."),
            });
        }
        IManagedExecutableLaunchLease? ownedLauncher = acquired.Lease;
        try
        {
            if (ownedLauncher is not StableManagedExecutableLaunchLease stable)
            {
                return new(null, ManagedDistributionPayloadIssue.Invalid);
            }
            var result = new ManagedDistributionPayloadCaptureResult(null, ManagedDistributionPayloadIssue.None)
            {
                Capture = new EmbeddedPayloadCapture(expected, stable, bootstrap.Bytes!),
            };
            ownedLauncher = null;
            return result;
        }
        finally { ownedLauncher?.Dispose(); }
    }

    private async ValueTask<EntryProjectionResult> AdmitEntryCoreAsync(
        CancellationToken cancellationToken)
    {
        DescriptorProjectionResult projection = await ProjectDescriptorAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!projection.IsSuccess)
        {
            return EntryProjectionResult.Failure(projection.Issue);
        }
        ManagedSetupEmbeddedBootstrap bootstrap = projection.Descriptor!.Bootstrap;
        ManagedImmutableBootstrapIdentity identity;
        try
        {
            identity = ManagedImmutableBootstrapIdentity.Create(
                _descriptor, bootstrap.InstalledFileName,
                bootstrap.Size,
                bootstrap.Sha256);
        }
        catch (ArgumentException)
        {
            return EntryProjectionResult.Failure(ManagedDistributionPayloadIssue.Invalid);
        }
        // The configured ceiling bounds Bootstrap like the Launcher, and applies before the resource opens.
        if (identity.Length > _maximumExecutableBytes)
        {
            return EntryProjectionResult.Failure(ManagedDistributionPayloadIssue.Invalid);
        }

        ResourceLengthResult resource = ObserveBootstrapLength();
        return resource.Issue == ManagedDistributionPayloadIssue.None &&
            resource.Length == identity.Length
                ? new(projection, identity, ManagedDistributionPayloadIssue.None)
                : EntryProjectionResult.Failure(
                    resource.Issue == ManagedDistributionPayloadIssue.None
                        ? ManagedDistributionPayloadIssue.Invalid
                        : resource.Issue);
    }

    private async ValueTask<DescriptorProjectionResult> ProjectDescriptorAsync(
        CancellationToken cancellationToken)
    {
        Stream? descriptorStream;
        try
        {
            descriptorStream = _openDescriptor();
        }
        catch (Exception exception) when (IsResourceUnavailable(exception))
        {
            return DescriptorProjectionResult.Failure(ManagedDistributionPayloadIssue.Unavailable);
        }
        if (descriptorStream is null)
        {
            return DescriptorProjectionResult.Failure(ManagedDistributionPayloadIssue.Unavailable);
        }

        await using (descriptorStream)
        {
            try
            {
                if (!descriptorStream.CanRead)
                {
                    return DescriptorProjectionResult.Failure(ManagedDistributionPayloadIssue.Invalid);
                }
                byte[] buffer = new byte[_maximumDescriptorBytes + 1];
                int count = 0;
                while (count < buffer.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = await descriptorStream.ReadAsync(
                        buffer.AsMemory(count, buffer.Length - count),
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                    count += read;
                }
                if (count == 0 || count > _maximumDescriptorBytes)
                {
                    return DescriptorProjectionResult.Failure(ManagedDistributionPayloadIssue.Invalid);
                }
                byte[] descriptorBytes = buffer.AsSpan(0, count).ToArray();
                ManagedSetupPayloadDescriptor? document = _descriptorReader.ReadValidated(descriptorBytes);
                return document is not null && document.Bootstrap is not null &&
                    document.SourceCommit is { Length: 40 } &&
                    document.SourceCommit.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
                    ManagedAppVersion.TryParse(
                        document.LauncherVersion,
                        out ManagedAppVersion version) &&
                    string.Equals(
                        document.SourceCommit,
                        document.Bootstrap.SourceCommit,
                        StringComparison.Ordinal)
                        ? new(
                            document,
                            version,
                            descriptorBytes.LongLength,
                            Convert.ToHexString(SHA256.HashData(descriptorBytes)).ToLowerInvariant(),
                            ManagedDistributionPayloadIssue.None)
                        : DescriptorProjectionResult.Failure(
                            ManagedDistributionPayloadIssue.Invalid);
            }
            catch (Exception exception) when (IsResourceUnavailable(exception))
            {
                return DescriptorProjectionResult.Failure(ManagedDistributionPayloadIssue.Unavailable);
            }
            catch (Exception exception) when (exception is
                JsonException or ArgumentException or InvalidOperationException)
            {
                return DescriptorProjectionResult.Failure(ManagedDistributionPayloadIssue.Invalid);
            }
        }
    }

    private ManagedDistributionPayloadIdentity CreateIdentity(
        DescriptorProjectionResult projection,
        long launcherSize,
        string launcherSha256)
    {
        ManagedSetupPayloadDescriptor descriptor = projection.Descriptor!;
        return new(
            _maximumDescriptorBytes, projection.LauncherVersion,
            descriptor.SourceCommit,
            launcherSize,
            launcherSha256,
            projection.DescriptorSize,
            projection.DescriptorSha256,
            ManagedImmutableBootstrapIdentity.Create(
                _descriptor, descriptor.Bootstrap.InstalledFileName,
                descriptor.Bootstrap.Size,
                descriptor.Bootstrap.Sha256));
    }

    private ResourceLengthResult ObserveBootstrapLength()
    {
        Stream? bootstrap;
        try
        {
            bootstrap = _openBootstrap();
        }
        catch (Exception exception) when (IsResourceUnavailable(exception))
        {
            return new(0, ManagedDistributionPayloadIssue.Unavailable);
        }
        if (bootstrap is null)
        {
            return new(0, ManagedDistributionPayloadIssue.Unavailable);
        }

        using (bootstrap)
        {
            try
            {
                return bootstrap.CanRead
                    ? new(bootstrap.Length, ManagedDistributionPayloadIssue.None)
                    : new(0, ManagedDistributionPayloadIssue.Invalid);
            }
            catch (Exception exception) when (IsResourceUnavailable(exception))
            {
                return new(0, ManagedDistributionPayloadIssue.Unavailable);
            }
        }
    }

    private async ValueTask<BootstrapReadResult> ReadBootstrapExactAsync(
        ManagedImmutableBootstrapIdentity expected,
        bool captureBytes,
        CancellationToken cancellationToken)
    {
        Stream? bootstrap;
        try
        {
            bootstrap = _openBootstrap();
        }
        catch (Exception exception) when (IsResourceUnavailable(exception))
        {
            return BootstrapReadResult.Failure(ManagedDistributionPayloadIssue.Unavailable);
        }
        if (bootstrap is null)
        {
            return BootstrapReadResult.Failure(ManagedDistributionPayloadIssue.Unavailable);
        }

        await using (bootstrap)
        {
            try
            {
                if (!bootstrap.CanRead || bootstrap.Length != expected.Length)
                {
                    return BootstrapReadResult.Failure(ManagedDistributionPayloadIssue.Invalid);
                }

                BoundedReadResult read;
                try
                {
                    read = await BoundedFileReader.ReadAndHashAsync(bootstrap, expected.Length,
                        captureBytes ? FileCaptureMode.CaptureBytes : FileCaptureMode.IdentityOnly,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (FileChangedDuringReadException)
                {
                    return BootstrapReadResult.Failure(ManagedDistributionPayloadIssue.Invalid);
                }
                string sha256 = Convert.ToHexString(read.Sha256!).ToLowerInvariant().ToLowerInvariant();
                return string.Equals(sha256, expected.Sha256, StringComparison.Ordinal)
                    ? new(read.Bytes, ManagedDistributionPayloadIssue.None)
                    : BootstrapReadResult.Failure(ManagedDistributionPayloadIssue.Invalid);
            }
            catch (Exception exception) when (IsResourceUnavailable(exception))
            {
                return BootstrapReadResult.Failure(ManagedDistributionPayloadIssue.Unavailable);
            }
        }
    }

    private static bool IsResourceUnavailable(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or NotSupportedException or
            ObjectDisposedException;
    }

    private static ManagedDistributionPayloadIssue MapMeasurementIssue(
        ManagedExecutableLaunchIssue issue)
    {
        return issue switch
        {
            ManagedExecutableLaunchIssue.Tampered or ManagedExecutableLaunchIssue.UnsafePath =>
                ManagedDistributionPayloadIssue.Invalid,
            ManagedExecutableLaunchIssue.Unavailable => ManagedDistributionPayloadIssue.Unavailable,
            ManagedExecutableLaunchIssue.None => throw new InvalidOperationException(
                "A successful Launcher measurement returned no identity."),
            _ => throw new InvalidOperationException(
                "Launcher measurement returned an undefined issue."),
        };
    }

    private sealed record DescriptorProjectionResult(
        ManagedSetupPayloadDescriptor? Descriptor,
        ManagedAppVersion LauncherVersion,
        long DescriptorSize,
        string DescriptorSha256,
        ManagedDistributionPayloadIssue Issue)
    {
        internal bool IsSuccess => Descriptor is not null && Issue == ManagedDistributionPayloadIssue.None;

        internal static DescriptorProjectionResult Failure(ManagedDistributionPayloadIssue issue)
        {
            return new(null, default, 0, string.Empty, issue);
        }
    }

    private sealed record EntryProjectionResult(
        DescriptorProjectionResult? Descriptor,
        ManagedImmutableBootstrapIdentity? Bootstrap,
        ManagedDistributionPayloadIssue Issue)
    {
        internal bool IsSuccess =>
            Descriptor?.IsSuccess == true && Bootstrap is not null &&
            Issue == ManagedDistributionPayloadIssue.None;

        internal static EntryProjectionResult Failure(ManagedDistributionPayloadIssue issue)
        {
            return new(null, null, issue);
        }
    }

    private readonly record struct ResourceLengthResult(
        long Length,
        ManagedDistributionPayloadIssue Issue);

    private sealed record BootstrapReadResult(
        byte[]? Bytes,
        ManagedDistributionPayloadIssue Issue)
    {
        internal bool IsSuccess => Issue == ManagedDistributionPayloadIssue.None;

        internal static BootstrapReadResult Failure(ManagedDistributionPayloadIssue issue)
        {
            return new(null, issue);
        }
    }

    private static void FlushToDisk(FileStream stream) => stream.Flush(flushToDisk: true);

    private sealed class EmbeddedPayloadCapture(
        ManagedDistributionPayloadIdentity identity,
        StableManagedExecutableLaunchLease launcher,
        byte[] bootstrapBytes)
        : IManagedDistributionPayloadCapture, IManagedDistributionPayloadContent
    {
        public ManagedDistributionPayloadIdentity Identity { get; } = identity;

        public ValueTask CopyDistributionLauncherAsync(
            string destination,
            CancellationToken cancellationToken)
        {
            return launcher.CopyToAsync(destination, cancellationToken);
        }

        public async ValueTask CopyBootstrapAsync(
            string destination,
            CancellationToken cancellationToken)
        {
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await output.WriteAsync(bootstrapBytes, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            FlushToDisk(output);
        }

        public void Dispose()
        {
            launcher.Dispose();
        }
    }
}

