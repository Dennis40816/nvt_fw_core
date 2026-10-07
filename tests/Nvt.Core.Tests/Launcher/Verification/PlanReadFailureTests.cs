// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Verification;

/// <summary>Pins how package read failures reach verification and installation callers.</summary>
public sealed class PlanReadFailureTests
{
    /// <summary>Verification keeps returning PackageUnavailable for each read failure kind.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task VerifyAsyncMapsReadFailuresToPackageUnavailable(int kind)
    {
        PackageFixture package = PackageFixture.Create();
        await using var stream = new FaultingReadStream(package.PackageBytes, Failure(kind));

        var result = await package.Verifier().VerifyAsync(stream, package.Candidate,
            TestContext.Current.CancellationToken);

        Assert.Null(result.Candidate);
        Assert.Equal(ManagedVersionInstallIssue.PackageUnavailable, result.Issue);
    }

    /// <summary>The default plan path collapses read failures; the installation path rethrows the same exception.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task PlanCreationPropagatesReadFailuresOnlyWhenRequested(int kind, bool propagate)
    {
        PackageFixture package = PackageFixture.Create();
        Exception failure = Failure(kind);
        await using var stream = new FaultingReadStream(package.PackageBytes, failure);

        if (propagate)
        {
            Exception thrown = await Assert.ThrowsAnyAsync<Exception>(async () =>
                await package.Verifier().CreatePlanAsync(stream, package.Candidate,
                    TestContext.Current.CancellationToken, propagateReadFailures: true));
            Assert.Same(failure, thrown);
        }
        else
        {
            var result = await package.Verifier().CreatePlanAsync(stream, package.Candidate,
                TestContext.Current.CancellationToken);
            Assert.Null(result.Plan);
            Assert.Equal(ManagedVersionInstallIssue.PackageUnavailable, result.Issue);
        }
    }

    private static Exception Failure(int kind) => kind switch
    {
        0 => new IOException("synthetic read failure"),
        1 => new UnauthorizedAccessException("synthetic access failure"),
        2 => new InvalidDataException("synthetic malformed data"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class FaultingReadStream(byte[] bytes, Exception failure) : MemoryStream(bytes, writable: false)
    {
        public override int Read(byte[] buffer, int offset, int count) => throw failure;

        public override int Read(Span<byte> buffer) => throw failure;

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(failure);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(failure);
    }
}
