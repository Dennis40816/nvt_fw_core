// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Nvt.Core.Processes;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Transport;

internal enum BootstrapAdmissionInheritanceOutcome
{
    NotInherited,
    Inherited,
    Invalid,
}

internal enum BootstrapStartGateInheritanceOutcome
{
    NotInherited,
    Inherited,
    Invalid,
}

/// <summary>One-use parent authorization captured before Root Bootstrap may inspect managed state.</summary>
internal sealed partial class BootstrapStartGate : IDisposable
{
    internal const string ContextVersion = "v1";
    private const uint HandleFlagInherit = 1;
    // Guard: the claimed operation owns the handle; Dispose is caller-serialized, as in the frozen source.
    private SafePipeHandle? _handle;

    private BootstrapStartGate(
        BootstrapStartGateInheritanceOutcome outcome,
        SafePipeHandle? handle)
    {
        Outcome = outcome;
        _handle = handle;
    }

    internal BootstrapStartGateInheritanceOutcome Outcome { get; }

    internal static BootstrapStartGate Capture(LauncherProtocolNames protocolNames)
    {
        ArgumentNullException.ThrowIfNull(protocolNames);
        string? context = TakeEnvironment(protocolNames.BootstrapStartContext);
        string? handle = TakeEnvironment(protocolNames.BootstrapStartHandle);
        if (context is null && handle is null)
        {
            return new(BootstrapStartGateInheritanceOutcome.NotInherited, null);
        }
        if (!long.TryParse(handle, NumberStyles.None, CultureInfo.InvariantCulture, out long rawHandle) ||
            rawHandle is 0 or -1)
        {
            return new(BootstrapStartGateInheritanceOutcome.Invalid, null);
        }
#pragma warning disable CA2000 // Ownership transfers into the returned one-use gate.
        var ownedHandle = new SafePipeHandle(new IntPtr(rawHandle), ownsHandle: true);
#pragma warning restore CA2000
        if (!SetHandleInformation(ownedHandle.DangerousGetHandle(), HandleFlagInherit, flags: 0))
        {
            ownedHandle.Dispose();
            return new(BootstrapStartGateInheritanceOutcome.Invalid, null);
        }
        if (!string.Equals(context, ContextVersion, StringComparison.Ordinal))
        {
            ownedHandle.Dispose();
            return new(BootstrapStartGateInheritanceOutcome.Invalid, null);
        }
        return new(BootstrapStartGateInheritanceOutcome.Inherited, ownedHandle);
    }

    internal ValueTask<bool> WaitForStartAsync(CancellationToken cancellationToken) =>
        WaitForStartCoreAsync(exactBytes: null, cancellationToken);

    // The linked child preserves the frozen probe's exact six-byte check independently
    // of the production gate's bounded line, CR, and partial-EOF behavior.
    internal ValueTask<bool> WaitForStartAsync(ReadOnlyMemory<byte> exactBytes, CancellationToken cancellationToken) =>
        WaitForStartCoreAsync(exactBytes, cancellationToken);

    private async ValueTask<bool> WaitForStartCoreAsync(ReadOnlyMemory<byte>? exactBytes, CancellationToken cancellationToken)
    {
        if (Outcome == BootstrapStartGateInheritanceOutcome.NotInherited)
        {
            return true;
        }
        if (Outcome != BootstrapStartGateInheritanceOutcome.Inherited || _handle is null)
        {
            return false;
        }
        try
        {
            await using var pipe = new AnonymousPipeClientStream(PipeDirection.In, _handle);
            _handle = null;
            if (exactBytes is { } expected)
            {
                var bytes = new byte[expected.Length];
                await pipe.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                return bytes.AsSpan().SequenceEqual(expected.Span);
            }
            string? signal = await BoundedUtf8LineReader.ReadAsync(
                pipe,
                maximumCharacters: 8,
                bufferSize: 32,
                cancellationToken).ConfigureAwait(false);
            return string.Equals(signal, "START", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is
            ArgumentException or DecoderFallbackException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }

    private static string? TakeEnvironment(string key)
    {
        string? value = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, null);
        return value;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetHandleInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}

/// <summary>Parent-owned one-use START authority. Pending may become Authorized or Aborted once.</summary>
internal sealed class BootstrapStartAuthorization : IDisposable
{
    private readonly AnonymousPipeServerStream _pipe;
    private readonly LauncherProtocolNames _protocolNames;
    // Guard: Interlocked.CompareExchange stores the AuthorizationPhase enum as its atomic integer.
    private int _state;

    internal BootstrapStartAuthorization(LauncherProtocolNames protocolNames)
    {
        _protocolNames = protocolNames ?? throw new ArgumentNullException(nameof(protocolNames));
        _pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
    }

    internal string ClientHandle => _pipe.GetClientHandleAsString();

    internal ProcessInheritedHandle InheritedHandle => ProcessInheritedHandle.Parse(
        _protocolNames.BootstrapStartHandle,
        ClientHandle);

    internal void ApplyInheritedContext(ProcessStartInfo startInfo)
    {
        startInfo.Environment[_protocolNames.BootstrapStartContext] = BootstrapStartGate.ContextVersion;
        startInfo.Environment[_protocolNames.BootstrapStartHandle] = ClientHandle;
    }

    internal bool TryAuthorize()
    {
        if (Interlocked.CompareExchange(ref _state, (int)AuthorizationPhase.Authorized, (int)AuthorizationPhase.Pending) != 0)
        {
            return false;
        }
        try
        {
            byte[] message = "START\n"u8.ToArray();
            _pipe.Write(message);
            _pipe.Flush();
            _pipe.Dispose();
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return false;
        }
    }

    internal bool TryAbort()
    {
        if (Interlocked.CompareExchange(ref _state, (int)AuthorizationPhase.Aborted, (int)AuthorizationPhase.Pending) != 0)
        {
            return false;
        }
        _pipe.Dispose();
        return true;
    }

    internal void DisposeLocalClientHandle()
    {
        try
        {
            _pipe.DisposeLocalCopyOfClientHandle();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        _ = TryAbort();
        _pipe.Dispose();
    }
    private enum AuthorizationPhase { Pending, Authorized, Aborted }
}

internal sealed partial class BootstrapAdmissionSignal : IDisposable
{
    private const string AdmittedLine = "ADMITTED\n";
    private const uint HandleFlagInherit = 1;
    // Guard: the claimed operation owns the handle; Dispose is caller-serialized, as in the frozen source.
    private SafePipeHandle? _handle;
    // Guard: Interlocked claims reporting; Volatile publishes the terminal AdmissionPhase.
    private int _reported;

    private BootstrapAdmissionSignal(
        BootstrapAdmissionInheritanceOutcome outcome,
        SafePipeHandle? handle)
    {
        Outcome = outcome;
        _handle = handle;
    }

    internal BootstrapAdmissionInheritanceOutcome Outcome { get; }

    internal static BootstrapAdmissionSignal Capture(LauncherProtocolNames protocolNames)
    {
        ArgumentNullException.ThrowIfNull(protocolNames);
        string? handle = Environment.GetEnvironmentVariable(
            protocolNames.BootstrapAdmissionHandle);
        Environment.SetEnvironmentVariable(
            protocolNames.BootstrapAdmissionHandle,
            null);
        if (handle is null)
        {
            return new(BootstrapAdmissionInheritanceOutcome.NotInherited, null);
        }
        if (string.IsNullOrWhiteSpace(handle) ||
            !long.TryParse(handle, NumberStyles.None, CultureInfo.InvariantCulture, out long rawHandle) ||
            rawHandle is 0 or -1)
        {
            return new(BootstrapAdmissionInheritanceOutcome.Invalid, null);
        }
#pragma warning disable CA2000 // Ownership transfers into the returned one-use admission signal.
        var ownedHandle = new SafePipeHandle(new IntPtr(rawHandle), ownsHandle: true);
#pragma warning restore CA2000
        if (!SetHandleInformation(
                ownedHandle.DangerousGetHandle(),
                HandleFlagInherit,
                flags: 0))
        {
            ownedHandle.Dispose();
            return new(BootstrapAdmissionInheritanceOutcome.Invalid, null);
        }
        return new(BootstrapAdmissionInheritanceOutcome.Inherited, ownedHandle);
    }

    internal async ValueTask<bool> ReportAdmittedAsync(CancellationToken cancellationToken)
    {
        if (Outcome == BootstrapAdmissionInheritanceOutcome.NotInherited)
        {
            return true;
        }
        if (Outcome != BootstrapAdmissionInheritanceOutcome.Inherited)
        {
            return false;
        }
        int prior = Interlocked.CompareExchange(ref _reported, (int)AdmissionPhase.Reporting, (int)AdmissionPhase.Pending);
        if (prior == (int)AdmissionPhase.Reported)
        {
            return true;
        }
        if (prior != (int)AdmissionPhase.Pending)
        {
            return false;
        }
        try
        {
            await using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, _handle!);
            _handle = null;
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(AdmittedLine), cancellationToken)
                .ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _reported, (int)AdmissionPhase.Reported);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException)
        {
            _handle?.Dispose();
            _handle = null;
            Volatile.Write(ref _reported, (int)AdmissionPhase.Failed);
            return false;
        }
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }

    private enum AdmissionPhase { Reporting = -1, Pending, Reported, Failed }

    [LibraryImport("kernel32.dll", EntryPoint = "SetHandleInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}
