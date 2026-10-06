// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Verification;

internal sealed class PackageFixture
{
    internal static readonly ManagedAppVersion Version = ManagedAppVersion.Parse("0.10.6");
    internal static readonly PackageVerificationLimits FrozenLimits = new(4096, 134_217_728, 536_870_912,
        1_048_576, 4096, 200_000_000, 4096);
    internal byte[] PackageBytes { get; private set; } = [];
    internal byte[] ManifestBytes { get; private set; } = [];
    internal byte[] ChecksumBytes { get; private set; } = [];
    internal Dictionary<string, byte[]> Files { get; private set; } = new(StringComparer.Ordinal);
    internal PackageManifest Manifest { get; private set; } = null!;
    internal FixturePolicy Policy { get; private set; } = null!;
    internal UpdateCatalogVersionSnapshot Candidate => CandidateFor(PackageBytes);
    internal long ExpandedBytes => ManifestBytes.LongLength + ChecksumBytes.LongLength + Files.Sum(pair => pair.Value.LongLength);

    internal static PackageFixture Create(Dictionary<string, byte[]>? files = null,
        bool includeLauncher = false, Func<byte[], byte[]>? mutateManifest = null,
        Func<byte[], byte[]>? mutateChecksums = null, Action<ZipArchive, string>? mutateArchive = null,
        Func<byte[], byte[]>? mutatePackage = null, string? omittedPayload = null,
        Func<PackageManifest, PackageManifest?>? project = null,
        CompressionLevel compressionLevel = CompressionLevel.NoCompression)
    {
        var fixture = new PackageFixture
        {
            Files = files ?? new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [ContractFixture.ApplicationPath] = [0x4d, 0x5a, 0x01],
                ["reference/tool.bin"] = [0x4d, 0x5a, 0x02],
                ["THIRD-PARTY-NOTICES.txt"] = "notices"u8.ToArray(),
                ["LICENSE.txt"] = "license"u8.ToArray(),
                ["README.txt"] = "readme"u8.ToArray(),
                ["reference/policy.json"] = "{}"u8.ToArray(),
                ["reference/index.json"] = "{}"u8.ToArray(),
            },
        };
        if (includeLauncher)
        {
            fixture.Files[ContractFixture.LauncherPath] = [0x4d, 0x5a, 0x03];
        }
        PackageFile[] declarations = [.. fixture.Files.Select(pair => new PackageFile(pair.Key, pair.Value.LongLength, Hash(pair.Value)))];
        fixture.Manifest = new(ContractFixture.ProductId, ContractFixture.RuntimeIdentifier, Version,
            declarations, includeLauncher ? new(Version, 1, ContractFixture.LauncherPath,
                fixture.Files[ContractFixture.LauncherPath].LongLength, Hash(fixture.Files[ContractFixture.LauncherPath])) : null);
        // A synthetic schema solely for proving the mandatory adapter boundary.
        fixture.ManifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "fixture-1", role = "reference", assetName = "fixture.spdx.json",
            files = declarations.Select(file => new { path = file.Path, size = file.Size, sha256 = file.Sha256 }),
        });
        if (mutateManifest is not null)
        {
            fixture.ManifestBytes = mutateManifest(fixture.ManifestBytes);
        }
        fixture.ChecksumBytes = Encoding.UTF8.GetBytes(string.Join('\n', declarations
            .Select(file => (file.Path, file.Sha256))
            .Append((ManagedPackageVerifier.ManifestFileName, Hash(fixture.ManifestBytes)))
            .OrderBy(pair => pair.Item1, StringComparer.Ordinal)
            .Select(pair => $"{pair.Item2}  {pair.Item1}")) + "\n");
        if (mutateChecksums is not null)
        {
            fixture.ChecksumBytes = mutateChecksums(fixture.ChecksumBytes);
        }
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            string root = ContractFixture.Descriptor.GetArchiveRootName(Version);
            WriteEntry(archive, root + "/" + ManagedPackageVerifier.ManifestFileName, fixture.ManifestBytes, compressionLevel);
            if (omittedPayload != ManagedPackageVerifier.ChecksumFileName)
            {
                WriteEntry(archive, root + "/" + ManagedPackageVerifier.ChecksumFileName, fixture.ChecksumBytes, compressionLevel);
            }
            foreach ((string path, byte[] content) in fixture.Files)
            {
                if (!string.Equals(path, omittedPayload, StringComparison.Ordinal))
                {
                    WriteEntry(archive, root + "/" + path, content, compressionLevel);
                }
            }
            mutateArchive?.Invoke(archive, root);
        }
        fixture.PackageBytes = mutatePackage is null ? buffer.ToArray() : mutatePackage(buffer.ToArray());
        fixture.Policy = new FixturePolicy(fixture.Manifest, project);
        return fixture;
    }

    internal ManagedPackageVerifier Verifier(PackageVerificationLimits? limits = null, IProductPackagePolicy? policy = null,
        ProductDescriptor? descriptor = null) => new(descriptor ?? ContractFixture.Descriptor, policy ?? Policy, limits ?? FrozenLimits);

    internal UpdateCatalogVersionSnapshot CandidateFor(byte[] bytes, string? manifestHash = null, string? packagePath = null) =>
        ContractFixture.CreateSnapshot(version: Version, packageSize: bytes.LongLength, packageSha: Hash(bytes),
            manifestSha: manifestHash ?? Hash(ManifestBytes), maximumPackageBytes: Math.Max(bytes.LongLength, FrozenLimits.MaximumPackageBytes),
            packagePath: packagePath ?? "packages/fixture-v0.10.6.zip");

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static void WriteEntry(ZipArchive archive, string path, byte[] content, CompressionLevel compressionLevel = CompressionLevel.NoCompression)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, compressionLevel);
        using Stream output = entry.Open();
        output.Write(content);
    }

    internal static byte[] UnderreportEntry(byte[] bytes, string relativePath, uint length)
    {
        int central = CentralStart(bytes);
        int count = EntryCount(bytes);
        for (int index = 0; index < count; index++)
        {
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(central + 28, 2));
            string name = Encoding.UTF8.GetString(bytes, central + 46, nameLength);
            if (name.EndsWith('/' + relativePath, StringComparison.Ordinal))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24, 4), length);
                int local = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(central + 42, 4)));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(local + 22, 4), length);
                return bytes;
            }
            central += CentralRecordLength(bytes, central);
        }
        throw new InvalidOperationException("Synthetic entry was not found.");
    }

    internal static byte[] PadPackageTo(byte[] original, int length)
    {
        int padding = length - original.Length;
        Assert.True(padding >= 0);
        byte[] bytes = new byte[length];
        original.CopyTo(bytes, padding);
        int central = CentralStart(original);
        int count = EntryCount(original);
        for (int index = 0; index < count; index++)
        {
            int field = central + padding + 42;
            uint local = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(field, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(field, 4), checked(local + (uint)padding));
            central += CentralRecordLength(original, central);
        }
        int end = EndOffset(original) + padding;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(end + 16, 4), checked((uint)(CentralStart(original) + padding)));
        return bytes;
    }

    internal static byte[] EmptyEntryWithPositiveDeclaredLength(byte[] bytes, string relativePath)
    {
        UnderreportEntry(bytes, relativePath, 1);
        int central = CentralStart(bytes);
        for (int index = 0; index < EntryCount(bytes); index++)
        {
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(central + 28, 2));
            string name = Encoding.UTF8.GetString(bytes, central + 46, nameLength);
            if (name.EndsWith('/' + relativePath, StringComparison.Ordinal))
            {
                int local = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(central + 42, 4)));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 20, 4), 0);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 16, 4), 0);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(local + 18, 4), 0);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(local + 14, 4), 0);
                return bytes;
            }
            central += CentralRecordLength(bytes, central);
        }
        throw new InvalidOperationException("Synthetic entry was not found.");
    }

    internal static byte[] InflateSecondCentralEntryToLongMax(byte[] original)
    {
        int oldEnd = EndOffset(original);
        int central = CentralStart(original);
        int second = central + CentralRecordLength(original, central);
        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(second + 28, 2));
        int oldExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(second + 30, 2));
        int insertion = second + 46 + nameLength + oldExtraLength;
        ReadOnlySpan<byte> zip64Extra = [1, 0, 8, 0, 255, 255, 255, 255, 255, 255, 255, 127];
        byte[] bytes = new byte[original.Length + zip64Extra.Length];
        original.AsSpan(0, insertion).CopyTo(bytes);
        zip64Extra.CopyTo(bytes.AsSpan(insertion));
        original.AsSpan(insertion).CopyTo(bytes.AsSpan(insertion + zip64Extra.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(second + 24, 4), uint.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(second + 30, 2), checked((ushort)(oldExtraLength + zip64Extra.Length)));
        int newEnd = oldEnd + zip64Extra.Length;
        uint oldSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(newEnd + 12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(newEnd + 12, 4), oldSize + (uint)zip64Extra.Length);
        return bytes;
    }

    internal static int EndOffset(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [0x50, 0x4b, 0x05, 0x06];
        int end = bytes.AsSpan().LastIndexOf(signature);
        Assert.True(end >= 0);
        return end;
    }

    internal static int CentralStart(byte[] bytes) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(EndOffset(bytes) + 16, 4)));
    private static int EntryCount(byte[] bytes) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(EndOffset(bytes) + 10, 2));
    private static int CentralRecordLength(byte[] bytes, int offset) => 46 +
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28, 2)) +
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 30, 2)) +
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 32, 2));
}

internal sealed class FixturePolicy(PackageManifest projection, Func<PackageManifest, PackageManifest?>? transform = null) : IProductPackagePolicy
{
    internal int Calls { get; private set; }
    internal int Projections { get; private set; }
    internal byte[]? ObservedBytes { get; private set; }
    internal IReadOnlyCollection<string>? ObservedPaths { get; private set; }
    internal Exception? Exception { get; set; }
    internal bool Reject { get; set; }
    internal Action? BeforeProjection { get; set; }
    internal Func<string, bool>? PathPolicy { get; set; }

    public bool IsSafeRelativePayloadPath(string path) => PathPolicy?.Invoke(path) ?? true;

    public bool TryReadManifest(ReadOnlyMemory<byte> exactBytes, ManagedAppVersion expectedVersion,
        IReadOnlyCollection<string>? archivePaths, out PackageManifest? manifest)
    {
        Calls++;
        ObservedBytes = exactBytes.ToArray();
        ObservedPaths = archivePaths;
        manifest = null;
        if (Exception is not null)
        {
            throw Exception;
        }
        if (Reject)
        {
            return false;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(exactBytes);
            JsonElement root = document.RootElement;
            string? assetName = root.GetProperty("assetName").GetString();
            // The frozen <=200 asset-name predicate belongs to strict manifest admission.
            if (root.EnumerateObject().Count() != 4 || root.GetProperty("schema").GetString() != "fixture-1" ||
                root.GetProperty("role").GetString() != "reference" || string.IsNullOrWhiteSpace(assetName) ||
                assetName.Length > 200 || assetName.Contains('/') || assetName.Contains('\\'))
            {
                return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
        BeforeProjection?.Invoke();
        Projections++;
        manifest = transform is null ? projection : transform(projection);
        return true;
    }
}
