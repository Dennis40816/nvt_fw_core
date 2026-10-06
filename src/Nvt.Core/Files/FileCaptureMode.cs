// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Files;

/// <summary>Specifies whether a complete stream read also keeps its bytes.</summary>
public enum FileCaptureMode
{
    /// <summary>Return the length and SHA-256 hash without keeping the content bytes.</summary>
    IdentityOnly,

    /// <summary>Also return the complete content bytes used to compute the hash.</summary>
    CaptureBytes,
}
