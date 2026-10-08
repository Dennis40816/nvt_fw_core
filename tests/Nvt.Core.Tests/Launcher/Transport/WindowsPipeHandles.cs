// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Tests.Launcher.Transport;

internal static class WindowsPipeHandles
{
    internal static string DuplicateClient(AnonymousPipeServerStream pipe)
    {
        IntPtr source = new(long.Parse(pipe.GetClientHandleAsString(), NumberStyles.None, CultureInfo.InvariantCulture));
        return Duplicate(source);
    }

    internal static string Duplicate(IntPtr source)
    {
        IntPtr process = GetCurrentProcess();
        if (!DuplicateHandle(process, source, process, out SafeFileHandle duplicate, 0, true, 2))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        string value = duplicate.DangerousGetHandle().ToInt64().ToString(CultureInfo.InvariantCulture);
        duplicate.SetHandleAsInvalid();
        duplicate.Dispose();
        return value;
    }

    internal static uint Flags(string handle)
    {
        if (!GetHandleInformation(new IntPtr(long.Parse(handle, CultureInfo.InvariantCulture)), out uint flags))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return flags;
    }

    internal static bool IsOpen(string handle) =>
        GetHandleInformation(new IntPtr(long.Parse(handle, CultureInfo.InvariantCulture)), out _);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess, out SafeFileHandle targetHandle,
        uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
