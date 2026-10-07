// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Files.Windows;

/// <summary>Characterizes write reservations, injected namespace races and exact residue ownership.</summary>
public sealed class WindowsStableRelativeWriteTreeEdgesTests
{
    /// <summary>A nested linked child never redirects a relative held-parent write outside custody.</summary>
    [Fact]
    public void NestedJunctionRaceNeverWritesOutsideAndPreservesForeignResidue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string managed = Directory.CreateDirectory(workspace.PathFor("managed")).FullName;
        string outside = Directory.CreateDirectory(workspace.PathFor("outside")).FullName;
        string sentinel = workspace.Write("outside/sentinel", [42]);
        using WindowsStableRelativeWriteRoot root = Acquire(managed);
        bool injected = false;
        using WindowsStableRelativeWriteTree tree = Create(root, new(1, 2, 4, new(1, 2, 4)), directory =>
        {
            if (Path.GetFileName(directory) == "external-tools")
            {
                try
                {
                    Directory.CreateSymbolicLink(Path.Combine(directory, "worker"), outside);
                    injected = true;
                }
                catch (UnauthorizedAccessException error)
                {
                    Assert.Skip($"Symbolic-link privilege is unavailable: {error.Message}");
                }
            }
        });
        var error = Assert.Throws<IOException>(() => tree.CreateFile("external-tools/worker/probe.exe"));
        Assert.Equal("A package directory changed during relative creation.", error.Message);
        Assert.True(injected);
        Assert.False(File.Exists(Path.Combine(outside, "probe.exe")));
        Assert.Equal([42], File.ReadAllBytes(sentinel));
        Assert.Equal(WindowsStableCustodyIssue.Changed, tree.Cleanup());
        Assert.True(Directory.Exists(tree.StagingPath));
    }

    /// <summary>A held file denies rewrites, while a late child invalidates topology and survives cleanup.</summary>
    [Fact]
    public void HeldRewriteIsDeniedAndLateChildPreventsPromotionAndExactCleanup()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        using WindowsStableRelativeWriteRoot root = Acquire(
            Directory.CreateDirectory(workspace.PathFor("managed")).FullName);
        using WindowsStableRelativeWriteTree tree = Create(root, new(1, 0, 4, new(1, 1, 4)));
        using (FileStream file = tree.CreateFile("payload"))
        {
            file.Write([1, 2, 3, 4]);
        }
        string payload = Path.Combine(tree.StagingPath, "payload");
        Assert.Throws<IOException>(() => File.WriteAllBytes(payload, [9]));
        string foreign = Path.Combine(tree.StagingPath, "foreign");
        Assert.Equal(WindowsStableCustodyIssue.Changed, tree.PrepareForPromotion(staging =>
            File.WriteAllText(Path.Combine(staging, "foreign"), "foreign")));
        var phaseError = Assert.Throws<InvalidOperationException>(() => tree.Promote());
        Assert.Equal("Cannot Promote while the write tree is in the Writing phase.", phaseError.Message);
        Assert.Equal(WindowsStableCustodyIssue.Changed, tree.Cleanup());
        Assert.Equal("foreign", File.ReadAllText(foreign));
        Assert.False(File.Exists(payload));
        Assert.Equal(WindowsStableCustodyIssue.Changed, tree.Cleanup());
    }

    /// <summary>The native no-replace rename preserves a destination introduced immediately before promotion.</summary>
    [Fact]
    public void PromotionNeverReplacesLateDestination()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string managed = Directory.CreateDirectory(workspace.PathFor("managed")).FullName;
        using WindowsStableRelativeWriteRoot root = Acquire(managed);
        using WindowsStableRelativeWriteTree tree = Create(root, new(1, 0, 4, new(1, 1, 4)));
        using (FileStream file = tree.CreateFile("payload"))
        {
            file.Write([1, 2, 3, 4]);
        }
        Assert.Equal(WindowsStableCustodyIssue.None, tree.PrepareForPromotion());
        string target = Directory.CreateDirectory(Path.Combine(managed, "versions", "1.0.0")).FullName;
        File.WriteAllText(Path.Combine(target, "owner.txt"), "destination-owner");
        Assert.Equal(WindowsStableCustodyIssue.Changed, tree.Promote());
        Assert.Equal("destination-owner", File.ReadAllText(Path.Combine(target, "owner.txt")));
        Assert.Equal(["owner.txt"], Directory.EnumerateFiles(target).Select(Path.GetFileName));
        Assert.Equal(WindowsStableCustodyIssue.None, tree.Cleanup());
        Assert.False(Directory.Exists(tree.StagingPath));
    }

    /// <summary>Reservation accounting checks actual length with subtraction before exact topology proof.</summary>
    [Theory]
    [InlineData(3, (int)WindowsStableCustodyIssue.Changed)]
    [InlineData(4, (int)WindowsStableCustodyIssue.None)]
    [InlineData(5, (int)WindowsStableCustodyIssue.Unavailable)]
    public void PrepareChecksExactByteReservation(int actualBytes, int expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        using WindowsStableRelativeWriteRoot root = Acquire(
            Directory.CreateDirectory(workspace.PathFor("managed")).FullName);
        using WindowsStableRelativeWriteTree tree = Create(root, new(1, 0, 4, new(1, 1, 4)));
        using (FileStream file = tree.CreateFile("payload"))
        {
            file.SetLength(actualBytes);
        }
        Assert.Equal(expected, (int)tree.PrepareForPromotion());
        Assert.Equal(WindowsStableCustodyIssue.None, tree.Cleanup());
        Assert.False(Directory.Exists(tree.StagingPath));
    }

    /// <summary>The caller path ceiling retains the exact 511, 512 and 513 character boundary.</summary>
    [Theory]
    [InlineData(511, true)]
    [InlineData(512, true)]
    [InlineData(513, false)]
    public void RelativePathCharacterBoundary(int length, bool accepted)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        using WindowsStableRelativeWriteRoot root = Acquire(
            Directory.CreateDirectory(workspace.PathFor("managed")).FullName);
        using WindowsStableRelativeWriteTree tree = Create(root, new(1, 2, 1, new(1, 2, 1)));
        string path = new string('a', 200) + "/" + new string('b', 200) + "/" + new string('c', length - 402);
        Assert.Equal(length, path.Length);
        if (accepted)
        {
            using FileStream file = tree.CreateFile(path);
            file.WriteByte(1);
        }
        else
        {
            var error = Assert.Throws<IOException>(() => tree.CreateFile(path));
            Assert.Equal("The package write reservation was exceeded.", error.Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(tree.StagingPath));
        }
    }

    /// <summary>File and directory creation stop at the exact independent reservation with frozen messages.</summary>
    [Fact]
    public void CreateFileStopsAtIndependentReservations()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        using WindowsStableRelativeWriteRoot root = Acquire(
            Directory.CreateDirectory(workspace.PathFor("managed")).FullName);
        using WindowsStableRelativeWriteTree tree = Create(root, new(2, 1, 2, new(2, 1, 2)));
        using (FileStream file = tree.CreateFile("one/a"))
        {
            file.WriteByte(1);
        }
        var directoryError = Assert.Throws<IOException>(() => tree.CreateFile("two/b"));
        Assert.Equal("The package directory reservation was exceeded.", directoryError.Message);
        using (FileStream file = tree.CreateFile("one/b"))
        {
            file.WriteByte(2);
        }
        var fileError = Assert.Throws<IOException>(() => tree.CreateFile("one/c"));
        Assert.Equal("The package write reservation was exceeded.", fileError.Message);
        Assert.Equal(WindowsStableCustodyIssue.None, tree.PrepareForPromotion());
    }

    /// <summary>A rejecting product callback cannot be replaced by a Core default admission.</summary>
    [Fact]
    public void ProductPathAdmissionIsMandatoryAndRunsBeforeCreation()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        using WindowsStableRelativeWriteRoot root = Acquire(
            Directory.CreateDirectory(workspace.PathFor("managed")).FullName);
        int calls = 0;
        Assert.Equal(WindowsStableCustodyIssue.None, root.TryCreateVersionTree("1.0.0", ".staging", "versions",
            new(1, 1, 1, new(1, 1, 1)), 512, _ => { calls++; return false; }, null, out var created));
        using WindowsStableRelativeWriteTree tree = created!;
        Assert.Throws<IOException>(() => tree.CreateFile("safe/payload"));
        Assert.Equal(1, calls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.StagingPath));
    }

    /// <summary>The relative path ceiling requires a positive value before creating any tree.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WriteTreeRejectsNonpositivePathLimit(int limit)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string managed = Directory.CreateDirectory(workspace.PathFor("managed")).FullName;
        using WindowsStableRelativeWriteRoot root = Acquire(managed);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => root.TryCreateVersionTree("1.0.0",
            ".staging", "versions", new(1, 1, 1, new(1, 1, 1)), limit, ContractValidation.IsSafeRelativePath,
            null, out _));
        Assert.Equal("maximumRelativePathCharacters", error.ParamName);
        Assert.Empty(Directory.EnumerateFileSystemEntries(managed));
    }

    /// <summary>Null policy and over-reservation rejection acquire no staging descendants.</summary>
    [Fact]
    public void WriteTreeRejectsMissingPolicyAndOverReservationWithoutResidue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string managed = Directory.CreateDirectory(workspace.PathFor("managed")).FullName;
        using WindowsStableRelativeWriteRoot root = Acquire(managed);
        Assert.Throws<ArgumentNullException>(() => root.TryCreateVersionTree("1.0.0", ".staging", "versions",
            new(1, 1, 1, new(1, 1, 1)), 512, null!, null, out _));
        Assert.Equal(WindowsStableCustodyIssue.InvalidPath, root.TryCreateVersionTree("1.0.0", ".staging", "versions",
            new(2, 1, 1, new(1, 1, 1)), 512, ContractValidation.IsSafeRelativePath, null, out var tree));
        Assert.Null(tree);
        Assert.Empty(Directory.EnumerateFileSystemEntries(managed));
    }

    private static WindowsStableRelativeWriteRoot Acquire(string managed)
    {
        Assert.Equal(WindowsStableCustodyIssue.None,
            WindowsStableRelativeWriteRoot.TryAcquire(managed, out var acquired));
        return acquired!;
    }

    private static WindowsStableRelativeWriteTree Create(WindowsStableRelativeWriteRoot root,
        WindowsStableTreeReservation reservation, Action<string>? hook = null)
    {
        Assert.Equal(WindowsStableCustodyIssue.None, root.TryCreateVersionTree("1.0.0", ".staging", "versions",
            reservation, 512, ContractValidation.IsSafeRelativePath, hook, out var tree));
        return tree!;
    }
}
