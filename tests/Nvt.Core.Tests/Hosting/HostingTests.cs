// Copyright (c) 2026 Dennis Liu. All rights reserved.

#pragma warning disable CS1591 // Test fixtures are not part of the library API.
#pragma warning disable CA1707 // Keep descriptive test names.

using Nvt.Core.Hosting;
using Xunit;

namespace Nvt.Core.Tests.Hosting;

public sealed class HostingTests
{
    [Fact]
    public void The_local_application_data_folder_is_an_absolute_path()
    {
        var folder = KnownFolders.GetLocalApplicationData();

        Assert.False(string.IsNullOrWhiteSpace(folder));
        Assert.True(Path.IsPathRooted(folder));
    }

    [Fact]
    public void A_set_variable_returns_its_value()
    {
        Assert.False(string.IsNullOrEmpty(HostEnvironment.GetVariable("PATH")));
    }

    [Fact]
    public void An_unset_variable_returns_null()
    {
        Assert.Null(HostEnvironment.GetVariable($"NVT_CORE_UNSET_{Guid.NewGuid():N}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_null_or_empty_name_is_rejected(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => HostEnvironment.GetVariable(name!));
    }
}
