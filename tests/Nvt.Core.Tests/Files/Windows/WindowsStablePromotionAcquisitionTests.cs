// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Xunit;

namespace Nvt.Core.Tests.Files.Windows;

/// <summary>Exercises failed held capture and promotion acquisition without releasing borrowed ownership.</summary>
public sealed class WindowsStablePromotionAcquisitionTests
{
    /// <summary>Each tree acquisition mode releases its partial capture on byte-budget rejection.</summary>
    [Theory]
    [InlineData("immutable")]
    [InlineData("promotable")]
    [InlineData("delete-capable")]
    [InlineData("held-root")]
    public void EveryAcquisitionModeReleasesRejectedCapture(string mode)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string payload = workspace.Write("tree/a", [1, 2, 3]);
        workspace.Write("tree/z", [4, 5, 6]);
        string root = workspace.PathFor("tree");
        var limits = new WindowsStableTreeLimits(2, 1, 3);
        WindowsStableCustodyResult acquired;
        if (mode == "held-root")
        {
            Assert.Equal(WindowsStableCustodyIssue.None,
                WindowsStableRelativeWriteRoot.TryAcquire(root, out var borrowed));
            using (borrowed)
            {
                acquired = WindowsStablePathCustody.TryCaptureImmutableTreeFromHeldDirectory(root,
                    borrowed!.RootHandle, limits, TestContext.Current.CancellationToken);
                Assert.False(borrowed.RootHandle.IsClosed);
            }
        }
        else if (mode == "delete-capable")
        {
            acquired = WindowsStablePathCustody.TryAcquireDeleteCapableTree(root, limits,
                TestContext.Current.CancellationToken);
        }
        else if (mode == "promotable")
        {
            acquired = WindowsStablePathCustody.TryAcquirePromotableTree(root, limits,
                TestContext.Current.CancellationToken);
        }
        else
        {
            acquired = WindowsStablePathCustody.TryAcquireImmutableTree(root, limits,
                TestContext.Current.CancellationToken);
        }
        Assert.Null(acquired.Custody);
        Assert.Equal(WindowsStableCustodyIssue.Unavailable, acquired.Issue);
        File.WriteAllBytes(payload, [9]);
        Directory.Move(root, root + ".released");
    }

    /// <summary>A failure during the second missing-child observation releases held ancestors.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingChildObservationFaultReleasesAncestors(bool cancel)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string parent = Directory.CreateDirectory(workspace.PathFor("parent")).FullName;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        void Hook(WindowsStableCustodyStage stage)
        {
            if (stage == WindowsStableCustodyStage.AfterMissingRootObservation)
            {
                if (cancel)
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                throw new InvalidOperationException("Synthetic absence probe failure.");
            }
        }
        if (cancel)
        {
            Assert.ThrowsAny<OperationCanceledException>(() =>
                WindowsStablePathCustody.TryAcquireFile(Path.Combine(parent, "missing"), Hook, cancellation.Token));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() =>
                WindowsStablePathCustody.TryAcquireFile(Path.Combine(parent, "missing"), Hook, cancellation.Token));
        }
        Directory.Move(parent, parent + ".released");
    }

    /// <summary>Checked child-count arithmetic retains its exception and releases all earlier acquisitions.</summary>
    [Theory]
    [InlineData("immutable")]
    [InlineData("promotable")]
    [InlineData("delete-capable")]
    [InlineData("held-root")]
    public void CheckedEnumerationAllowanceOverflowReleasesRootAndAncestors(string mode)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string payload = workspace.Write("tree/payload", [1]);
        string root = workspace.PathFor("tree");
        var limits = new WindowsStableTreeLimits(int.MaxValue, 1, 1);
        if (mode == "held-root")
        {
            Assert.Equal(WindowsStableCustodyIssue.None,
                WindowsStableRelativeWriteRoot.TryAcquire(root, out var borrowed));
            using (borrowed)
            {
                Assert.Throws<OverflowException>(() =>
                    WindowsStablePathCustody.TryCaptureImmutableTreeFromHeldDirectory(
                        root, borrowed!.RootHandle, limits, TestContext.Current.CancellationToken));
                Assert.False(borrowed!.RootHandle.IsClosed);
            }
        }
        else if (mode == "delete-capable")
        {
            Assert.Throws<OverflowException>(() =>
                WindowsStablePathCustody.TryAcquireDeleteCapableTree(root, limits, TestContext.Current.CancellationToken));
        }
        else if (mode == "promotable")
        {
            Assert.Throws<OverflowException>(() =>
                WindowsStablePathCustody.TryAcquirePromotableTree(root, limits, TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Throws<OverflowException>(() =>
                WindowsStablePathCustody.TryAcquireImmutableTree(root, limits, TestContext.Current.CancellationToken));
        }
        File.WriteAllBytes(payload, [2]);
        Directory.Move(root, root + ".released");
    }

    /// <summary>Null borrowed-root validation keeps precedence over an invalid default limits value.</summary>
    [Fact]
    public void NullBorrowedRootKeepsFrozenExceptionOrder()
    {
        var error = Assert.Throws<ArgumentNullException>(() =>
            WindowsStablePathCustody.TryCaptureImmutableTreeFromHeldDirectory(
                "relative", null!, default, TestContext.Current.CancellationToken));
        Assert.Equal("heldRoot", error.ParamName);
    }

    /// <summary>The native counted UTF-16 name preserves ushort boundaries without creating a leaf.</summary>
    [Theory]
    [InlineData(32766)]
    [InlineData(32767)]
    [InlineData(32768)]
    public void NativeUnicodeNameLengthUsesCheckedUshort(int characters)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string root = Directory.CreateDirectory(workspace.PathFor("tree")).FullName;
        Assert.Equal(WindowsStableCustodyIssue.None,
            WindowsStableRelativeWriteRoot.TryAcquire(root, out var borrowed));
        using (borrowed)
        {
            string name = new('n', characters);
            if (characters > 32767)
            {
                Assert.Throws<OverflowException>(() => WindowsStablePathCustody.OpenRelative(
                    borrowed!.RootHandle, name, directory: false, writableParent: false,
                    allowWriteShare: false, allowDeleteShare: false, requestDeleteAccess: false, out _));
            }
            else
            {
                int status = WindowsStablePathCustody.OpenRelative(
                    borrowed!.RootHandle, name, directory: false, writableParent: false,
                    allowWriteShare: false, allowDeleteShare: false, requestDeleteAccess: false, out var handle);
                using (handle)
                {
                    Assert.NotEqual(WindowsStablePathCustody.NativeMethods.StatusSuccess, status);
                }
            }
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        Directory.Move(root, root + ".released");
    }

    /// <summary>Closed borrowed roots fail without manufacturing a replacement path owner.</summary>
    [Fact]
    public void ClosedBorrowedRootDoesNotAcquireByPath()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string root = Directory.CreateDirectory(workspace.PathFor("tree")).FullName;
        Assert.Equal(WindowsStableCustodyIssue.None,
            WindowsStableRelativeWriteRoot.TryAcquire(root, out var borrowed));
        var handle = borrowed!.RootHandle;
        borrowed.Dispose();
        var acquired = WindowsStablePathCustody.TryCaptureImmutableTreeFromHeldDirectory(root, handle,
            new(1, 1, 1), TestContext.Current.CancellationToken);
        Assert.Null(acquired.Custody);
        Assert.Equal(WindowsStableCustodyIssue.InvalidPath, acquired.Issue);
        Directory.Move(root, root + ".released");
    }
}
