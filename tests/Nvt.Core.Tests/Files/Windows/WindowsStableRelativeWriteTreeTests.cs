// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files.Windows;
using Xunit;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Tests.Files.Windows;

/// <summary>Exercises relative writes, held promotion and exact cleanup ownership.</summary>
public sealed class WindowsStableRelativeWriteTreeTests
{
    /// <summary>The held staging parent permits rename and exact final-tree rollback.</summary>
    [Fact]
    public void PreparedTreePromotesWithHeldParentAndDeletesExactFinalTree()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string managedRoot = Directory.CreateDirectory(workspace.PathFor("managed")).FullName;
        Assert.Equal(
            WindowsStableCustodyIssue.None,
            WindowsStableRelativeWriteRoot.TryAcquire(
                managedRoot,
                out WindowsStableRelativeWriteRoot? acquiredRoot));
        using WindowsStableRelativeWriteRoot writeRoot = acquiredRoot!;
        Assert.Equal(
            WindowsStableCustodyIssue.None,
            writeRoot.TryCreateVersionTree(
                "0.10.6",
                ".staging",
                "versions",
                new WindowsStableTreeReservation(
                    Files: 1,
                    Directories: 1,
                    Bytes: 4,
                    new WindowsStableTreeLimits(1, 1, 4)),
                maximumRelativePathCharacters: 512,
                isSafeRelativePayloadPath: ContractValidation.IsSafeRelativePath,
                afterDirectoryCreated: null,
                out WindowsStableRelativeWriteTree? createdTree));
        using WindowsStableRelativeWriteTree tree = createdTree!;
        using (FileStream payload = tree.CreateFile("nested/payload.bin"))
        {
            payload.Write([1, 2, 3, 4]);
        }

        Assert.Equal(WindowsStableCustodyIssue.None, tree.PrepareForPromotion());
        Assert.Equal(WindowsStableCustodyIssue.None, tree.Promote());
        string finalPath = Path.Combine(managedRoot, "versions", "0.10.6");
        WindowsStableCustodyResult captured = tree.CapturePromotedImmutableTree(
            finalPath,
            TestContext.Current.CancellationToken);
        Assert.True(captured.IsAcquired);
        captured.Custody!.Dispose();

        Assert.Equal(WindowsStableCustodyIssue.None, tree.RollbackPromotionAndCleanup());
        Assert.False(Directory.Exists(finalPath));
    }

    /// <summary>Cancellation after promotion cannot prevent exact cleanup or delete a reused staging name.</summary>
    [Fact]
    public void PromotedTreeCancellationCleansHeldRootAndPreservesForeignStagingReplacement()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string managedRoot = Directory.CreateDirectory(workspace.PathFor("managed")).FullName;
        Assert.Equal(
            WindowsStableCustodyIssue.None,
            WindowsStableRelativeWriteRoot.TryAcquire(
                managedRoot,
                out WindowsStableRelativeWriteRoot? acquiredRoot));
        using WindowsStableRelativeWriteRoot writeRoot = acquiredRoot!;
        Assert.Equal(
            WindowsStableCustodyIssue.None,
            writeRoot.TryCreateVersionTree(
                "0.10.6",
                ".staging",
                "versions",
                new WindowsStableTreeReservation(
                    Files: 1,
                    Directories: 1,
                    Bytes: 4,
                    new WindowsStableTreeLimits(1, 1, 4)),
                maximumRelativePathCharacters: 512,
                isSafeRelativePayloadPath: ContractValidation.IsSafeRelativePath,
                afterDirectoryCreated: null,
                out WindowsStableRelativeWriteTree? createdTree));
        using WindowsStableRelativeWriteTree tree = createdTree!;
        using (FileStream payload = tree.CreateFile("nested/payload.bin"))
        {
            payload.Write([1, 2, 3, 4]);
        }

        Assert.Equal(WindowsStableCustodyIssue.None, tree.PrepareForPromotion());
        Assert.Equal(WindowsStableCustodyIssue.None, tree.Promote());
        string finalPath = Path.Combine(managedRoot, "versions", "0.10.6");
        string reusedStaging = tree.StagingPath;
        _ = Directory.CreateDirectory(reusedStaging);
        string foreign = Path.Combine(reusedStaging, "foreign.txt");
        File.WriteAllText(foreign, "foreign");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        _ = Assert.ThrowsAny<OperationCanceledException>(() =>
            tree.CapturePromotedImmutableTree(finalPath, cancellation.Token));

        Assert.Equal(WindowsStableCustodyIssue.None, tree.RollbackPromotionAndCleanup());
        Assert.False(Directory.Exists(finalPath));
        Assert.Equal("foreign", File.ReadAllText(foreign));
    }

    /// <summary>A released descendant replacement is never mistaken for an owned file.</summary>
    [Fact]
    public void PreparedTreeRejectsAndPreservesSubstitutedDescendant()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = new TestWorkspace();
        string managedRoot = Directory.CreateDirectory(workspace.PathFor("managed")).FullName;
        Assert.Equal(
            WindowsStableCustodyIssue.None,
            WindowsStableRelativeWriteRoot.TryAcquire(
                managedRoot,
                out WindowsStableRelativeWriteRoot? acquiredRoot));
        using WindowsStableRelativeWriteRoot writeRoot = acquiredRoot!;
        Assert.Equal(
            WindowsStableCustodyIssue.None,
            writeRoot.TryCreateVersionTree(
                "0.10.6",
                ".staging",
                "versions",
                new WindowsStableTreeReservation(
                    Files: 1,
                    Directories: 1,
                    Bytes: 4,
                    new WindowsStableTreeLimits(1, 1, 16)),
                maximumRelativePathCharacters: 512,
                isSafeRelativePayloadPath: ContractValidation.IsSafeRelativePath,
                afterDirectoryCreated: null,
                out WindowsStableRelativeWriteTree? createdTree));
        using WindowsStableRelativeWriteTree tree = createdTree!;
        using (FileStream payload = tree.CreateFile("nested/payload.bin"))
        {
            payload.Write([1, 2, 3, 4]);
        }
        Assert.Equal(WindowsStableCustodyIssue.None, tree.PrepareForPromotion());
        string stagedPayload = Path.Combine(tree.StagingPath, "nested", "payload.bin");
        string releasedOriginal = workspace.PathFor("released-original.bin");
        File.Move(stagedPayload, releasedOriginal);
        File.WriteAllText(stagedPayload, "foreign");

        Assert.Equal(WindowsStableCustodyIssue.None, tree.Promote());
        string finalPath = Path.Combine(managedRoot, "versions", "0.10.6");
        WindowsStableCustodyResult captured = tree.CapturePromotedImmutableTree(
            finalPath,
            TestContext.Current.CancellationToken);
        Assert.False(captured.IsAcquired);
        Assert.Equal(WindowsStableCustodyIssue.Changed, captured.Issue);
        Assert.Equal(WindowsStableCustodyIssue.Changed, tree.RollbackPromotionAndCleanup());
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(finalPath, "nested", "payload.bin")));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(releasedOriginal));
    }


}
