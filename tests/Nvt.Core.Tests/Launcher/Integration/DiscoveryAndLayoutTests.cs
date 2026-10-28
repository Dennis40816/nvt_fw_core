// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Frozen generation and managed-root resolution predicates.</summary>
public sealed class DiscoveryAndLayoutTests
{
    /// <summary>Obsolete completions cannot consume the current session's prompt.</summary>
    [Fact]
    public void ObsoleteGenerationCannotConsumePrompt()
    {
        var session = new VersionDiscoverySession();
        var candidate = new VerifiedUpdateCandidate(ManagedAppVersion.Parse("1.0.2"), "identity", "");
        long old = session.BeginCheck();
        long current = session.BeginCheck();
        Assert.Equal(1, old);
        Assert.Equal(2, current);
        Assert.False(session.TryPublishAutomaticPrompt(old, ManagedAppVersion.Parse("1.0.1"), candidate));
        Assert.True(session.TryPublishAutomaticPrompt(current, ManagedAppVersion.Parse("1.0.1"), candidate));
        Assert.False(session.TryPublishAutomaticPrompt(session.BeginCheck(), ManagedAppVersion.Parse("1.0.1"), candidate));
    }

    /// <summary>Equal and older versions cannot consume the sole automatic prompt.</summary>
    [Fact]
    public void PromptRequiresStrictlyNewerCandidate()
    {
        var session = new VersionDiscoverySession();
        var candidate = new VerifiedUpdateCandidate(ManagedAppVersion.Parse("1.0.2"), "identity", "");
        long generation = session.BeginCheck();
        Assert.False(session.TryPublishAutomaticPrompt(generation, ManagedAppVersion.Parse("1.0.3"), candidate));
        Assert.False(session.TryPublishAutomaticPrompt(generation, candidate.Version, candidate));
        Assert.True(session.TryPublishAutomaticPrompt(generation, ManagedAppVersion.Parse("1.0.1"), candidate));
        Assert.False(session.TryPublishAutomaticPrompt(generation, ManagedAppVersion.Parse("1.0.1"), candidate));
    }

    /// <summary>Candidate null checking precedes generation and publication guards.</summary>
    [Fact]
    public void DiscoveryChecksNullCandidateFirst()
    {
        var session = new VersionDiscoverySession();
        Assert.Throws<ArgumentNullException>(() => session.TryPublishAutomaticPrompt(-1, default, null!));
    }

    /// <summary>A canonical side-by-side version resolves the parent root case-insensitively.</summary>
    [Theory]
    [InlineData("versions")]
    [InlineData("Versions")]
    public void ManagedPayloadResolvesStableRoot(string versionsDirectory)
    {
        string root = Path.GetFullPath("layout-fixture");
        Assert.Equal(root, ManagedInstallationLayout.ResolveManagedRoot(Path.Combine(root, versionsDirectory, "1.2.3")));
    }

    /// <summary>Development and noncanonical version directories preserve their own base.</summary>
    [Theory]
    [InlineData("versions", "01.2.3")]
    [InlineData("versions", "development")]
    [InlineData("other", "1.2.3")]
    public void UnmanagedBaseIsPreserved(string parent, string leaf)
    {
        string path = Path.GetFullPath(Path.Combine("layout-fixture", parent, leaf));
        Assert.Equal(path, ManagedInstallationLayout.ResolveManagedRoot(path));
    }

    /// <summary>Missing layout input retains argument validation before path resolution.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingApplicationBaseIsRejected(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => ManagedInstallationLayout.ResolveManagedRoot(path!));
    }
}
