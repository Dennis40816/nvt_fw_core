// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Verifies native inheritance clearing and invalid-context handle closure.</summary>
public sealed partial class InheritedPipeApplicationReadySignalTests
{
    /// <summary>A real READY handle is closed when its expected version is missing or blank.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ApplicationReadyCaptureClosesHandleWhenExpectedVersionIsInvalid(
        string? expectedVersion)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test requires Windows anonymous handle capture.");
        }
        using var pipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        string inheritedDuplicate = DuplicateApplicationReadyClientHandle(pipe);
        string? priorHandle = Environment.GetEnvironmentVariable(
            TransportFixture.Names.ApplicationReadyHandle);
        string? priorVersion = Environment.GetEnvironmentVariable(
            TransportFixture.Names.ExpectedApplicationVersion);
        try
        {
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ApplicationReadyHandle,
                inheritedDuplicate);
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ExpectedApplicationVersion,
                expectedVersion);

            using var signal = new InheritedPipeApplicationReadySignal(TransportFixture.Names);
            ApplicationReadySignalOutcome outcome = await signal.ReportReadyAsync(
                ManagedAppVersion.Parse("1.2.3"),
                TestContext.Current.CancellationToken);
            pipe.DisposeLocalCopyOfClientHandle();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var reader = new StreamReader(pipe);

            Assert.Equal(ApplicationReadySignalOutcome.InvalidInheritedContext, outcome);
            Assert.Empty(await reader.ReadToEndAsync(timeout.Token));
            Assert.Null(Environment.GetEnvironmentVariable(
                TransportFixture.Names.ApplicationReadyHandle));
            Assert.Null(Environment.GetEnvironmentVariable(
                TransportFixture.Names.ExpectedApplicationVersion));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ApplicationReadyHandle,
                priorHandle);
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ExpectedApplicationVersion,
                priorVersion);
        }
    }

    /// <summary>Desktop capture clears inheritance before any later child can start.</summary>
    [Fact]
    public async Task ApplicationReadyHandleIsNonInheritableImmediatelyAfterCapture()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test requires Windows anonymous handle capture.");
        }
        using var pipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            HandleInheritability.Inheritable);
        string handle = DuplicateApplicationReadyClientHandle(pipe);
        string? priorHandle = Environment.GetEnvironmentVariable(
            TransportFixture.Names.ApplicationReadyHandle);
        string? priorVersion = Environment.GetEnvironmentVariable(
            TransportFixture.Names.ExpectedApplicationVersion);
        try
        {
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ApplicationReadyHandle,
                handle);
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ExpectedApplicationVersion,
                "1.2.3");

            using var signal = new InheritedPipeApplicationReadySignal(TransportFixture.Names);
            Assert.True(long.TryParse(
                handle,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long rawHandle));
            if (!GetApplicationReadyHandleInformation(new IntPtr(rawHandle), out uint flags))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            Assert.Equal(0u, flags & 1u);
            Assert.Equal(
                ApplicationReadySignalOutcome.Reported,
                await signal.ReportReadyAsync(
                    ManagedAppVersion.Parse("1.2.3"),
                    TestContext.Current.CancellationToken));
            using var reader = new StreamReader(pipe);
            Assert.Equal(
                "READY:1.2.3",
                await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ApplicationReadyHandle,
                priorHandle);
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ExpectedApplicationVersion,
                priorVersion);
        }
    }

    private static string DuplicateApplicationReadyClientHandle(AnonymousPipeServerStream pipe)
    {
        IntPtr process = GetApplicationReadyCurrentProcess();
        IntPtr source = new(long.Parse(
            pipe.GetClientHandleAsString(),
            NumberStyles.None,
            CultureInfo.InvariantCulture));
        if (!DuplicateApplicationReadyHandle(
                process,
                source,
                process,
                out SafeFileHandle duplicate,
                desiredAccess: 0,
                inheritHandle: true,
                options: 2))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
#pragma warning disable CA2000 // Ownership transfers to the ready-signal capture through the raw handle.
        string value = duplicate.DangerousGetHandle().ToInt64().ToString(CultureInfo.InvariantCulture);
        duplicate.SetHandleAsInvalid();
        duplicate.Dispose();
#pragma warning restore CA2000
        return value;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetHandleInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetApplicationReadyHandleInformation(IntPtr handle, out uint flags);

    [DllImport("kernel32.dll", EntryPoint = "DuplicateHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateApplicationReadyHandle(
        IntPtr sourceProcess,
        IntPtr sourceHandle,
        IntPtr targetProcess,
        out SafeFileHandle targetHandle,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint options);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    private static extern IntPtr GetApplicationReadyCurrentProcess();
}
