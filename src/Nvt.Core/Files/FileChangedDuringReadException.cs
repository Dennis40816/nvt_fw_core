// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Files;

/// <summary>The stream changed while its measured content was being read.</summary>
public sealed class FileChangedDuringReadException : IOException
{
    /// <summary>Creates a read rejection for an observed stream change.</summary>
    /// <param name="changeKind">The change observed during the read.</param>
    public FileChangedDuringReadException(FileChangeKind changeKind = FileChangeKind.Unspecified)
        : base("File length changed during complete-content read.")
    {
        ChangeKind = changeKind;
    }

    /// <summary>Gets the stream change observed before the read was rejected.</summary>
    public FileChangeKind ChangeKind { get; }
}
