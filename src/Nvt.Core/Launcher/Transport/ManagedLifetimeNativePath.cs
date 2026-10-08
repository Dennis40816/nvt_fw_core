// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Launcher.Transport;

// Resolves the physical lifetime lease path with the frozen native buffer bound.
internal static partial class ManagedLifetimeNativePath
{
    internal const int MaximumPathLength = 32_768;
    internal delegate uint FinalPathReader(SafeFileHandle handle, Span<char> path, uint flags);

    internal static bool IsExactLeaseHandle(
        SafeFileHandle handle, string expectedPath, FinalPathReader? readPath = null)
    {
        Span<char> path = stackalloc char[MaximumPathLength];
        uint length = (readPath ?? ReadFinalPath)(handle, path, 0);
        if (length is 0 or >= MaximumPathLength)
        {
            return false;
        }
        const string extendedPrefix = @"\\?\";
        string actual = new(path[..checked((int)length)]);
        if (actual.StartsWith(extendedPrefix, StringComparison.Ordinal))
        {
            actual = actual[extendedPrefix.Length..];
        }
        return string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expectedPath), PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static unsafe uint ReadFinalPath(SafeFileHandle handle, Span<char> path, uint flags)
    {
        fixed (char* buffer = path)
        {
            return GetFinalPathNameByHandle(handle, buffer, checked((uint)path.Length), flags);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        char* path,
        uint pathLength,
        uint flags);
}
