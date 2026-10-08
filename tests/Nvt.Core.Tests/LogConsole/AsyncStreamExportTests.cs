// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Nvt.Core.LogConsole;
using Xunit;

namespace Nvt.Core.Tests.LogConsole;

/// <summary>Async-only UTF-8 destinations and stream lifetime.</summary>
public sealed class AsyncStreamExportTests
{
    /// <summary>UTF-8 export works with destinations that forbid synchronous writes and flushes.</summary>
    [Fact]
    public async Task StreamExportDisposesWriterAsynchronously()
    {
        using var store = LogStoreTests.CreateStore();
        store.Add(LogLevel.Info, "app", "message");
        using var projection = ConsoleProjectionTests.Project(store);
        await using var stream = new AsyncOnlyStream();
        await ConsoleExportFormatter.WriteLogAsync(stream, projection, new ConsoleExportOptions(false, false), TestContext.Current.CancellationToken);
        Assert.Equal("[app] message", Encoding.UTF8.GetString(stream.ToArray()));
        Assert.True(stream.CanWrite);
    }
    private sealed class AsyncOnlyStream : MemoryStream
    {
        public override void Flush() => throw new InvalidOperationException("Synchronous flush is forbidden.");
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous write is forbidden.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Synchronous write is forbidden.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytes = buffer.ToArray();
            base.Write(bytes, 0, bytes.Length);
            return ValueTask.CompletedTask;
        }
    }}
