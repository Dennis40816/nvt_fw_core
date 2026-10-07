// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Coordination;
using Nvt.Core.Launcher.Persistence;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

internal sealed class TraceExecutableLease(string path) : IManagedExecutableLaunchLease
{
    public string ExecutablePath => path;
    public string WorkingDirectory => Path.GetDirectoryName(path)!;
    internal bool Disposed { get; private set; }
    public bool TryValidateForStart() => !Disposed;
    public void Dispose() => Disposed = true;
}

internal sealed class TraceLauncherRepository(CoordinationStateStore app, params ManagedLauncherIdentity[] identities) : IInstalledLauncherRepository
{
    public ValueTask<InstalledLauncherResult> VerifyAsync(string managedRoot, ManagedVersionAdmission admission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ManagedLauncherIdentity? identity = identities.SingleOrDefault(i => i.MatchesOwner(admission));
        return ValueTask.FromResult(new InstalledLauncherResult(identity,
            identity is null ? InstalledLauncherIssue.Unavailable : InstalledLauncherIssue.None));
    }
    public async ValueTask<InstalledLauncherLaunchResult> AcquireLaunchLeaseAsync(string managedRoot,
        ManagedVersionAdmission admission, CancellationToken cancellationToken)
    {
        app.Trace.Add($"repository:launcher:{admission.Version}");
        InstalledLauncherResult verified = await VerifyAsync(managedRoot, admission, cancellationToken);
        return verified.IsVerified ? new(verified.Identity,
            new TraceExecutableLease(Path.Combine(managedRoot, admission.Version.ToString(), "SyntheticApp.Launcher.exe")), InstalledLauncherIssue.None)
            : new(null, null, verified.Issue);
    }
}

internal sealed class TraceLauncherProcess(CoordinationStateStore app) : IManagedLauncherProcess
{
    internal ManagedProcessLifetimeStatus Lifetime { get; set; } = ManagedProcessLifetimeStatus.Exited;
    internal LauncherProcessStartOutcome Outcome { get; set; } = LauncherProcessStartOutcome.Ready;
    internal Action? AtReady { get; set; }
    internal List<ManagedLauncherIdentity> Starts { get; } = [];
    internal List<TimeSpan> Deadlines { get; } = [];
    public ValueTask<ManagedProcessLifetimeStatus> GetLifetimeStatusAsync(string statePath,
        ManagedProcessLifetimeKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(app.StatePath, statePath);
        app.Trace.Add($"lifetime:{kind}");
        return ValueTask.FromResult(Lifetime);
    }
    public async ValueTask<LauncherProcessStartResult> StartUntilReadyAsync(string managedRoot, string statePath,
        ManagedLauncherIdentity launcher, IManagedExecutableLaunchLease executableLease, TimeSpan readyDeadline, CancellationToken cancellationToken)
    {
        Assert.True(executableLease.TryValidateForStart());
        Starts.Add(launcher);
        Deadlines.Add(readyDeadline);
        app.Trace.Add($"process:launcher:{launcher.OwnerAppVersion}");
        using VersionManagerWriteLeaseResult duringReady = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            statePath, TimeSpan.Zero, cancellationToken);
        Assert.True(duringReady.HoldsStatePath(statePath));
        app.Trace.Add("process:writer:released");
        AtReady?.Invoke();
        VersionManagerState state = app.State;
        return new(Outcome, null, Outcome == LauncherProcessStartOutcome.Ready
            ? state.Admissions.SingleOrDefault(a => a.Version == state.ActiveVersion) : null);
    }
}

internal sealed class TraceApplicationRepository(CoordinationStateStore app, ProductDescriptor product) : IManagedVersionRepository
{
    internal bool InventoryUnavailable { get; set; }
    internal List<TraceExecutableLease> Leases { get; } = [];
    public ValueTask<ManagedExecutableLaunchLeaseResult> AcquireApplicationLaunchLeaseAsync(string managedRoot,
        ManagedVersionAdmission admission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        app.Trace.Add($"repository:app:{admission.Version}");
        var lease = new TraceExecutableLease(Path.Combine(managedRoot, admission.Version.ToString(), product.ApplicationExecutableRelativePath));
        Leases.Add(lease);
        return ValueTask.FromResult(new ManagedExecutableLaunchLeaseResult(lease, ManagedExecutableLaunchIssue.None));
    }
    public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(string managedRoot, IReadOnlyList<ManagedVersionAdmission> admissions,
        ManagedAppVersion? activeVersion, ManagedAppVersion? lastKnownGoodVersion, ManagedAppVersion? failedActivationVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        app.Trace.Add("inventory:app");
        return ValueTask.FromResult(InventoryUnavailable ? ManagedVersionInventoryReadResult.Unavailable() :
            ManagedVersionInventoryReadResult.Success(ManagedVersionInventory.Create(admissions.Select(a => new InstalledVersionSnapshot(
                a.Version, a.AdmissionIdentity, ManagedVersionIntegrity.Healthy, null, a.Version == activeVersion,
                a.Version == lastKnownGoodVersion, ManagedVersionAdmissionState.Admitted, a)))));
    }
    public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(string sourceRoot, UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken)
        => throw new NotSupportedException();
    public ValueTask<ManagedVersionInstallResult> InstallAsync(string managedRoot, string sourceRoot,
        UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(string managedRoot, ManagedVersionAdmission admission,
        ManagedAppVersion? activeVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class TraceApplicationProcess(CoordinationStateStore app) : IManagedApplicationProcess
{
    internal ManagedProcessLifetimeStatus Lifetime { get; set; } = ManagedProcessLifetimeStatus.Exited;
    internal ManagedProcessStartOutcome Outcome { get; set; } = ManagedProcessStartOutcome.Ready;
    internal Action? AtReady { get; set; }
    internal List<ManagedAppVersion> Starts { get; } = [];
    internal List<TimeSpan> Deadlines { get; } = [];
    public ValueTask<ManagedProcessLifetimeStatus> GetLifetimeStatusAsync(string managedRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        app.Trace.Add("lifetime:Application");
        return ValueTask.FromResult(Lifetime);
    }
    public async ValueTask<ManagedProcessStartResult> StartUntilReadyAsync(string managedRoot, ManagedAppVersion version,
        IManagedExecutableLaunchLease executableLease, TimeSpan readyDeadline, CancellationToken cancellationToken)
    {
        Assert.True(executableLease.TryValidateForStart());
        Starts.Add(version);
        Deadlines.Add(readyDeadline);
        app.Trace.Add($"process:app:{version}");
        using VersionManagerWriteLeaseResult competing = await FileSystemVersionManagerWriteLease.TryAcquireAsync(
            app.StatePath, TimeSpan.Zero, cancellationToken);
        Assert.Equal(VersionManagerWriteLeaseIssue.Busy, competing.Issue);
        app.Trace.Add("process:writer:held");
        AtReady?.Invoke();
        return new(Outcome, null);
    }
}
