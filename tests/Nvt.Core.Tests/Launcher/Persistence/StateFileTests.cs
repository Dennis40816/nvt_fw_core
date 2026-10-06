// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Persistence;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Persistence;

/// <summary>Frozen raw state assertions and exact caller-supplied byte boundaries.</summary>
public sealed class StateFileTests
{
    private const string LauncherSuffix = ".synthetic-launcher.bin";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Complete synthetic documents survive publication and strict adapter decoding.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAndLoadRoundTrip(bool launcher)
    {
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor("nested/state.bin");
        byte[] expected = SyntheticStateCodec.Encode("synthetic-source", reviewDue: true);
        var app = new VersionManagerStateFile(path, 1_048_576);
        var journal = new LauncherBootstrapStateFile(path, LauncherSuffix, 65_536);
        using VersionManagerWriteLeaseResult lease = await app.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        Assert.True(lease.HoldsStatePath(path));
        byte[]? actual;
        if (launcher)
        {
            Assert.True((await journal.TryWriteAsync(expected, Token)).IsSuccess);
            actual = await journal.ReadAsync(Token);
        }
        else
        {
            await app.WriteAsync(expected, Token);
            actual = await app.ReadAsync(Token);
        }
        Assert.Equal(expected, actual);
        Assert.Equal(("synthetic-source", true), SyntheticStateCodec.Decode(actual));
        string publishedPath = launcher ? journal.StatePathIdentity : app.StatePathIdentity;
        Assert.Equal(expected, await File.ReadAllBytesAsync(publishedPath, Token));
        Assert.False(File.Exists(publishedPath + ".tmp"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        Assert.True(lease.HoldsStatePath(path));
    }

    /// <summary>Cancellation preserves the prior complete document and removes temporary files.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledSavePreservesPriorStateAndCleansTemporaryFile(bool launcher)
    {
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor();
        var app = new VersionManagerStateFile(path, 1_048_576);
        var journal = new LauncherBootstrapStateFile(path, LauncherSuffix, 65_536);
        byte[] prior = SyntheticStateCodec.Encode("original", false);
        byte[] replacement = SyntheticStateCodec.Encode("replacement", true);
        using VersionManagerWriteLeaseResult lease = await app.TryAcquireWriteLeaseAsync(TimeSpan.Zero, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.Cancel();
        if (launcher)
        {
            Assert.True((await journal.TryWriteAsync(prior, Token)).IsSuccess);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await journal.TryWriteAsync(replacement, cancellation.Token));
            Assert.Equal(prior, await journal.ReadAsync(Token));
        }
        else
        {
            await app.WriteAsync(prior, Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await app.WriteAsync(replacement, cancellation.Token));
            Assert.Equal(prior, await app.ReadAsync(Token));
        }
        Assert.Empty(Directory.GetFiles(workspace.Root, ".*.tmp"));
        Assert.True(lease.HoldsStatePath(path));
    }

    /// <summary>Every distinct full app-state path retains a distinct launcher mapping.</summary>
    [Fact]
    public void StatePathMappingIsInjective()
    {
        using var workspace = new StateFileWorkspace();
        string first = workspace.PathFor("a.json");
        string second = workspace.PathFor("b.json");
        string firstLauncher = LauncherBootstrapStateFile.DerivePath(first, LauncherSuffix);
        string secondLauncher = LauncherBootstrapStateFile.DerivePath(second, LauncherSuffix);
        Assert.NotEqual(firstLauncher, secondLauncher);
        Assert.Equal(Path.GetFullPath(first) + LauncherSuffix, firstLauncher);
        Assert.Equal(Path.GetFullPath(second) + LauncherSuffix, secondLauncher);
        Assert.NotEqual(firstLauncher, LauncherBootstrapStateFile.DerivePath(firstLauncher, LauncherSuffix));
        Assert.Equal(firstLauncher, LauncherBootstrapStateFile.DerivePath(
            workspace.PathFor("unused/../a.json"), LauncherSuffix));
    }

    /// <summary>Raw access supplies exact bytes; only the mandatory adapter rejects noncanonical content.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task NonCanonicalStateIsRejected(int malformedShape)
    {
        using var workspace = new StateFileWorkspace();
        var file = new LauncherBootstrapStateFile(workspace.PathFor(), LauncherSuffix, 65_536);
        byte[] bytes = SyntheticStateCodec.Encode("source", false);
        switch (malformedShape)
        {
            case 0: bytes[0] = 0; break;
            case 1: bytes[1] = 2; break;
            case 2: bytes = [.. bytes, 0]; break;
        }
        await File.WriteAllBytesAsync(file.StatePathIdentity, bytes, Token);
        byte[]? raw = await file.ReadAsync(Token);
        Assert.Equal(bytes, raw);
        Assert.Null(SyntheticStateCodec.Decode(raw));
    }

    /// <summary>Both raw ceilings admit one below and exact, rejecting one above on reads and writes.</summary>
    [Theory]
    [InlineData(false, 1_048_575)]
    [InlineData(false, 1_048_576)]
    [InlineData(false, 1_048_577)]
    [InlineData(true, 65_535)]
    [InlineData(true, 65_536)]
    [InlineData(true, 65_537)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task ExplicitStateCeilingsAreInclusive(bool launcher, int length)
    {
        using var workspace = new StateFileWorkspace();
        int maximum = length <= 2 ? 1 : launcher ? 65_536 : 1_048_576;
        string path = workspace.PathFor();
        var app = new VersionManagerStateFile(path, maximum);
        var journal = new LauncherBootstrapStateFile(path, LauncherSuffix, maximum);
        string actualPath = launcher ? journal.StatePathIdentity : path;
        byte[] bytes = new byte[length];
        Array.Fill(bytes, (byte)0xA5);
        await File.WriteAllBytesAsync(actualPath, bytes, Token);
        byte[]? read = launcher ? await journal.ReadAsync(Token) : await app.ReadAsync(Token);
        if (length == 0 || length > maximum) Assert.Null(read);
        else Assert.Equal(bytes, read);
        byte[] prior = [0x13];
        await File.WriteAllBytesAsync(actualPath, prior, Token);
        if (launcher)
        {
            LauncherBootstrapStateSaveResult saved = await journal.TryWriteAsync(bytes, Token);
            Assert.Equal(length <= maximum, saved.IsSuccess);
        }
        else if (length > maximum)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await app.WriteAsync(bytes, Token));
            Assert.Equal("Version-manager state exceeds its bounded size.", error.Message);
        }
        else await app.WriteAsync(bytes, Token);
        Assert.Equal(length > maximum ? prior : bytes, await File.ReadAllBytesAsync(actualPath, Token));
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));
    }

    /// <summary>All positive byte parameters reject zero and negative values.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NonpositiveCeilingsAreRejected(int maximumBytes)
    {
        using var workspace = new StateFileWorkspace();
        Assert.Throws<ArgumentOutOfRangeException>(nameof(maximumBytes), () => new VersionManagerStateFile(workspace.PathFor(), maximumBytes));
        Assert.Throws<ArgumentOutOfRangeException>(nameof(maximumBytes), () => new LauncherBootstrapStateFile(workspace.PathFor(), LauncherSuffix, maximumBytes));
    }

    /// <summary>Path arguments retain their validation order ahead of byte ceilings.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void InvalidPathsAreRejectedBeforeCeilings(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => new VersionManagerStateFile(path!, 0));
        Assert.ThrowsAny<ArgumentException>(() => new LauncherBootstrapStateFile(path!, LauncherSuffix, 0));
        Assert.ThrowsAny<ArgumentException>(() => LauncherBootstrapStateFile.DerivePath(path!, LauncherSuffix));
    }

    /// <summary>Product path suffixes must be supplied explicitly.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingProductSuffixIsRejected(string? suffix)
    {
        using var workspace = new StateFileWorkspace();
        Assert.ThrowsAny<ArgumentException>(() => LauncherBootstrapStateFile.DerivePath(workspace.PathFor(), suffix!));
    }

    /// <summary>Absent and empty raw files return null before cancellation, as in the source.</summary>
    [Fact]
    public async Task MissingAndEmptyStatePreserveCheckOrder()
    {
        using var workspace = new StateFileWorkspace();
        var file = new VersionManagerStateFile(workspace.PathFor(), 1);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancelled.Cancel();
        Assert.Null(await file.ReadAsync(cancelled.Token));
        await File.WriteAllBytesAsync(file.StatePathIdentity, [], Token);
        Assert.Null(await file.ReadAsync(cancelled.Token));
        await File.WriteAllBytesAsync(file.StatePathIdentity, new byte[1], Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await file.ReadAsync(cancelled.Token));
    }

    /// <summary>Linked state files remain invalid raw input.</summary>
    [Fact]
    public async Task ReparseStateIsRejected()
    {
        using var workspace = new StateFileWorkspace();
        string target = workspace.PathFor("target.bin");
        string link = workspace.PathFor("link.bin");
        await File.WriteAllBytesAsync(target, new byte[1], Token);
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic link creation is unsupported: {error.GetType().Name}.");
        }
        Assert.Null(await new VersionManagerStateFile(link, 1).ReadAsync(Token));
    }

    /// <summary>A locked physical file throws I/O rather than publishing partial bytes.</summary>
    [Fact]
    public async Task HeldStateCannotBeRead()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows sharing modes are required.");
        using var workspace = new StateFileWorkspace();
        string path = workspace.PathFor();
        await File.WriteAllBytesAsync(path, new byte[1], Token);
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAnyAsync<IOException>(async () => await new VersionManagerStateFile(path, 1).ReadAsync(Token));
    }
}
