// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Parent-acquired start lease plus named Windows job for one managed process tree.</summary>
internal sealed partial class ManagedProcessLifetimeLease : IDisposable
{
    internal const string ApplicationSuffix = ".application-lifetime.v1.lock";
    internal const string LauncherSuffix = ".launcher-lifetime.v1.lock";
    internal const string BootstrapSuffix = ".bootstrap-lifetime.v1.lock";
    private const string ContextVersion = "v1";
    private const uint HandleFlagInherit = 1;

    private readonly LifetimeAuthority _authority;

    private ManagedProcessLifetimeLease(
        ManagedLifetimeProtocol protocol,
        FileStream stream,
        SafeFileHandle job,
        string jobName,
        string statePath,
        ManagedProcessLifetimeKind kind)
    {
        _authority = new(protocol, stream, job, jobName, statePath, kind);
    }

    internal string InheritedHandle => InheritedHandleValue.ToInt64().ToString(CultureInfo.InvariantCulture);
    internal IntPtr InheritedHandleValue => _authority.Stream.SafeFileHandle.DangerousGetHandle();
    internal string JobName => _authority.JobName;

    internal void ApplyInheritedContext(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment[_authority.Protocol.Names.LifetimeContext] = ContextVersion;
        startInfo.Environment[_authority.Protocol.Names.LifetimeHandle] = InheritedHandle;
        startInfo.Environment[_authority.Protocol.Names.LifetimeJob] = JobName;
        startInfo.Environment[_authority.Protocol.Names.LifetimeStatePath] = _authority.StatePath;
        startInfo.Environment[_authority.Protocol.Names.LifetimeKind] = _authority.Kind.ToString();
    }

    internal static ManagedProcessLifetimeLease? TryAcquire(
        ManagedLifetimeProtocol protocol,
        string statePath,
        ManagedProcessLifetimeKind kind)
    {
        _ = Acquire(protocol, statePath, kind, out ManagedProcessLifetimeLease? lease);
        return lease;
    }

    internal static ManagedProcessLifetimeLease? TryAcquire(ManagedLifetimeProtocol protocol, string statePath, string suffix)
    {
        _ = Acquire(protocol, statePath, suffix, out ManagedProcessLifetimeLease? lease);
        return lease;
    }

    internal static ManagedProcessLifetimeLeaseAcquisitionOutcome Acquire(
        ManagedLifetimeProtocol protocol,
        string statePath,
        ManagedProcessLifetimeKind kind,
        out ManagedProcessLifetimeLease? lease)
    {
        return Acquire(protocol, statePath, GetSuffix(kind), out lease);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification =
        "Successful lifetime ownership transfers through the acquisition out parameter.")]
    private static ManagedProcessLifetimeLeaseAcquisitionOutcome Acquire(
        ManagedLifetimeProtocol protocol,
        string statePath,
        string suffix,
        out ManagedProcessLifetimeLease? lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        lease = null;
        if (!OperatingSystem.IsWindows())
        {
            return ManagedProcessLifetimeLeaseAcquisitionOutcome.Unavailable;
        }
        FileStream? stream = null;
        SafeFileHandle? job = null;
        try
        {
            string normalizedStatePath = Path.GetFullPath(statePath);
            string path = normalizedStatePath + suffix;
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path) ??
                throw new ArgumentException("Lifetime lease has no parent directory.", nameof(statePath)));
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _ = SetHandleInformation(
                    stream.SafeFileHandle.DangerousGetHandle(),
                    HandleFlagInherit,
                    flags: 0)
                ? true
                : throw new Win32Exception(Marshal.GetLastPInvokeError());
            ManagedProcessLifetimeKind kind = GetKind(suffix);
            string jobName = kind == ManagedProcessLifetimeKind.Bootstrap
                ? GetBootstrapInvocationJobName(protocol, statePath, suffix)
                : GetJobName(protocol, statePath, suffix);
            job = OpenOrCreateJob(jobName);
            if (job is null)
            {
                return ManagedProcessLifetimeLeaseAcquisitionOutcome.Unavailable;
            }
            lease = new ManagedProcessLifetimeLease(
                protocol,
                stream,
                job,
                jobName,
                normalizedStatePath,
                kind);
            stream = null;
            job = null;
            return ManagedProcessLifetimeLeaseAcquisitionOutcome.Acquired;
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
        {
            return ManagedProcessLifetimeLeaseAcquisitionOutcome.Busy;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or Win32Exception)
        {
            return ManagedProcessLifetimeLeaseAcquisitionOutcome.Unavailable;
        }
        finally
        {
            stream?.Dispose();
            job?.Dispose();
        }
    }

    internal static ManagedProcessLifetimeStatus GetStatus(ManagedLifetimeProtocol protocol, string statePath, string suffix)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ManagedProcessLifetimeStatus.Unavailable;
        }
        ManagedProcessLifetimeStatus lease = GetLeaseStatus(statePath, suffix);
        if (GetKind(suffix) == ManagedProcessLifetimeKind.Bootstrap)
        {
            return lease;
        }
        ManagedProcessLifetimeStatus tree = GetTreeStatus(GetJobName(protocol, statePath, suffix));
        return lease == ManagedProcessLifetimeStatus.Active || tree == ManagedProcessLifetimeStatus.Active
            ? ManagedProcessLifetimeStatus.Active
            : lease == ManagedProcessLifetimeStatus.Unavailable || tree == ManagedProcessLifetimeStatus.Unavailable
                ? ManagedProcessLifetimeStatus.Unavailable
                : ManagedProcessLifetimeStatus.Exited;
    }

    internal static ManagedProcessLifetimeStatus GetStatus(
        ManagedLifetimeProtocol protocol,
        string statePath,
        ManagedProcessLifetimeKind kind)
    {
        return GetStatus(protocol, statePath, GetSuffix(kind));
    }

    internal bool TerminateTreeAndConfirmEmpty(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        if (!TerminateJobObject(_authority.Job.DangerousGetHandle(), exitCode: 1))
        {
            return false;
        }
        long deadline = Environment.TickCount64 + checked((long)Math.Ceiling(timeout.TotalMilliseconds));
        while (Environment.TickCount64 <= deadline)
        {
            if (!TryGetActiveProcessCount(_authority.Job, out uint active))
            {
                return false;
            }
            if (active == 0)
            {
                return true;
            }
            Thread.Sleep(25);
        }
        return false;
    }

    /// <summary>Stops closing the accepted READY job from terminating its process tree.</summary>
    internal bool TryReleaseAcceptedTree()
    {
        var limits = new JobExtendedLimitInformation();
        return SetInformationJobObject(
            _authority.Job.DangerousGetHandle(),
            JobObjectExtendedLimitInformation,
            in limits,
            Marshal.SizeOf<JobExtendedLimitInformation>());
    }

    internal static InheritedManagedProcessLifetimeCapture CaptureInherited(
        ManagedLifetimeProtocol protocol,
        string? statePath,
        ManagedProcessLifetimeKind kind,
        bool managedContextAdvertised)
    {
        string? context = TakeEnvironment(protocol.Names.LifetimeContext);
        string? value = TakeEnvironment(protocol.Names.LifetimeHandle);
        string? jobName = TakeEnvironment(protocol.Names.LifetimeJob);
        string? advertisedStatePath = TakeEnvironment(protocol.Names.LifetimeStatePath);
        string? advertisedKind = TakeEnvironment(protocol.Names.LifetimeKind);
        if (context is null && value is null && jobName is null &&
            advertisedStatePath is null && advertisedKind is null)
        {
            return managedContextAdvertised
                ? InheritedManagedProcessLifetimeCapture.Invalid
                : InheritedManagedProcessLifetimeCapture.NotInherited;
        }
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long rawHandle) ||
            rawHandle is 0 or -1 ||
            !OperatingSystem.IsWindows())
        {
            return InheritedManagedProcessLifetimeCapture.Invalid;
        }

#pragma warning disable CA2000 // Ownership transfers to the typed capture or the failure path.
        var handle = new SafeFileHandle(new IntPtr(rawHandle), ownsHandle: true);
#pragma warning restore CA2000
        SafeFileHandle? job = null;
        FileStream? stream = null;
        try
        {
            _ = SetHandleInformation(handle.DangerousGetHandle(), HandleFlagInherit, flags: 0)
                ? true
                : throw new Win32Exception(Marshal.GetLastPInvokeError());
            string? normalizedStatePath = TryNormalizePath(statePath);
            if (!string.Equals(context, ContextVersion, StringComparison.Ordinal) ||
                normalizedStatePath is null ||
                !string.Equals(TryNormalizePath(advertisedStatePath), normalizedStatePath, PathComparison) ||
                !string.Equals(advertisedKind, kind.ToString(), StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(jobName) ||
                !IsExpectedJobName(protocol, normalizedStatePath, kind, jobName))
            {
                throw new InvalidOperationException("Inherited lifetime metadata is invalid.");
            }
            if (!ManagedLifetimeNativePath.IsExactLeaseHandle(handle, normalizedStatePath + GetSuffix(kind)))
            {
                throw new InvalidOperationException("Inherited lifetime lease does not match the managed state path.");
            }
            job = OpenJob(jobName, JobObjectAssignProcess | JobObjectQuery);
            if (job is null || !AssignProcessToJobObject(job.DangerousGetHandle(), GetCurrentProcess()))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            stream = new FileStream(handle, FileAccess.ReadWrite);
            return InheritedManagedProcessLifetimeCapture.Create(stream, job);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or Win32Exception)
        {
            stream?.Dispose();
            job?.Dispose();
            if (stream is null)
            {
                handle.Dispose();
            }
            return InheritedManagedProcessLifetimeCapture.Invalid;
        }
    }

    public void Dispose()
    {
        _authority.Stream.Dispose();
        _authority.Job.Dispose();
    }

    private static ManagedProcessLifetimeStatus GetLeaseStatus(string statePath, string suffix)
    {
        if (!LifetimeStatePath.TryNormalizeExactAbsolutePath(statePath, out string normalizedStatePath))
        {
            return ManagedProcessLifetimeStatus.Unavailable;
        }
        WindowsStableCustodyResult observation = WindowsStablePathCustody.TryAcquireFile(
            normalizedStatePath + suffix);
        using WindowsStablePathCustody? custody = observation.Custody;
        return observation.IsExactChildMissing ||
            (observation.IsAcquired && custody!.RevalidateClosedTree())
            ? ManagedProcessLifetimeStatus.Exited
            : observation.Issue == WindowsStableCustodyIssue.Contended
                ? ManagedProcessLifetimeStatus.Active
                : ManagedProcessLifetimeStatus.Unavailable;
    }

    private static ManagedProcessLifetimeStatus GetTreeStatus(string jobName)
    {
        using SafeFileHandle? job = OpenJob(jobName, JobObjectQuery);
        return job is null
            ? Marshal.GetLastPInvokeError() == ErrorFileNotFound
                ? ManagedProcessLifetimeStatus.Exited
                : ManagedProcessLifetimeStatus.Unavailable
            : TryGetActiveProcessCount(job, out uint active)
                ? active == 0
                    ? ManagedProcessLifetimeStatus.Exited
                    : ManagedProcessLifetimeStatus.Active
                : ManagedProcessLifetimeStatus.Unavailable;
    }

    private static bool TryGetActiveProcessCount(SafeFileHandle job, out uint active)
    {
        bool success = QueryInformationJobObject(
            job.DangerousGetHandle(),
            JobObjectBasicAccountingInformation,
            out JobBasicAccountingInformation information,
            Marshal.SizeOf<JobBasicAccountingInformation>(),
            out _);
        active = success ? information.ActiveProcesses : 0;
        return success;
    }

    private static SafeFileHandle? OpenOrCreateJob(string jobName)
    {
        IntPtr raw = CreateJobObject(IntPtr.Zero, jobName);
        if (raw is 0 or -1)
        {
            return null;
        }
#pragma warning disable CA2000 // Ownership transfers to the caller or the explicit failure path.
        var job = new SafeFileHandle(raw, ownsHandle: true);
#pragma warning restore CA2000
        var limits = new JobExtendedLimitInformation
        {
            BasicLimitInformation = new JobBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };
        if (SetInformationJobObject(
                job.DangerousGetHandle(),
                JobObjectExtendedLimitInformation,
                in limits,
                Marshal.SizeOf<JobExtendedLimitInformation>()))
        {
            return job;
        }
        job.Dispose();
        return null;
    }

    private static SafeFileHandle? OpenJob(string jobName, uint access)
    {
        IntPtr raw = OpenJobObject(access, inheritHandle: false, jobName);
        return raw is 0 or -1 ? null : new SafeFileHandle(raw, ownsHandle: true);
    }

    private static string GetJobName(ManagedLifetimeProtocol protocol, string statePath, string suffix)
    {
        string identity = FileSystemVersionManagerWriteLease.GetLockPath(statePath) + suffix;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"{protocol.JobNamePrefix}.{hash[..32]}";
    }

    private static string GetBootstrapInvocationJobName(ManagedLifetimeProtocol protocol, string statePath, string suffix)
    {
        return $"{GetJobName(protocol, statePath, suffix)}.Invocation.{Guid.NewGuid():N}";
    }

    internal static bool IsExpectedJobName(
        ManagedLifetimeProtocol protocol,
        string statePath,
        ManagedProcessLifetimeKind kind,
        string? jobName)
    {
        string expected = GetJobName(protocol, statePath, GetSuffix(kind));
        if (kind != ManagedProcessLifetimeKind.Bootstrap)
        {
            return string.Equals(jobName, expected, StringComparison.Ordinal);
        }
        const string invocationMarker = ".Invocation.";
        return jobName is not null &&
            jobName.StartsWith(expected + invocationMarker, StringComparison.Ordinal) &&
            jobName.Length == expected.Length + invocationMarker.Length + 32 &&
            jobName[(expected.Length + invocationMarker.Length)..].All(
                static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }

    private static ManagedProcessLifetimeKind GetKind(string suffix)
    {
        return suffix switch
        {
            BootstrapSuffix => ManagedProcessLifetimeKind.Bootstrap,
            ApplicationSuffix => ManagedProcessLifetimeKind.Application,
            LauncherSuffix => ManagedProcessLifetimeKind.Launcher,
            _ => throw new ArgumentOutOfRangeException(nameof(suffix)),
        };
    }

    private static string GetSuffix(ManagedProcessLifetimeKind kind)
    {
        return kind switch
        {
            ManagedProcessLifetimeKind.Bootstrap => BootstrapSuffix,
            ManagedProcessLifetimeKind.Application => ApplicationSuffix,
            ManagedProcessLifetimeKind.Launcher => LauncherSuffix,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string? TryNormalizePath(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string? TakeEnvironment(string key)
    {
        string? value = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, null);
        return value;
    }

    private sealed record LifetimeAuthority(
        ManagedLifetimeProtocol Protocol,
        FileStream Stream,
        SafeFileHandle Job,
        string JobName,
        string StatePath,
        ManagedProcessLifetimeKind Kind);

    private const int ErrorFileNotFound = 2;
    private const int JobObjectBasicAccountingInformation = 1;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint JobObjectAssignProcess = 0x0001;
    private const uint JobObjectQuery = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicAccountingInformation
    {
        internal long TotalUserTime;
        internal long TotalKernelTime;
        internal long ThisPeriodTotalUserTime;
        internal long ThisPeriodTotalKernelTime;
        internal uint TotalPageFaultCount;
        internal uint TotalProcesses;
        internal uint ActiveProcesses;
        internal uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimitInformation
    {
        internal JobBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryUsed;
        internal UIntPtr PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetHandleInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObject(IntPtr securityAttributes, string name);

    [LibraryImport("kernel32.dll", EntryPoint = "OpenJobObjectW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenJobObject(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(
        IntPtr job,
        int informationClass,
        out JobBasicAccountingInformation information,
        int informationLength,
        out int returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        in JobExtendedLimitInformation information,
        int informationLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(IntPtr job, uint exitCode);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();
}

internal enum ManagedProcessLifetimeLeaseAcquisitionOutcome
{
    Acquired,
    Busy,
    Unavailable,
}
