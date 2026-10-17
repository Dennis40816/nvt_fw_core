// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Transport;

namespace Nvt.Core.Launcher.Setup;

internal static class FirstInstallationOutcomes
{
    internal static ManagedVersionAdmission CreateAdmission(FreshInstallationCandidate candidate)
    {
        return new(
            candidate.Package.Version,
            candidate.Package.Identity,
            candidate.Package.ReleaseManifestSha256);
    }

    internal static ManagedFirstInstallationOutcome MapCandidateIssue(
        FreshInstallationCandidateIssue issue)
    {
        return issue switch
        {
            FreshInstallationCandidateIssue.RegistryNotConfigured or
            FreshInstallationCandidateIssue.SourceUnavailable =>
                ManagedFirstInstallationOutcome.SourceUnavailable,
            FreshInstallationCandidateIssue.SourceRejected =>
                ManagedFirstInstallationOutcome.SourceRejected,
            FreshInstallationCandidateIssue.CandidateUnavailable =>
                ManagedFirstInstallationOutcome.CandidateUnavailable,
            FreshInstallationCandidateIssue.SourceChanged =>
                ManagedFirstInstallationOutcome.SourceChanged,
            FreshInstallationCandidateIssue.None =>
                ManagedFirstInstallationOutcome.SourceChanged,
            _ => throw new InvalidOperationException("Candidate source returned an undefined issue."),
        };
    }

    internal static ManagedFirstInstallationOutcome MapMaterializationIssue(
        ManagedFirstInstallationMaterializationIssue issue)
    {
        return issue switch
        {
            ManagedFirstInstallationMaterializationIssue.InvalidDestination =>
                ManagedFirstInstallationOutcome.InvalidDestination,
            ManagedFirstInstallationMaterializationIssue.PermissionDenied =>
                ManagedFirstInstallationOutcome.PermissionDenied,
            ManagedFirstInstallationMaterializationIssue.SourceUnavailable =>
                ManagedFirstInstallationOutcome.SourceUnavailable,
            ManagedFirstInstallationMaterializationIssue.SourceChanged =>
                ManagedFirstInstallationOutcome.SourceChanged,
            ManagedFirstInstallationMaterializationIssue.PromotionFailed or
            ManagedFirstInstallationMaterializationIssue.StateUnavailable =>
                ManagedFirstInstallationOutcome.StateUnavailable,
            ManagedFirstInstallationMaterializationIssue.RecoveryRequired =>
                ManagedFirstInstallationOutcome.RecoveryRequired,
            ManagedFirstInstallationMaterializationIssue.None =>
                ManagedFirstInstallationOutcome.StateUnavailable,
            _ => throw new InvalidOperationException("Materializer returned an undefined issue."),
        };
    }

    internal static ManagedFirstInstallationOutcome MapBootstrapStartIssue(
        ImmutableBootstrapStartIssue issue)
    {
        return issue switch
        {
            ImmutableBootstrapStartIssue.Busy =>
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
            ImmutableBootstrapStartIssue.Damaged => ManagedFirstInstallationOutcome.RecoveryRequired,
            ImmutableBootstrapStartIssue.StartFailed or
            ImmutableBootstrapStartIssue.Unavailable =>
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
            ImmutableBootstrapStartIssue.None => throw new InvalidOperationException(
                "A successful Bootstrap start did not return its launch receipt."),
            _ => throw new InvalidOperationException("Bootstrap start returned an undefined issue."),
        };
    }

    internal static ImmutableBootstrapStartIssue MapBootstrapLeaseFailure(
        ManagedExecutableLaunchLeaseResult acquired)
    {
        return acquired.Lease is not null || !Enum.IsDefined(acquired.Issue) ||
            acquired.Issue == ManagedExecutableLaunchIssue.None
                ? ImmutableBootstrapStartIssue.Damaged
                : acquired.Issue switch
                {
                    ManagedExecutableLaunchIssue.Tampered or
                    ManagedExecutableLaunchIssue.UnsafePath =>
                        ImmutableBootstrapStartIssue.Damaged,
                    ManagedExecutableLaunchIssue.Unavailable =>
                        ImmutableBootstrapStartIssue.Unavailable,
                    ManagedExecutableLaunchIssue.None => throw new InvalidOperationException(
                        "A successful Bootstrap launch lease was mapped as a failure."),
                    _ => throw new InvalidOperationException(
                        "Bootstrap launch lease returned an undefined issue."),
                };
    }

    internal static ManagedFirstInstallationLaunchIssue MapBootstrapStartFailure(
        ImmutableBootstrapStartIssue issue)
    {
        return issue switch
        {
            ImmutableBootstrapStartIssue.Busy => ManagedFirstInstallationLaunchIssue.Busy,
            ImmutableBootstrapStartIssue.Damaged => ManagedFirstInstallationLaunchIssue.Damaged,
            ImmutableBootstrapStartIssue.StartFailed => ManagedFirstInstallationLaunchIssue.StartFailed,
            ImmutableBootstrapStartIssue.Unavailable => ManagedFirstInstallationLaunchIssue.Unavailable,
            ImmutableBootstrapStartIssue.None => throw new InvalidOperationException(
                "A successful Bootstrap start was mapped as a launch failure."),
            _ => throw new InvalidOperationException("Bootstrap start returned an undefined issue."),
        };
    }

    internal static ManagedFirstInstallationOutcome MapBootstrapAdmissionIssue(
        ImmutableBootstrapAdmissionOutcome outcome)
    {
        return outcome switch
        {
            ImmutableBootstrapAdmissionOutcome.RecoveryRequired =>
                ManagedFirstInstallationOutcome.RecoveryRequired,
            ImmutableBootstrapAdmissionOutcome.Busy or
            ImmutableBootstrapAdmissionOutcome.LaunchFailed or
            ImmutableBootstrapAdmissionOutcome.HealthUnavailable or
            ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed =>
                ManagedFirstInstallationOutcome.InstalledButLaunchFailed,
            ImmutableBootstrapAdmissionOutcome.Admitted => throw new InvalidOperationException(
                "An admitted Bootstrap was handled as an admission failure."),
            _ => throw new InvalidOperationException(
                "Bootstrap admission returned an undefined outcome."),
        };
    }

    internal static ManagedFirstInstallationLaunchIssue MapBootstrapAdmissionFailure(
        ImmutableBootstrapAdmissionResult admission)
    {
        return admission.ExitIssue != ImmutableBootstrapExitIssue.None
            ? ManagedFirstInstallationLaunchFailure.MapBootstrapExitIssue(admission.ExitIssue)
            : admission.Outcome switch
            {
                // Transport requires a typed exit issue for these failed admissions,
                // including LaunchFailed without an exit code. Frozen fallback mappings
                // (ManagedInstallationSetup.cs:701-705,708-709) are unreachable.
                ImmutableBootstrapAdmissionOutcome.Busy or
                ImmutableBootstrapAdmissionOutcome.RecoveryRequired or
                ImmutableBootstrapAdmissionOutcome.LaunchFailed or
                ImmutableBootstrapAdmissionOutcome.TerminationUnconfirmed =>
                    throw new InvalidOperationException(
                        "A valid failed Bootstrap admission requires one typed exit issue."),
                ImmutableBootstrapAdmissionOutcome.HealthUnavailable =>
                    ManagedFirstInstallationLaunchIssue.HealthUnavailable,
                ImmutableBootstrapAdmissionOutcome.Admitted => throw new InvalidOperationException(
                    "An admitted Bootstrap was mapped as an admission failure."),
                _ => throw new InvalidOperationException(
                    "Bootstrap admission returned an undefined outcome."),
            };
    }

    internal static ManagedFirstInstallationLaunchIssue MapBootstrapCompletionFailure(
        ImmutableBootstrapCompletionResult completion)
    {
        return completion.ExitIssue != ImmutableBootstrapExitIssue.None
            ? ManagedFirstInstallationLaunchFailure.MapBootstrapExitIssue(completion.ExitIssue)
            : completion.Outcome switch
            {
                ImmutableBootstrapCompletionOutcome.RolledBack =>
                    ManagedFirstInstallationLaunchIssue.RolledBack,
                ImmutableBootstrapCompletionOutcome.Failed => throw new InvalidOperationException(
                    "A valid failed Bootstrap completion requires one typed exit issue."),
                ImmutableBootstrapCompletionOutcome.Unavailable =>
                    ManagedFirstInstallationLaunchIssue.Unavailable,
                ImmutableBootstrapCompletionOutcome.TerminationUnconfirmed =>
                    ManagedFirstInstallationLaunchIssue.TerminationUnconfirmed,
                ImmutableBootstrapCompletionOutcome.Ready => throw new InvalidOperationException(
                    "A READY Bootstrap was mapped as a completion failure."),
                _ => throw new InvalidOperationException(
                    "Bootstrap completion returned an undefined outcome."),
            };
    }

    internal static ManagedFirstInstallationOutcome MapDestinationAdmissionIssue(
        ManagedFirstInstallationMaterializationIssue issue)
    {
        return issue switch
        {
            ManagedFirstInstallationMaterializationIssue.InvalidDestination =>
                ManagedFirstInstallationOutcome.InvalidDestination,
            ManagedFirstInstallationMaterializationIssue.PermissionDenied =>
                ManagedFirstInstallationOutcome.PermissionDenied,
            ManagedFirstInstallationMaterializationIssue.StateUnavailable =>
                ManagedFirstInstallationOutcome.StateUnavailable,
            ManagedFirstInstallationMaterializationIssue.RecoveryRequired =>
                ManagedFirstInstallationOutcome.RecoveryRequired,
            ManagedFirstInstallationMaterializationIssue.None =>
                throw new InvalidOperationException("Successful destination admission was mapped as failure."),
            ManagedFirstInstallationMaterializationIssue.SourceUnavailable or
            ManagedFirstInstallationMaterializationIssue.SourceChanged or
            ManagedFirstInstallationMaterializationIssue.PromotionFailed =>
                throw new InvalidOperationException(
                    "Destination admission returned an issue outside its closed contract."),
            _ => throw new InvalidOperationException(
                "Destination admission returned an undefined issue."),
        };
    }

    internal static ManagedFirstInstallationOutcome MapNonMissingState(
        VersionManagerStateLoadResult state)
    {
        return state.Issue == VersionManagerStateLoadIssue.Unavailable
            ? ManagedFirstInstallationOutcome.StateUnavailable
            : ManagedFirstInstallationOutcome.RecoveryRequired;
    }

    internal static ManagedFirstInstallationOutcome MapPayloadIssue(
        ManagedDistributionPayloadIssue issue)
    {
        return issue switch
        {
            ManagedDistributionPayloadIssue.Unavailable =>
                ManagedFirstInstallationOutcome.PayloadUnavailable,
            ManagedDistributionPayloadIssue.Invalid =>
                ManagedFirstInstallationOutcome.PayloadInvalid,
            ManagedDistributionPayloadIssue.Changed =>
                ManagedFirstInstallationOutcome.SourceChanged,
            ManagedDistributionPayloadIssue.None =>
                ManagedFirstInstallationOutcome.PayloadInvalid,
            _ => throw new InvalidOperationException("Payload source returned an undefined issue."),
        };
    }

    internal static ManagedFirstInstallationOutcome MapRootStatus(
        ManagedInstallationRootStatus status)
    {
        return status switch
        {
            ManagedInstallationRootStatus.InvalidDestination =>
                ManagedFirstInstallationOutcome.InvalidDestination,
            ManagedInstallationRootStatus.PermissionDenied =>
                ManagedFirstInstallationOutcome.PermissionDenied,
            ManagedInstallationRootStatus.Unavailable =>
                ManagedFirstInstallationOutcome.StateUnavailable,
            ManagedInstallationRootStatus.Present or
            ManagedInstallationRootStatus.Residue =>
                ManagedFirstInstallationOutcome.RecoveryRequired,
            ManagedInstallationRootStatus.Absent =>
                ManagedFirstInstallationOutcome.RecoveryRequired,
            _ => throw new InvalidOperationException("Root probe returned an undefined status."),
        };
    }

    internal static ManagedFirstInstallationPlanResult PlanFailure(
        ManagedFirstInstallationOutcome outcome)
    {
        return new(null, outcome);
    }

    internal static ManagedFirstInstallationLaunchIssue MapTransactionFailure(
        ManagedFirstInstallationTransactionIssue issue)
    {
        return issue switch
        {
            ManagedFirstInstallationTransactionIssue.RecoveryRequired =>
                ManagedFirstInstallationLaunchIssue.RecoveryRequired,
            ManagedFirstInstallationTransactionIssue.StateUnavailable =>
                ManagedFirstInstallationLaunchIssue.StateUnavailable,
            ManagedFirstInstallationTransactionIssue.None => throw new InvalidOperationException(
                "A successful transaction was mapped as a launch-terminal failure."),
            _ => throw new InvalidOperationException("Transaction returned an undefined issue."),
        };
    }

    internal static ManagedFirstInstallationResult Result(
        ManagedFirstInstallationPlan plan,
        ManagedFirstInstallationOutcome outcome,
        ManagedFirstInstallationLaunchFailure? launchFailure = null)
    {
        return new(outcome, plan.ManagedRoot, plan.Candidate.Package.Version, launchFailure);
    }

    internal static ManagedFirstInstallationResult LaunchFailure(
        ManagedFirstInstallationPlan plan,
        ManagedFirstInstallationOutcome outcome,
        ManagedFirstInstallationLaunchStage stage,
        ManagedFirstInstallationLaunchIssue issue,
        int? exitCode = null)
    {
        return stage == ManagedFirstInstallationLaunchStage.None ||
            issue == ManagedFirstInstallationLaunchIssue.None
                ? throw new InvalidOperationException("A launch failure requires an exact stage and issue.")
                : Result(plan, outcome, new(stage, issue, exitCode));
    }

    internal static bool TryNormalizeRoot(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            return false;
        }
        try
        {
            normalized = ManagedRootPathIdentity.Normalize(value);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

}
