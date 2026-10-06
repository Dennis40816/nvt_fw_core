// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Files.Windows;

internal sealed partial class WindowsStablePathCustody
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Successful ownership transfers into the result custody; failures dispose handles.")]
    private static partial WindowsStableCustodyResult TryAcquireWindows(
        string rootPath,
        string? relativeFileName,
        AcquisitionKind kind,
        Action<WindowsStableCustodyStage>? testHook,
        WindowsStableTreeLimits? treeLimits,
        CancellationToken cancellationToken)
    {
        var handles = new List<SafeFileHandle>();
        bool ownershipTransferred = false;
        var files = new Dictionary<string, HeldEntry>(PathComparer);
        var topology = new Dictionary<string, string[]>(PathComparer);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? parentPath = Path.GetDirectoryName(rootPath);
            string? volumeRoot = Path.GetPathRoot(rootPath);
            if (parentPath is null || volumeRoot is null ||
                PathComparer.Equals(
                    Path.TrimEndingDirectorySeparator(rootPath),
                    Path.TrimEndingDirectorySeparator(volumeRoot)))
            {
                return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.InvalidPath);
            }
            var identities = new List<(string Path, SafeFileHandle Handle)>();
            WindowsStableCustodyIssue ancestorIssue = OpenAncestorChain(
                volumeRoot,
                parentPath,
                handles,
                identities,
                out SafeFileHandle? parentHandle);
            if (ancestorIssue != WindowsStableCustodyIssue.None || parentHandle is null)
            {
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(ancestorIssue);
            }
            bool rootIsDirectory = relativeFileName is null;
            string rootName = Path.GetFileName(rootPath);
            testHook?.Invoke(WindowsStableCustodyStage.BeforeRootOpen);
            int rootStatus = OpenRelative(
                parentHandle,
                rootName,
                rootIsDirectory,
                writableParent: kind == AcquisitionKind.WritableParent,
                allowWriteShare: kind == AcquisitionKind.WritableParent,
                allowDeleteShare: kind == AcquisitionKind.PromotableTree,
                requestDeleteAccess: false,
                out SafeFileHandle rootHandle);
            if (IsExactChildMissingStatus(rootStatus))
            {
                rootHandle.Dispose();
                testHook?.Invoke(WindowsStableCustodyStage.AfterMissingRootObservation);
                int confirmedStatus = OpenRelative(
                    parentHandle,
                    rootName,
                    rootIsDirectory,
                    writableParent: kind == AcquisitionKind.WritableParent,
                    allowWriteShare: kind == AcquisitionKind.WritableParent,
                    allowDeleteShare: kind == AcquisitionKind.PromotableTree,
                    requestDeleteAccess: false,
                    out rootHandle);
                if (confirmedStatus == NativeMethods.StatusSuccess)
                {
                    rootHandle.Dispose();
                    DisposeHandles(handles);
                    return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Changed);
                }
                if (IsExactChildMissingStatus(confirmedStatus))
                {
                    rootHandle.Dispose();
                    DisposeHandles(handles);
                    return kind == AcquisitionKind.SingleFile
                        ? WindowsStableCustodyResult.MissingExactChild()
                        : WindowsStableCustodyResult.Failure(
                            WindowsStableCustodyIssue.Unavailable);
                }
                rootStatus = confirmedStatus;
            }
            if (rootStatus != NativeMethods.StatusSuccess)
            {
                rootHandle.Dispose();
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(MapStatus(rootStatus));
            }
            if (!HasExpectedType(rootHandle, rootIsDirectory))
            {
                rootHandle.Dispose();
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.ReparsePoint);
            }
            handles.Add(rootHandle);
            identities.Add((rootPath, rootHandle));
            SafeFileHandle? rootDirectoryHandle = rootIsDirectory ? rootHandle : null;
            string custodyRoot = rootPath;
            if (kind == AcquisitionKind.SingleFile)
            {
                custodyRoot = parentPath;
                files.Add(NormalizeRelativeFile(rootName), new(rootPath, rootHandle));
            }
            else if (kind is AcquisitionKind.ImmutableTree or AcquisitionKind.PromotableTree)
            {
                if (treeLimits is not { } limits)
                {
                    DisposeHandles(handles);
                    return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Unavailable);
                }
                WindowsStableCustodyIssue treeIssue = CaptureTree(
                    rootPath,
                    rootHandle,
                    handles,
                    files,
                    identities,
                    topology,
                    limits,
                    deleteCapable: false,
                    cancellationToken);
                if (treeIssue != WindowsStableCustodyIssue.None)
                {
                    DisposeHandles(handles);
                    return WindowsStableCustodyResult.Failure(treeIssue);
                }
            }
            testHook?.Invoke(WindowsStableCustodyStage.AfterTreeCaptured);
            cancellationToken.ThrowIfCancellationRequested();
            if (!RevalidateIdentities(identities))
            {
                DisposeHandles(handles);
                return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Changed);
            }
            WindowsStableCustodyResult result = new(
                new WindowsStablePathCustody(
                    custodyRoot,
                    rootDirectoryHandle,
                    handles,
                    files,
                    identities,
                    topology),
                WindowsStableCustodyIssue.None);
            ownershipTransferred = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            DisposeHandles(handles);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            DisposeHandles(handles);
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.AccessDenied);
        }
        catch (Exception exception) when (exception is
            IOException or NotSupportedException or ArgumentException)
        {
            DisposeHandles(handles);
            return WindowsStableCustodyResult.Failure(WindowsStableCustodyIssue.Unavailable);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                DisposeHandles(handles);
            }
        }
    }

    internal static int OpenRelative(
        SafeFileHandle parent,
        string name,
        bool directory,
        bool writableParent,
        bool allowWriteShare,
        bool allowDeleteShare,
        bool requestDeleteAccess,
        out SafeFileHandle handle)
    {
        uint access = (directory ? NativeMethods.ListDirectory : NativeMethods.GenericRead) |
            NativeMethods.ReadAttributes |
            NativeMethods.Synchronize;
        if (writableParent)
        {
            access |= NativeMethods.AddFile | NativeMethods.AddSubdirectory;
        }
        if (requestDeleteAccess)
        {
            access |= NativeMethods.Delete;
        }
        uint share = NativeMethods.ShareRead;
        if (allowWriteShare)
        {
            share |= NativeMethods.ShareWrite;
        }
        if (allowDeleteShare)
        {
            share |= NativeMethods.ShareDelete;
        }
        uint options = (directory ? NativeMethods.DirectoryFile : NativeMethods.NonDirectoryFile) |
            NativeMethods.SynchronousIoNonAlert |
            NativeMethods.OpenReparsePoint;
        return CreateRelative(parent, name, access, share, options, NativeMethods.FileOpen, out handle);
    }

    internal static int CreateRelative(
        SafeFileHandle parent,
        string name,
        uint desiredAccess,
        uint shareAccess,
        uint createOptions,
        uint createDisposition,
        out SafeFileHandle handle)
    {
        if (!IsSingleComponent(name))
        {
            handle = new SafeFileHandle(IntPtr.Zero, ownsHandle: true);
            return NativeMethods.StatusObjectNameInvalid;
        }
        nint nameBuffer = Marshal.StringToHGlobalUni(name);
        nint unicodeBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.UnicodeString>());
        try
        {
            var unicode = new NativeMethods.UnicodeString
            {
                Length = checked((ushort)(name.Length * sizeof(char))),
                MaximumLength = checked((ushort)(name.Length * sizeof(char))),
                Buffer = nameBuffer,
            };
            Marshal.StructureToPtr(unicode, unicodeBuffer, fDeleteOld: false);
            var attributes = new NativeMethods.ObjectAttributes
            {
                Length = checked((uint)Marshal.SizeOf<NativeMethods.ObjectAttributes>()),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicodeBuffer,
                Attributes = NativeMethods.ObjectCaseInsensitive | NativeMethods.ObjectDontReparse,
            };
            return NativeMethods.NtCreateFile(
                out handle,
                desiredAccess,
                in attributes,
                out _,
                0,
                NativeMethods.NormalAttribute,
                shareAccess,
                createDisposition,
                createOptions,
                0,
                0);
        }
        finally
        {
            Marshal.FreeHGlobal(unicodeBuffer);
            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    private static bool RevalidateIdentities(
        IEnumerable<(string Path, SafeFileHandle Handle)> identities)
    {
        foreach ((string path, SafeFileHandle held) in identities)
        {
            bool directory = IsDirectory(held);
            using SafeFileHandle resolved = NativeMethods.CreateFile(
                $@"\\?\{path}",
                NativeMethods.ReadAttributes | NativeMethods.Synchronize,
                NativeMethods.ShareRead | NativeMethods.ShareWrite | NativeMethods.ShareDelete,
                0,
                NativeMethods.OpenExisting,
                (directory ? NativeMethods.BackupSemantics : 0) | NativeMethods.OpenReparsePoint,
                0);
            if (resolved.IsInvalid || !HasExpectedType(resolved, directory) ||
                !IsSameIdentity(held, resolved))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool HasExpectedType(SafeFileHandle handle, bool directory)
    {
        return TryGetAttributes(handle, out NativeMethods.FileAttributeTagInfo attributes) &&
            (attributes.FileAttributes & NativeMethods.ReparsePointAttribute) == 0 &&
            (attributes.FileAttributes & NativeMethods.DirectoryAttribute) != 0 == directory;
    }

    private static bool IsDirectory(SafeFileHandle handle)
    {
        return TryGetAttributes(handle, out NativeMethods.FileAttributeTagInfo attributes) &&
            (attributes.FileAttributes & NativeMethods.DirectoryAttribute) != 0;
    }

    private static bool TryGetAttributes(
        SafeFileHandle handle,
        out NativeMethods.FileAttributeTagInfo attributes)
    {
        return NativeMethods.GetFileInformationByHandleEx(
            handle,
            NativeMethods.FileAttributeTagInfoClass,
            out attributes,
            checked((uint)Marshal.SizeOf<NativeMethods.FileAttributeTagInfo>()));
    }

    private static bool IsSameIdentity(SafeFileHandle left, SafeFileHandle right)
    {
        return TryGetIdentity(left, out WindowsStablePathIdentity leftIdentity) &&
            TryGetIdentity(right, out WindowsStablePathIdentity rightIdentity) &&
            leftIdentity == rightIdentity;
    }

    internal static bool TryGetIdentity(
        SafeFileHandle handle,
        out WindowsStablePathIdentity identity)
    {
        bool success = NativeMethods.GetFileIdentityByHandle(
            handle,
            NativeMethods.FileIdInfoClass,
            out NativeMethods.FileIdInfo native,
            checked((uint)Marshal.SizeOf<NativeMethods.FileIdInfo>()));
        identity = success
            ? new(native.VolumeSerialNumber, native.FileIdLow, native.FileIdHigh)
            : default;
        return success;
    }

    internal static SafeFileHandle Duplicate(SafeFileHandle source)
    {
        nint process = NativeMethods.GetCurrentProcess();
        if (!NativeMethods.DuplicateHandle(
                process,
                source,
                process,
                out SafeFileHandle duplicate,
                0,
                inheritHandle: false,
                NativeMethods.DuplicateSameAccess))
        {
            duplicate.Dispose();
            throw new IOException("Unable to duplicate stable file custody.");
        }
        return duplicate;
    }

    internal static bool MarkDeleteOnClose(SafeFileHandle handle)
    {
        var disposition = new NativeMethods.FileDispositionInfo { DeleteFile = 1 };
        return NativeMethods.SetFileInformationByHandle(
            handle,
            NativeMethods.FileDispositionInfoClass,
            in disposition,
            size: 1);
    }

    internal static int RenameRelative(
        SafeFileHandle source,
        SafeFileHandle destinationParent,
        string destinationName)
    {
        if (!IsSingleComponent(destinationName))
        {
            return NativeMethods.StatusObjectNameInvalid;
        }
        byte[] fileName = System.Text.Encoding.Unicode.GetBytes(destinationName);
        int nameOffset = GetRenameFileNameOffset(IntPtr.Size);
        nint buffer = Marshal.AllocHGlobal(checked(nameOffset + fileName.Length));
        try
        {
            if (IntPtr.Size == 8)
            {
                Marshal.WriteInt64(buffer, 0, 0);
            }
            else
            {
                Marshal.WriteInt32(buffer, 0, 0);
            }
            Marshal.WriteIntPtr(
                buffer,
                IntPtr.Size == 8 ? 8 : 4,
                destinationParent.DangerousGetHandle());
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 16 : 8, fileName.Length);
            Marshal.Copy(fileName, 0, buffer + nameOffset, fileName.Length);
            return NativeMethods.NtSetInformationFile(
                source,
                out _,
                buffer,
                checked((uint)(nameOffset + fileName.Length)),
                NativeMethods.FileRenameInformationClass);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static int GetRenameFileNameOffset(int pointerSize)
    {
        return pointerSize switch
        {
            4 => 12,
            8 => 20,
            _ => throw new ArgumentOutOfRangeException(
                nameof(pointerSize),
                pointerSize,
                "FILE_RENAME_INFORMATION supports only 32-bit and 64-bit pointer layouts."),
        };
    }

    internal static partial class NativeMethods
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint ReadData = 0x00000001;
        internal const uint WriteData = 0x00000002;
        internal const uint ListDirectory = 0x00000001;
        internal const uint AddFile = 0x00000002;
        internal const uint AddSubdirectory = 0x00000004;
        internal const uint ReadAttributes = 0x00000080;
        internal const uint WriteAttributes = 0x00000100;
        internal const uint Delete = 0x00010000;
        internal const uint Synchronize = 0x00100000;
        internal const uint ShareRead = 0x00000001;
        internal const uint ShareWrite = 0x00000002;
        internal const uint ShareDelete = 0x00000004;
        internal const uint OpenExisting = 3;
        internal const uint BackupSemantics = 0x02000000;
        internal const uint OpenReparsePoint = 0x00200000;
        internal const uint NormalAttribute = 0x00000080;
        internal const uint DirectoryFile = 0x00000001;
        internal const uint SynchronousIoNonAlert = 0x00000020;
        internal const uint NonDirectoryFile = 0x00000040;
        internal const uint WriteThrough = 0x00000002;
        internal const uint DeleteOnClose = 0x00001000;
        internal const uint DirectoryAttribute = 0x00000010;
        internal const uint ReparsePointAttribute = 0x00000400;
        internal const uint FileOpen = 1;
        internal const uint FileCreate = 2;
        internal const uint FileOpenIf = 3;
        internal const uint ObjectCaseInsensitive = 0x00000040;
        internal const uint ObjectDontReparse = 0x00001000;
        internal const uint DuplicateSameAccess = 0x00000002;
        internal const int FileAttributeTagInfoClass = 9;
        internal const int FileIdInfoClass = 18;
        internal const int ErrorFileNotFound = 2;
        internal const int ErrorPathNotFound = 3;
        internal const int ErrorAccessDenied = 5;
        internal const int ErrorSharingViolation = 32;
        internal const int StatusSuccess = 0;
        internal const int StatusAccessDenied = unchecked((int)0xC0000022);
        internal const int StatusObjectNameInvalid = unchecked((int)0xC0000033);
        internal const int StatusObjectNameNotFound = unchecked((int)0xC0000034);
        internal const int StatusObjectNameCollision = unchecked((int)0xC0000035);
        internal const int StatusObjectPathInvalid = unchecked((int)0xC0000039);
        internal const int StatusObjectPathNotFound = unchecked((int)0xC000003A);
        internal const int StatusSharingViolation = unchecked((int)0xC0000043);
        internal const int StatusDeletePending = unchecked((int)0xC0000056);
        internal const int StatusNotADirectory = unchecked((int)0xC0000103);
        internal const int StatusFileIsADirectory = unchecked((int)0xC00000BA);
        internal const int StatusReparsePointEncountered = unchecked((int)0xC000050B);
        internal const int FileDispositionInfoClass = 4;
        internal const int FileRenameInformationClass = 10;

        [StructLayout(LayoutKind.Sequential)]
        internal struct UnicodeString
        {
            internal ushort Length;
            internal ushort MaximumLength;
            internal nint Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ObjectAttributes
        {
            internal uint Length;
            internal nint RootDirectory;
            internal nint ObjectName;
            internal uint Attributes;
            internal nint SecurityDescriptor;
            internal nint SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IoStatusBlock
        {
            internal nint Status;
            internal nuint Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileAttributeTagInfo
        {
            internal uint FileAttributes;
            internal uint ReparseTag;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileDispositionInfo
        {
            internal byte DeleteFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileIdInfo
        {
            internal ulong VolumeSerialNumber;
            internal ulong FileIdLow;
            internal ulong FileIdHigh;
        }

        [LibraryImport(
            "kernel32.dll",
            EntryPoint = "CreateFileW",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            nint securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            nint templateFile);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtCreateFile(
            out SafeFileHandle fileHandle,
            uint desiredAccess,
            in ObjectAttributes objectAttributes,
            out IoStatusBlock ioStatusBlock,
            nint allocationSize,
            uint fileAttributes,
            uint shareAccess,
            uint createDisposition,
            uint createOptions,
            nint eaBuffer,
            uint eaLength);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtSetInformationFile(
            SafeFileHandle fileHandle,
            out IoStatusBlock ioStatusBlock,
            nint fileInformation,
            uint length,
            int fileInformationClass);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            int fileInformationClass,
            out FileAttributeTagInfo fileInformation,
            uint bufferSize);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetFileInformationByHandle(
            SafeFileHandle file,
            int fileInformationClass,
            in FileDispositionInfo fileInformation,
            uint size);

        [LibraryImport(
            "kernel32.dll",
            EntryPoint = "GetFileInformationByHandleEx",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetFileIdentityByHandle(
            SafeFileHandle file,
            int fileInformationClass,
            out FileIdInfo fileInformation,
            uint bufferSize);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DuplicateHandle(
            nint sourceProcessHandle,
            SafeFileHandle sourceHandle,
            nint targetProcessHandle,
            out SafeFileHandle targetHandle,
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint options);

        [LibraryImport("kernel32.dll")]
        internal static partial nint GetCurrentProcess();
    }
}
