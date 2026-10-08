// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Microsoft.Win32.SafeHandles;
using Nvt.Core.Files.Windows;
using Nvt.Core.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.Files.Windows;

/// <summary>Verifies phase guards, unchanged namespaces and ownership through disposal.</summary>
public sealed class WindowsStableRelativeWriteTreeLifecycleTests
{
    private static readonly byte[] Payload = [1, 2, 3, 4];

    /// <summary>Preparation rejects further writes before path admission or native creation.</summary>
    [Fact]
    public void CreateFileAfterPrepareIsRejectedWithoutCreation()
    {
        using var fixture = new LifecycleFixture();
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.PrepareForPromotion());

        fixture.AssertRejected("Prepared", nameof(WindowsStableRelativeWriteTree.CreateFile), () =>
        {
            using FileStream unexpected = fixture.Tree.CreateFile("unexpected/payload.bin");
        });
    }

    /// <summary>Cleanup rejects further writes and preserves a replacement at the old staging name.</summary>
    [Fact]
    public void CreateFileAfterCleanupIsRejectedWithoutCreation()
    {
        using var fixture = new LifecycleFixture();
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.Cleanup());
        fixture.CreateForeignStagingReplacement();

        fixture.AssertRejected("CleanedUp", nameof(WindowsStableRelativeWriteTree.CreateFile), () =>
        {
            using FileStream unexpected = fixture.Tree.CreateFile("unexpected/payload.bin");
        });
    }

    /// <summary>A second preparation stops before reading released handles or invoking its hook.</summary>
    [Fact]
    public void PrepareAfterPrepareIsRejectedWithoutCreationOrRename()
    {
        using var fixture = new LifecycleFixture();
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.PrepareForPromotion());

        fixture.AssertRejected("Prepared", nameof(WindowsStableRelativeWriteTree.PrepareForPromotion), () =>
            fixture.Tree.PrepareForPromotion(_ => Assert.Fail("The preparation hook must not run.")));
    }

    /// <summary>Cleanup of a prepared tree prevents rename and preserves a reused staging name.</summary>
    [Fact]
    public void PromoteAfterPreparedCleanupIsRejectedWithoutRename()
    {
        using var fixture = new LifecycleFixture();
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.PrepareForPromotion());
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.Cleanup());
        fixture.CreateForeignStagingReplacement();

        fixture.AssertRejected("CleanedUp", nameof(WindowsStableRelativeWriteTree.Promote), () =>
            fixture.Tree.Promote());
        Assert.False(Directory.Exists(fixture.FinalPath));
    }

    /// <summary>Successful promotion cannot replay the native rename.</summary>
    [Fact]
    public void PromoteAfterPromoteIsRejectedWithoutRename()
    {
        using var fixture = new LifecycleFixture();
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.PrepareForPromotion());
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.Promote());
        fixture.CreateForeignStagingReplacement();

        fixture.AssertRejected("Promoted", nameof(WindowsStableRelativeWriteTree.Promote), () =>
            fixture.Tree.Promote());
        AssertPayload(fixture.FinalPath);
    }

    /// <summary>The ordinary write, prepare, promote, capture and dispose sequence preserves its results.</summary>
    [Fact]
    public void ValidSequenceKeepsResultsAndDisposePreservesPromotedFiles()
    {
        using var fixture = new LifecycleFixture(writeFiles: false);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.StagingPath));
        fixture.WriteFiles();
        AssertPayload(fixture.StagingPath);
        Assert.Equal(2, fixture.AdmittedPaths.Count);
        Assert.Single(fixture.CreatedDirectories);

        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.PrepareForPromotion());
        Assert.True(Directory.Exists(fixture.StagingPath));
        Assert.False(Directory.Exists(fixture.FinalPath));
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.Promote());
        Assert.False(Directory.Exists(fixture.StagingPath));
        AssertPayload(fixture.FinalPath);

        WindowsStableCustodyResult captured = fixture.Tree.CapturePromotedImmutableTree(
            fixture.FinalPath, TestContext.Current.CancellationToken);
        Assert.Equal(WindowsStableCustodyIssue.None, captured.Issue);
        Assert.True(captured.IsAcquired);
        captured.Custody!.Dispose();

        fixture.Tree.Dispose();
        AssertPayload(fixture.FinalPath);
        // Ordinary writes are possible once all promotion custody has been released.
        File.WriteAllBytes(Path.Combine(fixture.FinalPath, "nested", "first.bin"), Payload);
    }

    /// <summary>Cleanup caches its result in every owning phase and disposal releases the parents.</summary>
    [Theory]
    [InlineData("Writing")]
    [InlineData("Prepared")]
    [InlineData("Promoted")]
    public void CleanupThenDisposeIsIdempotent(string phase)
    {
        using var fixture = new LifecycleFixture();
        fixture.EnterPhase(phase);
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.Cleanup());
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.Cleanup());
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.RollbackPromotionAndCleanup());
        Assert.False(Directory.Exists(fixture.StagingPath));
        Assert.False(Directory.Exists(fixture.FinalPath));

        fixture.Tree.Dispose();
        Directory.Delete(Path.Combine(fixture.ManagedRoot, ".staging"));
        Directory.Delete(Path.Combine(fixture.ManagedRoot, "versions"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.ManagedRoot));
    }

    /// <summary>Disposal is idempotent from every live phase, including a successfully promoted tree.</summary>
    [Theory]
    [InlineData("Writing")]
    [InlineData("Prepared")]
    [InlineData("Promoted")]
    [InlineData("CleanedUp")]
    public void DisposeTwiceDoesNotRepeatCleanupOrChangeTheNamespace(string phase)
    {
        using var fixture = new LifecycleFixture();
        fixture.EnterPhase(phase);
        fixture.Tree.Dispose();
        fixture.CreateForeignStagingReplacement();
        string[] before = fixture.SnapshotPaths();

        fixture.Tree.Dispose();

        Assert.Equal(before, fixture.SnapshotPaths());
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(fixture.StagingPath, "foreign.txt")));
        if (phase == "Promoted")
        {
            AssertPayload(fixture.FinalPath);
        }
        else
        {
            Assert.False(Directory.Exists(fixture.FinalPath));
        }
    }

    /// <summary>All other out-of-order operations report their phase before callbacks or native work.</summary>
    [Theory]
    [InlineData("Promoted", nameof(WindowsStableRelativeWriteTree.CreateFile))]
    [InlineData("Promoted", nameof(WindowsStableRelativeWriteTree.PrepareForPromotion))]
    [InlineData("CleanedUp", nameof(WindowsStableRelativeWriteTree.PrepareForPromotion))]
    [InlineData("Writing", nameof(WindowsStableRelativeWriteTree.Promote))]
    [InlineData("Writing", nameof(WindowsStableRelativeWriteTree.CapturePromotedImmutableTree))]
    [InlineData("Prepared", nameof(WindowsStableRelativeWriteTree.CapturePromotedImmutableTree))]
    [InlineData("CleanedUp", nameof(WindowsStableRelativeWriteTree.CapturePromotedImmutableTree))]
    public void OtherWrongPhasesAreRejectedBeforeWork(string phase, string operation)
    {
        using var fixture = new LifecycleFixture();
        fixture.EnterPhase(phase);

        fixture.AssertRejected(phase, operation, () => Attempt(fixture.Tree, operation));
    }

    /// <summary>Disposal takes precedence over all arguments and phase errors, including cleanup and paths.</summary>
    [Theory]
    [InlineData(nameof(WindowsStableRelativeWriteTree.StagingPath))]
    [InlineData(nameof(WindowsStableRelativeWriteTree.CreateFile))]
    [InlineData(nameof(WindowsStableRelativeWriteTree.PrepareForPromotion))]
    [InlineData(nameof(WindowsStableRelativeWriteTree.Promote))]
    [InlineData(nameof(WindowsStableRelativeWriteTree.CapturePromotedImmutableTree))]
    [InlineData(nameof(WindowsStableRelativeWriteTree.Cleanup))]
    [InlineData(nameof(WindowsStableRelativeWriteTree.RollbackPromotionAndCleanup))]
    public void EveryMemberExceptDisposeThrowsAfterDisposal(string operation)
    {
        using var fixture = new LifecycleFixture();
        fixture.Tree.Dispose();
        string[] before = fixture.SnapshotPaths();

        var error = Assert.Throws<ObjectDisposedException>(() => Attempt(fixture.Tree, operation));

        Assert.Equal(typeof(WindowsStableRelativeWriteTree).FullName, error.ObjectName);
        Assert.Equal(before, fixture.SnapshotPaths());
    }

    /// <summary>An unsuccessful preparation retains Writing and can be completed and retried.</summary>
    [Fact]
    public void FailedPreparationRemainsWriting()
    {
        using var fixture = new LifecycleFixture(writeFiles: false);
        Assert.Equal(WindowsStableCustodyIssue.Changed, fixture.Tree.PrepareForPromotion());
        fixture.AssertRejected("Writing", nameof(WindowsStableRelativeWriteTree.Promote), () =>
            fixture.Tree.Promote());

        fixture.WriteFiles();

        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.PrepareForPromotion());
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.Promote());
    }

    /// <summary>A failed no-replace rename retains Prepared and allows a later promotion attempt.</summary>
    [Fact]
    public void FailedPromotionRemainsPrepared()
    {
        using var fixture = new LifecycleFixture();
        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.PrepareForPromotion());
        Directory.CreateDirectory(fixture.FinalPath);
        Assert.Equal(WindowsStableCustodyIssue.Changed, fixture.Tree.Promote());
        fixture.AssertRejected("Prepared", nameof(WindowsStableRelativeWriteTree.CreateFile), () =>
        {
            using FileStream unexpected = fixture.Tree.CreateFile("unexpected/payload.bin");
        });

        Directory.Delete(fixture.FinalPath);

        Assert.Equal(WindowsStableCustodyIssue.None, fixture.Tree.Promote());
        AssertPayload(fixture.FinalPath);
    }

    /// <summary>Even an incomplete cleanup caches Changed and disposal preserves foreign residue.</summary>
    [Theory]
    [InlineData("Writing")]
    [InlineData("Prepared")]
    [InlineData("Promoted")]
    public void CleanupRetainsChangedResultAndDisposePreservesForeignResidue(string phase)
    {
        using var fixture = new LifecycleFixture();
        fixture.EnterPhase(phase);
        string ownedPath = phase == "Promoted" ? fixture.FinalPath : fixture.StagingPath;
        string foreign = Path.Combine(ownedPath, "foreign.txt");
        File.WriteAllText(foreign, "foreign");
        Assert.Equal(WindowsStableCustodyIssue.Changed, fixture.Tree.Cleanup());
        Assert.Equal(WindowsStableCustodyIssue.Changed, fixture.Tree.Cleanup());
        Assert.Equal(WindowsStableCustodyIssue.Changed, fixture.Tree.RollbackPromotionAndCleanup());

        fixture.Tree.Dispose();

        Assert.Equal("foreign", File.ReadAllText(foreign));
    }

    private static void AssertPayload(string root)
    {
        Assert.Equal(Payload, ReadSharedBytes(Path.Combine(root, "nested", "first.bin")));
        Assert.Equal(Payload, ReadSharedBytes(Path.Combine(root, "nested", "second.bin")));
    }

    private static byte[] ReadSharedBytes(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        file.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void Attempt(WindowsStableRelativeWriteTree tree, string operation)
    {
        switch (operation)
        {
            case nameof(WindowsStableRelativeWriteTree.StagingPath):
                _ = tree.StagingPath;
                break;
            case nameof(WindowsStableRelativeWriteTree.CreateFile):
                using (tree.CreateFile(null!)) { }
                break;
            case nameof(WindowsStableRelativeWriteTree.PrepareForPromotion):
                _ = tree.PrepareForPromotion(_ => Assert.Fail("The preparation hook must not run."));
                break;
            case nameof(WindowsStableRelativeWriteTree.Promote):
                _ = tree.Promote();
                break;
            case nameof(WindowsStableRelativeWriteTree.CapturePromotedImmutableTree):
                _ = tree.CapturePromotedImmutableTree(null!, new CancellationToken(canceled: true));
                break;
            case nameof(WindowsStableRelativeWriteTree.Cleanup):
                _ = tree.Cleanup();
                break;
            case nameof(WindowsStableRelativeWriteTree.RollbackPromotionAndCleanup):
                _ = tree.RollbackPromotionAndCleanup();
                break;
            default:
                Assert.Fail($"Unknown operation: {operation}.");
                break;
        }
    }

    private static WindowsStableRelativeWriteRoot AcquireHeldRoot(string path)
    {
        // Lifecycle cases start at their own directory; ancestor acquisition has separate coverage.
        using SafeFileHandle held = WindowsStablePathCustody.NativeMethods.CreateFile(
            path,
            WindowsStablePathCustody.NativeMethods.ListDirectory |
                WindowsStablePathCustody.NativeMethods.AddFile |
                WindowsStablePathCustody.NativeMethods.AddSubdirectory |
                WindowsStablePathCustody.NativeMethods.ReadAttributes |
                WindowsStablePathCustody.NativeMethods.Synchronize,
            WindowsStablePathCustody.NativeMethods.ShareRead | WindowsStablePathCustody.NativeMethods.ShareWrite,
            0,
            WindowsStablePathCustody.NativeMethods.OpenExisting,
            WindowsStablePathCustody.NativeMethods.BackupSemantics | WindowsStablePathCustody.NativeMethods.OpenReparsePoint,
            0);
        Assert.False(held.IsInvalid);
        return WindowsStableRelativeWriteRoot.FromHeldDirectory(path, held);
    }

    private sealed class LifecycleFixture : IDisposable
    {
        private readonly TestWorkspace _workspace;
        private readonly WindowsStableRelativeWriteRoot _root;

        internal LifecycleFixture(bool writeFiles = true)
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
            _workspace = new TestWorkspace();
            ManagedRoot = Directory.CreateDirectory(_workspace.PathFor("managed")).FullName;
            _root = AcquireHeldRoot(ManagedRoot);
            Assert.Equal(WindowsStableCustodyIssue.None, _root.TryCreateVersionTree(
                "1.0.0", ".staging", "versions", new(2, 1, 8, new(3, 1, 32)), 512,
                path =>
                {
                    AdmittedPaths.Add(path);
                    return ContractValidation.IsSafeRelativePath(path);
                },
                CreatedDirectories.Add, out var tree));
            Tree = tree!;
            StagingPath = Tree.StagingPath;
            FinalPath = Path.Combine(ManagedRoot, "versions", "1.0.0");
            if (writeFiles)
            {
                WriteFiles();
            }
        }

        internal WindowsStableRelativeWriteTree Tree { get; }
        internal string ManagedRoot { get; }
        internal string StagingPath { get; }
        internal string FinalPath { get; }
        internal List<string> AdmittedPaths { get; } = [];
        internal List<string> CreatedDirectories { get; } = [];

        internal void WriteFiles()
        {
            using (FileStream file = Tree.CreateFile("nested/first.bin"))
            {
                file.Write(Payload);
            }
            using (FileStream file = Tree.CreateFile("nested/second.bin"))
            {
                file.Write(Payload);
            }
        }

        internal void EnterPhase(string phase)
        {
            if (phase is "Prepared" or "Promoted")
            {
                Assert.Equal(WindowsStableCustodyIssue.None, Tree.PrepareForPromotion());
            }
            if (phase == "Promoted")
            {
                Assert.Equal(WindowsStableCustodyIssue.None, Tree.Promote());
            }
            if (phase == "CleanedUp")
            {
                Assert.Equal(WindowsStableCustodyIssue.None, Tree.Cleanup());
            }
        }

        internal void CreateForeignStagingReplacement()
        {
            Directory.CreateDirectory(StagingPath);
            File.WriteAllText(Path.Combine(StagingPath, "foreign.txt"), "foreign");
        }

        internal string[] SnapshotPaths() => Directory
            .EnumerateFileSystemEntries(ManagedRoot, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();

        internal void AssertRejected(string phase, string operation, Action attempt)
        {
            string[] paths = SnapshotPaths();
            var files = paths.Where(File.Exists).ToDictionary(path => path, ReadSharedBytes);
            int admissions = AdmittedPaths.Count;
            int creations = CreatedDirectories.Count;

            var error = Assert.Throws<InvalidOperationException>(attempt);

            Assert.Equal($"Cannot {operation} while the write tree is in the {phase} phase.", error.Message);
            Assert.Equal(admissions, AdmittedPaths.Count);
            Assert.Equal(creations, CreatedDirectories.Count);
            Assert.Equal(paths, SnapshotPaths());
            foreach ((string path, byte[] bytes) in files)
            {
                Assert.Equal(bytes, ReadSharedBytes(path));
            }
        }

        public void Dispose()
        {
            Tree.Dispose();
            _root.Dispose();
            _workspace.Dispose();
        }
    }
}
