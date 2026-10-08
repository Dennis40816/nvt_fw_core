// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Pipes;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Transport;
using Nvt.Core.Tests.Processes;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Transport;

/// <summary>Tests inherited READY capture, version binding, and one-use ownership.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed partial class InheritedPipeApplicationReadySignalTests
{
    /// <summary>The application-side inherited channel is version-bound and consumed exactly once.</summary>
    [Fact]
    public async Task ApplicationReadySignalIsVersionBoundAndOneUse()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This test requires Windows anonymous handle capture.");
        }
        ManagedAppVersion version = ManagedAppVersion.Parse("1.2.3");
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
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
                version.ToString());
            using var signal = new InheritedPipeApplicationReadySignal(TransportFixture.Names);

            ApplicationReadySignalOutcome first = await signal.ReportReadyAsync(
                version,
                TestContext.Current.CancellationToken);
            using var reader = new StreamReader(pipe);
            string? message = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
            ApplicationReadySignalOutcome second = await signal.ReportReadyAsync(
                version,
                TestContext.Current.CancellationToken);

            Assert.Equal(ApplicationReadySignalOutcome.Reported, first);
            Assert.Equal("READY:1.2.3", message);
            Assert.Equal(ApplicationReadySignalOutcome.NotInherited, second);
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

    /// <summary>A mismatched expected version consumes no untrusted handle and clears both ambient values.</summary>
    [Fact]
    public async Task ApplicationReadySignalRejectsWrongExpectedVersion()
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
                "1.2.2");

            using var signal = new InheritedPipeApplicationReadySignal(TransportFixture.Names);
            ApplicationReadySignalOutcome result = await signal.ReportReadyAsync(
                ManagedAppVersion.Parse("1.2.3"),
                TestContext.Current.CancellationToken);
            pipe.DisposeLocalCopyOfClientHandle();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var reader = new StreamReader(pipe);
            string discardedReady = await reader.ReadToEndAsync(timeout.Token);

            Assert.Equal(ApplicationReadySignalOutcome.InvalidInheritedContext, result);
            Assert.Empty(discardedReady);
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

    /// <summary>An invalid inherited handle is consumed and reported without escaping an I/O exception.</summary>
    [Fact]
    public async Task ApplicationReadySignalRejectsInvalidInheritedHandle()
    {
        ManagedAppVersion version = ManagedAppVersion.Parse("1.2.3");
        string? priorHandle = Environment.GetEnvironmentVariable(
            TransportFixture.Names.ApplicationReadyHandle);
        string? priorVersion = Environment.GetEnvironmentVariable(
            TransportFixture.Names.ExpectedApplicationVersion);
        try
        {
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ApplicationReadyHandle,
                "not-a-pipe-handle");
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ExpectedApplicationVersion,
                version.ToString());

            using var signal = new InheritedPipeApplicationReadySignal(TransportFixture.Names);
            ApplicationReadySignalOutcome result = await signal.ReportReadyAsync(
                version,
                TestContext.Current.CancellationToken);

            Assert.NotEqual(ApplicationReadySignalOutcome.Reported, result);
            Assert.NotEqual(ApplicationReadySignalOutcome.NotInherited, result);
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

    /// <summary>Missing launcher inheritance is distinct from a partial untrusted context.</summary>
    [Fact]
    public async Task ApplicationReadySignalDistinguishesUnmanagedAndPartialInheritance()
    {
        string? priorHandle = Environment.GetEnvironmentVariable(
            TransportFixture.Names.ApplicationReadyHandle);
        string? priorVersion = Environment.GetEnvironmentVariable(
            TransportFixture.Names.ExpectedApplicationVersion);
        try
        {
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ApplicationReadyHandle,
                null);
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ExpectedApplicationVersion,
                null);
            using var unmanagedSignal = new InheritedPipeApplicationReadySignal(TransportFixture.Names);
            ApplicationReadySignalOutcome unmanaged = await unmanagedSignal.ReportReadyAsync(
                ManagedAppVersion.Parse("1.2.3"),
                TestContext.Current.CancellationToken);
            Environment.SetEnvironmentVariable(
                TransportFixture.Names.ExpectedApplicationVersion,
                "1.2.3");
            using var partialSignal = new InheritedPipeApplicationReadySignal(TransportFixture.Names);
            ApplicationReadySignalOutcome partial = await partialSignal.ReportReadyAsync(
                ManagedAppVersion.Parse("1.2.3"),
                TestContext.Current.CancellationToken);

            Assert.Equal(ApplicationReadySignalOutcome.NotInherited, unmanaged);
            Assert.Equal(ApplicationReadySignalOutcome.InvalidInheritedContext, partial);
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

}
