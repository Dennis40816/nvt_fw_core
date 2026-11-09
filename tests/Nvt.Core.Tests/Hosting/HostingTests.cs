// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Hosting;
using Xunit;

namespace Nvt.Core.Tests.Hosting;

public sealed class HostingTests
{
    [Fact]
    public void TheLocalApplicationDataFolderIsAnAbsolutePath()
    {
        var folder = KnownFolders.GetLocalApplicationData();

        Assert.False(string.IsNullOrWhiteSpace(folder));
        Assert.True(Path.IsPathRooted(folder));
    }

    [Fact]
    public void ASetVariableReturnsItsValue()
    {
        Assert.False(string.IsNullOrEmpty(HostEnvironment.GetVariable("PATH")));
    }

    [Fact]
    public void AnUnsetVariableReturnsNull()
    {
        Assert.Null(HostEnvironment.GetVariable($"NVT_CORE_UNSET_{Guid.NewGuid():N}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ANullOrEmptyNameIsRejected(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => HostEnvironment.GetVariable(name!));
    }
}
