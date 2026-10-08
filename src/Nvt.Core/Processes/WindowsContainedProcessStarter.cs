// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Processes;

internal static partial class WindowsContainedProcessStarter
{
    private static readonly char[] QuotingCharacters = [' ', '\t', '"'];

    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint DuplicateSameAccess = 0x00000002;
    private const uint HandleFlagInherit = 0x00000001;
    private const nuint ProcThreadAttributeHandleList = 0x00020002;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitFailed = 0xFFFFFFFF;
    private const uint TerminationConfirmationMilliseconds = 5_000;

    [SupportedOSPlatform("windows")]
    internal static Process? Start(
        ProcessStartInfo startInfo,
        IReadOnlyList<ProcessInheritedHandle> inheritedHandles,
        Func<bool> validateImmediatelyBeforeCreate,
        Action? beforeFinalValidationForTesting = null,
        Action? afterCreateProcessForTesting = null)
    {
        ArgumentNullException.ThrowIfNull(validateImmediatelyBeforeCreate);
        Validate(startInfo, inheritedHandles);
        var duplicates = new List<SafeFileHandle>(inheritedHandles.Count);
        IntPtr attributeList = IntPtr.Zero;
        IntPtr handleList = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        IntPtr commandLine = IntPtr.Zero;
        var processInformation = new ProcessInformation();
        Process? process = null;
        bool resumed = false;
        try
        {
            var inheritedEnvironment = new Dictionary<string, string?>(
                startInfo.Environment,
                StringComparer.OrdinalIgnoreCase);
            foreach (ProcessInheritedHandle binding in inheritedHandles)
            {
                if (!DuplicateHandle(
                        GetCurrentProcess(),
                        binding.Handle,
                        GetCurrentProcess(),
                        out SafeFileHandle duplicate,
                        desiredAccess: 0,
                        inheritHandle: true,
                        DuplicateSameAccess))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
                duplicates.Add(duplicate);
                inheritedEnvironment[binding.EnvironmentVariable!] = duplicate.DangerousGetHandle()
                    .ToInt64()
                    .ToString(CultureInfo.InvariantCulture);
            }

            bool inheritHandles = duplicates.Count != 0;
            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = checked((uint)(inheritHandles
                        ? Marshal.SizeOf<StartupInfoEx>()
                        : Marshal.SizeOf<StartupInfo>())),
                },
            };
            uint creationFlags = CreateSuspended | CreateUnicodeEnvironment;
            if (startInfo.CreateNoWindow)
            {
                creationFlags |= CreateNoWindow;
            }
            if (inheritHandles)
            {
                nuint attributeListSize = 0;
                _ = InitializeProcThreadAttributeList(
                    IntPtr.Zero,
                    attributeCount: 1,
                    flags: 0,
                    ref attributeListSize);
                int sizingError = Marshal.GetLastPInvokeError();
                if (attributeListSize == 0 || sizingError != 122)
                {
                    throw new Win32Exception(sizingError);
                }
                attributeList = Marshal.AllocHGlobal(checked((nint)attributeListSize));
                if (!InitializeProcThreadAttributeList(
                        attributeList,
                        attributeCount: 1,
                        flags: 0,
                        ref attributeListSize))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
                handleList = Marshal.AllocHGlobal(checked(IntPtr.Size * duplicates.Count));
                for (int index = 0; index < duplicates.Count; index++)
                {
                    Marshal.WriteIntPtr(
                        handleList,
                        checked(index * IntPtr.Size),
                        duplicates[index].DangerousGetHandle());
                }
                if (!UpdateProcThreadAttribute(
                        attributeList,
                        flags: 0,
                        ProcThreadAttributeHandleList,
                        handleList,
                        checked((nuint)(IntPtr.Size * duplicates.Count)),
                        IntPtr.Zero,
                        IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
                startupInfo.AttributeList = attributeList;
                creationFlags |= ExtendedStartupInfoPresent;
            }

            environment = Marshal.StringToHGlobalUni(CreateEnvironmentBlock(inheritedEnvironment));
            commandLine = Marshal.StringToHGlobalUni(CreateCommandLine(startInfo));
            beforeFinalValidationForTesting?.Invoke();
            if (!validateImmediatelyBeforeCreate())
            {
                return null;
            }
            if (!CreateProcess(
                    startInfo.FileName,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles,
                    creationFlags,
                    environment,
                    string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                        ? null
                        : startInfo.WorkingDirectory,
                    ref startupInfo,
                    out processInformation))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            afterCreateProcessForTesting?.Invoke();
            process = Process.GetProcessById(checked((int)processInformation.ProcessId));
            _ = process.Handle;
            if (ResumeThread(processInformation.Thread) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            resumed = true;
            return process;
        }
        catch (Exception startFailure)
        {
            process?.Dispose();
            if (processInformation.Process is not 0 and not -1 && !resumed)
            {
                TerminateSuspendedProcess(processInformation.Process, startFailure);
            }
            throw;
        }
        finally
        {
            if (processInformation.Thread is not 0 and not -1)
            {
                _ = CloseHandle(processInformation.Thread);
            }
            if (processInformation.Process is not 0 and not -1)
            {
                _ = CloseHandle(processInformation.Process);
            }
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
            }
            if (handleList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handleList);
            }
            if (environment != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environment);
            }
            if (commandLine != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(commandLine);
            }
            foreach (SafeFileHandle duplicate in duplicates)
            {
                duplicate.Dispose();
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void TerminateSuspendedProcess(IntPtr process, Exception startFailure)
    {
        if (!TerminateProcess(process, exitCode: 1))
        {
            throw new InvalidOperationException(
                "Contained process creation failed and the suspended child could not be terminated.",
                new AggregateException(startFailure, new Win32Exception(Marshal.GetLastPInvokeError())));
        }

        uint waitResult = WaitForSingleObject(process, TerminationConfirmationMilliseconds);
        if (waitResult == WaitObject0)
        {
            return;
        }

        Exception confirmationFailure = waitResult == WaitFailed
            ? new Win32Exception(Marshal.GetLastPInvokeError())
            : new TimeoutException(
                "The suspended child did not terminate within the bounded confirmation deadline.");
        throw new InvalidOperationException(
            "Contained process creation failed and child termination could not be confirmed.",
            new AggregateException(startFailure, confirmationFailure));
    }

    [SupportedOSPlatform("windows")]
    internal static bool TryClearInheritance(IntPtr handle)
    {
        return handle is not 0 and not -1 &&
            SetHandleInformation(handle, HandleFlagInherit, flags: 0);
    }

    [SupportedOSPlatform("windows")]
    private static void Validate(
        ProcessStartInfo startInfo,
        IReadOnlyList<ProcessInheritedHandle> inheritedHandles)
    {
        if (startInfo.UseShellExecute || startInfo.RedirectStandardInput ||
            startInfo.RedirectStandardOutput || startInfo.RedirectStandardError ||
            !Path.IsPathFullyQualified(startInfo.FileName))
        {
            throw new InvalidOperationException(
                "Contained process starts require an absolute executable, shell disabled, and no redirected streams.");
        }
        if (!string.IsNullOrEmpty(startInfo.UserName) || startInfo.Password is not null)
        {
            throw new InvalidOperationException("Contained process starts do not support alternate credentials.");
        }
        if (startInfo.ArgumentList.Count != 0 && !string.IsNullOrEmpty(startInfo.Arguments))
        {
            throw new InvalidOperationException("Contained process starts cannot mix Arguments and ArgumentList.");
        }
        if (inheritedHandles.Select(static value => value.EnvironmentVariable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != inheritedHandles.Count)
        {
            throw new ArgumentException("Inherited handle environment names must be unique.", nameof(inheritedHandles));
        }
    }

    private static string CreateEnvironmentBlock(IReadOnlyDictionary<string, string?> environment)
    {
        var result = new StringBuilder();
        foreach ((string key, string? value) in environment.OrderBy(
                     static pair => pair.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (value is null)
            {
                continue;
            }
            _ = result.Append(key).Append('=').Append(value).Append('\0');
        }
        return result.Append('\0').ToString();
    }

    private static string CreateCommandLine(ProcessStartInfo startInfo)
    {
        var result = new StringBuilder();
        AppendQuotedArgument(result, startInfo.FileName);
        if (startInfo.ArgumentList.Count != 0)
        {
            foreach (string argument in startInfo.ArgumentList)
            {
                _ = result.Append(' ');
                AppendQuotedArgument(result, argument);
            }
        }
        else if (!string.IsNullOrEmpty(startInfo.Arguments))
        {
            _ = result.Append(' ').Append(startInfo.Arguments);
        }
        return result.ToString();
    }

    private static void AppendQuotedArgument(StringBuilder result, string argument)
    {
        if (argument.Length != 0 && argument.IndexOfAny(QuotingCharacters) < 0)
        {
            _ = result.Append(argument);
            return;
        }
        _ = result.Append('"');
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                _ = result.Append('\\', checked((backslashes * 2) + 1)).Append('"');
                backslashes = 0;
                continue;
            }
            _ = result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        _ = result.Append('\\', checked(backslashes * 2)).Append('"');
    }

#pragma warning disable IDE0044 // Native mutable-layout fields are populated by Windows.
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
        private uint flags;
        private ushort showWindow;
        private ushort reserved2;
        private IntPtr reservedBytes;
        private IntPtr standardInput;
        private IntPtr standardOutput;
        private IntPtr standardError;
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

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", EntryPoint = "DuplicateHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateHandle(
        IntPtr sourceProcess,
        IntPtr sourceHandle,
        IntPtr targetProcess,
        out SafeFileHandle targetHandle,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint options);

    [LibraryImport("kernel32.dll", EntryPoint = "InitializeProcThreadAttributeList", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        uint attributeCount,
        uint flags,
        ref nuint size);

    [LibraryImport("kernel32.dll", EntryPoint = "UpdateProcThreadAttribute", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        nuint attribute,
        IntPtr value,
        nuint size,
        IntPtr previousValue,
        IntPtr returnSize);

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteProcThreadAttributeList")]
    private static partial void DeleteProcThreadAttributeList(IntPtr attributeList);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(
        string applicationName,
        IntPtr commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll", EntryPoint = "ResumeThread", SetLastError = true)]
    private static partial uint ResumeThread(IntPtr thread);

    [LibraryImport("kernel32.dll", EntryPoint = "TerminateProcess", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(IntPtr process, uint exitCode);

    [LibraryImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
    private static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", EntryPoint = "SetHandleInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}

