// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Processes;

namespace Nvt.Core.Launcher.Transport;

/// <summary>Consumes and clears the inherited one-use anonymous ready-pipe handle.</summary>
public sealed class InheritedPipeApplicationReadySignal : IApplicationReadySignal, IDisposable
{
    private readonly CaptureContext _context;
    // Guard: Interlocked.Exchange transfers exclusive ownership to ReportReadyAsync or Dispose.
    private SafePipeHandle? _handle;

    /// <summary>Captures and clears one inherited READY context immediately at process entry.</summary>
    /// <param name="protocolNames">Exact environment names supplied by the product descriptor.</param>
    public InheritedPipeApplicationReadySignal(LauncherProtocolNames protocolNames)
    {
        ArgumentNullException.ThrowIfNull(protocolNames);
        string? handle = Environment.GetEnvironmentVariable(protocolNames.ApplicationReadyHandle);
        string? expected = Environment.GetEnvironmentVariable(protocolNames.ExpectedApplicationVersion);
        Environment.SetEnvironmentVariable(protocolNames.ApplicationReadyHandle, null);
        Environment.SetEnvironmentVariable(protocolNames.ExpectedApplicationVersion, null);
        if (handle is null && expected is null)
        {
            _context = new(ApplicationReadySignalOutcome.NotInherited, null);
            return;
        }
        if (string.IsNullOrWhiteSpace(handle) ||
            !long.TryParse(handle, NumberStyles.None, CultureInfo.InvariantCulture, out long rawHandle) ||
            rawHandle <= 0)
        {
            _context = new(ApplicationReadySignalOutcome.InvalidInheritedContext, null);
            return;
        }
#pragma warning disable CA2000 // Ownership transfers into this typed signal.
        var ownedHandle = new SafePipeHandle(new IntPtr(rawHandle), ownsHandle: true);
#pragma warning restore CA2000
        if (!ProcessLaunchGate.TryClearInheritance(ownedHandle.DangerousGetHandle()))
        {
            ownedHandle.Dispose();
            _context = new(ApplicationReadySignalOutcome.InvalidInheritedContext, null);
            return;
        }
        if (string.IsNullOrWhiteSpace(expected))
        {
            ownedHandle.Dispose();
            _context = new(ApplicationReadySignalOutcome.InvalidInheritedContext, null);
            return;
        }
        _handle = ownedHandle;
        _context = new(ApplicationReadySignalOutcome.Reported, expected);
    }

    /// <inheritdoc />
    public async ValueTask<ApplicationReadySignalOutcome> ReportReadyAsync(
        ManagedAppVersion version,
        CancellationToken cancellationToken)
    {
        if (_context.Outcome != ApplicationReadySignalOutcome.Reported)
        {
            return _context.Outcome;
        }
        SafePipeHandle? handle = Interlocked.Exchange(ref _handle, null);
        if (handle is null)
        {
            return ApplicationReadySignalOutcome.NotInherited;
        }
        if (!string.Equals(_context.Expected, version.ToString(), StringComparison.Ordinal))
        {
            handle.Dispose();
            return ApplicationReadySignalOutcome.InvalidInheritedContext;
        }
        try
        {
            await using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle);
            byte[] message = Encoding.UTF8.GetBytes($"READY:{version}\n");
            await pipe.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            return ApplicationReadySignalOutcome.Reported;
        }
        catch (ArgumentException)
        {
            return ApplicationReadySignalOutcome.InvalidInheritedContext;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ApplicationReadySignalOutcome.WriteFailed;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
    }

    private sealed record CaptureContext(ApplicationReadySignalOutcome Outcome, string? Expected);
}
