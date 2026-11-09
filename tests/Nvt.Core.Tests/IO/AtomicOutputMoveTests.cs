// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.IO;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.IO;

public sealed class AtomicOutputMoveTests : IDisposable
{
    private static readonly byte[] _sourceBytes = [1, 2, 3, 4, 5];
    private static readonly byte[] _priorBytes = [9, 9];
    private readonly TestWorkspace _workspace = TestWorkspace.Create();
    private readonly string _directory;

    public AtomicOutputMoveTests() => _directory = _workspace.RootPath;

    [Fact]
    public async Task WriteAsyncReplacesAnExistingFile()
    {
        var path = Path.Combine(_directory, "doc.json");
        TestFiles.WriteAllBytes(path, _priorBytes);

        await AtomicOutput.WriteAsync(path, (stream, token) => stream.WriteAsync(_sourceBytes, token).AsTask(), TestContext.Current.CancellationToken);

        Assert.Equal(_sourceBytes, await ReadBytesAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMovePutsTheContentAtTheTargetAndRemovesTheSource(bool sameVolume)
    {
        var source = Path.Combine(_directory, "draft.json");
        var target = Path.Combine(_directory, "sub", "project.json");
        TestFiles.WriteAllBytes(source, _sourceBytes);

        await AtomicOutput.MoveAsync(source, target, (_, _) => sameVolume, TestContext.Current.CancellationToken);

        Assert.Equal(_sourceBytes, await ReadBytesAsync(target));
        Assert.False(File.Exists(source));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target)!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMoveReplacesAnExistingTarget(bool sameVolume)
    {
        var source = Path.Combine(_directory, "draft.json");
        var target = Path.Combine(_directory, "project.json");
        TestFiles.WriteAllBytes(source, _sourceBytes);
        TestFiles.WriteAllBytes(target, _priorBytes);

        await AtomicOutput.MoveAsync(source, target, (_, _) => sameVolume, TestContext.Current.CancellationToken);

        Assert.Equal(_sourceBytes, await ReadBytesAsync(target));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task ThePublicOverloadMovesOnOneVolume()
    {
        var source = Path.Combine(_directory, "draft.json");
        var target = Path.Combine(_directory, "project.json");
        TestFiles.WriteAllBytes(source, _sourceBytes);

        await AtomicOutput.MoveAsync(source, target, TestContext.Current.CancellationToken);

        Assert.Equal(_sourceBytes, await ReadBytesAsync(target));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task AMissingSourceThrowsAndKeepsTheTarget()
    {
        var target = Path.Combine(_directory, "project.json");
        TestFiles.WriteAllBytes(target, _priorBytes);

        await Assert.ThrowsAsync<FileNotFoundException>(() => AtomicOutput.MoveAsync(Path.Combine(_directory, "none.json"), target, TestContext.Current.CancellationToken));

        Assert.Equal(_priorBytes, await ReadBytesAsync(target));
    }

    [Fact]
    public async Task AMoveOntoTheSamePathDoesNothing()
    {
        var path = Path.Combine(_directory, "project.json");
        TestFiles.WriteAllBytes(path, _sourceBytes);

        await AtomicOutput.MoveAsync(path, path, TestContext.Current.CancellationToken);

        Assert.Equal(_sourceBytes, await ReadBytesAsync(path));
    }

    [Fact]
    public async Task ACancelledMoveKeepsTheSourceAndTheTarget()
    {
        var source = Path.Combine(_directory, "draft.json");
        var target = Path.Combine(_directory, "project.json");
        TestFiles.WriteAllBytes(source, _sourceBytes);
        TestFiles.WriteAllBytes(target, _priorBytes);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AtomicOutput.MoveAsync(source, target, (_, _) => false, cancelled.Token));

        Assert.Equal(_sourceBytes, await ReadBytesAsync(source));
        Assert.Equal(_priorBytes, await ReadBytesAsync(target));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Theory]
    [InlineData(null, "b")]
    [InlineData("a", null)]
    [InlineData("", "b")]
    [InlineData("a", " ")]
    public async Task BlankPathsAreRejected(string? source, string? target)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => AtomicOutput.MoveAsync(source!, target!, TestContext.Current.CancellationToken));
    }

    public void Dispose() => _workspace.Dispose();

    private static async Task<byte[]> ReadBytesAsync(string path)
    {
        await using var input = File.OpenRead(path);
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, TestContext.Current.CancellationToken);
        return buffer.ToArray();
    }
}
