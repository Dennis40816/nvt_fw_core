// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

namespace Nvt.Core.Launcher.Coordination;

public sealed partial class ManagedMutationCoordinator
{
    private async ValueTask<LauncherMutationProtection?> LoadClearLauncherFenceAsync(
        CancellationToken cancellationToken)
    {
        LauncherMutationProtection protection = await _launcherFence.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        return protection.IsClear ? protection : null;
    }

    private async ValueTask<LauncherMutationProtection?> LoadLauncherProtectionAsync(
        CancellationToken cancellationToken)
    {
        LauncherMutationProtection protection = await _launcherFence.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        return protection.Issue == LauncherMutationFenceIssue.None ? protection : null;
    }
}
