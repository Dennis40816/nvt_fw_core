// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Files;

/// <summary>Rejects non-regular filesystem objects before and after opening files.</summary>
public static partial class RegularFileGuard
{
    private const uint WindowsDiskFileType = 0x0001;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixRegularFileType = 0x8000;

    /// <summary>Requires a regular filesystem file, rejecting directories, devices, and links.</summary>
    public static void RequirePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (OperatingSystem.IsWindows())
        {
            FileAttributes attributes = File.GetAttributes(path);
            const FileAttributes rejectedAttributes =
                FileAttributes.Device | FileAttributes.Directory | FileAttributes.ReparsePoint;
            if ((attributes & rejectedAttributes) == 0)
            {
                return;
            }

            throw NotRegularFile(path);
        }

        if (UnixLStat(path, out UnixFileStatus status) != 0)
        {
            throw NativeInspectionFailure(path);
        }

        if ((status.Mode & UnixFileTypeMask) != UnixRegularFileType)
        {
            throw NotRegularFile(path);
        }
    }

    /// <summary>Requires a valid open handle to a regular file.</summary>
    public static void RequireOpenHandle(SafeFileHandle handle, string displayPath)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        if (handle.IsInvalid || handle.IsClosed)
        {
            throw new IOException($"File '{displayPath}' has no valid open handle.");
        }

        if (OperatingSystem.IsWindows())
        {
            if (WindowsGetFileType(handle) != WindowsDiskFileType)
            {
                throw NotRegularFile(displayPath);
            }

            return;
        }

        if (UnixFStat(handle, out UnixFileStatus status) != 0)
        {
            throw NativeInspectionFailure(displayPath);
        }

        if ((status.Mode & UnixFileTypeMask) != UnixRegularFileType)
        {
            throw NotRegularFile(displayPath);
        }
    }

    private static UnauthorizedAccessException NotRegularFile(string path)
    {
        return new UnauthorizedAccessException(
            $"File '{path}' must be a regular filesystem file.");
    }

    /// <summary>Reads Unix device and inode identity, returning null for absent paths or non-directory parents.</summary>
    /// <remarks>This method is for non-Windows hosts and follows file links through stat.</remarks>
    public static (long Device, long Inode)? ReadUnixIdentity(string path)
    {
        if (UnixStat(path, out UnixFileStatus status) == 0)
        {
            return (status.Dev, status.Ino);
        }
        int error = Marshal.GetLastPInvokeError();
        // ENOENT/ENOTDIR mean the path is absent, including a concurrent deletion.
        return error is 2 or 20 ? null
            : throw new IOException($"Could not inspect local file '{path}' (native error {error}).");
    }

    private static IOException NativeInspectionFailure(string path)
    {
        return new IOException(
            $"Could not inspect file '{path}' (native error {Marshal.GetLastPInvokeError()}).");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileType", SetLastError = true)]
    private static partial uint WindowsGetFileType(SafeFileHandle handle);

    [LibraryImport(
        "System.Native",
        EntryPoint = "SystemNative_LStat",
        StringMarshalling = StringMarshalling.Utf8,
        SetLastError = true)]
    private static partial int UnixLStat(string path, out UnixFileStatus status);

    [LibraryImport(
        "System.Native",
        EntryPoint = "SystemNative_Stat",
        StringMarshalling = StringMarshalling.Utf8,
        SetLastError = true)]
    private static partial int UnixStat(string path, out UnixFileStatus status);

    [LibraryImport("System.Native", EntryPoint = "SystemNative_FStat", SetLastError = true)]
    private static partial int UnixFStat(SafeFileHandle handle, out UnixFileStatus status);

    // Stable System.Native FileStatus ABI used by the pinned .NET runtime, not a platform struct stat.
    [StructLayout(LayoutKind.Sequential)]
    private struct UnixFileStatus
    {
        internal int Flags;
        internal int Mode;
        internal uint Uid;
        internal uint Gid;
        internal long Size;
        internal long ATime;
        internal long ATimeNsec;
        internal long MTime;
        internal long MTimeNsec;
        internal long CTime;
        internal long CTimeNsec;
        internal long BirthTime;
        internal long BirthTimeNsec;
        internal long Dev;
        internal long RDev;
        internal long Ino;
        internal uint UserFlags;
    }
}
