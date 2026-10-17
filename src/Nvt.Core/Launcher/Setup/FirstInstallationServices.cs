// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Persistence;
using Nvt.Core.Launcher.Transport;

namespace Nvt.Core.Launcher.Setup;

internal sealed record FirstInstallationBudgets(TimeSpan AdmissionOperationCutoff, TimeSpan CompletionOperationCutoff);

internal sealed record FirstInstallationServices(
    string StatePathIdentity,
    IVersionManagerStateStore StateStore,
    IManagedInstallationRootProbe RootProbe,
    IManagedDistributionPayloadSource PayloadSource,
    IFreshInstallationCandidateSource CandidateSource,
    IManagedFirstInstallationRootMaterializer RootMaterializer,
    IImmutableBootstrapLeaseHandoff Handoff,
    FirstInstallationBudgets Budgets,
    TimeProvider TimeProvider)
{
    internal TimeSpan AdmissionOperationCutoff => Budgets.AdmissionOperationCutoff;
    internal TimeSpan CompletionOperationCutoff => Budgets.CompletionOperationCutoff;
}
