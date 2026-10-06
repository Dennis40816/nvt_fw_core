// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Files;
using Xunit;

namespace Nvt.Core.Tests.Files;

public sealed partial class RootedPathGuardTests
{
    /// <summary>A missing root is created and normalized with exactly one trailing separator.</summary>
    [Fact]
    public void ResolveRootCreatesMissingDirectory()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.PathFor("new/nested");
        string expected = Path.GetFullPath(path) + Path.DirectorySeparatorChar;

        Assert.Equal(expected, RootedPathGuard.ResolveRoot(path));
        Assert.True(Directory.Exists(path));
        Assert.Equal(expected, RootedPathGuard.ResolveRoot(expected));
        Assert.Equal(expected, RootedPathGuard.ResolveExistingRoot(path));
    }

    /// <summary>Existing-root resolution rejects a missing root without creating it.</summary>
    [Fact]
    public void ResolveExistingRootRejectsMissingDirectory()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.PathFor("missing");

        DirectoryNotFoundException exception = Assert.Throws<DirectoryNotFoundException>(() =>
            RootedPathGuard.ResolveExistingRoot(path));

        Assert.Equal($"Allowed root was not found: {path}", exception.Message);
        Assert.False(Directory.Exists(path));
    }

    /// <summary>Outside paths and a sibling sharing the root name prefix remain outside.</summary>
    [Theory]
    [InlineData("outside")]
    [InlineData("root2")]
    public void ResolveExistingFileUnderRootsRejectsOutsidePath(string sibling)
    {
        using var workspace = new TestWorkspace();
        string root = RootedPathGuard.ResolveRoot(workspace.PathFor("root"));
        string path = workspace.Write($"{sibling}/input.bin", [1]);

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            RootedPathGuard.ResolveExistingFileUnderRoots(path, [root]));

        Assert.Equal("Path is outside the configured root.", exception.Message);
        Assert.False(RootedPathGuard.IsUnderRoot(path, root));
    }

    /// <summary>A file under the second allowed root is accepted after canonical normalization.</summary>
    [Fact]
    public void ResolveExistingFileUnderRootsAcceptsSecondRoot()
    {
        using var workspace = new TestWorkspace();
        string first = RootedPathGuard.ResolveRoot(workspace.PathFor("first"));
        string second = RootedPathGuard.ResolveRoot(workspace.PathFor("second"));
        string path = workspace.Write("second/input.bin", [1]);

        Assert.Equal(path, RootedPathGuard.ResolveExistingFileUnderRoots(path, [first, second]));
        Assert.True(RootedPathGuard.IsUnderRoot(path, second.TrimEnd(Path.DirectorySeparatorChar)));
        Assert.False(RootedPathGuard.IsUnderRoot(second.TrimEnd(Path.DirectorySeparatorChar), second));
        Assert.True(RootedPathGuard.IsUnderRoot(workspace.PathFor("second/../second/input.bin"), second));
    }

    /// <summary>A new target is accepted only when its parent already exists.</summary>
    [Fact]
    public void ResolveFileUnderRootsAcceptsNewFileWithExistingParent()
    {
        using var workspace = new TestWorkspace();
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);
        string path = workspace.PathFor("new.json");

        Assert.Equal(path, RootedPathGuard.ResolveFileUnderRoots(path, [root], mustExist: false));
        Assert.False(File.Exists(path));
    }

    /// <summary>A missing parent, directory target, and required missing file retain their exception order.</summary>
    [Fact]
    public void ResolveFileUnderRootsRejectsUnavailableTargets()
    {
        using var workspace = new TestWorkspace();
        string root = RootedPathGuard.ResolveExistingRoot(workspace.Root);
        string parent = workspace.PathFor("missing");
        string missing = workspace.PathFor("missing/input.bin");
        string directory = Directory.CreateDirectory(workspace.PathFor("directory")).FullName;

        DirectoryNotFoundException parentException = Assert.Throws<DirectoryNotFoundException>(() =>
            RootedPathGuard.ResolveFileUnderRoots(missing, [root], mustExist: false));
        Assert.Equal($"Document target directory was not found: {parent}", parentException.Message);
        IOException directoryException = Assert.Throws<IOException>(() =>
            RootedPathGuard.ResolveFileUnderRoots(directory, [root], mustExist: false));
        Assert.Equal("A document target cannot be an existing directory.", directoryException.Message);
        foreach (string target in new[] { missing, directory })
        {
            FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() =>
                RootedPathGuard.ResolveFileUnderRoots(target, [root], mustExist: true));
            Assert.Equal("Artifact file was not found.", exception.Message);
            Assert.Equal(target, exception.FileName);
        }
    }

    /// <summary>Root-list validation precedes existence checks and path validation precedes root-list validation.</summary>
    [Fact]
    public void ResolveFileUnderRootsValidatesArgumentsInFrozenOrder()
    {
        using var workspace = new TestWorkspace();
        string missing = workspace.PathFor("missing.bin");

        InvalidOperationException empty = Assert.Throws<InvalidOperationException>(() =>
            RootedPathGuard.ResolveFileUnderRoots(missing, [], mustExist: true));
        Assert.Equal("At least one allowed root is required.", empty.Message);
        ArgumentNullException path = Assert.Throws<ArgumentNullException>(() =>
            RootedPathGuard.ResolveFileUnderRoots(null!, null!, mustExist: true));
        Assert.Equal("path", path.ParamName);
        ArgumentNullException roots = Assert.Throws<ArgumentNullException>(() =>
            RootedPathGuard.ResolveFileUnderRoots(missing, null!, mustExist: true));
        Assert.Equal("allowedRoots", roots.ParamName);
    }

    /// <summary>Root arguments reject null, empty, and whitespace strings before filesystem work.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ResolveRootsRejectBlankArguments(string? rootDirectory)
    {
        ArgumentException created = Assert.ThrowsAny<ArgumentException>(() => RootedPathGuard.ResolveRoot(rootDirectory!));
        ArgumentException existing = Assert.ThrowsAny<ArgumentException>(() => RootedPathGuard.ResolveExistingRoot(rootDirectory!));
        Assert.Equal("rootDirectory", created.ParamName);
        Assert.Equal("rootDirectory", existing.ParamName);
    }

    /// <summary>A plain filename does not require an existing root or target.</summary>
    [Fact]
    public void ResolveFileNameUnderRootAcceptsPlainFileName()
    {
        using var workspace = new TestWorkspace();
        string root = workspace.PathFor("missing");

        Assert.Equal(Path.Combine(root, "a.json"), RootedPathGuard.ResolveFileNameUnderRoot("a.json", root));
        Assert.False(Directory.Exists(root));
    }

    /// <summary>Every original plain-filename path-syntax rejection retains its message.</summary>
    [Theory]
    [InlineData("a/b.json")]
    [InlineData("a\\b.json")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("c:x.json")]
    public void ResolveFileNameUnderRootRejectsPathSyntax(string fileName)
    {
        using var workspace = new TestWorkspace();
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            RootedPathGuard.ResolveFileNameUnderRoot(fileName, workspace.Root));
        Assert.Equal("fileName", exception.ParamName);
        Assert.StartsWith("File name must be a plain filename without path syntax.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Null, empty, and whitespace filenames or relative paths fail before root validation.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ResolveFilePathsRejectBlankArguments(string? value)
    {
        ArgumentException file = Assert.ThrowsAny<ArgumentException>(() =>
            RootedPathGuard.ResolveFileNameUnderRoot(value!, null!));
        ArgumentException relative = Assert.ThrowsAny<ArgumentException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot(value!, null!));
        ArgumentException path = Assert.ThrowsAny<ArgumentException>(() =>
            RootedPathGuard.ResolveFileUnderRoots(value!, null!, mustExist: false));
        Assert.Equal("fileName", file.ParamName);
        Assert.Equal("relativePath", relative.ParamName);
        Assert.Equal("path", path.ParamName);
    }

    /// <summary>Additional relative syntax edges retain the renamed argument and approved messages.</summary>
    [Theory]
    [InlineData("a\0b", "File paths must be relative and use forward slashes.")]
    [InlineData("a/./b", "File paths cannot contain empty, current, or parent segments.")]
    [InlineData("a/", "File paths cannot contain empty, current, or parent segments.")]
    [InlineData(".", "File paths cannot contain empty, current, or parent segments.")]
    [InlineData("..", "File paths cannot contain empty, current, or parent segments.")]
    public void ResolveExistingRelativeFileUnderRootRejectsAdditionalSyntax(string relativePath, string message)
    {
        using var workspace = new TestWorkspace();
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot(relativePath, workspace.Root));
        Assert.Equal("relativePath", exception.ParamName);
        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Root argument validation precedes relative path syntax, which precedes root existence.</summary>
    [Fact]
    public void ResolveExistingRelativeFileUnderRootPreservesCheckOrderAndMissingMessage()
    {
        using var workspace = new TestWorkspace();
        ArgumentNullException root = Assert.Throws<ArgumentNullException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot("a\\b", null!));
        Assert.Equal("rootDirectory", root.ParamName);
        _ = Assert.Throws<ArgumentException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot("a\\b", workspace.PathFor("missing")));
        FileNotFoundException file = Assert.Throws<FileNotFoundException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot("missing.bin", workspace.Root));
        Assert.Equal("Relative file was not found.", file.Message);
        Assert.Equal(workspace.PathFor("missing.bin"), file.FileName);
    }

    /// <summary>File links, linked parents, and linked roots are explicitly rejected.</summary>
    [Theory]
    [InlineData("File")]
    [InlineData("Parent")]
    [InlineData("Root")]
    public void ResolvePathsRejectSymbolicLinks(string linkKind)
    {
        using var workspace = new TestWorkspace();
        string file = workspace.Write("target/input.bin", [1]);
        string link = workspace.PathFor("link");
        try
        {
            if (linkKind == "File")
            {
                _ = File.CreateSymbolicLink(link, file);
            }
            else
            {
                _ = Directory.CreateSymbolicLink(link, workspace.PathFor("target"));
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip("Symbolic links are not available in this environment.");
        }

        if (linkKind == "Root")
        {
            AssertReparseRejection(() => RootedPathGuard.ResolveExistingRoot(link));
            AssertReparseRejection(() => RootedPathGuard.ResolveRoot(Path.Combine(link, "new")));
        }
        else
        {
            string path = linkKind == "File" ? link : Path.Combine(link, "input.bin");
            AssertReparseRejection(() => RootedPathGuard.ResolveExistingFileUnderRoots(path, [workspace.Root]));
            string relative = linkKind == "File" ? "link" : "link/input.bin";
            AssertReparseRejection(() => RootedPathGuard.ResolveExistingRelativeFileUnderRoot(relative, workspace.Root));
            if (linkKind == "Parent")
            {
                AssertReparseRejection(() => RootedPathGuard.ResolveFileUnderRoots(
                    Path.Combine(link, "new.bin"), [workspace.Root], mustExist: false));
            }
        }
    }

    /// <summary>Windows admission folds path case using ordinal comparison.</summary>
    [Fact]
    public void ResolveExistingFileUnderRootsFoldsWindowsCase()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows case folding requires Windows execution.");
        using var workspace = new TestWorkspace();
        string path = workspace.Write("mixed/Input.bin", [1]);
        string folded = path.ToUpperInvariant();
        Assert.True(RootedPathGuard.IsUnderRoot(folded, workspace.Root.ToLowerInvariant()));
        Assert.Equal(folded, RootedPathGuard.ResolveExistingFileUnderRoots(folded, [workspace.Root]));
    }

    /// <summary>Non-Windows admission compares path case ordinally even without filesystem access.</summary>
    [Fact]
    public void IsUnderRootPreservesNonWindowsCase()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Ordinal path case sensitivity requires a non-Windows host.");
        using var workspace = new TestWorkspace();
        string root = workspace.PathFor("mixed");
        Assert.True(RootedPathGuard.IsUnderRoot(Path.Combine(root, "input.bin"), root));
        Assert.False(RootedPathGuard.IsUnderRoot(Path.Combine(root.ToUpperInvariant(), "input.bin"), root));
    }

    private static void AssertReparseRejection(Action action)
    {
        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(action);
        Assert.Equal("Reparse points are not allowed.", exception.Message);
    }
}
