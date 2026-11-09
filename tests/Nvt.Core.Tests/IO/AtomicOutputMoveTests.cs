// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.
#pragma warning disable CA1707 // Keep descriptive test names.
#pragma warning disable xUnit1051 // Characterize default and explicitly controlled cancellation tokens.

using Nvt.Core.IO;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.IO;

public sealed class AtomicOutputMoveTests : IDisposable
{
    private static readonly byte[] SourceBytes = [1, 2, 3, 4, 5];
    private static readonly byte[] PriorBytes = [9, 9];
    private readonly TestWorkspace workspace = TestWorkspace.Create();
    private readonly string directory;

    public AtomicOutputMoveTests() => directory = workspace.RootPath;

    [Fact]
    public async Task WriteAsync_replaces_an_existing_file()
    {
        var path = Path.Combine(directory, "doc.json");
        TestFiles.WriteAllBytes(path, PriorBytes);

        await AtomicOutput.WriteAsync(path, (stream, token) => stream.WriteAsync(SourceBytes, token).AsTask());

        Assert.Equal(SourceBytes, await ReadBytesAsync(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_move_puts_the_content_at_the_target_and_removes_the_source(bool sameVolume)
    {
        var source = Path.Combine(directory, "draft.json");
        var target = Path.Combine(directory, "sub", "project.json");
        TestFiles.WriteAllBytes(source, SourceBytes);

        await AtomicOutput.MoveAsync(source, target, (_, _) => sameVolume, CancellationToken.None);

        Assert.Equal(SourceBytes, await ReadBytesAsync(target));
        Assert.False(File.Exists(source));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target)!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_move_replaces_an_existing_target(bool sameVolume)
    {
        var source = Path.Combine(directory, "draft.json");
        var target = Path.Combine(directory, "project.json");
        TestFiles.WriteAllBytes(source, SourceBytes);
        TestFiles.WriteAllBytes(target, PriorBytes);

        await AtomicOutput.MoveAsync(source, target, (_, _) => sameVolume, CancellationToken.None);

        Assert.Equal(SourceBytes, await ReadBytesAsync(target));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task The_public_overload_moves_on_one_volume()
    {
        var source = Path.Combine(directory, "draft.json");
        var target = Path.Combine(directory, "project.json");
        TestFiles.WriteAllBytes(source, SourceBytes);

        await AtomicOutput.MoveAsync(source, target);

        Assert.Equal(SourceBytes, await ReadBytesAsync(target));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task A_missing_source_throws_and_keeps_the_target()
    {
        var target = Path.Combine(directory, "project.json");
        TestFiles.WriteAllBytes(target, PriorBytes);

        await Assert.ThrowsAsync<FileNotFoundException>(() => AtomicOutput.MoveAsync(Path.Combine(directory, "none.json"), target));

        Assert.Equal(PriorBytes, await ReadBytesAsync(target));
    }

    [Fact]
    public async Task A_move_onto_the_same_path_does_nothing()
    {
        var path = Path.Combine(directory, "project.json");
        TestFiles.WriteAllBytes(path, SourceBytes);

        await AtomicOutput.MoveAsync(path, path);

        Assert.Equal(SourceBytes, await ReadBytesAsync(path));
    }

    [Fact]
    public async Task A_cancelled_move_keeps_the_source_and_the_target()
    {
        var source = Path.Combine(directory, "draft.json");
        var target = Path.Combine(directory, "project.json");
        TestFiles.WriteAllBytes(source, SourceBytes);
        TestFiles.WriteAllBytes(target, PriorBytes);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AtomicOutput.MoveAsync(source, target, (_, _) => false, cancelled.Token));

        Assert.Equal(SourceBytes, await ReadBytesAsync(source));
        Assert.Equal(PriorBytes, await ReadBytesAsync(target));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Theory]
    [InlineData(null, "b")]
    [InlineData("a", null)]
    [InlineData("", "b")]
    [InlineData("a", " ")]
    public async Task Blank_paths_are_rejected(string? source, string? target)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => AtomicOutput.MoveAsync(source!, target!));
    }

    public void Dispose() => workspace.Dispose();

    private static async Task<byte[]> ReadBytesAsync(string path)
    {
        await using var input = File.OpenRead(path);
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
