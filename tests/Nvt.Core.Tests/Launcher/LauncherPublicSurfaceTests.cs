// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Verification;
using Xunit;

namespace Nvt.Core.Tests.Launcher;

/// <summary>Guards the Launcher implementation boundary before the public API freeze. The compiler already rejects a public signature that exposes an internal type, so this class pins only the visibility decisions.</summary>
public sealed class LauncherPublicSurfaceTests
{
    private static readonly HashSet<Type> _internalTypes =
    [
        typeof(InstalledApplicationCoordinator),
        typeof(InstalledApplicationPresentation),
        typeof(InstalledApplicationInfo),
        typeof(IInstalledApplicationPresentation),
        typeof(ManagedPackageVerifier),
    ];

    /// <summary>Implementation types stay internal while the intended activation policy stays public.</summary>
    [Fact]
    public void ImplementationTypesAreInternalAndActivationPoliciesArePublic()
    {
        foreach (Type type in _internalTypes.OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            Assert.False(type.IsPublic || type.IsNestedPublic, $"{type.FullName} must be internal.");
        }

        Assert.True(typeof(VersionActivationPolicy).IsPublic);
        Assert.True(typeof(ActivationRecoveryDecision).IsPublic);
    }
}
