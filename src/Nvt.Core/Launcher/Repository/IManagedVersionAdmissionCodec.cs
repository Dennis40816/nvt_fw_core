// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Repository;

/// <summary>Product-owned strict installed-admission wire codec.</summary>
/// <remarks>The adapter must preserve its complete schema, canonical version and exact identity rules.</remarks>
public interface IManagedVersionAdmissionCodec
{
    /// <summary>Encodes the exact admitted version and content identity.</summary>
    /// <param name="admission">The complete exact admission to encode.</param>
    /// <returns>Complete wire bytes; Repository copies them after checking its admission bound.</returns>
    ReadOnlyMemory<byte> Encode(ManagedVersionAdmission admission);

    /// <summary>Strictly decodes a complete bounded document without inventing an admission.</summary>
    /// <param name="exactBytes">The complete bounded wire document.</param>
    /// <param name="admission">The normalized admission on success, otherwise null.</param>
    /// <returns>Whether all product wire and schema predicates succeeded.</returns>
    bool TryDecode(ReadOnlyMemory<byte> exactBytes, out ManagedVersionAdmission? admission);
}
