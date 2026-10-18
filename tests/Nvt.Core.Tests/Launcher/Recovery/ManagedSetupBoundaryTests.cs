// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Recovery;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.TestSupport;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>Checks explicit byte ceilings, exact capture continuity and abandoned reads.</summary>
public sealed class ManagedSetupBoundaryTests
{
    /// <summary>Every positive product limit rejects zero and negative input.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PositiveLimitsRejectNonPositiveValues(int limit)
    {
        using var fixture = new SetupFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemManagedFirstInstallationRootMaterializer(
            fixture.Repository, fixture.Repository.InstallHeldAsync, new(10, 10, 100), SetupFixture.Layout, fixture.SeedStore,
            fixture.Codec, limit, SetupFixture.Ceiling, 200_000_000, new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemManagedFirstInstallationRootMaterializer(
            fixture.Repository, fixture.Repository.InstallHeldAsync, new(10, 10, 100), SetupFixture.Layout, fixture.SeedStore,
            fixture.Codec, SetupFixture.Ceiling, limit, 200_000_000, new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedSetupMarkerIO(fixture.Codec, limit, SetupFixture.Ceiling));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedSetupMarkerIO(fixture.Codec, SetupFixture.Ceiling, limit));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemManagedSetupRecoveryProbe(fixture.Codec, limit, SetupFixture.Ceiling));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemManagedSetupRecoveryProbe(fixture.Codec, SetupFixture.Ceiling, limit));
        Assert.Throws<ArgumentOutOfRangeException>(() => Source([], new DescriptorReader(), maximumDescriptorBytes: limit));
        Assert.Throws<ArgumentOutOfRangeException>(() => Source([], new DescriptorReader(), maximumExecutableBytes: limit));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemManagedFirstInstallationRootMaterializer(
            fixture.Repository, fixture.Repository.InstallHeldAsync, new(10, 10, 100), SetupFixture.Layout, fixture.SeedStore,
            fixture.Codec, SetupFixture.Ceiling, SetupFixture.Ceiling, limit, new()));
    }

    /// <summary>Explicit executable and descriptor storage bounds admit exact endpoints and reject one above.</summary>
    [Theory]
    [InlineData(199_999_999, true)]
    [InlineData(200_000_000, true)]
    [InlineData(200_000_001, false)]
    public void ExecutableCeilingPreservesFixedMaximum(long limit, bool admitted)
    {
        if (admitted) Assert.NotNull(Source([], new DescriptorReader(), maximumExecutableBytes: limit));
        else Assert.Throws<ArgumentOutOfRangeException>(() => Source([], new DescriptorReader(), maximumExecutableBytes: limit));
    }

    /// <summary>Descriptor storage accounts for the one-byte overflow probe without integer overflow.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void DescriptorStorageCeilingHasExactBoundary(int offset)
    {
        int limit = Array.MaxLength - 1 + offset;
        if (offset <= 0) Assert.NotNull(Source([], new DescriptorReader(), maximumDescriptorBytes: limit));
        else Assert.Throws<ArgumentOutOfRangeException>(() => Source([], new DescriptorReader(), maximumDescriptorBytes: limit));
    }

    /// <summary>Descriptor reads preserve the exact inclusive NFC ceiling and never call the adapter outside it.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(65_535, true)]
    [InlineData(65_536, true)]
    [InlineData(65_537, false)]
    public async Task DescriptorByteBoundaries(int length, bool admitted)
    {
        var reader = new DescriptorReader();
        var source = Source(new byte[length], reader);
        var result = await source.AdmitEntryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(admitted, result.IsSuccess);
        Assert.Equal(admitted ? 1 : 0, reader.Calls);
        if (admitted) Assert.Equal(length, reader.ExactBytes);
    }

    /// <summary>The configured executable ceiling bounds the embedded Bootstrap before its resource opens.</summary>
    [Theory]
    [InlineData(4, true)]
    [InlineData(3, true)]
    [InlineData(2, false)]
    public async Task BootstrapSizeObeysConfiguredExecutableCeiling(long limit, bool admitted)
    {
        int opens = 0;
        var source = Source("descriptor"u8.ToArray(), new DescriptorReader(), maximumExecutableBytes: limit,
            bootstrapOpened: () => opens++);
        var admission = await source.AdmitEntryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(admitted, admission.IsSuccess);
        Assert.Equal(admitted ? 1 : 0, opens);
        if (admitted) return;

        Assert.Equal(ManagedDistributionPayloadIssue.Invalid, admission.Issue);
        var inspection = await source.InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedDistributionPayloadIssue.Invalid, inspection.Issue);
        var expected = new ManagedDistributionPayloadIdentity(SetupFixture.Ceiling, ManagedAppVersion.Parse("1.0.4"),
            new string('c', 40), 123, SetupFixture.HashA, "descriptor"u8.Length, SetupFixture.Hash("descriptor"u8),
            ManagedImmutableBootstrapIdentity.Create(ContractFixture.Descriptor, SetupFixture.Layout.BootstrapFileName,
                3, SetupFixture.Hash(new byte[] { 1, 2, 3 })));
        var capture = await source.CaptureExactAsync(expected, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedDistributionPayloadIssue.Changed, capture.Issue);
        Assert.Null(capture.Capture);
        Assert.Equal(0, opens);
    }

    /// <summary>A Bootstrap stream that drifts after its last byte is invalid. The frozen NFC reader accepted it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BootstrapStreamThatDriftsAfterItsLastByteIsInvalid(bool lengthDrift)
    {
        var source = new EmbeddedManagedDistributionPayloadSource(
            Path.Combine(AppContext.BaseDirectory, "missing-synthetic-launcher.exe"),
            () => new MemoryStream("descriptor"u8.ToArray(), writable: false),
            () => new DriftingStream([1, 2, 3], lengthDrift),
            ContractFixture.Descriptor, new DescriptorReader(), SetupFixture.Ceiling, 200_000_000);

        Assert.True((await source.AdmitEntryAsync(TestContext.Current.CancellationToken)).IsSuccess);
        var inspection = await source.InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManagedDistributionPayloadIssue.Invalid, inspection.Issue);
    }

    /// <summary>Marker reads preserve positive exact-size and one-over bounds independently of the strict codec.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(65_535, true)]
    [InlineData(65_536, true)]
    [InlineData(65_537, false)]
    public async Task MarkerByteBoundaries(int length, bool admitted)
    {
        using var fixture = new SetupFixture();
        var codec = new SyntheticMarkerCodec { PadTo = length };
        var io = new ManagedSetupMarkerIO(codec, SetupFixture.Ceiling, SetupFixture.Ceiling);
        byte[] bytes = length == 0 ? [] : codec.Encode(fixture.Marker()).ToArray();
        await using var stream = new MemoryStream(bytes, writable: false);
        var result = await io.ReadAsync(stream, TestContext.Current.CancellationToken);
        Assert.Equal(admitted, result is not null);
        if (admitted) Assert.Equal(fixture.Marker().Payload, result!.Payload);
        else if (length > SetupFixture.Ceiling) Assert.Throws<InvalidDataException>(() => io.Serialize(fixture.Marker()));
    }

    /// <summary>Mechanical descriptor lengths are checked again after strict marker admission.</summary>
    [Theory]
    [InlineData(65_535, true)]
    [InlineData(65_536, true)]
    [InlineData(65_537, false)]
    public async Task MarkerDescriptorIdentityLimits(long descriptorSize, bool admitted)
    {
        using var fixture = new SetupFixture();
        var marker = fixture.Marker();
        marker = marker with { Payload = marker.Payload with { DescriptorSize = descriptorSize } };
        var io = new ManagedSetupMarkerIO(fixture.Codec, SetupFixture.Ceiling, SetupFixture.Ceiling);
        await using var stream = new MemoryStream(fixture.Codec.Encode(marker).ToArray(), writable: false);
        Assert.Equal(admitted, await io.ReadAsync(stream, TestContext.Current.CancellationToken) is not null);
    }

    /// <summary>Mandatory adapter rejection and malformed marker bytes never admit a transaction.</summary>
    [Fact]
    public async Task MalformedAndRejectedMarkersStayClosed()
    {
        using var fixture = new SetupFixture();
        foreach (byte[] bytes in new[] { Encoding.UTF8.GetBytes("{}"), Encoding.UTF8.GetBytes("malformed"), new byte[] { 1 } })
        {
            var io = new ManagedSetupMarkerIO(fixture.Codec, SetupFixture.Ceiling, SetupFixture.Ceiling);
            await using var stream = new MemoryStream(bytes, writable: false);
            Assert.Null(await io.ReadAsync(stream, TestContext.Current.CancellationToken));
        }
        var rejected = new ManagedSetupMarkerIO(new SyntheticMarkerCodec { Reject = true }, SetupFixture.Ceiling, SetupFixture.Ceiling);
        Assert.Throws<InvalidDataException>(() => rejected.Serialize(fixture.Marker()));
        Assert.Throws<ArgumentNullException>(() => new ManagedSetupMarkerIO(null!, 1, 1));
        Assert.Throws<ArgumentNullException>(() => Source([], null!));
    }

    /// <summary>Descriptor and Bootstrap drift fail capture before any executable custody is requested.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaptureRejectsDescriptorAndBootstrapDrift(bool descriptorDrift)
    {
        byte[] descriptor = "descriptor"u8.ToArray();
        var reader = new DescriptorReader();
        var expected = new ManagedDistributionPayloadIdentity(SetupFixture.Ceiling, ManagedAppVersion.Parse("1.0.4"),
            new string('c', 40), 123, SetupFixture.HashA, descriptor.Length, SetupFixture.Hash(descriptor),
            ManagedImmutableBootstrapIdentity.Create(ContractFixture.Descriptor, SetupFixture.Layout.BootstrapFileName,
                3, SetupFixture.Hash(new byte[] { 1, 2, 3 })));
        var source = Source(descriptorDrift ? "changed"u8.ToArray() : descriptor, reader,
            bootstrapBytes: descriptorDrift ? [1, 2, 3] : [3, 2, 1]);
        var result = await source.CaptureExactAsync(expected, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedDistributionPayloadIssue.Changed, result.Issue);
        Assert.Null(result.Capture);
    }

    /// <summary>Abandoning a blocked local observation creates no filesystem entry.</summary>
    [Fact]
    public async Task BlockedRootObservationCanBeAbandonedWithoutMutation()
    {
        using var fixture = new SetupFixture();
        var before = await fixture.SnapshotAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new FileSystemManagedInstallationRootProbe(_ =>
        {
            entered.SetResult();
            TaskBlock.UntilComplete(release.Task);
            finished.SetResult();
            return new(ManagedInstallationRootStatus.Present);
        });
        using var cancellation = new CancellationTokenSource();
        Task observation = probe.ObserveAsync(fixture.Root, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await observation.WaitAsync(TestContext.Current.CancellationToken)); }
        finally { release.SetResult(); }
        await finished.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    private static EmbeddedManagedDistributionPayloadSource Source(byte[] bytes, IManagedSetupPayloadDescriptorReader reader,
        int maximumDescriptorBytes = SetupFixture.Ceiling, long maximumExecutableBytes = 200_000_000,
        byte[]? bootstrapBytes = null, Action? bootstrapOpened = null) => new(
            Path.Combine(AppContext.BaseDirectory, "missing-synthetic-launcher.exe"),
            () => new MemoryStream(bytes, writable: false),
            () =>
            {
                bootstrapOpened?.Invoke();
                return new MemoryStream(bootstrapBytes ?? [1, 2, 3], writable: false);
            },
            ContractFixture.Descriptor, reader, maximumDescriptorBytes, maximumExecutableBytes);

    private sealed class DriftingStream(byte[] bytes, bool lengthDrift) : MemoryStream(bytes, writable: false)
    {
        private bool AtEnd => base.Position == bytes.Length;
        public override long Length => lengthDrift && AtEnd ? base.Length + 1 : base.Length;
        public override long Position
        {
            get => !lengthDrift && AtEnd ? base.Position + 1 : base.Position;
            set => base.Position = value;
        }
    }

    private sealed class DescriptorReader : IManagedSetupPayloadDescriptorReader
    {
        internal int Calls { get; private set; }
        internal int ExactBytes { get; private set; }
        public ManagedSetupPayloadDescriptor ReadValidated(ReadOnlyMemory<byte> exactBytes)
        {
            Calls++; ExactBytes = exactBytes.Length;
            return new("1.0.4", new string('c', 40), new(SetupFixture.Layout.BootstrapFileName,
                3, SetupFixture.Hash(new byte[] { 1, 2, 3 }), new string('c', 40)));
        }
    }
}
