// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Security.Cryptography;
using Nvt.Core.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Verification;

/// <summary>Tests actual expanded-byte accounting and the frozen archive read order.</summary>
public sealed class BoundedArchiveReaderTests
{
    private const long FrozenExpandedBytes = 536_870_912;
    private static readonly byte[] SampleBytes = [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>Actual entry bytes stop at the original one-byte overflow sentinel.</summary>
    [Fact]
    public async Task ActualEntryBytesCannotExceedDeclaredLength()
    {
        await using var source = new MemoryStream(new byte[1024]);
        var budget = new ExpandedByteBudget(maximumBytes: 2048);

        BoundedArchiveReadResult result = await BoundedArchiveReader.ReadAndHashAsync(
            source, declaredLength: 8, budget, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(BoundedArchiveReadIssue.EntryLengthExceeded, result.Issue);
        Assert.Equal(9, budget.ConsumedBytes);
        Assert.Equal(9, source.Position);
    }

    /// <summary>Two entries share the original aggregate budget and overflow sentinel.</summary>
    [Fact]
    public async Task ActualExpandedBytesShareOneAggregateBudget()
    {
        var budget = new ExpandedByteBudget(maximumBytes: 10);
        await using var first = new MemoryStream(new byte[6]);
        await using var second = new MemoryStream(new byte[1024]);

        BoundedArchiveReadResult accepted = await BoundedArchiveReader.ReadAndHashAsync(
            first, declaredLength: 6, budget, TestContext.Current.CancellationToken);
        BoundedArchiveReadResult rejected = await BoundedArchiveReader.ReadAndHashAsync(
            second, declaredLength: 1024, budget, TestContext.Current.CancellationToken);

        Assert.True(accepted.IsSuccess);
        Assert.False(rejected.IsSuccess);
        Assert.Equal(BoundedArchiveReadIssue.AggregateLengthExceeded, rejected.Issue);
        Assert.Equal(11, budget.ConsumedBytes);
        Assert.Equal(5, second.Position);
    }

    /// <summary>Every actual expanded-byte budget must be positive.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void ExpandedByteBudgetRejectsNonPositiveMaximum(long maximumBytes)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExpandedByteBudget(maximumBytes));

        Assert.Equal("maximumBytes", exception.ParamName);
    }

    /// <summary>The accounting mechanism preserves caller-supplied positive long bounds.</summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(536870911L)]
    [InlineData(536870912L)]
    [InlineData(536870913L)]
    [InlineData(long.MaxValue)]
    public void ExpandedByteBudgetRetainsExplicitMaximum(long maximumBytes)
    {
        var budget = new ExpandedByteBudget(maximumBytes);

        Assert.Equal(maximumBytes, budget.MaximumBytes);
        Assert.Equal(0, budget.ConsumedBytes);
        Assert.Equal(maximumBytes, budget.RemainingBytes);
    }

    /// <summary>Consumption at and beside the bound records the overflow byte before rejecting it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void ExpandedByteBudgetPreservesConsumptionBoundary(int count)
    {
        var budget = new ExpandedByteBudget(10);

        Assert.Equal(count <= 10, budget.Consume(count));
        Assert.Equal(count, budget.ConsumedBytes);
        Assert.Equal(10 - count, budget.RemainingBytes);
    }

    /// <summary>Negative consumption fails without changing already consumed bytes.</summary>
    [Fact]
    public void ExpandedByteBudgetRejectsNegativeConsumption()
    {
        var budget = new ExpandedByteBudget(10);
        Assert.True(budget.Consume(3));

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => budget.Consume(-1));

        Assert.Equal("count", exception.ParamName);
        Assert.Equal(3, budget.ConsumedBytes);
        Assert.Equal(7, budget.RemainingBytes);
    }

    /// <summary>Aggregate counters retain lengths beyond the unsigned 32-bit boundary.</summary>
    [Fact]
    public void ExpandedByteBudgetUsesLongConsumptionCounters()
    {
        var budget = new ExpandedByteBudget(long.MaxValue);

        Assert.True(budget.Consume(int.MaxValue));
        Assert.True(budget.Consume(int.MaxValue));
        Assert.True(budget.Consume(2));
        Assert.Equal(4_294_967_296L, budget.ConsumedBytes);
        Assert.Equal(long.MaxValue - 4_294_967_296L, budget.RemainingBytes);
    }

    /// <summary>The NFC 512 MiB actual-byte bound accepts equality and rejects precisely one more byte.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ActualExpandedBytesPreserveFrozenBoundary(int delta)
    {
        long length = FrozenExpandedBytes + delta;
        await using var source = new GeneratedExpandedStream(length);
        var budget = new ExpandedByteBudget(FrozenExpandedBytes);

        BoundedArchiveReadResult result = await BoundedArchiveReader.ReadAndHashAsync(
            source, length, budget, TestContext.Current.CancellationToken);

        Assert.Equal(delta <= 0, result.IsSuccess);
        Assert.Equal(delta <= 0 ? BoundedArchiveReadIssue.None :
            BoundedArchiveReadIssue.AggregateLengthExceeded, result.Issue);
        Assert.Equal(length, result.Length);
        Assert.Equal(length, source.TotalBytesRead);
        Assert.Equal(length, budget.ConsumedBytes);
        Assert.InRange(source.MaximumRequested, 1, 65536);
        Assert.Equal(1, source.LastRequested);
        Assert.Equal(delta <= 0, result.Sha256 is not null);
        Assert.False(source.WasDisposed);
    }

    /// <summary>Exact-length reads distinguish a short entry from equality and the overflow sentinel.</summary>
    [Theory]
    [InlineData(7, 2)]
    [InlineData(8, 0)]
    [InlineData(9, 1)]
    public async Task ExactEntryLengthPreservesNeighboringBoundaries(int actualLength, int expectedIssue)
    {
        await using var source = new GeneratedExpandedStream(actualLength);
        var budget = new ExpandedByteBudget(2048);

        BoundedArchiveReadResult result = await BoundedArchiveReader.ReadAndHashAsync(
            source, 8, budget, TestContext.Current.CancellationToken);

        Assert.Equal((BoundedArchiveReadIssue)expectedIssue, result.Issue);
        Assert.Equal(actualLength, result.Length);
        Assert.Equal(actualLength, budget.ConsumedBytes);
        Assert.Equal(actualLength, source.TotalBytesRead);
        Assert.Equal(expectedIssue == 0, result.IsSuccess);
        Assert.Equal(expectedIssue == 0, result.Sha256 is not null);
    }

    /// <summary>At-most reads accept shorter content and equality, while rejecting one extra byte.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task AtMostEntryLengthPreservesNeighboringBoundaries(int actualLength)
    {
        await using var source = new GeneratedExpandedStream(actualLength);
        await using var destination = new MemoryStream();
        var budget = new ExpandedByteBudget(2048);
        int transferred = 0;

        BoundedArchiveReadResult result = await BoundedArchiveReader.ReadAtMostAndHashAsync(
            source, 8, budget, destination, TestContext.Current.CancellationToken,
            count => transferred += count);

        Assert.Equal(actualLength <= 8, result.IsSuccess);
        Assert.Equal(actualLength <= 8 ? BoundedArchiveReadIssue.None :
            BoundedArchiveReadIssue.EntryLengthExceeded, result.Issue);
        Assert.Equal(actualLength, result.Length);
        Assert.Equal(actualLength, budget.ConsumedBytes);
        Assert.Equal(actualLength <= 8 ? actualLength : 0, destination.Length);
        Assert.Equal(actualLength <= 8 ? actualLength : 0, transferred);
    }

    /// <summary>Empty exact and at-most entries hash the empty byte sequence with a one-byte EOF probe.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyEntryPreservesZeroLengthBoundary(bool atMost)
    {
        await using var source = new GeneratedExpandedStream(0);
        var budget = new ExpandedByteBudget(1);

        BoundedArchiveReadResult result = atMost
            ? await BoundedArchiveReader.ReadAtMostAndHashAsync(
                source, 0, budget, null, TestContext.Current.CancellationToken)
            : await BoundedArchiveReader.ReadAndHashAsync(
                source, 0, budget, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            result.Sha256);
        Assert.Equal(0, result.Length);
        Assert.Equal(0, budget.ConsumedBytes);
        Assert.Equal(1, source.ReadCalls);
        Assert.Equal(1, source.LastRequested);
    }

    /// <summary>Partial reads across the fixed 64 KiB buffer boundary preserve complete hashes.</summary>
    [Theory]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    public async Task PartialReadsPreserveFixedBufferBoundary(int length)
    {
        await using var source = new GeneratedExpandedStream(length, maximumReadSize: 17);
        var budget = new ExpandedByteBudget(length);
        byte[] expected = new byte[length];
        Array.Fill(expected, (byte)0xA5);
        int transferred = 0;

        BoundedArchiveReadResult result = await BoundedArchiveReader.ReadAndHashAsync(
            source, length, budget, TestContext.Current.CancellationToken,
            count => transferred += count);

        Assert.True(result.IsSuccess);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), result.Sha256);
        Assert.Equal(length, result.Length);
        Assert.Equal(length, budget.ConsumedBytes);
        Assert.Equal(length, transferred);
        Assert.InRange(source.MaximumRequested, 1, 65536);
        Assert.Equal(1, source.LastRequested);
        Assert.Equal(TestContext.Current.CancellationToken, source.LastToken);
    }

    /// <summary>Aggregate overflow takes precedence when an entry crosses both bounds in the same read.</summary>
    [Fact]
    public async Task AggregateOverflowPrecedesEntryOverflowAndMaterialization()
    {
        await using var source = new GeneratedExpandedStream(1024);
        await using var destination = new MemoryStream();
        var budget = new ExpandedByteBudget(8);
        int transferred = 0;

        BoundedArchiveReadResult result = await BoundedArchiveReader.CopyAndHashAsync(
            source, 8, budget, destination, TestContext.Current.CancellationToken,
            count => transferred += count);

        Assert.Equal(BoundedArchiveReadIssue.AggregateLengthExceeded, result.Issue);
        Assert.Null(result.Sha256);
        Assert.Equal(9, result.Length);
        Assert.Equal(9, budget.ConsumedBytes);
        Assert.Equal(9, source.TotalBytesRead);
        Assert.Equal(1, source.ReadCalls);
        Assert.Equal(0, destination.Length);
        Assert.Equal(0, transferred);
    }

    /// <summary>A full initial buffer is copied, while the final one-byte overflow sentinel is withheld.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverflowSentinelNeverReachesDestinationOrProgress(bool aggregateOverflow)
    {
        const int bound = 65536;
        await using var source = new GeneratedExpandedStream(bound + 100);
        await using var destination = new MemoryStream();
        var budget = new ExpandedByteBudget(aggregateOverflow ? bound : bound + 100);
        int transferred = 0;

        BoundedArchiveReadResult result = await BoundedArchiveReader.CopyAndHashAsync(
            source, bound, budget, destination, TestContext.Current.CancellationToken,
            count => transferred += count);

        Assert.Equal(aggregateOverflow ? BoundedArchiveReadIssue.AggregateLengthExceeded :
            BoundedArchiveReadIssue.EntryLengthExceeded, result.Issue);
        Assert.Null(result.Sha256);
        Assert.Equal(bound + 1, result.Length);
        Assert.Equal(bound + 1, budget.ConsumedBytes);
        Assert.Equal(bound + 1, source.TotalBytesRead);
        Assert.Equal(1, source.LastRequested);
        Assert.Equal(bound, destination.Length);
        Assert.Equal(bound, transferred);
    }

    /// <summary>A short copy records and copies actual bytes, then returns a mismatch without a hash.</summary>
    [Fact]
    public async Task ShortCopyPreservesActualConsumptionAndWrittenPrefix()
    {
        await using var source = new MemoryStream(SampleBytes, writable: false);
        await using var destination = new MemoryStream();
        var budget = new ExpandedByteBudget(32);
        int transferred = 0;

        BoundedArchiveReadResult result = await BoundedArchiveReader.CopyAndHashAsync(
            source, 9, budget, destination, TestContext.Current.CancellationToken,
            count => transferred += count);

        Assert.Equal(BoundedArchiveReadIssue.EntryLengthMismatch, result.Issue);
        Assert.Null(result.Sha256);
        Assert.Equal(8, result.Length);
        Assert.Equal(8, budget.ConsumedBytes);
        Assert.Equal(8, transferred);
        Assert.Equal(SampleBytes, destination.ToArray());
    }

    /// <summary>Successful copying hashes canonical lowercase SHA-256 and keeps both borrowed streams open.</summary>
    [Fact]
    public async Task CopyHashesExactBytesAndLeavesBorrowedStreamsOpen()
    {
        await using var source = new MemoryStream(SampleBytes, writable: false);
        await using var destination = new MemoryStream();
        var budget = new ExpandedByteBudget(SampleBytes.Length);

        BoundedArchiveReadResult result = await BoundedArchiveReader.CopyAndHashAsync(
            source, SampleBytes.Length, budget, destination, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(SampleBytes)).ToLowerInvariant(), result.Sha256);
        Assert.Equal(SampleBytes, destination.ToArray());
        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
        Assert.Equal(0, budget.RemainingBytes);
    }

    /// <summary>Long maximum hints cannot overflow request arithmetic or replace actual-byte reads.</summary>
    [Fact]
    public async Task LongMaximumEntryHintProducesTypedShortRead()
    {
        await using var source = new GeneratedExpandedStream(8);
        var budget = new ExpandedByteBudget(long.MaxValue);

        BoundedArchiveReadResult result = await BoundedArchiveReader.ReadAndHashAsync(
            source, long.MaxValue, budget, TestContext.Current.CancellationToken);

        Assert.Equal(BoundedArchiveReadIssue.EntryLengthMismatch, result.Issue);
        Assert.Equal(8, result.Length);
        Assert.Equal(8, budget.ConsumedBytes);
        Assert.Equal(65536, source.MaximumRequested);
    }

    /// <summary>Entry-length validation precedes null stream, budget and destination checks.</summary>
    [Theory]
    [InlineData("Exact", "declaredLength")]
    [InlineData("AtMost", "maximumLength")]
    [InlineData("Copy", "declaredLength")]
    public async Task NegativeEntryLimitsPreserveArgumentOrder(string operation, string expectedParameter)
    {
        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            InvokeRead(operation, null!, -1, null!, null).AsTask());

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    /// <summary>Copy destination validation precedes source validation, which precedes budget validation.</summary>
    [Theory]
    [InlineData("Exact", false, false, "source")]
    [InlineData("AtMost", false, false, "source")]
    [InlineData("Copy", false, false, "destination")]
    [InlineData("Copy", false, true, "source")]
    [InlineData("Exact", true, false, "budget")]
    [InlineData("AtMost", true, false, "budget")]
    [InlineData("Copy", true, true, "budget")]
    public async Task NullArgumentsPreserveExceptionOrder(
        string operation, bool hasSource, bool hasDestination, string expectedParameter)
    {
        await using var source = new MemoryStream();
        await using var destination = new MemoryStream();

        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            InvokeRead(operation, hasSource ? source : null!, 0, null!,
                hasDestination ? destination : null).AsTask());

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    /// <summary>A cancelled read propagates the same cancellation token without consuming bytes.</summary>
    [Fact]
    public async Task CancellationBeforeReadingPropagatesWithoutConsumption()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await using var source = new GeneratedExpandedStream(8);
        var budget = new ExpandedByteBudget(8);

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedArchiveReader.ReadAndHashAsync(source, 8, budget, cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, budget.ConsumedBytes);
        Assert.Equal(0, source.TotalBytesRead);
        Assert.False(source.WasDisposed);
    }

    /// <summary>A deterministic read gate proves in-flight cancellation escapes without a result or consumption.</summary>
    [Fact]
    public async Task CancellationDuringReadPropagatesThroughDeterministicGate()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var source = new GatedExpandedStream();
        var budget = new ExpandedByteBudget(8);
        Task<BoundedArchiveReadResult> pending = BoundedArchiveReader.ReadAndHashAsync(
            source, 8, budget, cancellation.Token).AsTask();
        await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        cancellation.Cancel();
        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, budget.ConsumedBytes);
        Assert.True(source.CanRead);
    }

    /// <summary>Read faults escape unchanged after accounting for only the earlier successful prefix.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadFaultPreservesExceptionAndAcceptedPrefix(bool accessDenied)
    {
        Exception fault = accessDenied
            ? new UnauthorizedAccessException("Synthetic read fault.")
            : new IOException("Synthetic read fault.");
        await using var source = new GeneratedExpandedStream(1024, maximumReadSize: 17)
        {
            FaultOnRead = 2,
            ReadFault = fault,
        };
        await using var destination = new MemoryStream();
        var budget = new ExpandedByteBudget(2048);
        int transferred = 0;

        Exception? observed = await Record.ExceptionAsync(() => BoundedArchiveReader.CopyAndHashAsync(
            source, 1024, budget, destination, TestContext.Current.CancellationToken,
            count => transferred += count).AsTask());

        Assert.Same(fault, observed);
        Assert.Equal(17, budget.ConsumedBytes);
        Assert.Equal(17, source.TotalBytesRead);
        Assert.Equal(17, destination.Length);
        Assert.Equal(17, transferred);
        Assert.False(source.WasDisposed);
        Assert.True(destination.CanWrite);
    }

    /// <summary>Write faults occur after actual-byte accounting and before the progress callback.</summary>
    [Fact]
    public async Task WriteFaultPreservesAccountingBeforeProgress()
    {
        var fault = new IOException("Synthetic write fault.");
        await using var source = new GeneratedExpandedStream(8);
        await using var destination = new FaultingWriteStream(fault);
        var budget = new ExpandedByteBudget(8);
        int transferred = 0;

        IOException observed = await Assert.ThrowsAsync<IOException>(() => BoundedArchiveReader.CopyAndHashAsync(
            source, 8, budget, destination, TestContext.Current.CancellationToken,
            count => transferred += count).AsTask());

        Assert.Same(fault, observed);
        Assert.Equal(8, budget.ConsumedBytes);
        Assert.Equal(8, source.TotalBytesRead);
        Assert.Equal(1, source.ReadCalls);
        Assert.Equal(0, transferred);
        Assert.Equal(TestContext.Current.CancellationToken, destination.LastToken);
        Assert.False(source.WasDisposed);
        Assert.True(destination.CanWrite);
    }

    /// <summary>Callback faults occur after the admitted bytes have been written and counted.</summary>
    [Fact]
    public async Task CallbackFaultPreservesWriteBeforeNotification()
    {
        var fault = new InvalidOperationException("Synthetic callback fault.");
        await using var source = new MemoryStream(SampleBytes, writable: false);
        await using var destination = new MemoryStream();
        var budget = new ExpandedByteBudget(8);

        InvalidOperationException observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedArchiveReader.CopyAndHashAsync(source, 8, budget, destination,
                TestContext.Current.CancellationToken, _ => throw fault).AsTask());

        Assert.Same(fault, observed);
        Assert.Equal(8, budget.ConsumedBytes);
        Assert.Equal(SampleBytes, destination.ToArray());
        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
    }

    /// <summary>The file wrapper hashes complete content and releases the file it owns.</summary>
    [Fact]
    public async Task FileReadHashesCompleteContentAndReleasesOwnedStream()
    {
        string directory = Directory.CreateTempSubdirectory("nvt-core-archive-").FullName;
        string path = Path.Combine(directory, "payload.dat");
        try
        {
            await File.WriteAllBytesAsync(path, SampleBytes, TestContext.Current.CancellationToken);
            var budget = new ExpandedByteBudget(8);

            BoundedArchiveReadResult result = await BoundedArchiveReader.ReadFileAndHashAsync(
                path, 8, budget, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(SampleBytes)).ToLowerInvariant(), result.Sha256);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(8, exclusive.Length);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    /// <summary>The frozen file wrapper opens the file before validating the declared length.</summary>
    [Fact]
    public async Task FileOpenFaultPrecedesDeclaredLengthValidation()
    {
        string directory = Directory.CreateTempSubdirectory("nvt-core-archive-").FullName;
        string path = Path.Combine(directory, "absent.dat");
        try
        {
            _ = await Assert.ThrowsAsync<FileNotFoundException>(() => BoundedArchiveReader.ReadFileAndHashAsync(
                path, -1, null!, TestContext.Current.CancellationToken).AsTask());
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    /// <summary>The file wrapper validates its path before opening or checking other arguments.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task FileReadRejectsBlankPathBeforeOtherArguments(string? path)
    {
        ArgumentException exception = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            BoundedArchiveReader.ReadFileAndHashAsync(
                path!, -1, null!, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("path", exception.ParamName);
    }

    private static ValueTask<BoundedArchiveReadResult> InvokeRead(
        string operation, Stream source, long length, ExpandedByteBudget budget, Stream? destination) =>
        operation switch
        {
            "Exact" => BoundedArchiveReader.ReadAndHashAsync(
                source, length, budget, TestContext.Current.CancellationToken),
            "AtMost" => BoundedArchiveReader.ReadAtMostAndHashAsync(
                source, length, budget, destination, TestContext.Current.CancellationToken),
            "Copy" => BoundedArchiveReader.CopyAndHashAsync(
                source, length, budget, destination!, TestContext.Current.CancellationToken),
            _ => throw new ArgumentException("Unknown synthetic read operation.", nameof(operation)),
        };
}
