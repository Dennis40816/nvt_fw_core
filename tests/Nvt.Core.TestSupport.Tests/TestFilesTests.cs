// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Xunit;

namespace Nvt.Core.TestSupport.Tests;

/// <summary>Pins the stream-based fixture file helpers.</summary>
public sealed class TestFilesTests
{
    /// <summary>Bytes written to a file are the bytes on disk, and a second write replaces them.</summary>
    [Fact]
    public async Task WriteAllBytesCreatesAndReplacesTheFile()
    {
        using var workspace = TestWorkspace.Create();
        string path = workspace.GetPath("data.bin");

        TestFiles.WriteAllBytes(path, [1, 2, 3, 4]);
        TestFiles.WriteAllBytes(path, [9]);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Equal(1, stream.Length);
        Assert.Equal(9, stream.ReadByte());
    }

    /// <summary>Lines come back without terminators, for LF and CRLF alike.</summary>
    [Fact]
    public async Task ReadLinesAsyncSplitsLines()
    {
        using var workspace = TestWorkspace.Create();
        string path = workspace.GetPath("lines.txt");
        TestFiles.WriteAllBytes(path, Encoding.UTF8.GetBytes("one\r\ntwo\nthree"));

        string[] lines = await TestFiles.ReadLinesAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(["one", "two", "three"], lines);
    }

    /// <summary>A canceled token stops the read.</summary>
    [Fact]
    public async Task ReadLinesAsyncHonorsCancellation()
    {
        using var workspace = TestWorkspace.Create();
        string path = workspace.GetPath("lines.txt");
        TestFiles.WriteAllBytes(path, Encoding.UTF8.GetBytes("one\ntwo\n"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        OperationCanceledException? canceled = null;
        try { _ = await TestFiles.ReadLinesAsync(path, cancellation.Token); } catch (OperationCanceledException thrown) { canceled = thrown; }

        Assert.NotNull(canceled);
    }

    /// <summary>An empty path is rejected.</summary>
    [Fact]
    public void EmptyPathIsRejected()
    {
        _ = Assert.Throws<ArgumentException>(() => TestFiles.WriteAllBytes(" ", []));
        _ = Assert.Throws<ArgumentNullException>(() => TestFiles.WriteAllBytes("x", null!));
    }
}
