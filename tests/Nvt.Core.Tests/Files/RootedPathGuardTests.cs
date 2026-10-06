// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using Nvt.Core.Files;
using Xunit;

namespace Nvt.Core.Tests.Files;

/// <summary>Tests relative path confinement in the shared filesystem guard.</summary>
public sealed partial class RootedPathGuardTests
{
    /// <summary>Verifies a canonical relative path resolves to one existing file under the root.</summary>
    [Fact]
    public void ResolveExistingRelativeFileUnderRootReturnsConfinedFile()
    {
        using var workspace = new TestWorkspace();
        string expected = workspace.Write("profiles/profile.json", Encoding.UTF8.GetBytes("{}"));

        string actual = RootedPathGuard.ResolveExistingRelativeFileUnderRoot(
            "profiles/profile.json",
            workspace.Root);

        Assert.Equal(Path.GetFullPath(expected), actual);
    }

    /// <summary>Verifies absolute, traversal, alternate-separator, ADS, and empty segments fail closed.</summary>
    [Theory]
    [InlineData("../outside.json")]
    [InlineData("profiles/../outside.json")]
    [InlineData("./profiles/profile.json")]
    [InlineData("profiles//profile.json")]
    [InlineData("profiles\\profile.json")]
    [InlineData("profiles/profile.json:stream")]
    [InlineData("/profiles/profile.json")]
    [InlineData("C:/profiles/profile.json")]
    public void ResolveExistingRelativeFileUnderRootRejectsPathSyntax(string relativePath)
    {
        using var workspace = new TestWorkspace();

        _ = Assert.Throws<ArgumentException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot(relativePath, workspace.Root));
    }

    /// <summary>Verifies missing files and directories cannot masquerade as relative files.</summary>
    [Fact]
    public void ResolveExistingRelativeFileUnderRootRequiresFile()
    {
        using var workspace = new TestWorkspace();
        _ = Directory.CreateDirectory(workspace.PathFor("profiles/directory.json"));

        _ = Assert.Throws<FileNotFoundException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot(
                "profiles/missing.json",
                workspace.Root));
        _ = Assert.Throws<FileNotFoundException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot(
                "profiles/directory.json",
                workspace.Root));
    }

    /// <summary>Verifies the selected directory root must already exist and cannot be a file.</summary>
    [Fact]
    public void ResolveExistingRelativeFileUnderRootRequiresDirectoryRoot()
    {
        using var workspace = new TestWorkspace();
        string fileRoot = workspace.Write("root.bin", []);

        _ = Assert.Throws<DirectoryNotFoundException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot(
                "profiles/profile.json",
                workspace.PathFor("missing")));
        _ = Assert.Throws<DirectoryNotFoundException>(() =>
            RootedPathGuard.ResolveExistingRelativeFileUnderRoot(
                "profiles/profile.json",
                fileRoot));
    }
}
