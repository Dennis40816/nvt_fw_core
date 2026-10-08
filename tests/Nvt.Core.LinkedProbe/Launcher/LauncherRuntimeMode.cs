// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Transport;

namespace Nvt.Core.LinkedProbe.Launcher;

internal static class LauncherRuntimeMode
{
    [LinkedProbeMode("bootstrap-runtime")]
    internal static async Task<int> RunAsync(ProbeContext context)
    {
        string marker = context.Inputs.Required("marker");
        string state = context.Inputs.Required("state-path");
        string root = context.Inputs.Optional("managed-root") ?? Environment.CurrentDirectory;
        string? startHandle = Environment.GetEnvironmentVariable(context.ProtocolNames.BootstrapStartHandle);
        string? admissionHandle = Environment.GetEnvironmentVariable(context.ProtocolNames.BootstrapAdmissionHandle);
        var runtime = new LauncherBootstrapRuntime(LauncherModes.Descriptor(context), 4096, 128,
            LauncherModes.JobNamePrefix, (actualRoot, actualState) =>
            {
                if (actualRoot != Path.GetFullPath(root) || actualState != Path.GetFullPath(state) ||
                    Environment.GetEnvironmentVariable(context.ProtocolNames.BootstrapIdentity) is not null)
                {
                    throw new InvalidOperationException("Runtime composition context differs.");
                }
                File.AppendAllText(marker, "compose" + Environment.NewLine);
                var unused = new UnusedRepositories();
                return new(new StateStore(marker), new StateStore(marker),
                    new UnusedLauncherStore(), unused, unused);
            });
        Task<int> entry = runtime.RunEntryAsync(root, state, context.CancellationToken).AsTask();
        // RunEntryAsync synchronously captures real lifetime, START, admission, and identity
        // before its first incomplete await. These observations use the production capture path.
        await File.WriteAllLinesAsync(marker + ".waiting",
            [entry.IsCompleted.ToString(), Environment.GetEnvironmentVariable(context.ProtocolNames.BootstrapIdentity) ?? "<null>",
                Flags(startHandle), Flags(admissionHandle),
                Environment.GetEnvironmentVariable(context.ProtocolNames.LifetimeContext) ?? "<null>",
                Environment.GetEnvironmentVariable(context.ProtocolNames.LifetimeHandle) ?? "<null>",
                Environment.GetEnvironmentVariable(context.ProtocolNames.LifetimeJob) ?? "<null>",
                Environment.GetEnvironmentVariable(context.ProtocolNames.LifetimeStatePath) ?? "<null>",
                Environment.GetEnvironmentVariable(context.ProtocolNames.LifetimeKind) ?? "<null>",
                Environment.GetEnvironmentVariable(context.ProtocolNames.BootstrapStartContext) ?? "<null>",
                Environment.GetEnvironmentVariable(context.ProtocolNames.BootstrapStartHandle) ?? "<null>",
                Environment.GetEnvironmentVariable(context.ProtocolNames.BootstrapAdmissionHandle) ?? "<null>"],
            context.CancellationToken).ConfigureAwait(false);
        int exit = await entry.ConfigureAwait(false);
        await File.WriteAllLinesAsync(marker + ".closed", [Flags(startHandle), Flags(admissionHandle)],
            context.CancellationToken).ConfigureAwait(false);
        return exit;
    }

    private static string Flags(string? raw) => raw is not null &&
        GetHandleInformation(new IntPtr(long.Parse(raw, CultureInfo.InvariantCulture)), out uint flags)
            ? (flags & 1u).ToString(CultureInfo.InvariantCulture) : "closed";

    private sealed class StateStore(string marker) : IVersionManagerStateStore
    {
        public ValueTask<VersionManagerStateLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.AppendAllText(marker, "app.load" + Environment.NewLine);
            return ValueTask.FromResult(new VersionManagerStateLoadResult(null, VersionManagerStateLoadIssue.Invalid));
        }
        public ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(TimeSpan waitTimeout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid existing state cannot acquire a writer.");
        public ValueTask SaveAsync(VersionManagerState state, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This scenario cannot save state.");
    }

    private sealed class UnusedLauncherStore : ILauncherBootstrapStateStore
    {
        public ValueTask<LauncherBootstrapStateLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Launcher state cannot be read in this scenario.");
        public ValueTask<LauncherBootstrapStateSaveResult> TrySaveAsync(LauncherBootstrapState state, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Launcher state cannot be saved in this scenario.");
    }

    private sealed class UnusedRepositories : IManagedVersionRepository, IInstalledLauncherRepository
    {
        public ValueTask<ManagedPackageVerificationResult> VerifyPackageAsync(string sourceRoot, UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) => throw new InvalidOperationException("Package verification cannot run.");
        public ValueTask<ManagedVersionInstallResult> InstallAsync(string managedRoot, string sourceRoot, UpdateCatalogVersionSnapshot package, CancellationToken cancellationToken) => throw new InvalidOperationException("Installation cannot run.");
        public ValueTask<ManagedVersionInventoryReadResult> InventoryAsync(string managedRoot, IReadOnlyList<ManagedVersionAdmission> admissions, ManagedAppVersion? activeVersion,
            ManagedAppVersion? lastKnownGoodVersion, ManagedAppVersion? failedActivationVersion, CancellationToken cancellationToken) => throw new InvalidOperationException("Inventory cannot run.");
        public ValueTask<ManagedVersionDeleteIssue> DeleteAsync(string managedRoot, ManagedVersionAdmission admission, ManagedAppVersion? activeVersion, CancellationToken cancellationToken) => throw new InvalidOperationException("Deletion cannot run.");
        public ValueTask<InstalledLauncherResult> VerifyAsync(string managedRoot, ManagedVersionAdmission admission, CancellationToken cancellationToken) => throw new InvalidOperationException("Launcher verification cannot run.");
        public ValueTask<InstalledLauncherLaunchResult> AcquireLaunchLeaseAsync(string managedRoot, ManagedVersionAdmission admission, CancellationToken cancellationToken) => throw new InvalidOperationException("Launcher acquisition cannot run.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
}
