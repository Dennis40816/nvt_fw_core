// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Microsoft.Win32.SafeHandles;
using Nvt.Core.Files.Windows;
using Nvt.Core.TestSupport;
using Xunit;
using SharedTestWorkspace = Nvt.Core.TestSupport.TestWorkspace;

namespace Nvt.Core.Tests.Files.Windows;

public sealed partial class WindowsStablePathCustodyAcquisitionContractTests
{
    /// <summary>Promotable root sharing permits replacement, which final identity revalidation rejects.</summary>
    [Fact]
    public void ReplacedPromotableRootReturnsChangedAndReleasesOriginalRoot()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = Directory.CreateDirectory(workspace.GetPath("case/tree")).FullName;
        bool replaced = false;

        WindowsStableCustodyResult acquired = Acquire("promotable", root,
            TestContext.Current.CancellationToken, AtStage(WindowsStableCustodyStage.AfterTreeCaptured, () =>
            {
                Directory.Move(root, root + ".original");
                _ = Directory.CreateDirectory(root);
                replaced = true;
            }));
        using (acquired.Custody)
        {
            Assert.True(replaced);
            AssertFailure(acquired, WindowsStableCustodyIssue.Changed);
            Assert.False(acquired.IsExactChildMissing);
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>Repeated disposal is harmless. A repeated SafeFileHandle disposal is a no-op, so this cannot show a double native close.</summary>
    [Theory]
    [InlineData("file")]
    [InlineData("writable")]
    [InlineData("promotable")]
    public void SuccessfulDisposeIsIdempotentAndPreservesLaterHandle(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = PrepareRoot(workspace, kind);
        WindowsStableCustodyResult acquired = Acquire(kind, root, TestContext.Current.CancellationToken);
        using WindowsStablePathCustody custody = Assert.IsType<WindowsStablePathCustody>(acquired.Custody);
        Assert.Equal(WindowsStableCustodyIssue.None, acquired.Issue);

        custody.Dispose();
        using (SafeFileHandle probe = OpenProbe(root, directory: kind != "file"))
        {
            Assert.True(WindowsStablePathCustody.TryGetIdentity(probe, out WindowsStablePathIdentity expected));
            custody.Dispose();
            Assert.True(WindowsStablePathCustody.TryGetIdentity(probe, out WindowsStablePathIdentity actual));
            Assert.Equal(expected, actual);
        }

        _ = Assert.Throws<ObjectDisposedException>(() => custody.RevalidateClosedTree());
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>Disposing the clone first leaves the original's complete handle inventory independent.</summary>
    [Fact]
    public void DisposingCloneFirstPreservesOriginalUntilItsDisposal()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = PrepareRoot(workspace, "immutable");
        WindowsStableCustodyResult acquired = Acquire("immutable", root, TestContext.Current.CancellationToken);
        using WindowsStablePathCustody original = Assert.IsType<WindowsStablePathCustody>(acquired.Custody);
        WindowsStableCustodyResult cloned = original.TryClone(cancellationToken: TestContext.Current.CancellationToken);
        using WindowsStablePathCustody clone = Assert.IsType<WindowsStablePathCustody>(cloned.Custody);
        Assert.Equal(WindowsStableCustodyIssue.None, cloned.Issue);
        SafeFileHandle originalRoot = Assert.IsType<SafeFileHandle>(original.RootDirectoryHandle);
        SafeFileHandle cloneRoot = Assert.IsType<SafeFileHandle>(clone.RootDirectoryHandle);
        Assert.NotSame(originalRoot, cloneRoot);
        Assert.True(original.HasSameRootIdentity(cloneRoot));

        clone.Dispose();
        clone.Dispose();

        Assert.True(cloneRoot.IsClosed);
        Assert.False(originalRoot.IsClosed);
        Assert.True(original.RevalidateClosedTree());
        _ = Assert.Throws<IOException>(() => TestFiles.WriteAllBytes(Path.Combine(root, "a.bin"), [9]));
        using (FileStream read = original.OpenReadOnlyFile("nested/z.bin"))
        {
            Assert.Equal(2, read.Length);
        }
        original.Dispose();
        Assert.True(originalRoot.IsClosed);
        AssertReleasedFixture(workspace.GetPath("case"));
    }
}
