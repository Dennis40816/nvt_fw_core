// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.
#pragma warning disable CA1707 // Keep descriptive names consistent with the ported NFU test.
#pragma warning disable xUnit1051 // Characterize default and explicitly controlled cancellation tokens.

using System.Text;
using Nvt.Core.IO;
using Xunit;

namespace Nvt.Core.Tests.IO;

public sealed class AtomicOutputTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"nvt-atomic-{Guid.NewGuid():N}");

    public AtomicOutputTests() => Directory.CreateDirectory(directory);

    // Ported from the frozen NFU ReplaySidecarTests baseline.
    [Fact]
    public async Task Interrupted_atomic_write_preserves_prior_output_and_removes_temporary_file()
    {
        var path = Path.Combine(directory, "stable.json");
        await File.WriteAllTextAsync(path, "original");
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicOutput.WriteAsync(
            path,
            async (stream, token) =>
            {
                await stream.WriteAsync("replacement"u8.ToArray(), token);
                cancellation.Cancel();
            },
            cancellation.Token));

        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(directory, ".stable.json.*.tmp"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(131073)]
    public async Task Binary_output_is_identical_and_missing_parent_directories_are_created(int length)
    {
        var path = Path.Combine(directory, "nested", "output.dat");
        var bytes = Enumerable.Range(0, length).Select(index => (byte)(index % 256)).ToArray();

        await AtomicOutput.WriteAsync(path, async (stream, token) => await stream.WriteAsync(bytes, token));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Theory]
    [InlineData("{\"message\":\"你好\"}\n")]
    [InlineData("label,value\r\n\"comma,label\",42\r\n")]
    public async Task Text_output_preserves_UTF8_bytes_without_adding_a_BOM(string text)
    {
        var path = Path.Combine(directory, "output.txt");

        await AtomicOutput.WriteAsync(path, async (stream, token) =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
            await writer.WriteAsync(text.AsMemory(), token);
            await writer.FlushAsync(token);
        });

        Assert.Equal(Encoding.UTF8.GetBytes(text), await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Replacement_is_visible_only_after_the_writer_completes_and_the_stream_is_closed()
    {
        var path = Path.Combine(directory, "stable.txt");
        await File.WriteAllTextAsync(path, "original");
        Stream? writerStream = null;
        var calls = 0;

        await AtomicOutput.WriteAsync(path, async (stream, token) =>
        {
            calls++;
            writerStream = stream;
            Assert.Equal("original", await File.ReadAllTextAsync(path, token));
            await stream.WriteAsync("replacement"u8.ToArray(), token);
            Assert.Equal("original", await File.ReadAllTextAsync(path, token));
        });

        Assert.Equal(1, calls);
        Assert.NotNull(writerStream);
        Assert.False(writerStream.CanWrite);
        Assert.Equal("replacement", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Writer_failure_preserves_the_destination_and_removes_temporary_files(bool existingOutput, bool partialWrite)
    {
        var path = Path.Combine(directory, "output.txt");
        if (existingOutput) await File.WriteAllTextAsync(path, "original");
        var failure = new IOException("Synthetic writer failure.");

        var error = await Assert.ThrowsAsync<IOException>(() => AtomicOutput.WriteAsync(path, async (stream, token) =>
        {
            if (partialWrite) await stream.WriteAsync("partial"u8.ToArray(), token);
            throw failure;
        }));

        Assert.Same(failure, error);
        if (existingOutput)
        {
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        else
        {
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(directory));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Already_cancelled_token_still_reaches_the_writer_but_never_publishes(bool existingOutput)
    {
        var parent = Path.Combine(directory, "nested");
        var path = Path.Combine(parent, "output.txt");
        if (existingOutput)
        {
            Directory.CreateDirectory(parent);
            await File.WriteAllTextAsync(path, "original");
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicOutput.WriteAsync(
            path,
            async (stream, token) =>
            {
                calls++;
                Assert.Equal(cancellation.Token, token);
                Assert.True(token.IsCancellationRequested);
                await stream.WriteAsync("partial"u8.ToArray(), CancellationToken.None);
            },
            cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, calls);
        Assert.True(Directory.Exists(parent));
        if (existingOutput)
        {
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Equal([path], Directory.GetFiles(parent));
        }
        else
        {
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(parent));
        }
    }

    [Fact]
    public async Task Publication_failure_preserves_the_destination_directory_and_removes_temporary_files()
    {
        var path = Path.Combine(directory, "destination");
        Directory.CreateDirectory(path);
        var existingFile = Path.Combine(path, "keep.txt");
        await File.WriteAllTextAsync(existingFile, "original");

        var error = await Record.ExceptionAsync(() => AtomicOutput.WriteAsync(
            path, async (stream, token) => await stream.WriteAsync("replacement"u8.ToArray(), token)));

        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("original", await File.ReadAllTextAsync(existingFile));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Closing_the_writer_stream_causes_flush_failure_without_replacing_prior_output()
    {
        var path = Path.Combine(directory, "stable.txt");
        await File.WriteAllTextAsync(path, "original");

        await Assert.ThrowsAsync<ObjectDisposedException>(() => AtomicOutput.WriteAsync(path, async (stream, token) =>
        {
            await stream.WriteAsync("partial"u8.ToArray(), token);
            await stream.DisposeAsync();
        }));

        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Output_path_is_normalized_before_creating_parent_directories()
    {
        var path = Path.Combine(directory, "unused", "..", "nested", "output.txt");

        await AtomicOutput.WriteAsync(path, async (stream, token) => await stream.WriteAsync("output"u8.ToArray(), token));

        Assert.Equal("output", await File.ReadAllTextAsync(Path.GetFullPath(path)));
        Assert.False(Directory.Exists(Path.Combine(directory, "unused")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Null_path_is_rejected_before_a_null_writer()
    {
        var error = await Assert.ThrowsAsync<ArgumentNullException>(() => AtomicOutput.WriteAsync(null!, null!));

        Assert.Equal("path", error.ParamName);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public async Task Blank_path_is_rejected_without_invoking_the_writer(string path)
    {
        var called = false;

        var error = await Assert.ThrowsAsync<ArgumentException>(() => AtomicOutput.WriteAsync(path, (_, _) =>
        {
            called = true;
            return Task.CompletedTask;
        }));

        Assert.Equal("path", error.ParamName);
        Assert.False(called);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Null_writer_is_rejected_without_creating_parent_directories()
    {
        var path = Path.Combine(directory, "nested", "output.txt");

        var error = await Assert.ThrowsAsync<ArgumentNullException>(() => AtomicOutput.WriteAsync(path, null!));

        Assert.Equal("write", error.ParamName);
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
