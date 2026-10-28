// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Nvt.Core.Tests.Launcher.Coordination;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Mandatory experience dependencies have no default-accepting replacement.</summary>
public sealed class VersionManagementExperienceConstructionTests
{
    /// <summary>Every mandatory port fails at construction with its exact parameter name.</summary>
    [Theory]
    [InlineData("stateStore")]
    [InlineData("catalogSource")]
    [InlineData("repository")]
    [InlineData("launcherFence")]
    [InlineData("descriptor")]
    [InlineData("limits")]
    [InlineData("retention")]
    public void MissingMandatoryAdapterIsRejected(string parameter)
    {
        ManagedAppVersion version = ManagedAppVersion.Parse("0.10.5");
        using var store = new MemoryStateStore(State([Admission("0.10.5")], "0.10.5", "0.10.5"));

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
        {
            using var experience = new VersionManagementExperience(version, "managed-root", store.StatePath,
                parameter == "stateStore" ? null! : store,
                parameter == "catalogSource" ? null! : new FixedCatalogSource(Catalog("0.10.6")),
                parameter == "repository" ? null! : new HealthyRepository(),
                parameter == "launcherFence" ? null! : new ManagedMutationTestSupport.ClearLauncherFence(),
                parameter == "descriptor" ? null! : IntegrationTestData.Descriptor(),
                parameter == "limits" ? null! : IntegrationTestData.Limits,
                parameter == "retention" ? null! : new ManagedMutationTestSupport.RetentionPolicy());
        });

        Assert.Equal(parameter, exception.ParamName);
    }
}
