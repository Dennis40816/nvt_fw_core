// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Tests.Launcher.Coordination;

internal static class CoordinationFixture
{
    internal static readonly ProductDescriptor Product = new(
        "Synthetic App", "win-x64", "synthetic.registry", "SyntheticApp.exe",
        "launcher/SyntheticApp.Launcher.exe", "SyntheticApp.Bootstrap.exe",
        version => $"SyntheticApp-{version}",
        new("APP_READY", "APP_VERSION", "LAUNCHER_READY", "LAUNCHER_VERSION", "ADMISSION",
            "START_CONTEXT", "START_HANDLE", "LIFETIME_CONTEXT", "LIFETIME_HANDLE", "JOB", "STATE_PATH", "KIND", "BOOTSTRAP"));
}

// These disposables characterize the state-port contract only. Physical capability tests use L04 directly.
internal static class VersionManagerWriteLeaseTestSupport
{
    internal static VersionManagerWriteLeaseResult Acquired() => new(VersionManagerWriteLeaseIssue.None, new NoOpLease());
    internal static VersionManagerWriteLeaseResult Busy() => new(VersionManagerWriteLeaseIssue.Busy);
    private sealed class NoOpLease : IDisposable
    {
        public void Dispose() { }
    }
}
