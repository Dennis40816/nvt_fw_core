// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

/// <summary>Initialization dispatch under the experience owner's mutation lock, using its durable reload and lease ports.</summary>
internal static class VersionManagementInitialization
{
    internal static async ValueTask<ManagedMutationSnapshot> LoadAsync(
        bool isReadOnly,
        TimeSpan writerLeaseTimeout,
        Func<CancellationToken, bool, ValueTask<ManagedMutationSnapshot>> reload,
        Func<TimeSpan, CancellationToken, ValueTask<VersionManagerWriteLeaseResult>> acquireLease,
        Func<ManagedMutationSnapshot> publishUnavailable,
        CancellationToken cancellationToken)
    {
        if (isReadOnly)
        {
            return await reload(cancellationToken, false).ConfigureAwait(false);
        }
        using VersionManagerWriteLeaseResult lease = await acquireLease(
            writerLeaseTimeout,
            cancellationToken).ConfigureAwait(false);
        return lease.IsAcquired
            ? await reload(cancellationToken, true).ConfigureAwait(false)
            : publishUnavailable();
    }
}
