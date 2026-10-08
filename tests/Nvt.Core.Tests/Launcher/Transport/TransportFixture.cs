// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Tests.Launcher.Transport;

internal static class TransportFixture
{
    internal const int ApplicationReadyCharacters = 128;
    internal const int LauncherReadyCharacters = 4_096;
    internal const int BootstrapIdentityCharacters = 128;
    internal static readonly ManagedAppVersion Version = ManagedAppVersion.Parse("1.2.3");
    internal static LauncherProtocolNames Names { get; } = new(
        "CORE_TEST_APP_READY_HANDLE", "CORE_TEST_APP_EXPECTED_VERSION",
        "CORE_TEST_LAUNCHER_READY_HANDLE", "CORE_TEST_LAUNCHER_EXPECTED_READY",
        "CORE_TEST_BOOTSTRAP_ADMISSION_HANDLE", "CORE_TEST_BOOTSTRAP_START_CONTEXT",
        "CORE_TEST_BOOTSTRAP_START_HANDLE", "CORE_TEST_LIFETIME_CONTEXT",
        "CORE_TEST_LIFETIME_HANDLE", "CORE_TEST_LIFETIME_JOB",
        "CORE_TEST_LIFETIME_STATE_PATH", "CORE_TEST_LIFETIME_KIND", "CORE_TEST_BOOTSTRAP_IDENTITY");

    internal static ProductDescriptor Descriptor { get; } = CreateDescriptor();

    internal static ProductDescriptor CreateDescriptor(string bootstrapFileName = "Fixture.Bootstrap.exe") =>
        new("Fixture", "test-runtime", "test-registry", "Fixture.exe", "launcher/Fixture.Launcher.exe",
            bootstrapFileName, static version => $"Fixture-v{version}-test-runtime", Names);

    internal static ManagedLauncherIdentity Launcher() =>
        ManagedLauncherIdentity.Create(Descriptor, 200_000_000, Version, "fixture-admission",
            new string('a', 64), Version, 1, Descriptor.LauncherExecutableRelativePath, 123,
            new string('b', 64));
}
