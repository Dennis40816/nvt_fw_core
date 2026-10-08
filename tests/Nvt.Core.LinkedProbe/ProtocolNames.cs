// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.LinkedProbe;

internal static class ProtocolNames
{
    internal const string DefaultPrefix = "CORE_TEST_";

    internal static LauncherProtocolNames Create(string prefix) => new(
        prefix + "APP_READY_HANDLE",
        prefix + "APP_EXPECTED_VERSION",
        prefix + "LAUNCHER_READY_HANDLE",
        prefix + "LAUNCHER_EXPECTED_READY",
        prefix + "BOOTSTRAP_ADMISSION_HANDLE",
        prefix + "BOOTSTRAP_START_CONTEXT",
        prefix + "BOOTSTRAP_START_HANDLE",
        prefix + "LIFETIME_CONTEXT",
        prefix + "LIFETIME_HANDLE",
        prefix + "LIFETIME_JOB",
        prefix + "LIFETIME_STATE_PATH",
        prefix + "LIFETIME_KIND",
        prefix + "BOOTSTRAP_IDENTITY");
}
