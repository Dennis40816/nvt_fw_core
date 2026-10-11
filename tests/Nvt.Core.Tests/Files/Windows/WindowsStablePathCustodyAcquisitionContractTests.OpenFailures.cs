// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Microsoft.Win32.SafeHandles;
using Nvt.Core.Files.Windows;
using Nvt.Core.TestSupport;
using Xunit;
using SharedTestWorkspace = Nvt.Core.TestSupport.TestWorkspace;

namespace Nvt.Core.Tests.Files.Windows;

public sealed partial class WindowsStablePathCustodyAcquisitionContractTests
{
    /// <summary>Every kind unwinds ancestors acquired before a contended intermediate directory.</summary>
    [Theory]
    [InlineData("file")]
    [InlineData("immutable")]
    [InlineData("writable")]
    [InlineData("promotable")]
    public void ContendedAncestorReleasesEarlierAncestorHandles(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        _ = PrepareRoot(workspace, kind);
        string blocked = workspace.GetPath("case/tree/nested");
        string root = kind == "file" ? Path.Combine(blocked, "z.bin")
            : Directory.CreateDirectory(Path.Combine(blocked, "target")).FullName;
        var stages = new List<WindowsStableCustodyStage>();

        using (SafeFileHandle blocker = OpenProbe(blocked, directory: true))
        {
            WindowsStableCustodyResult acquired = Acquire(kind, root,
                TestContext.Current.CancellationToken, stages.Add);
            using (acquired.Custody)
            {
                AssertFailure(acquired, WindowsStableCustodyIssue.Contended);
                Assert.Empty(stages);
                Assert.False(acquired.IsExactChildMissing);
            }
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>A root open failure closes its failed handle and the complete ancestor chain.</summary>
    [Theory]
    [InlineData("file")]
    [InlineData("immutable")]
    [InlineData("writable")]
    [InlineData("promotable")]
    public void ContendedRootReleasesAncestorHandles(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = PrepareRoot(workspace, kind);
        var stages = new List<WindowsStableCustodyStage>();

        using (SafeFileHandle blocker = OpenProbe(root, directory: kind != "file"))
        {
            WindowsStableCustodyResult acquired = Acquire(kind, root,
                TestContext.Current.CancellationToken, stages.Add);
            using (acquired.Custody)
            {
                AssertFailure(acquired, WindowsStableCustodyIssue.Contended);
                Assert.Equal([WindowsStableCustodyStage.BeforeRootOpen], stages);
                Assert.False(acquired.IsExactChildMissing);
            }
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>The second missing-root probe preserves a nonmissing failure instead of claiming absence.</summary>
    [Theory]
    [InlineData("file")]
    [InlineData("immutable")]
    [InlineData("writable")]
    [InlineData("promotable")]
    public void ContendedMissingConfirmationDoesNotClaimExactAbsence(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        _ = Directory.CreateDirectory(workspace.GetPath("case"));
        string root = workspace.GetPath("case/missing");
        SafeFileHandle? blocker = null;
        try
        {
            WindowsStableCustodyResult acquired = Acquire(kind, root,
                TestContext.Current.CancellationToken,
                AtStage(WindowsStableCustodyStage.AfterMissingRootObservation, () =>
                {
                    CreateRootEntry(root, kind);
                    blocker = OpenProbe(root, directory: kind != "file");
                }));
            using (acquired.Custody)
            {
                Assert.NotNull(blocker);
                AssertFailure(acquired, WindowsStableCustodyIssue.Contended);
                Assert.False(acquired.IsExactChildMissing);
            }
        }
        finally
        {
            blocker?.Dispose();
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>A directory open failure in the walk returns the issue and releases every held handle.</summary>
    [Theory]
    [InlineData("immutable")]
    [InlineData("promotable")]
    public void ContendedWalkDirectoryReleasesEarlierFileAndAncestors(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = PrepareRoot(workspace, kind);
        var stages = new List<WindowsStableCustodyStage>();

        using (SafeFileHandle blocker = OpenProbe(workspace.GetPath("case/tree/nested"), directory: true))
        {
            WindowsStableCustodyResult acquired = Acquire(kind, root,
                TestContext.Current.CancellationToken, stages.Add);
            using (acquired.Custody)
            {
                AssertFailure(acquired, WindowsStableCustodyIssue.Contended);
                Assert.Equal([WindowsStableCustodyStage.BeforeRootOpen], stages);
            }
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>A file open failure in a promotable walk returns the issue and releases every held handle.</summary>
    [Fact]
    public void ContendedPromotableWalkFileReleasesPartialTree()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = PrepareRoot(workspace, "promotable");
        var stages = new List<WindowsStableCustodyStage>();

        using (var blocker = new FileStream(workspace.GetPath("case/tree/nested/z.bin"),
                   FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            WindowsStableCustodyResult acquired = Acquire("promotable", root,
                TestContext.Current.CancellationToken, stages.Add);
            using (acquired.Custody)
            {
                AssertFailure(acquired, WindowsStableCustodyIssue.Contended);
                Assert.Equal([WindowsStableCustodyStage.BeforeRootOpen], stages);
            }
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>No directory acquisition kind grants custody through a linked root.</summary>
    [Theory]
    [InlineData("immutable")]
    [InlineData("writable")]
    [InlineData("promotable")]
    public void LinkedDirectoryRootRejectsCustodyAndReleasesAncestors(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string target = PrepareRoot(workspace, kind);
        string link = workspace.GetPath("case/link");
        CreateDirectoryLink(link, target);

        WindowsStableCustodyResult acquired = Acquire(kind, link, TestContext.Current.CancellationToken);
        using (acquired.Custody)
        {
            AssertFailure(acquired, WindowsStableCustodyIssue.ReparsePoint);
            Assert.False(acquired.IsExactChildMissing);
        }
        Directory.Delete(link);
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>An overfull directory is rejected as Unavailable. The test does not tell the allowance check from the count limits.</summary>
    [Theory]
    [InlineData("immutable")]
    [InlineData("promotable")]
    public void EnumerationAllowanceRejectionReleasesRootAndAncestors(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = Directory.CreateDirectory(workspace.GetPath("case/tree")).FullName;
        TestFiles.WriteAllBytes(Path.Combine(root, "a.bin"), [1]);
        TestFiles.WriteAllBytes(Path.Combine(root, "b.bin"), [2]);
        TestFiles.WriteAllBytes(Path.Combine(root, "c.bin"), [3]);
        TestFiles.WriteAllBytes(Path.Combine(root, "d.bin"), [4]);
        var limits = new WindowsStableTreeLimits(1, 1, 4);
        var stages = new List<WindowsStableCustodyStage>();

        WindowsStableCustodyResult acquired = kind == "immutable"
            ? WindowsStablePathCustody.TryAcquireImmutableTree(root, limits,
                TestContext.Current.CancellationToken, stages.Add)
            : WindowsStablePathCustody.TryAcquirePromotableTree(root, limits,
                TestContext.Current.CancellationToken, stages.Add);
        using (acquired.Custody)
        {
            AssertFailure(acquired, WindowsStableCustodyIssue.Unavailable);
            Assert.Equal([WindowsStableCustodyStage.BeforeRootOpen], stages);
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        try
        {
            _ = Directory.CreateSymbolicLink(link, target);
        }
        catch (UnauthorizedAccessException exception)
        {
            Assert.Skip($"Symbolic-link privilege is unavailable: {exception.Message}");
        }
    }
}
