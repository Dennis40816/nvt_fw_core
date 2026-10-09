// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.LogConsole;

/// <summary>Immutable message content. Apps can supply segmented or spill-backed implementations.</summary>
/// <remarks>Length, Version, and ResidentCharacterCount stay fixed. Read must fill the requested range.
/// Reads may run concurrently. Disposal waits for all leases and active reads.
/// An accepted instance must not be reused in another input.</remarks>
public interface ILogTextContent : IDisposable
{
    /// <summary>Gets the full UTF-16 length.</summary>
    int Length { get; }
    /// <summary>Gets the UTF-16 characters kept in memory by this handle.</summary>
    int ResidentCharacterCount { get; }
    /// <summary>Gets the immutable content revision.</summary>
    long Version { get; }
    /// <summary>Copies the requested range into destination.</summary>
    void Read(int offset, Span<char> destination);
}

/// <summary>The default in-memory content. Its entire length is charged to both character budgets.</summary>
public sealed class InMemoryLogTextContent : ILogTextContent
{
    private readonly string _text;

    /// <summary>Creates immutable content.</summary>
    public InMemoryLogTextContent(string text, long version = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        Version = version;
    }

    /// <inheritdoc />
    public int Length => _text.Length;
    /// <inheritdoc />
    public int ResidentCharacterCount => Length;
    /// <inheritdoc />
    public long Version { get; }
    /// <inheritdoc />
    public void Read(int offset, Span<char> destination) => _text.AsSpan(offset, destination.Length).CopyTo(destination);
    /// <summary>Releases no external resource. Store and snapshot leases release their string references.</summary>
    public void Dispose() { }
}

internal static class LogText
{
    internal static string ReadAll(ILogTextContent content) => string.Create(content.Length, content,
        static (span, value) => value.Read(0, span));

    internal static ulong Fingerprint(ILogTextContent content)
    {
        Span<char> chunk = stackalloc char[1024];
        var hash = 14695981039346656037UL;
        for (var offset = 0; offset < content.Length; offset += chunk.Length)
        {
            var part = chunk[..Math.Min(chunk.Length, content.Length - offset)];
            content.Read(offset, part);
            foreach (var character in part)
            {
                hash = unchecked((hash ^ character) * 1099511628211UL);
            }
        }
        return hash;
    }

    internal static bool Equal(ILogTextContent left, ILogTextContent right)
    {
        if (left.Length != right.Length) return false;
        Span<char> a = stackalloc char[1024];
        Span<char> b = stackalloc char[1024];
        for (var offset = 0; offset < left.Length; offset += a.Length)
        {
            var length = Math.Min(a.Length, left.Length - offset);
            left.Read(offset, a[..length]);
            right.Read(offset, b[..length]);
            if (!a[..length].SequenceEqual(b[..length])) return false;
        }
        return true;
    }
}

// _contentGate protects only reference state. Injected reads and disposal run outside it.
// Eviction releases the store reference immediately. Existing snapshots keep their own leases.
internal sealed class ContentOwner(ILogTextContent content, int length, int residentCharacters, long version, Action<ContentOwner, Action> cleanup)
{
    internal int ResidentCharacters => residentCharacters;
    private readonly object _contentGate = new();
    private ILogTextContent? _content = content;
    private int _references = 1;

    // Transfers the initial reference on acceptance. Called exactly once by LogStore.
    internal ILogTextContent TakeInitialLease() => new ContentLease(this, length, residentCharacters, version);
    internal static ILogTextContent Retain(ILogTextContent content) => ((ContentLease)content).Retain();

    internal ILogTextContent Lease()
    {
        lock (_contentGate)
        {
            ObjectDisposedException.ThrowIf(_content is null, this);
            _references++;
            return new ContentLease(this, length, residentCharacters, version);
        }
    }

    internal void Read(int offset, Span<char> destination)
    {
        ILogTextContent current;
        lock (_contentGate)
        {
            ObjectDisposedException.ThrowIf(_content is null, this);
            current = _content;
            _references++;
        }
        try { current.Read(offset, destination); }
        finally { Release(); }
    }

    internal void Release()
    {
        ILogTextContent? released = null;
        lock (_contentGate)
        {
            if (--_references == 0)
            {
                released = _content;
                _content = null;
            }
        }
        if (released is not null) cleanup(this, released.Dispose);
    }

    private sealed class ContentLease(ContentOwner owner, int length, int residentCharacters, long version) : ILogTextContent
    {
        // Interlocked owns the nullable lease reference. Concurrent Read and Dispose on one lease is unsupported.
        private ContentOwner? _owner = owner;
        public int Length => length;
        public int ResidentCharacterCount => residentCharacters;
        public long Version => version;
        internal ILogTextContent Retain()
        {
            var current = Volatile.Read(ref _owner);
            ObjectDisposedException.ThrowIf(current is null, this);
            return current.Lease();
        }
        public void Read(int offset, Span<char> destination)
        {
            var current = Volatile.Read(ref _owner);
            ObjectDisposedException.ThrowIf(current is null, this);
            current.Read(offset, destination);
        }
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
