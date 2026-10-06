// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Tests.TestProbe;

internal sealed class ProbeJob : IDisposable
{
    private readonly SafeFileHandle _handle;
    internal string Name { get; } = "core-test-job-" + Guid.NewGuid().ToString("N");

    internal ProbeJob()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Named Jobs require Windows.");
        }
        _handle = CreateJobObject(IntPtr.Zero, Name);
        if (_handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            _handle.Dispose();
            throw new Win32Exception(error);
        }
    }

    internal bool Contains(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Named Jobs require Windows.");
        }
        if (!IsProcessInJob(process.Handle, _handle, out bool member))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return member;
    }

    internal void Terminate(uint code)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Named Jobs require Windows.");
        }
        if (!TerminateJobObject(_handle, code))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public void Dispose()
    {
        try
        {
            Terminate(42);
        }
        finally
        {
            _handle.Dispose();
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool member);
}