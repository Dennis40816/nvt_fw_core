// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Buffers.Binary;
using System.Text.Json;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Repository;
using Nvt.Core.Tests.Files;
using Nvt.Core.Tests.Launcher.Contracts;
using Nvt.Core.Tests.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Repository;

internal sealed class RepositoryFixture : IDisposable
{
    private readonly TestWorkspace workspace = new();

    internal RepositoryFixture(PackageFixture? package = null, PackageVerificationLimits? limits = null,
        RepositoryOperations? operations = null, AdmissionCodec? codec = null)
    {
        Package = package ?? PackageFixture.Create(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ContractFixture.ApplicationPath] = PortableExecutable(),
            ["reference/nested/data.bin"] = [1, 2, 3, 4],
            ["README.txt"] = "readme"u8.ToArray(),
        });
        Limits = limits ?? PackageFixture.FrozenLimits;
        Codec = codec ?? new AdmissionCodec();
        SourceRoot = PathFor("source");
        ManagedRoot = Directory.CreateDirectory(PathFor("managed")).FullName;
        workspace.Write("source/" + Package.Candidate.PackagePath.Value, Package.PackageBytes);
        Repository = new FileSystemManagedVersionRepository(ContractFixture.Descriptor, Package.Policy, Limits, Codec,
            operations ?? new RepositoryOperations());
    }

    internal PackageFixture Package { get; }
    internal PackageVerificationLimits Limits { get; }
    internal AdmissionCodec Codec { get; }
    internal FileSystemManagedVersionRepository Repository { get; }
    internal string SourceRoot { get; }
    internal string ManagedRoot { get; }
    internal string VersionRoot => Path.Combine(ManagedRoot, "versions", PackageFixture.Version.ToString());
    internal string PathFor(string relative) => workspace.PathFor(relative);
    internal string InstalledPath(string relative) => Path.Combine(VersionRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    internal ValueTask<ManagedVersionInstallResult> InstallAsync() => Repository.InstallAsync(ManagedRoot, SourceRoot,
        Package.Candidate, TestContext.Current.CancellationToken);
    internal ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(ManagedVersionAdmission admission,
        ManagedAppVersion? failed = null) => Repository.InventoryAsync(ManagedRoot, [admission], admission.Version,
            admission.Version, failed, TestContext.Current.CancellationToken);

    internal async ValueTask<ManagedVersionAdmission> SeedInstalledAsync()
    {
        Directory.CreateDirectory(VersionRoot);
        foreach ((string path, byte[] bytes) in Package.Files)
        {
            string target = InstalledPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, bytes, TestContext.Current.CancellationToken);
        }
        await File.WriteAllBytesAsync(InstalledPath("RELEASE-MANIFEST.json"), Package.ManifestBytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(InstalledPath("SHA256SUMS.txt"), Package.ChecksumBytes, TestContext.Current.CancellationToken);
        var admission = new ManagedVersionAdmission(PackageFixture.Version, Package.Candidate.Identity, Package.Candidate.ReleaseManifestSha256);
        await File.WriteAllBytesAsync(InstalledPath(".managed-admission.v1.json"), Codec.Encode(admission).ToArray(),
            TestContext.Current.CancellationToken);
        return admission;
    }

    internal void AssertEmptyStaging()
    {
        string staging = Path.Combine(ManagedRoot, ".staging");
        Assert.True(!Directory.Exists(staging) || !Directory.EnumerateFileSystemEntries(staging).Any());
    }

    internal static byte[] PortableExecutable(byte marker = 1)
    {
        byte[] bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c, 4), 64);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(64));
        bytes[^1] = marker;
        return bytes;
    }

    internal static void RequireWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        var issue = WindowsStableRelativeWriteRoot.TryAcquire(workspace.Root, out var root);
        root?.Dispose();
        Assert.SkipWhen(issue == WindowsStableCustodyIssue.AccessDenied,
            "The Windows fixture's ancestor chain denies the native custody access required by this test.");
        Assert.Equal(WindowsStableCustodyIssue.None, issue);
    }
    public void Dispose() => workspace.Dispose();
}

internal sealed class AdmissionCodec : IManagedVersionAdmissionCodec
{
    internal int? PadToBytes { get; init; }
    internal Func<ManagedVersionAdmission, ReadOnlyMemory<byte>>? Encoder { get; init; }
    internal bool Reject { get; set; }

    public ReadOnlyMemory<byte> Encode(ManagedVersionAdmission admission)
    {
        if (Encoder is not null)
        {
            return Encoder(admission);
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = admission.Version.ToString(), admissionIdentity = admission.AdmissionIdentity,
            releaseManifestSha256 = admission.ReleaseManifestSha256,
        });
        if (PadToBytes is { } length)
        {
            byte[] padded = new byte[length];
            bytes.CopyTo(padded, 0);
            padded.AsSpan(bytes.Length).Fill((byte)' ');
            bytes = padded;
        }
        return bytes;
    }

    public bool TryDecode(ReadOnlyMemory<byte> exactBytes, out ManagedVersionAdmission? admission)
    {
        admission = null;
        if (Reject)
        {
            return false;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(exactBytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3 ||
                !ManagedAppVersion.TryParse(root.GetProperty("version").GetString(), out ManagedAppVersion version))
            {
                return false;
            }
            admission = new(version, root.GetProperty("admissionIdentity").GetString()!,
                root.GetProperty("releaseManifestSha256").GetString()!);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }
}
