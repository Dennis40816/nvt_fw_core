// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Files;

/// <summary>Contains the complete length, SHA-256 hash, and optional content bytes.</summary>
/// <remarks>The uninitialized default has zero length and null hash and content. Successful reads always supply a hash.</remarks>
/// <param name="Length">The measured length that was read in full.</param>
/// <param name="Sha256">The raw SHA-256 hash bytes, or null for an uninitialized default.</param>
/// <param name="Bytes">The captured content bytes, or null when only the identity was requested.</param>
public readonly record struct BoundedReadResult(long Length, byte[]? Sha256, byte[]? Bytes);
