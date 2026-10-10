// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>Verifies fixture path boundaries and bounded, repeatable cleanup without sleeps.</summary>
public sealed class TestWorkspaceTests
{
    /// <summary>Creation owns distinct short directories under the system temp directory.</summary>
    [Fact]
    public void CreateUsesUniqueShortRoots()
    {
        using var first = TestWorkspace.Create();
        using var second = TestWorkspace.Create();
        Assert.NotEqual(first.RootPath, second.RootPath);
        Assert.True(Directory.Exists(first.RootPath));
        Assert.True(Directory.Exists(second.RootPath));
        Assert.StartsWith("nvt-", Path.GetFileName(first.RootPath), StringComparison.Ordinal);
        Assert.True(Path.GetFileName(first.RootPath).Length <= 16);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
            Path.GetDirectoryName(first.RootPath));
    }

    /// <summary>Slash forms resolve consistently, and resolution creates no filesystem entries.</summary>
    [Theory]
    [InlineData("nested/file.txt")]
    [InlineData(@"nested\file.txt")]
    [InlineData(@"nested/more\../file.txt", true)]
    public void RelativeSeparatorsAreNormalized(string relativePath, bool rejected = false)
    {
        using var workspace = TestWorkspace.Create();
        if (rejected)
        {
            Assert.Throws<ArgumentException>(() => workspace.GetPath(relativePath));
            return;
        }
        string path = workspace.GetPath(relativePath);
        Assert.Equal(Path.Combine(workspace.RootPath, "nested", "file.txt"), path);
        Assert.False(Directory.Exists(Path.Combine(workspace.RootPath, "nested")));
        Assert.Equal(workspace.RootPath, workspace.GetPath("."));
        Assert.Equal(Path.Combine(workspace.RootPath, "..fixture"), workspace.GetPath("..fixture"));
    }

    /// <summary>Rooted, drive, UNC, parent, alternate separator and Windows-ambiguous paths are rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData(@"..\outside")]
    [InlineData("inside/../outside")]
    [InlineData(@"inside\..\outside")]
    [InlineData(@"inside/..\outside")]
    [InlineData(@"C:\outside")]
    [InlineData("C:outside")]
    [InlineData(@"\\server\share\file")]
    [InlineData("//server/share/file")]
    [InlineData("/outside")]
    [InlineData(@"\outside")]
    [InlineData("fixture:stream")]
    [InlineData("inside/.. /outside")]
    [InlineData("inside/.../outside")]
    [InlineData("inside/. ./outside")]
    [InlineData("invalid\0path")]
    public void EscapeAndInvalidPathsAreRejected(string path)
    {
        using var workspace = TestWorkspace.Create();
        Assert.Throws<ArgumentException>(() => workspace.GetPath(path));
    }

    /// <summary>A null path is an argument-null error, like the BCL path helpers.</summary>
    [Fact]
    public void NullPathIsRejectedAsArgumentNull()
    {
        using var workspace = TestWorkspace.Create();
        Assert.Throws<ArgumentNullException>(() => workspace.GetPath(null!));
    }

    /// <summary>The internal constructor refuses a root outside the temporary directory, so cleanup cannot delete it.</summary>
    [Fact]
    public void RootOutsideTemporaryDirectoryIsRejected()
    {
        string outside = Path.GetPathRoot(Path.GetTempPath())!;
        string root = Path.Combine(outside, "nvt-outside-root-check");
        if (root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("The temporary directory is the volume root, so no outside path exists.");
        }
        Assert.Throws<ArgumentException>(() => new TestWorkspace(root, static _ => { }, static _ => { }));
    }

    /// <summary>A trailing separator on the internal root does not break path resolution.</summary>
    [Fact]
    public void TrailingSeparatorOnTheRootIsIgnored()
    {
        using var owner = TestWorkspace.Create();
        var workspace = new TestWorkspace(owner.RootPath + Path.DirectorySeparatorChar, static _ => { }, static _ => { });
        Assert.Equal(owner.RootPath, workspace.RootPath);
        Assert.Equal(Path.Combine(owner.RootPath, "a"), workspace.GetPath("a"));
    }

    /// <summary>The temporary directory itself is not a valid root, with or without a trailing separator.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemporaryDirectoryItselfIsRejected(bool trailingSeparator)
    {
        string temp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        string root = trailingSeparator ? temp + Path.DirectorySeparatorChar : temp;
        Assert.Throws<ArgumentException>(() => new TestWorkspace(root, static _ => { }, static _ => { }));
    }

    /// <summary>Public disposal members work directly, without interface casts.</summary>
    [Fact]
    public async Task DisposalMembersAreCallableDirectly()
    {
        var first = TestWorkspace.Create();
        first.Dispose();
        Assert.False(Directory.Exists(first.RootPath));
        var second = TestWorkspace.Create();
        await second.DisposeAsync();
        Assert.False(Directory.Exists(second.RootPath));
    }

    /// <summary>Real nested synthetic fixtures are removed; both disposal forms are repeatable.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalRemovesFixturesAndIsIdempotent(bool asynchronous)
    {
        var workspace = TestWorkspace.Create();
        string file = workspace.GetPath("nested/fixture.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "synthetic fixture", TestContext.Current.CancellationToken);
        if (asynchronous) { await ((IAsyncDisposable)workspace).DisposeAsync(); } else { ((IDisposable)workspace).Dispose(); }
        Assert.False(Directory.Exists(workspace.RootPath));
        ((IDisposable)workspace).Dispose();
        await ((IAsyncDisposable)workspace).DisposeAsync();
        Assert.False(Directory.Exists(workspace.RootPath));
    }

    /// <summary>A directory already removed by the fixture is considered successfully disposed.</summary>
    [Fact]
    public async Task MissingRootIsAnIdempotentSuccess()
    {
        var workspace = TestWorkspace.Create();
        Directory.Delete(workspace.RootPath);
        ((IDisposable)workspace).Dispose();
        await ((IAsyncDisposable)workspace).DisposeAsync();
    }

    /// <summary>Transient sharing/access failures retry through controllable internal hooks.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransientFailuresRetryWithoutSleeping(bool accessFailure)
    {
        using var owner = TestWorkspace.Create();
        int calls = 0;
        var waits = new List<TimeSpan>();
        var workspace = new TestWorkspace(owner.RootPath, path =>
        {
            Assert.Equal(owner.RootPath, path);
            calls++;
            if (calls < 3)
            {
                if (accessFailure) { throw new UnauthorizedAccessException("synthetic access failure"); }
                throw new IOException("synthetic sharing failure");
            }
            Directory.Delete(path, recursive: true);
        }, waits.Add, static () => TimeSpan.Zero);
        await ((IAsyncDisposable)workspace).DisposeAsync();
        ((IDisposable)workspace).Dispose();
        Assert.Equal(3, calls);
        Assert.Equal(2, waits.Count);
        Assert.All(waits, wait => Assert.Equal(TimeSpan.FromMilliseconds(50), wait));
        Assert.False(Directory.Exists(owner.RootPath));
    }

    /// <summary>Persistent failure stops after ten attempts, identifies the retained root, and remains idempotent.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureNamesRetainedPathAndHasAnAttemptCap(bool accessFailure)
    {
        using var owner = TestWorkspace.Create();
        int attempts = 0;
        int waits = 0;
        var workspace = new TestWorkspace(owner.RootPath, path =>
        {
            attempts++;
            if (accessFailure) { throw new UnauthorizedAccessException("synthetic"); }
            throw new IOException("synthetic");
        }, _ => waits++, static () => TimeSpan.Zero);
        IOException failure = Assert.Throws<IOException>(((IDisposable)workspace).Dispose);
        Assert.Contains(owner.RootPath, failure.Message, StringComparison.Ordinal);
        Assert.NotNull(failure.InnerException);
        Assert.True(Directory.Exists(owner.RootPath));
        Assert.Equal(10, attempts);
        Assert.Equal(9, waits);
        Assert.Same(failure, Assert.Throws<IOException>(((IDisposable)workspace).Dispose));
        IOException repeated = await Assert.ThrowsAsync<IOException>(async () => await ((IAsyncDisposable)workspace).DisposeAsync());
        Assert.Same(failure, repeated);
        Assert.Equal(10, attempts);
        Assert.Equal(9, waits);
    }

    /// <summary>The elapsed retry budget also stops cleanup, independently of the attempt cap.</summary>
    [Fact]
    public async Task CleanupBudgetIsDrivenWithoutRealTimeWaits()
    {
        using var owner = TestWorkspace.Create();
        int attempts = 0;
        TimeSpan elapsed = TimeSpan.Zero;
        var workspace = new TestWorkspace(owner.RootPath,
            _ => { attempts++; throw new IOException("synthetic"); },
            duration => { Assert.Equal(TimeSpan.FromMilliseconds(50), duration); elapsed = TimeSpan.FromMilliseconds(500); },
            () => elapsed);
        IOException failure = await Assert.ThrowsAsync<IOException>(async () => await ((IAsyncDisposable)workspace).DisposeAsync());
        Assert.Contains(owner.RootPath, failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, attempts);
        Assert.True(Directory.Exists(owner.RootPath));
    }
}
