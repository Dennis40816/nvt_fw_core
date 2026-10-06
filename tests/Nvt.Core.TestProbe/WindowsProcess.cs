// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.TestProbe;

[SupportedOSPlatform("windows")]
internal static class WindowsProcess
{
    internal static Process StartDescendant(string mode, string marker, bool holdStandardPipes)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
        string[] arguments = [executable, "--mode", mode, "--tree-marker", marker];
        if (mode == "tree-root-exit")
        {
            arguments = [.. arguments, "--exit-code", "0"];
        }
        IntPtr command = Marshal.StringToHGlobalUni(string.Join(' ', arguments.Select(Quote)));
        var startup = new StartupInfoEx();
        startup.StartupInfo.Size = (uint)Marshal.SizeOf<StartupInfo>();
        uint flags = 0x08000000 | 0x00000004; // No console window; resume after acquiring the process handle.
        IntPtr attributes = IntPtr.Zero;
        IntPtr handles = IntPtr.Zero;
        bool initialized = false;
        var duplicates = new List<SafeFileHandle>();
        var information = new ProcessInformation();
        Process? process = null;
        bool resumed = false;
        try
        {
            if (holdStandardPipes)
            {
                using var nullDevice = File.OpenHandle("NUL", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                duplicates.Add(Duplicate(nullDevice.DangerousGetHandle()));
                foreach (int kind in new[] { -11, -12 })
                {
                    IntPtr standard = GetStdHandle(kind);
                    duplicates.Add(Duplicate(standard is 0 or -1 ? nullDevice.DangerousGetHandle() : standard));
                }
                startup.StartupInfo.Size = (uint)Marshal.SizeOf<StartupInfoEx>();
                startup.StartupInfo.Flags = 0x00000100; // STARTF_USESTDHANDLES
                startup.StartupInfo.StandardInput = duplicates[0].DangerousGetHandle();
                startup.StartupInfo.StandardOutput = duplicates[1].DangerousGetHandle();
                startup.StartupInfo.StandardError = duplicates[2].DangerousGetHandle();
                nuint size = 0;
                _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                if (size == 0 || Marshal.GetLastPInvokeError() != 122)
                {
                    throw NativeFailure();
                }
                attributes = Marshal.AllocHGlobal(checked((nint)size));
                if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size))
                {
                    throw NativeFailure();
                }
                initialized = true;
                handles = Marshal.AllocHGlobal(IntPtr.Size * duplicates.Count);
                for (int index = 0; index < duplicates.Count; index++)
                {
                    Marshal.WriteIntPtr(handles, index * IntPtr.Size, duplicates[index].DangerousGetHandle());
                }
                if (!UpdateProcThreadAttribute(attributes, 0, 0x00020002, handles,
                    (nuint)(IntPtr.Size * duplicates.Count), IntPtr.Zero, IntPtr.Zero))
                {
                    throw NativeFailure();
                }
                startup.AttributeList = attributes;
                flags |= 0x00080000; // EXTENDED_STARTUPINFO_PRESENT
            }
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, holdStandardPipes,
                flags, IntPtr.Zero, null, ref startup, out information))
            {
                throw NativeFailure();
            }
            process = Process.GetProcessById(checked((int)information.ProcessId));
            _ = process.Handle;
            if (ResumeThread(information.Thread) == uint.MaxValue)
            {
                throw NativeFailure();
            }
            resumed = true;
            return process;
        }
        finally
        {
            if (!resumed && information.Process is not 0 and not -1)
            {
                _ = TerminateProcess(information.Process, 1);
                _ = WaitForSingleObject(information.Process, Probe.HandshakeLimitMilliseconds);
                process?.Dispose();
            }
            if (information.Thread is not 0 and not -1)
            {
                _ = CloseHandle(information.Thread);
            }
            if (information.Process is not 0 and not -1)
            {
                _ = CloseHandle(information.Process);
            }
            Marshal.FreeHGlobal(command);
            if (initialized)
            {
                DeleteProcThreadAttributeList(attributes);
            }
            if (attributes != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(attributes);
            }
            if (handles != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handles);
            }
            foreach (var duplicate in duplicates)
            {
                duplicate.Dispose();
            }
        }
    }

    private static SafeFileHandle Duplicate(IntPtr source)
    {
        if (!DuplicateHandle(new IntPtr(-1), source, new IntPtr(-1), out var duplicate, 0, true, 2))
        {
            duplicate.Dispose();
            throw NativeFailure();
        }
        return duplicate;
    }

    private static Win32Exception NativeFailure() => new(Marshal.GetLastPInvokeError());

    private static string Quote(string argument)
    {
        var result = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            result.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes).Append(character);
            backslashes = 0;
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }

#pragma warning disable IDE0044 // Windows fills the mutable native layout fields.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        internal uint Size;
        private IntPtr reserved;
        private IntPtr desktop;
        private IntPtr title;
        private uint x;
        private uint y;
        private uint xSize;
        private uint ySize;
        private uint xCountChars;
        private uint yCountChars;
        private uint fillAttribute;
        internal uint Flags;
        private ushort showWindow;
        private ushort reserved2;
        private IntPtr reservedBytes;
        internal IntPtr StandardInput;
        internal IntPtr StandardOutput;
        internal IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        internal StartupInfo StartupInfo;
        internal IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        internal IntPtr Process;
        internal IntPtr Thread;
        internal uint ProcessId;
        private uint threadId;
    }
#pragma warning restore IDE0044

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int kind);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess,
        out SafeFileHandle target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, uint count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute,
        IntPtr value, nuint size, IntPtr previous, IntPtr returnedSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, IntPtr command, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags,
        IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}