// Copyright (c) 2026 Dennis Liu. All rights reserved.

/* TryAcquireWindows contract (S = retained handles; A = retained ancestors).
 * Open order: volume, ancestor components, root, breadth-first/ordinal children.
 * Release S/A in reverse open order. Close an unretained failed handle first.
 * The order is read from the code. The tests assert issue kinds and that every handle is
 * eventually released (see AssertReleasedFixture), not the order of the releases.
 * Row | Stage / exit or throw                         | Result / cleanup
 * 01  | Entry token check                             | OCE; empty S
 * 02  | Invalid parent/volume/root guard              | InvalidPath; empty S
 * 03  | Ancestor native open fails                    | mapped issue; failed handle, A
 *     | (a None issue with no parent handle also returns Failure(None); not reachable)
 * 04  | Ancestor type/reparse rejection               | ReparsePoint; rejected handle, A
 * 05  | BeforeRootOpen hook                           | faults map/propagate; A
 * 06  | AfterMissingRootObservation hook              | first root already closed; A
 * 07  | Missing confirmation succeeds                | Changed; confirmed root, A
 * 08  | Missing confirmation remains missing         | Unavailable; root, A; file marks absence
 * 09  | Root/confirmation nonmissing native failure   | mapped issue; root, A
 * 10  | Root expected-type check fails               | ReparsePoint; root, A
 * 11  | Single-file normalization/inventory throws    | ArgumentException -> Unavailable; S
 * 12  | Tree limits absent                           | Unavailable; S (wrappers supply limits)
 * 13  | Walk queue/child token checks                | OCE; partial S
 * 14  | Checked enumeration allowance overflows      | OverflowException; partial S
 * 15  | First enumeration exceeds allowance          | Unavailable; partial S
 * 16  | Enumeration/attributes throw                 | access -> AccessDenied; IO -> Unavailable; S
 * 17  | Observed child is reparse                     | ReparsePoint; partial S
 * 18  | Child native open fails                      | mapped issue; failed child, partial S
 * 19  | Child expected-type check fails              | ReparsePoint; rejected child, partial S
 * 20  | Directory count exceeds limit                | Unavailable; S includes excess directory
 * 21  | File count exceeds limit                     | Unavailable; S includes excess file
 * 22  | Length/byte budget fails or length throws    | Unavailable (IO catch); S includes file
 * 23  | Normalized file inventory collides           | Changed; partial S
 * 24  | Second enumeration changes/fails             | Changed; partial S
 * 25  | AfterTreeCaptured hook                       | faults map/propagate; complete S
 * 26  | Final token check                            | OCE; complete S
 * 27  | Identity reopen/type/identity mismatch        | Changed; probe closes before S
 * 28  | Success                                      | None; transfer S to custody, no release
 * 29  | OCE catch                                    | reverse S, rethrow, finally repeats Dispose
 * 30  | UnauthorizedAccessException catch            | AccessDenied; reverse S, finally repeats
 * 31  | IO/NotSupported/ArgumentException catch       | Unavailable; reverse S, finally repeats
 * 32  | Other hook/allocation/marshalling faults      | propagate; finally reverse S
 * 33  | Dispose / clone disposal                     | independent owners; Dispose is idempotent
 * Repeated SafeFileHandle.Dispose calls are not repeated native closes. The
 * seam exposes no close counter/order or per-child observation hook.
 */

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nvt.Core.Files.Windows;
using Nvt.Core.TestSupport;
using Xunit;
using SharedTestWorkspace = Nvt.Core.TestSupport.TestWorkspace;

namespace Nvt.Core.Tests.Files.Windows;

/// <summary>Pins acquisition exits through the existing stage hook and filesystem behavior.</summary>
public sealed partial class WindowsStablePathCustodyAcquisitionContractTests
{
    private static readonly WindowsStableTreeLimits _limits = new(8, 8, 32);

    /// <summary>A canceled entry token wins before any native handle opens.</summary>
    [Theory]
    [InlineData("file")]
    [InlineData("immutable")]
    [InlineData("writable")]
    [InlineData("promotable")]
    public void PreCanceledAcquisitionDoesNotReachRootHook(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = PrepareRoot(workspace, kind);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var stages = new List<WindowsStableCustodyStage>();

        var error = Assert.ThrowsAny<OperationCanceledException>(() =>
            Acquire(kind, root, cancellation.Token, stages.Add));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(stages);
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>Canceling the token alone at the root hook eventually releases every acquisition.</summary>
    [Theory]
    [InlineData("file")]
    [InlineData("immutable")]
    [InlineData("writable")]
    [InlineData("promotable")]
    public void TokenCancellationBeforeRootOpenReleasesCustody(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = PrepareRoot(workspace, kind);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bool reached = false;

        var error = Assert.ThrowsAny<OperationCanceledException>(() => Acquire(
            kind, root, cancellation.Token, AtStage(WindowsStableCustodyStage.BeforeRootOpen, () =>
            {
                reached = true;
                cancellation.Cancel();
            })));

        Assert.True(reached);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>Missing-root confirmation returns its issue without checking a newly canceled token.</summary>
    [Theory]
    [InlineData("file", true)]
    [InlineData("immutable", false)]
    [InlineData("writable", false)]
    [InlineData("promotable", false)]
    public void CancellationAfterMissingObservationKeepsMissingResult(string kind, bool exactChildMissing)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        _ = Directory.CreateDirectory(workspace.GetPath("case"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bool reached = false;

        WindowsStableCustodyResult acquired = Acquire(
            kind, workspace.GetPath("case/missing"), cancellation.Token,
            AtStage(WindowsStableCustodyStage.AfterMissingRootObservation, () =>
            {
                reached = true;
                cancellation.Cancel();
            }));
        using (acquired.Custody)
        {
            Assert.True(reached);
            AssertFailure(acquired, WindowsStableCustodyIssue.Unavailable);
            Assert.Equal(exactChildMissing, acquired.IsExactChildMissing);
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>A root appearing during canceled missing-root confirmation still returns Changed.</summary>
    [Theory]
    [InlineData("file")]
    [InlineData("immutable")]
    [InlineData("writable")]
    [InlineData("promotable")]
    public void CancellationAfterMissingObservationKeepsChangedResult(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        _ = Directory.CreateDirectory(workspace.GetPath("case"));
        string root = workspace.GetPath("case/missing");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        WindowsStableCustodyResult acquired = Acquire(kind, root, cancellation.Token,
            AtStage(WindowsStableCustodyStage.AfterMissingRootObservation, () =>
            {
                CreateRootEntry(root, kind);
                cancellation.Cancel();
            }));
        using (acquired.Custody)
        {
            Assert.True(cancellation.IsCancellationRequested);
            AssertFailure(acquired, WindowsStableCustodyIssue.Changed);
            Assert.False(acquired.IsExactChildMissing);
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>The final cancellation check covers the modes missing from immutable-tree tests.</summary>
    [Theory]
    [InlineData("file")]
    [InlineData("writable")]
    [InlineData("promotable")]
    public void CancellationAfterCaptureReleasesEveryHeldEntry(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string root = PrepareRoot(workspace, kind);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bool reached = false;

        var error = Assert.ThrowsAny<OperationCanceledException>(() => Acquire(
            kind, root, cancellation.Token, AtStage(WindowsStableCustodyStage.AfterTreeCaptured, () =>
            {
                reached = true;
                cancellation.Cancel();
            })));

        Assert.True(reached);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    /// <summary>The catches preserve typed issues for faults before, during absence, and after capture.</summary>
    [Theory]
    [InlineData("before", "access")]
    [InlineData("before", "io")]
    [InlineData("before", "unsupported")]
    [InlineData("before", "argument")]
    [InlineData("missing", "access")]
    [InlineData("missing", "io")]
    [InlineData("missing", "unsupported")]
    [InlineData("missing", "argument")]
    [InlineData("captured", "access")]
    [InlineData("captured", "io")]
    [InlineData("captured", "unsupported")]
    [InlineData("captured", "argument")]
    public void CaughtStageFaultReturnsIssueAndReleasesHandles(string stage, string fault)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native custody is required.");
        using var workspace = SharedTestWorkspace.Create();
        string populatedRoot = PrepareRoot(workspace, "immutable");
        string root = stage == "missing" ? workspace.GetPath("case/missing") : populatedRoot;
        WindowsStableCustodyStage target = stage switch
        {
            "before" => WindowsStableCustodyStage.BeforeRootOpen,
            "missing" => WindowsStableCustodyStage.AfterMissingRootObservation,
            _ => WindowsStableCustodyStage.AfterTreeCaptured,
        };
        bool reached = false;

        WindowsStableCustodyResult acquired = Acquire("immutable", root,
            TestContext.Current.CancellationToken, AtStage(target, () =>
            {
                reached = true;
                throw CreateFault(fault);
            }));
        using (acquired.Custody)
        {
            Assert.True(reached);
            AssertFailure(acquired, fault == "access"
                ? WindowsStableCustodyIssue.AccessDenied : WindowsStableCustodyIssue.Unavailable);
            Assert.False(acquired.IsExactChildMissing);
        }
        AssertReleasedFixture(workspace.GetPath("case"));
    }

    private static WindowsStableCustodyResult Acquire(
        string kind, string root, CancellationToken token, Action<WindowsStableCustodyStage>? hook = null)
    {
        return kind switch
        {
            "file" => WindowsStablePathCustody.TryAcquireFile(root, hook, token),
            "immutable" => WindowsStablePathCustody.TryAcquireImmutableTree(root, _limits, token, hook),
            "writable" => WindowsStablePathCustody.TryAcquireWritableParent(root, hook, token),
            "promotable" => WindowsStablePathCustody.TryAcquirePromotableTree(root, _limits, token, hook),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static Action<WindowsStableCustodyStage> AtStage(WindowsStableCustodyStage target, Action action)
    {
        return stage =>
        {
            if (stage == target)
            {
                action();
            }
        };
    }

    private static Exception CreateFault(string fault)
    {
        return fault switch
        {
            "access" => new UnauthorizedAccessException("Synthetic acquisition access failure."),
            "io" => new IOException("Synthetic acquisition IO failure."),
            "unsupported" => new NotSupportedException("Synthetic acquisition operation failure."),
            "argument" => new ArgumentException("Synthetic acquisition argument failure."),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
    }

    private static string PrepareRoot(SharedTestWorkspace workspace, string kind)
    {
        string root = workspace.GetPath("case/tree");
        _ = Directory.CreateDirectory(Path.Combine(root, "nested"));
        TestFiles.WriteAllBytes(Path.Combine(root, "a.bin"), [1]);
        TestFiles.WriteAllBytes(Path.Combine(root, "nested", "z.bin"), [2, 3]);
        return kind == "file" ? Path.Combine(root, "a.bin") : root;
    }

    private static void CreateRootEntry(string root, string kind)
    {
        if (kind == "file")
        {
            TestFiles.WriteAllBytes(root, [1]);
        }
        else
        {
            _ = Directory.CreateDirectory(root);
        }
    }

    private static SafeFileHandle OpenProbe(string path, bool directory, uint shareMode = 0)
    {
        SafeFileHandle handle = WindowsStablePathCustody.NativeMethods.CreateFile(
            path,
            (directory ? WindowsStablePathCustody.NativeMethods.ListDirectory
                : WindowsStablePathCustody.NativeMethods.GenericRead) |
                WindowsStablePathCustody.NativeMethods.ReadAttributes | WindowsStablePathCustody.NativeMethods.Synchronize,
            shareMode, 0, WindowsStablePathCustody.NativeMethods.OpenExisting,
            WindowsStablePathCustody.NativeMethods.OpenReparsePoint |
                (directory ? WindowsStablePathCustody.NativeMethods.BackupSemantics : 0),
            0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException($"Could not open the synthetic fixture probe (Win32 error {error}).");
        }
        return handle;
    }

    private static void AssertFailure(WindowsStableCustodyResult acquired, WindowsStableCustodyIssue expected)
    {
        Assert.Null(acquired.Custody);
        Assert.False(acquired.IsAcquired);
        Assert.Equal(expected, acquired.Issue);
    }

    private static void AssertReleasedFixture(string fixture)
    {
        // Ancestors are held without delete sharing, so renaming the workspace directory proves that the ancestor
        // handles up to that directory were released. Directories above it are shared with other tests.
        string workspaceDirectory = Path.GetDirectoryName(fixture)!;
        string renamedWorkspace = workspaceDirectory + ".released-workspace";
        try
        {
            Directory.Move(workspaceDirectory, renamedWorkspace);
        }
        finally
        {
            if (Directory.Exists(renamedWorkspace))
            {
                Directory.Move(renamedWorkspace, workspaceDirectory);
            }
        }
        string released = fixture + ".released";
        Directory.Move(fixture, released);
        using (SafeFileHandle probe = OpenProbe(released, directory: true))
        {
            Assert.True(WindowsStablePathCustody.TryGetIdentity(probe, out _));
        }
        foreach (string directory in Directory.GetDirectories(released, "*", SearchOption.AllDirectories))
        {
            using SafeFileHandle probe = OpenProbe(directory, directory: true);
            Assert.True(WindowsStablePathCustody.TryGetIdentity(probe, out _));
        }
        foreach (string file in Directory.GetFiles(released, "*", SearchOption.AllDirectories))
        {
            using var probe = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(probe.CanWrite);
        }
    }
}
