// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.TestProbe;

internal static class LifetimeJob
{
    internal static SafeFileHandle? Join()
    {
        string? name = Environment.GetEnvironmentVariable("CORE_TEST_LIFETIME_JOB");
        if (name is null)
        {
            return null;
        }
        Probe.RequireWindows();
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        string? handle = Environment.GetEnvironmentVariable("CORE_TEST_LIFETIME_HANDLE");
        if (long.TryParse(handle, NumberStyles.None, CultureInfo.InvariantCulture, out long raw) &&
            !SetHandleInformation(new IntPtr(raw), 1, 0))
        {
            throw Failure();
        }
        foreach (string key in new[] { "CONTEXT", "HANDLE", "JOB", "STATE_PATH", "KIND" })
        {
            Environment.SetEnvironmentVariable("CORE_TEST_LIFETIME_" + key, null);
        }
        var job = OpenJobObject(0x0001 | 0x0004, false, name);
        if (job.IsInvalid)
        {
            var failure = Failure();
            job.Dispose();
            throw failure;
        }
        if (!AssignProcessToJobObject(job, new IntPtr(-1)))
        {
            var failure = Failure();
            job.Dispose();
            throw failure;
        }
        return job;
    }

    private static JobJoinException Failure() => new(
        "Job join failed: " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture));

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "OpenJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenJobObject(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}

internal sealed class JobJoinException(string message) : Exception(message);
