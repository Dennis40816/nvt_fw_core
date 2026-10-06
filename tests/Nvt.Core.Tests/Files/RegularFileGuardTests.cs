// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using Nvt.Core.Files;
using Xunit;

namespace Nvt.Core.Tests.Files;

/// <summary>Tests regular filesystem paths, open handle admission, and Unix identities.</summary>
public sealed class RegularFileGuardTests
{
    /// <summary>A regular path and its valid open handle are accepted by the native guard.</summary>
    [Fact]
    public void RequirePathAndOpenHandleAcceptRegularFile()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.Write("input.bin", [1]);
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        RegularFileGuard.RequirePath(path);
        RegularFileGuard.RequireOpenHandle(handle, path);
    }

    /// <summary>A directory is not a regular file on either native path implementation.</summary>
    [Fact]
    public void RequirePathRejectsDirectory()
    {
        using var workspace = new TestWorkspace();
        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            RegularFileGuard.RequirePath(workspace.Root));
        Assert.Equal($"File '{workspace.Root}' must be a regular filesystem file.", exception.Message);
    }

    /// <summary>Windows missing paths preserve the File.GetAttributes exception.</summary>
    [Fact]
    public void RequirePathRejectsMissingWindowsPath()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows missing-path behavior requires Windows execution.");
        using var workspace = new TestWorkspace();
        _ = Assert.Throws<FileNotFoundException>(() => RegularFileGuard.RequirePath(workspace.PathFor("missing.bin")));
    }

    /// <summary>Unix missing paths report the original native inspection failure.</summary>
    [Fact]
    public void RequirePathRejectsMissingUnixPath()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix lstat requires a non-Windows host.");
        using var workspace = new TestWorkspace();
        string path = workspace.PathFor("missing.bin");
        IOException exception = Assert.Throws<IOException>(() => RegularFileGuard.RequirePath(path));
        Assert.Equal($"Could not inspect file '{path}' (native error 2).", exception.Message);
    }

    /// <summary>Null, empty, and whitespace paths retain their argument validation.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RequirePathRejectsBlankPath(string? path)
    {
        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(() => RegularFileGuard.RequirePath(path!));
        Assert.Equal("path", exception.ParamName);
    }

    /// <summary>Null handles fail before display-path validation.</summary>
    [Fact]
    public void RequireOpenHandleRejectsNullHandleFirst()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            RegularFileGuard.RequireOpenHandle(null!, null!));
        Assert.Equal("handle", exception.ParamName);
    }

    /// <summary>Display-path validation precedes invalid-handle validation.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RequireOpenHandleRejectsBlankDisplayPathBeforeInvalidHandle(string? displayPath)
    {
        using var handle = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(() =>
            RegularFileGuard.RequireOpenHandle(handle, displayPath!));
        Assert.Equal("displayPath", exception.ParamName);
    }

    /// <summary>Zero and minus-one handles are rejected before native inspection.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RequireOpenHandleRejectsInvalidHandle(int handleValue)
    {
        using var handle = new SafeFileHandle(new IntPtr(handleValue), ownsHandle: false);
        IOException exception = Assert.Throws<IOException>(() => RegularFileGuard.RequireOpenHandle(handle, "input.bin"));
        Assert.Equal("File 'input.bin' has no valid open handle.", exception.Message);
    }

    /// <summary>A previously valid closed handle is rejected before native inspection.</summary>
    [Fact]
    public void RequireOpenHandleRejectsClosedHandle()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.Write("input.bin", [1]);
        using SafeFileHandle handle = File.OpenHandle(path);
        handle.Dispose();

        IOException exception = Assert.Throws<IOException>(() => RegularFileGuard.RequireOpenHandle(handle, path));
        Assert.Equal($"File '{path}' has no valid open handle.", exception.Message);
    }

    /// <summary>A real Windows pipe handle fails GetFileType's disk-file requirement.</summary>
    [Fact]
    public void RequireOpenHandleRejectsWindowsPipe()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "GetFileType evidence requires a real Windows handle.");
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In);
        using var handle = new SafeFileHandle(pipe.SafePipeHandle.DangerousGetHandle(), ownsHandle: false);

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            RegularFileGuard.RequireOpenHandle(handle, "pipe"));
        Assert.Equal("File 'pipe' must be a regular filesystem file.", exception.Message);
    }

    /// <summary>A real Unix pipe handle fails fstat's regular-file requirement.</summary>
    [Fact]
    public void RequireOpenHandleRejectsUnixPipe()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix fstat requires a non-Windows host.");
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In);
        using var handle = new SafeFileHandle(pipe.SafePipeHandle.DangerousGetHandle(), ownsHandle: false);

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            RegularFileGuard.RequireOpenHandle(handle, "pipe"));
        Assert.Equal("File 'pipe' must be a regular filesystem file.", exception.Message);
    }

    /// <summary>Path admission rejects links through Windows attributes or Unix lstat.</summary>
    [Fact]
    public void RequirePathRejectsSymbolicLink()
    {
        using var workspace = new TestWorkspace();
        string file = workspace.Write("input.bin", [1]);
        string link = workspace.PathFor("link.bin");
        CreateFileLinkOrSkip(link, file);

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() => RegularFileGuard.RequirePath(link));
        Assert.Equal($"File '{link}' must be a regular filesystem file.", exception.Message);
    }

    /// <summary>Repeated Unix identity reads return the same device and inode for one file.</summary>
    [Fact]
    public void ReadUnixIdentityReturnsEqualIdentity()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix stat requires a non-Windows host.");
        using var workspace = new TestWorkspace();
        string path = workspace.Write("input.bin", [1]);

        (long Device, long Inode)? first = RegularFileGuard.ReadUnixIdentity(path);
        (long Device, long Inode)? second = RegularFileGuard.ReadUnixIdentity(path);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    /// <summary>Unix stat follows a symbolic link and returns the target identity.</summary>
    [Fact]
    public void ReadUnixIdentityFollowsFileLink()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix stat requires a non-Windows host.");
        using var workspace = new TestWorkspace();
        string file = workspace.Write("input.bin", [1]);
        string link = workspace.PathFor("link.bin");
        CreateFileLinkOrSkip(link, file);

        (long Device, long Inode)? expected = RegularFileGuard.ReadUnixIdentity(file);
        Assert.NotNull(expected);
        Assert.Equal(expected, RegularFileGuard.ReadUnixIdentity(link));
    }

    /// <summary>Unix ENOENT and ENOTDIR return null rather than native inspection exceptions.</summary>
    [Fact]
    public void ReadUnixIdentityReturnsNullForMissingPathAndNonDirectoryParent()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix stat requires a non-Windows host.");
        using var workspace = new TestWorkspace();
        string file = workspace.Write("input.bin", [1]);

        Assert.Null(RegularFileGuard.ReadUnixIdentity(workspace.PathFor("missing.bin")));
        Assert.Null(RegularFileGuard.ReadUnixIdentity(Path.Combine(file, "child.bin")));
    }

    private static void CreateFileLinkOrSkip(string link, string target)
    {
        try
        {
            _ = File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip("Symbolic links are not available in this environment.");
        }
    }
}
