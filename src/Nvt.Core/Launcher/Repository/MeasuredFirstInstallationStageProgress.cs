// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;

namespace Nvt.Core.Launcher.Repository;

// Projects authoritative counters once per increasing integer percentage within one stage.
internal sealed class MeasuredFirstInstallationStageProgress(
    IProgress<ManagedFirstInstallationProgress> progress,
    ManagedFirstInstallationProgressStage stage)
{
    private readonly IProgress<ManagedFirstInstallationProgress> _progress = progress ??
        throw new ArgumentNullException(nameof(progress));
    private int _lastPercent = -1;

    internal static Action<long, long>? Create(IProgress<ManagedFirstInstallationProgress>? progress,
        ManagedFirstInstallationProgressStage stage) => progress is null
            ? null : new MeasuredFirstInstallationStageProgress(progress, stage).Report;

    internal void Report(long completed, long total)
    {
        var current = new ManagedFirstInstallationProgress(stage, completed, total);
        int percent = current.Percent!.Value;
        if (percent <= _lastPercent)
        {
            return;
        }
        _lastPercent = percent;
        _progress.Report(current);
    }
}
