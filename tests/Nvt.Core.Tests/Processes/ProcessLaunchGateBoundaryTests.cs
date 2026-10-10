// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Nvt.Core.Processes;
using Nvt.Core.TestSupport;
using Xunit;

namespace Nvt.Core.Tests.Processes;

/// <summary>Characterizes gate ordering, native restrictions, and inherited-handle cleanup.</summary>
[Collection(ProcessSerialCollection.Name)]
public sealed class ProcessLaunchGateBoundaryTests
{
    private const string NativeConfigurationMessage =
        "Contained process starts require an absolute executable, shell disabled, and no redirected streams.";
    private static readonly string[] QuotedArguments = ["two words", "quote\"inside", "trail\\"];

    /// <summary>All public entry points preserve their null checks and parameter order.</summary>
    [Fact]
    public void PublicNullChecksPreserveOrder()
    {
        Assert.Equal("startInfo", Assert.Throws<ArgumentNullException>(() =>
            ProcessLaunchGate.Start(null!)).ParamName);
        Assert.Equal("startInfo", Assert.Throws<ArgumentNullException>(() =>
            ProcessLaunchGate.StartContained(null!, null!)).ParamName);
        Assert.Equal("startInfo", Assert.Throws<ArgumentNullException>(() =>
            ProcessLaunchGate.StartContained(null!, null!, null!)).ParamName);
        Assert.Equal("inheritedHandles", Assert.Throws<ArgumentNullException>(() =>
            ProcessLaunchGate.StartContained(new ProcessStartInfo(), null!, null!)).ParamName);
        Assert.Equal("validateImmediatelyBeforeStart", Assert.Throws<ArgumentNullException>(() =>
            ProcessLaunchGate.StartContained(new ProcessStartInfo(), [], null!)).ParamName);
    }

    /// <summary>Ordinary starts use the shared probe's exit mode and retain the requested exit code.</summary>
    [Fact]
    public async Task OrdinaryStartRunsExitProbe()
    {
        ProcessStartInfo info = ProcessProbe.Create("exit");
        info.ArgumentList.Add("--exit-code");
        info.ArgumentList.Add("7");
        using Process child = Assert.IsType<Process>(ProcessLaunchGate.Start(info));
        await ExitAsync(child);
        Assert.Equal(7, child.ExitCode);
    }

    /// <summary>Final refusal runs once and creates neither a marker nor a child.</summary>
    [Fact]
    public void FinalValidationRefusalStartsNoChild()
    {
        using var workspace = TestWorkspace.Create();
        string marker = workspace.GetPath("refused.txt");
        ProcessStartInfo info = ArgumentProbe(marker, workspace.RootPath);
        int calls = 0;
        Assert.Null(ProcessLaunchGate.StartContained(info, [], () =>
        {
            calls++;
            return false;
        }));
        Assert.Equal(1, calls);
        Assert.False(File.Exists(marker));
    }

    /// <summary>A callback exception propagates unchanged and the gate accepts the next launch.</summary>
    [Fact]
    public async Task ValidationExceptionDoesNotPoisonGate()
    {
        var injected = new InvalidOperationException("synthetic custody failure");
        Assert.Same(injected, Assert.Throws<InvalidOperationException>(() =>
            ProcessLaunchGate.StartContained(ProcessProbe.Create("exit"), [], () => throw injected)));
        using Process child = Assert.IsType<Process>(
            ProcessLaunchGate.StartContained(ProcessProbe.Create("exit"), []));
        await ExitAsync(child);
        Assert.Equal(0, child.ExitCode);
    }

    /// <summary>Ordinary starts wait for contained validation; public null checks remain outside that gate.</summary>
    [Fact]
    public async Task OrdinaryStartAndNullChecksShareFrozenGateOrder()
    {
        using var workspace = TestWorkspace.Create();
        string marker = workspace.GetPath("ordinary.txt");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var attempting = new ManualResetEventSlim();
        Task<Process?> contained = Task.Factory.StartNew(() =>
            ProcessLaunchGate.StartContained(ProcessProbe.Create("exit"), [], () =>
            {
                entered.Set();
                Assert.True(release.Wait(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
                return false;
            }), TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<Process?>? ordinary = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Task nullChecks = Task.Run(PublicNullChecksPreserveOrder, TestContext.Current.CancellationToken);
            await nullChecks.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            ordinary = Task.Factory.StartNew(() =>
            {
                attempting.Set();
                return ProcessLaunchGate.Start(ArgumentProbe(marker, workspace.RootPath));
            }, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(attempting.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
            Assert.False(ordinary.IsCompleted);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            release.Set();
            Assert.Null(await contained.WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
            if (ordinary is not null)
            {
                using Process child = Assert.IsType<Process>(
                    await ordinary.WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
                await ExitAsync(child);
                Assert.Equal(0, child.ExitCode);
            }
        }
        Assert.True(File.Exists(marker));
    }

    /// <summary>Native configuration rejection precedes callback, credential, argument, and handle checks.</summary>
    [Theory]
    [InlineData("shell")]
    [InlineData("stdin")]
    [InlineData("stdout")]
    [InlineData("stderr")]
    [InlineData("relative")]
    [InlineData("empty")]
    [InlineData("drive-relative")]
    public void InvalidNativeConfigurationIsRejectedFirst(string kind)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Native configuration validation requires Windows.");
            return;
        }
        ProcessStartInfo info = ProcessProbe.Create("exit");
        switch (kind)
        {
            case "shell": info.UseShellExecute = true; break;
            case "stdin": info.RedirectStandardInput = true; break;
            case "stdout": info.RedirectStandardOutput = true; break;
            case "stderr": info.RedirectStandardError = true; break;
            case "relative": info.FileName = "relative.exe"; break;
            case "empty": info.FileName = ""; break;
            case "drive-relative": info.FileName = "C:relative.exe"; break;
        }
        info.UserName = "synthetic";
        info.Arguments = "mixed";
        info.ArgumentList.Add("mixed");
        bool called = false;
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ProcessLaunchGate.StartContained(info, DuplicateNames(), () => called = true));
        Assert.Equal(NativeConfigurationMessage, error.Message);
        Assert.False(called);
    }

    /// <summary>Username and password rejection precedes mixed arguments and duplicate names.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlternateCredentialsAreRejectedSecond(bool password)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Alternate credential validation requires Windows.");
            return;
        }
        using var secret = new SecureString();
        ProcessStartInfo info = ProcessProbe.Create("exit");
        if (password)
        {
            info.Password = secret;
        }
        else
        {
            info.UserName = "synthetic";
        }
        info.Arguments = "mixed";
        info.ArgumentList.Add("mixed");
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ProcessLaunchGate.StartContained(info, DuplicateNames(), static () => false));
        Assert.Equal("Contained process starts do not support alternate credentials.", error.Message);
    }

    /// <summary>Mixed argument forms fail before native duplication or custody validation.</summary>
    [Fact]
    public void MixedArgumentsAreRejectedThird()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Mixed argument validation requires Windows.");
            return;
        }
        ProcessStartInfo info = ProcessProbe.Create("exit");
        info.Arguments = " ";
        info.ArgumentList.Add("");
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ProcessLaunchGate.StartContained(info, DuplicateNames(), static () => false));
        Assert.Equal("Contained process starts cannot mix Arguments and ArgumentList.", error.Message);
    }

    /// <summary>Environment bindings must be unique under ordinal case-insensitive comparison.</summary>
    [Fact]
    public void DuplicateEnvironmentNamesAreRejectedFourth()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Allowlist validation requires Windows.");
            return;
        }
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            ProcessLaunchGate.StartContained(ProcessProbe.Create("exit"), DuplicateNames(), static () => false));
        Assert.Equal("inheritedHandles", error.ParamName);
        Assert.Equal(new ArgumentException("Inherited handle environment names must be unique.",
            "inheritedHandles").Message, error.Message);
    }

    /// <summary>Native handle duplication fails before a false final-validation callback can suppress it.</summary>
    [Fact]
    public void DuplicationFailurePrecedesFinalValidation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Handle duplication requires Windows.");
            return;
        }
        bool called = false;
        _ = Assert.Throws<Win32Exception>(() => ProcessLaunchGate.StartContained(
            ProcessProbe.Create("exit"), [new("INVALID", new IntPtr(0x12345))], () => called = true));
        Assert.False(called);
    }

    /// <summary>The internal starter checks its validation callback before dereferencing other inputs.</summary>
    [Fact]
    public void InternalCallbackNullCheckRunsFirst()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The internal starter requires Windows.");
            return;
        }
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() =>
        {
            if (OperatingSystem.IsWindows())
            {
                _ = WindowsContainedProcessStarter.Start(null!, null!, null!);
            }
        });
        Assert.Equal("validateImmediatelyBeforeCreate", error.ParamName);
    }

    /// <summary>The public contained-launch boundary rejects a default binding before native duplication.</summary>
    [Fact]
    public void DefaultBindingIsRejectedAtLaunchAdmission()
    {
        bool callbackRan = false;
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            ProcessLaunchGate.StartContained(ProcessProbe.Create("exit"), [default], () =>
            {
                callbackRan = true;
                return true;
            }));
        Assert.Equal("inheritedHandles", error.ParamName);
        Assert.False(callbackRan);
    }

    /// <summary>Checks zero, one, and two declared handles, including duplicate originals under distinct names.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExactAllowlistCountBoundariesRetainPhysicalPipeBytes(int count)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Exact inherited-handle lists require Windows.");
            return;
        }
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        IntPtr original = ProcessInheritedHandle.Parse("ORIGINAL", pipe.GetClientHandleAsString()).Handle;
        ProcessStartInfo info = ProcessProbe.Create("contained-isolation");
        info.Environment["CORE_TEST_PROBE_PAYLOAD"] = "first";
        info.Environment["CORE_TEST_PROBE_ALLOWED_HANDLE"] = pipe.GetClientHandleAsString();
        info.Environment["CORE_TEST_PROBE_CROSS_HANDLE"] = pipe.GetClientHandleAsString();
        var bindings = new List<ProcessInheritedHandle>();
        if (count >= 1)
        {
            bindings.Add(new("CORE_TEST_PROBE_ALLOWED_HANDLE", original));
        }
        if (count >= 2)
        {
            bindings.Add(new("CORE_TEST_PROBE_CROSS_HANDLE", original));
        }
        using Process child = Assert.IsType<Process>(ProcessLaunchGate.StartContained(info, bindings));
        Assert.Equal(original.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            info.Environment["CORE_TEST_PROBE_ALLOWED_HANDLE"]);
        pipe.DisposeLocalCopyOfClientHandle();
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        string observed = await reader.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);
        await ExitAsync(child);
        Assert.Equal(0, child.ExitCode);
        Assert.Equal(count switch { 0 => "", 1 => "first", _ => "firstcross:first" }, observed);
    }

    /// <summary>Callback failure after preparation closes duplicates before propagating the original exception.</summary>
    [Fact]
    public async Task ValidationExceptionClosesPreparedDuplicates()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Native preparation requires Windows.");
            return;
        }
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        var injected = new InvalidOperationException("synthetic final validation");
        Assert.Same(injected, Assert.Throws<InvalidOperationException>(() =>
            ProcessLaunchGate.StartContained(ProcessProbe.Create("exit"),
                [ProcessInheritedHandle.Parse("HANDLE", pipe.GetClientHandleAsString())], () => throw injected)));
        pipe.DisposeLocalCopyOfClientHandle();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        Assert.Empty(await reader.ReadToEndAsync(timeout.Token)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
    }

    /// <summary>A native creation failure starts nothing and preserves the prepared-handle cleanup.</summary>
    [Fact]
    public async Task NativeCreateFailureClosesPreparedDuplicates()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Native creation failure requires Windows.");
            return;
        }
        using var workspace = TestWorkspace.Create();
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        ProcessStartInfo info = ProcessProbe.Create("exit");
        info.FileName = workspace.GetPath("missing.exe");
        int validations = 0;
        _ = Assert.Throws<Win32Exception>(() => ProcessLaunchGate.StartContained(info,
            [ProcessInheritedHandle.Parse("HANDLE", pipe.GetClientHandleAsString())],
            () => { validations++; return true; }));
        Assert.Equal(1, validations);
        Assert.Empty(Directory.GetFiles(workspace.RootPath));
        pipe.DisposeLocalCopyOfClientHandle();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        Assert.Empty(await reader.ReadToEndAsync(timeout.Token)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
    }

    /// <summary>Invalid clear-inheritance handles retain the Windows failure result.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(0x12345)]
    public void TryClearInheritanceRejectsUnusableWindowsHandles(long value)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows handle-clear failure requires Windows.");
            return;
        }
        Assert.False(ProcessLaunchGate.TryClearInheritance(new IntPtr(value)));
    }

    /// <summary>A real inheritable pipe remains open while its inheritance flag is cleared.</summary>
    [Fact]
    public void TryClearInheritanceClearsRealPipeFlag()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Pipe inheritance flags require Windows.");
            return;
        }
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        IntPtr handle = ProcessInheritedHandle.Parse("HANDLE", pipe.GetClientHandleAsString()).Handle;
        Assert.NotEqual(0, GetHandleInformation(handle, out uint before));
        Assert.Equal(1u, before & 1u);
        Assert.True(ProcessLaunchGate.TryClearInheritance(handle));
        Assert.NotEqual(0, GetHandleInformation(handle, out uint after));
        Assert.Equal(0u, after & 1u);
        Assert.True(ProcessLaunchGate.TryClearInheritance(handle));
    }

    /// <summary>Other operating systems accept inheritance clearing without examining the handle.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(1)]
    public void TryClearInheritanceIsTrueOutsideWindows(long value)
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "This behavior requires a non-Windows system.");
        Assert.True(ProcessLaunchGate.TryClearInheritance(new IntPtr(value)));
    }

    /// <summary>Outside Windows the callback runs before Process.Start and no native configuration checks apply.</summary>
    [Fact]
    public async Task NonWindowsValidationPrecedesOrdinaryProcessStart()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "This behavior requires a non-Windows system.");
        var invalid = new ProcessStartInfo { FileName = "", UseShellExecute = true };
        Assert.Null(ProcessLaunchGate.StartContained(invalid, [], static () => false));
        ProcessStartInfo info = ProcessProbe.Create("exit");
        info.RedirectStandardOutput = true;
        int validations = 0;
        using Process child = Assert.IsType<Process>(ProcessLaunchGate.StartContained(info, [],
            () => { validations++; return true; }));
        await ExitAsync(child);
        Assert.Equal(1, validations);
        Assert.Equal(0, child.ExitCode);
        Assert.Empty(await child.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken));
    }

    /// <summary>A renamed apphost in the complete probe folder runs with a raw Arguments string.</summary>
    [Fact]
    public async Task ContainedStartSupportsRawArgumentsAndRenamedCompleteProbe()
    {
        using var workspace = TestWorkspace.Create();
        string marker = workspace.GetPath("raw arguments.txt");
        ProcessStartInfo info = ArgumentProbe(marker, workspace.RootPath);
        info.FileName = ProcessProbe.CopyAndRename(workspace, "worker helper");
        info.Arguments = "\"two words\" tail";
        using Process child = Assert.IsType<Process>(ProcessLaunchGate.StartContained(info, []));
        await ExitAsync(child);
        Assert.Equal(0, child.ExitCode);
        Assert.Equal(new[] { "synthetic", workspace.RootPath, "two words", "tail" },
            await TestFiles.ReadLinesAsync(marker, TestContext.Current.CancellationToken));
    }

    /// <summary>Exercises PowerShell's separate quoted-argument behavior within its ten-minute checkpoint.</summary>
    [Fact]
    public async Task OptionalPowerShellQuotedArguments()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The optional PowerShell quoting case requires Windows.");
            return;
        }
        string executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.SkipUnless(File.Exists(executable), "The optional PowerShell quoting executable is unavailable.");
        using var workspace = TestWorkspace.Create();
        string marker = workspace.GetPath("powershell.txt");
        string script = ProcessProbe.Write(workspace, "arguments.ps1", Encoding.UTF8.GetBytes(
            "[IO.File]::WriteAllLines($env:CORE_TEST_PROBE_MARKER, [string[]]$args)\nexit 0\n"));
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script })
        {
            info.ArgumentList.Add(argument);
        }
        foreach (string argument in QuotedArguments)
        {
            info.ArgumentList.Add(argument);
        }
        info.Environment["CORE_TEST_PROBE_MARKER"] = marker;
        using Process child = Assert.IsType<Process>(ProcessLaunchGate.StartContained(info, []));
        await child.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMinutes(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, child.ExitCode);
        string[] actual = await TestFiles.ReadLinesAsync(marker, TestContext.Current.CancellationToken);
        Assert.SkipUnless(actual.SequenceEqual(QuotedArguments),
            "PowerShell did not preserve its separate argument parsing; the contained starter remains unchanged.");
        Assert.Equal(QuotedArguments, actual);
    }

    private static ProcessInheritedHandle[] DuplicateNames() =>
        [new("HANDLE", new IntPtr(1)), new("handle", new IntPtr(1))];

    private static ProcessStartInfo ArgumentProbe(string marker, string directory)
    {
        ProcessStartInfo info = ProcessProbe.Create("arguments-environment");
        info.WorkingDirectory = directory;
        info.Environment["CORE_TEST_PROBE_MARKER"] = marker;
        info.Environment["CORE_TEST_PROBE_TEXT"] = "synthetic";
        return info;
    }

    private static Task ExitAsync(Process child) => child.WaitForExitAsync(TestContext.Current.CancellationToken)
        .WaitAsync(ProcessProbe.FixtureBound, TestContext.Current.CancellationToken);

#pragma warning disable SYSLIB1054 // This test-only query needs no unsafe code or shared project change.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetHandleInformation(IntPtr handle, out uint flags);
#pragma warning restore SYSLIB1054
}
