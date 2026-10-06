// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Xunit;

namespace Nvt.Core.Tests.Files.Windows;

/// <summary>Exercises native reservation boundaries and every partial acquisition stage.</summary>
public sealed class WindowsStablePathCustodyEdgesTests
{
    private static readonly WindowsStableTreeLimits InstalledLimits =
        WindowsStableTreeLimits.ForInstalledVersion(4097, 4096, 536870912, 4096);

    /// <summary>Physical file, directory and sparse byte boundaries are independent and inclusive.</summary>
    [Theory]
    [InlineData("files", -1)]
    [InlineData("files", 0)]
    [InlineData("files", 1)]
    [InlineData("directories", -1)]
    [InlineData("directories", 0)]
    [InlineData("directories", 1)]
    [InlineData("bytes", -1)]
    [InlineData("bytes", 0)]
    [InlineData("bytes", 1)]
    public void ImmutableTreeEnforcesFrozenPhysicalBoundaries(string dimension, int offset)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string root = Directory.CreateDirectory(workspace.PathFor("tree")).FullName;
        if (dimension == "files")
        {
            for (int index = 0; index < 4097 + offset; index++)
            {
                File.WriteAllBytes(Path.Combine(root, $"f{index:D4}"), []);
            }
        }
        else if (dimension == "directories")
        {
            for (int index = 0; index < 4096 + offset; index++)
            {
                Directory.CreateDirectory(Path.Combine(root, $"d{index:D4}"));
            }
        }
        else
        {
            using FileStream file = File.Create(Path.Combine(root, "sparse"));
            file.SetLength(536875008 + offset);
        }
        WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireImmutableTree(
            root, InstalledLimits, TestContext.Current.CancellationToken);
        using (acquired.Custody)
        {
            Assert.Equal(offset <= 0, acquired.IsAcquired);
            Assert.Equal(offset <= 0 ? WindowsStableCustodyIssue.None : WindowsStableCustodyIssue.Unavailable,
                acquired.Issue);
            if (offset <= 0)
            {
                Assert.True(acquired.Custody!.RevalidateClosedTree());
            }
        }
        Directory.Move(root, root + ".released");
    }

    /// <summary>Cancellation and arbitrary hook faults release all handles at both populated stages.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeAcquisitionReleasesEveryHandleAfterStageFailure(bool afterTree, bool cancel)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string payload = workspace.Write("tree/nested/payload", [1, 2, 3]);
        string root = workspace.PathFor("tree");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        void Hook(WindowsStableCustodyStage stage)
        {
            if (stage == (afterTree ? WindowsStableCustodyStage.AfterTreeCaptured : WindowsStableCustodyStage.BeforeRootOpen))
            {
                if (cancel)
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                throw new InvalidOperationException("Synthetic hook failure.");
            }
        }
        if (cancel)
        {
            Assert.ThrowsAny<OperationCanceledException>(() =>
                WindowsStablePathCustody.TryAcquireImmutableTree(root, InstalledLimits, cancellation.Token, Hook));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() =>
                WindowsStablePathCustody.TryAcquireImmutableTree(root, InstalledLimits, cancellation.Token, Hook));
        }
        File.WriteAllBytes(payload, [4]);
        Directory.Move(root, root + ".released");
    }

    /// <summary>Native open, ancestor, root-type and descendant failures leave no acquired custody.</summary>
    [Theory]
    [InlineData("missing-ancestor")]
    [InlineData("missing-root")]
    [InlineData("wrong-root-type")]
    [InlineData("contended-child")]
    public void NativeAcquisitionFailureReleasesPartialCustody(string scenario)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string root = Directory.CreateDirectory(workspace.PathFor("tree")).FullName;
        workspace.Write("tree/a", [1]);
        string leaf = workspace.Write("tree/z", [2]);
        FileStream? blocker = null;
        if (scenario == "missing-ancestor")
        {
            root = workspace.PathFor("missing/tree");
        }
        else if (scenario == "missing-root")
        {
            root = workspace.PathFor("missing");
        }
        else if (scenario == "wrong-root-type")
        {
            root = leaf;
        }
        else
        {
            blocker = new FileStream(leaf, FileMode.Open, FileAccess.Write, FileShare.Read);
        }
        using (blocker)
        {
            WindowsStableCustodyResult acquired = WindowsStablePathCustody.TryAcquireImmutableTree(
                root, InstalledLimits, TestContext.Current.CancellationToken);
            acquired.Custody?.Dispose();
            Assert.False(acquired.IsAcquired);
        }
        File.WriteAllBytes(leaf, [3]);
        Directory.Move(workspace.PathFor("tree"), workspace.PathFor("released"));
    }

    /// <summary>A linked ancestor is rejected before any original leaf can supply identity.</summary>
    [Fact]
    public void LinkedAncestorNeverAcquiresCustody()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string target = Directory.CreateDirectory(workspace.PathFor("target")).FullName;
        workspace.Write("target/leaf", [1]);
        string link = workspace.PathFor("link");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (UnauthorizedAccessException error)
        {
            Assert.Skip($"Symbolic-link privilege is unavailable: {error.Message}");
        }
        var acquired = WindowsStablePathCustody.TryAcquireFile(Path.Combine(link, "leaf"),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(acquired.Custody);
        Assert.Equal(WindowsStableCustodyIssue.ReparsePoint, acquired.Issue);
        File.WriteAllBytes(Path.Combine(target, "leaf"), [2]);
    }

    /// <summary>Each possible interrupted duplicate is released while the original keeps its identity.</summary>
    [Fact]
    public void CloneCancellationReleasesEveryPartialDuplicate()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string payload = workspace.Write("tree/nested/payload", [1]);
        string root = workspace.PathFor("tree");
        var acquired = WindowsStablePathCustody.TryAcquireImmutableTree(root, InstalledLimits,
            TestContext.Current.CancellationToken);
        using WindowsStablePathCustody custody = Assert.IsType<WindowsStablePathCustody>(acquired.Custody);
        int duplicates = 0;
        var cloned = custody.TryClone(count => duplicates = count, TestContext.Current.CancellationToken);
        cloned.Custody!.Dispose();
        Assert.True(duplicates > 0);
        for (int index = 1; index <= duplicates; index++)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            Assert.ThrowsAny<OperationCanceledException>(() => custody.TryClone(count =>
            {
                if (count == index)
                {
                    cancellation.Cancel();
                }
            }, cancellation.Token));
            Assert.True(custody.RevalidateClosedTree());
        }
        custody.Dispose();
        File.WriteAllBytes(payload, [2]);
        Directory.Move(root, root + ".released");
    }

    /// <summary>A cloned custody retains deny-write and deny-delete after releasing the original.</summary>
    [Fact]
    public void ClonePreservesIdentityAndSharingUntilItsOwnDisposal()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string payload = workspace.Write("tree/payload", [1]);
        var acquired = WindowsStablePathCustody.TryAcquireImmutableTree(workspace.PathFor("tree"), InstalledLimits,
            TestContext.Current.CancellationToken);
        using WindowsStablePathCustody original = acquired.Custody!;
        using WindowsStablePathCustody clone = original.TryClone(cancellationToken: TestContext.Current.CancellationToken).Custody!;
        original.Dispose();
        Assert.Throws<IOException>(() => File.WriteAllBytes(payload, [2]));
        Assert.Throws<IOException>(() => File.Move(payload, payload + ".moved"));
        Assert.True(clone.RevalidateClosedTree());
        clone.Dispose();
        File.WriteAllBytes(payload, [2]);
    }

    /// <summary>The native rename layout preserves both supported pointer widths and its exact error message.</summary>
    [Theory]
    [InlineData(3, -1)]
    [InlineData(4, 12)]
    [InlineData(5, -1)]
    [InlineData(7, -1)]
    [InlineData(8, 20)]
    [InlineData(9, -1)]
    public void RenameLayoutBoundaries(int pointerSize, int expected)
    {
        if (expected < 0)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                WindowsStablePathCustody.GetRenameFileNameOffset(pointerSize));
            Assert.Equal("pointerSize", error.ParamName);
            Assert.StartsWith("FILE_RENAME_INFORMATION supports only 32-bit and 64-bit pointer layouts.", error.Message);
        }
        else
        {
            Assert.Equal(expected, WindowsStablePathCustody.GetRenameFileNameOffset(pointerSize));
        }
    }
}
