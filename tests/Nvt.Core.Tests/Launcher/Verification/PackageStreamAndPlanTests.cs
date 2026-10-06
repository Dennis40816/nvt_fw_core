// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Verification;
using Nvt.Core.Tests.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Verification;

/// <summary>Checks borrowed stream custody, Files read ordering and internal closed-plan lifetime.</summary>
public sealed class PackageStreamAndPlanTests
{
    /// <summary>The public verifier requires all mandatory dependencies and exports no plan or extraction API.</summary>
    [Fact]
    public void PublicSurfaceRequiresPolicyAndKeepsPlansInternal()
    {
        PackageFixture fixture = PackageFixture.Create();
        Assert.Equal("descriptor", Assert.Throws<ArgumentNullException>(() => new ManagedPackageVerifier(null!, fixture.Policy, PackageFixture.FrozenLimits)).ParamName);
        Assert.Equal("productPolicy", Assert.Throws<ArgumentNullException>(() => new ManagedPackageVerifier(ContractFixture.Descriptor, null!, PackageFixture.FrozenLimits)).ParamName);
        Assert.Equal("limits", Assert.Throws<ArgumentNullException>(() => new ManagedPackageVerifier(ContractFixture.Descriptor, fixture.Policy, null!)).ParamName);
        Assert.Equal(nameof(ManagedPackageVerifier), Assert.Single(typeof(ManagedPackageVerifier).Assembly.GetExportedTypes(),
            type => type.Namespace == "Nvt.Core.Launcher.Verification").Name);
        Assert.Equal(nameof(ManagedPackageVerifier.VerifyAsync), Assert.Single(typeof(ManagedPackageVerifier)
            .GetMethods(System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)).Name);
    }

    /// <summary>Null argument validation precedes cancellation and no input is disposed.</summary>
    [Fact]
    public async Task NullArgumentsPrecedeCancellation()
    {
        PackageFixture fixture = PackageFixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var package = new MemoryStream(fixture.PackageBytes);
        ArgumentNullException missingStream = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        { await fixture.Verifier().VerifyAsync(null!, null!, cancellation.Token); });
        Assert.Equal("package", missingStream.ParamName);
        ArgumentNullException missingCandidate = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        { await fixture.Verifier().VerifyAsync(package, null!, cancellation.Token); });
        Assert.Equal("candidate", missingCandidate.ParamName);
        Assert.True(package.CanRead);
    }

    /// <summary>Unsupported capability shapes fail without asking for metadata or reading buffered content.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task UnsupportedStreamsAreRejectedWithoutBuffering(bool readable, bool seekable)
    {
        PackageFixture fixture = PackageFixture.Create();
        await using var package = new ObservedPackageStream(fixture.PackageBytes) { Readable = readable, Seekable = seekable, FaultOnMetadata = true };
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedVersionInstallIssue.PackageUnavailable, result.Issue);
        Assert.Equal(0, package.AsyncReads);
        Assert.Equal(0, package.SyncReads);
        Assert.False(package.WasDisposed);
        Assert.Equal(0, fixture.Policy.Calls);
    }

    /// <summary>Files completes the one-byte EOF probe and final metadata checks before ZIP admission.</summary>
    [Theory]
    [InlineData("short")]
    [InlineData("growth")]
    [InlineData("final-length")]
    [InlineData("position")]
    [InlineData("read-fault")]
    public async Task CompressedReadFaultsNeverReachZipOrPolicy(string fault)
    {
        PackageFixture fixture = PackageFixture.Create();
        byte[] content = fault switch
        {
            "short" => fixture.PackageBytes[..^1],
            "growth" => [.. fixture.PackageBytes, 1],
            _ => fixture.PackageBytes,
        };
        await using var package = new ObservedPackageStream(content)
        {
            ObservedLength = fixture.PackageBytes.LongLength,
            FinalLength = fault == "final-length" ? fixture.PackageBytes.LongLength - 1 : null,
            WrongFinalPosition = fault == "position",
            ReadFault = fault == "read-fault" ? new IOException("synthetic compressed read fault") : null,
        };
        ManagedPackageVerificationResult result = await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        Assert.Equal(ManagedVersionInstallIssue.PackageUnavailable, result.Issue);
        Assert.Equal(0, fixture.Policy.Calls);
        Assert.Equal(0, package.SyncReads);
        Assert.False(package.WasDisposed);
        if (fault is "growth" or "final-length" or "position") Assert.Equal(1, package.LastRequested);
        if (fault == "growth") Assert.Equal(fixture.PackageBytes.LongLength + 1, package.BytesRead);
        Assert.True(package.MaximumRequested <= 65_536);
    }

    /// <summary>A matching compressed stream performs its exact-length one-byte probe and leaves custody with the caller.</summary>
    [Fact]
    public async Task CompleteCompressedReadHasOneHashOwnerAndOneByteProbe()
    {
        PackageFixture fixture = PackageFixture.Create();
        await using var package = new ObservedPackageStream(fixture.PackageBytes);
        Assert.True((await fixture.Verifier().VerifyAsync(package, fixture.Candidate, TestContext.Current.CancellationToken)).IsVerified);
        Assert.Equal(fixture.PackageBytes.LongLength, package.BytesRead);
        Assert.Equal(1, package.LastRequested);
        Assert.True(package.SyncReads > 0);
        Assert.True(package.HashProbeCompletedBeforeZipRead);
        Assert.False(package.WasDisposed);
    }

    /// <summary>Cancellation at a deterministic held read propagates without ZIP admission or releasing caller custody.</summary>
    [Fact]
    public async Task CancellationDuringCompressedReadPreservesCustody()
    {
        PackageFixture fixture = PackageFixture.Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var package = new ObservedPackageStream(fixture.PackageBytes) { Gate = true };
        Task<ManagedPackageVerificationResult> verification = fixture.Verifier().VerifyAsync(package, fixture.Candidate, cancellation.Token).AsTask();
        await package.ReadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(verification.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await verification; });
        Assert.Equal(cancellation.Token, package.LastToken);
        Assert.Equal(0, fixture.Policy.Calls);
        Assert.False(package.WasDisposed);
    }

    /// <summary>Cancellation requested by the policy is observed before checksum or payload work.</summary>
    [Fact]
    public async Task CancellationAfterManifestAdmissionPropagates()
    {
        PackageFixture fixture = PackageFixture.Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.Policy.BeforeProjection = cancellation.Cancel;
        await using var package = new MemoryStream(fixture.PackageBytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        { await fixture.Verifier().VerifyAsync(package, fixture.Candidate, cancellation.Token); });
        Assert.True(package.CanRead);
        Assert.Equal(1, fixture.Policy.Projections);
    }

    /// <summary>A pre-cancelled call reads nothing and cannot admit any normalized facts.</summary>
    [Fact]
    public async Task PreCancelledVerificationDoesNoRead()
    {
        PackageFixture fixture = PackageFixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var package = new ObservedPackageStream(fixture.PackageBytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        { await fixture.Verifier().VerifyAsync(package, fixture.Candidate, cancellation.Token); });
        Assert.Equal(0, package.AsyncReads);
        Assert.Equal(0, package.SyncReads);
        Assert.False(package.WasDisposed);
    }

    /// <summary>Closed plans snapshot adapter inventories, own only their ZIP reader and reject use after disposal.</summary>
    [Fact]
    public async Task PlanSnapshotsFactsAndLeavesThePackageOpen()
    {
        PackageFixture fixture = PackageFixture.Create();
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackagePlanResult result = await fixture.Verifier().CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan plan = Assert.IsType<ManagedPackagePlan>(result.Plan);
        PackageFile original = fixture.Manifest.Files[0];
        PackageFile[] projectedFiles = Assert.IsType<PackageFile[]>(fixture.Manifest.Files);
        projectedFiles[0] = original with { Sha256 = new string('0', 64) };
        Assert.Equal(original, plan.Manifest.Files[0]);
        Assert.True(Assert.IsAssignableFrom<IList<PackageFile>>(plan.Manifest.Files).IsReadOnly);
        Assert.True(Assert.IsAssignableFrom<IList<string>>(plan.MemberPaths).IsReadOnly);
        var outputs = new Dictionary<string, MemoryStream>(StringComparer.Ordinal);
        long completed = 0;
        await plan.ExtractAsync(path =>
        {
            var output = new MemoryStream();
            outputs.Add(path, output);
            return output;
        }, TestContext.Current.CancellationToken, (done, total) => { completed = done; Assert.Equal(plan.ExpandedBytes, total); });
        Assert.Equal(plan.ExpandedBytes, completed);
        Assert.All(outputs.Values, output => Assert.False(output.CanWrite));
        Assert.Equal(fixture.ManifestBytes, outputs[ManagedPackageVerifier.ManifestFileName].ToArray());
        plan.Dispose();
        plan.Dispose();
        Assert.True(package.CanRead);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        { await plan.ExtractAsync(_ => throw new InvalidOperationException(), TestContext.Current.CancellationToken); });
    }

    /// <summary>Extraction rechecks the exact admitted hashes when caller custody is violated after verification.</summary>
    [Fact]
    public async Task ContentChangedAfterAdmissionFailsInternalExtraction()
    {
        PackageFixture fixture = PackageFixture.Create();
        byte[] bytes = (byte[])fixture.PackageBytes.Clone();
        await using var package = new MemoryStream(bytes);
        ManagedPackagePlanResult result = await fixture.Verifier().CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan plan = Assert.IsType<ManagedPackagePlan>(result.Plan);
        int offset = bytes.AsSpan().IndexOf("readme"u8);
        Assert.True(offset >= 0);
        bytes[offset] ^= 1;
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        { await plan.ExtractAsync(_ => new MemoryStream(), TestContext.Current.CancellationToken); });
        Assert.Equal("Archive content changed after admission.", exception.Message);
        Assert.True(package.CanRead);
    }

    /// <summary>Destination write faults propagate and dispose the owned output while the input stays held.</summary>
    [Fact]
    public async Task ExtractionWriteFaultDisposesDestinationAndKeepsPackageOpen()
    {
        PackageFixture fixture = PackageFixture.Create();
        await using var package = new MemoryStream(fixture.PackageBytes);
        ManagedPackagePlanResult result = await fixture.Verifier().CreatePlanAsync(package, fixture.Candidate, TestContext.Current.CancellationToken);
        using ManagedPackagePlan plan = Assert.IsType<ManagedPackagePlan>(result.Plan);
        var failure = new IOException("synthetic extraction write fault");
        using var destination = new FaultingWriteStream(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(async () =>
        { await plan.ExtractAsync(_ => destination, TestContext.Current.CancellationToken); }));
        Assert.False(destination.CanWrite);
        Assert.True(package.CanRead);
    }
}

internal sealed class ObservedPackageStream(byte[] bytes) : Stream
{
    private readonly MemoryStream input = new(bytes);
    internal bool Readable { get; init; } = true;
    internal bool Seekable { get; init; } = true;
    internal bool FaultOnMetadata { get; init; }
    internal long? ObservedLength { get; init; }
    internal long? FinalLength { get; init; }
    internal bool WrongFinalPosition { get; init; }
    internal IOException? ReadFault { get; init; }
    internal bool Gate { get; init; }
    internal bool WasDisposed { get; private set; }
    internal int AsyncReads { get; private set; }
    internal int SyncReads { get; private set; }
    internal int LastRequested { get; private set; }
    internal int MaximumRequested { get; private set; }
    internal long BytesRead { get; private set; }
    internal CancellationToken LastToken { get; private set; }
    internal bool HashProbeCompletedBeforeZipRead { get; private set; }
    internal TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool probed;
    private bool zipReading;

    public override bool CanRead => Readable && input.CanRead;
    public override bool CanSeek => Seekable && input.CanSeek;
    public override bool CanWrite => false;
    public override long Length => FaultOnMetadata ? throw new InvalidOperationException("Unsupported stream metadata was accessed.") :
        probed && FinalLength.HasValue ? FinalLength.Value : ObservedLength ?? input.Length;
    public override long Position
    {
        get => probed && WrongFinalPosition ? 0 : input.Position;
        set
        {
            if (probed && value == 0) zipReading = true;
            input.Position = value;
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!zipReading)
        {
            AsyncReads++;
            LastRequested = buffer.Length;
            MaximumRequested = Math.Max(MaximumRequested, buffer.Length);
            LastToken = cancellationToken;
        }
        ReadStarted.TrySetResult();
        if (Gate) await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadFault is not null) throw ReadFault;
        int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (!zipReading)
        {
            BytesRead += read;
            if (read == 0 && buffer.Length == 1) probed = true;
        }
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObserveZipRead();
        return input.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        ObserveZipRead();
        return input.Read(buffer);
    }

    private void ObserveZipRead()
    {
        SyncReads++;
        if (SyncReads == 1) HashProbeCompletedBeforeZipRead = probed;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => input.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        if (disposing) input.Dispose();
        base.Dispose(disposing);
    }
}
