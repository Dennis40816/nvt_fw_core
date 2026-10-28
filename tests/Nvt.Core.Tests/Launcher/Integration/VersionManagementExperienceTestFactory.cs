// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Nvt.Core.Tests.Launcher.Coordination;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;

namespace Nvt.Core.Tests.Launcher.Integration;

internal static class VersionManagementExperienceTestFactory
{
    internal static VersionManagementExperience Create(
        ManagedAppVersion currentAppVersion, string managedRoot, MemoryStateStore stateStore,
        IUpdateCatalogSource catalogSource, IManagedVersionRepository repository,
        ILauncherMutationFence launcherFence) => Create(currentAppVersion, managedRoot,
            stateStore, catalogSource, repository, null, launcherFence);

    internal static VersionManagementExperience Create(
        ManagedAppVersion currentAppVersion, string managedRoot, MemoryStateStore stateStore,
        IUpdateCatalogSource catalogSource, IManagedVersionRepository repository,
        IUpdateSourceRegistry? sourceRegistry = null, ILauncherMutationFence? launcherFence = null,
        VersionManagementLimits? limits = null) => new(
            currentAppVersion, managedRoot, stateStore.StatePath, stateStore, catalogSource, repository,
            launcherFence ?? new ManagedMutationTestSupport.ClearLauncherFence(), IntegrationTestData.Descriptor(),
            limits ?? IntegrationTestData.Limits, new ManagedMutationTestSupport.RetentionPolicy(), sourceRegistry);
}
