// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Tests.Launcher.Coordination;

// Closed synthetic grammar, deliberately unrelated to any application's state wire format.
internal static class CoordinationStateCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static byte[] Encode(VersionManagerState state) => JsonSerializer.SerializeToUtf8Bytes(new AppDocument(
        state.ManagedRootIdentity, state.UpdateSource, state.ActiveVersion?.ToString(), state.LastKnownGoodVersion?.ToString(),
        state.Admissions.Select(Admission).ToArray(),
        state.PendingActivation is { } activation ? new ActivationDocument(
            activation.CandidateVersion.ToString(), activation.CandidateAdmissionIdentity,
            activation.PreviousActiveVersion?.ToString(), activation.PreviousLastKnownGoodVersion?.ToString(), (int)activation.Phase) : null,
        state.PendingMutation is { } mutation ? new MutationDocument((int)mutation.Kind, Admission(mutation.Admission)) : null,
        state.FailedActivationVersion?.ToString(), state.RetentionReviewDue,
        state.SourceRegistryState is { } registry ? new RegistryDocument(registry.AcceptedRevision, registry.AcceptedDigest, registry.IsManualPin) : null), Options);

    internal static VersionManagerState Decode(byte[] bytes)
    {
        AppDocument value = JsonSerializer.Deserialize<AppDocument>(bytes, Options) ?? throw new JsonException();
        VersionManagerState state = VersionManagerState.Create(value.Source, Version(value.Active), Version(value.Fallback),
            value.Admissions.Select(Admission),
            value.Activation is { } a ? new PendingVersionActivation(ManagedAppVersion.Parse(a.Candidate), a.Identity,
                Version(a.PreviousActive), Version(a.PreviousFallback), (VersionActivationPhase)a.Phase) : null,
            Version(value.Failed), value.ReviewDue,
            value.Mutation is { } m ? new PendingManagedVersionMutation((ManagedVersionMutationKind)m.Kind, Admission(m.Admission)) : null,
            value.Root,
            value.Registry is { } r ? new VersionSourceRegistryState(r.Revision, r.Digest, r.Manual) : null);
        if (!bytes.AsSpan().SequenceEqual(Encode(state))) { throw new JsonException("Noncanonical synthetic state."); }
        return state;
    }

    internal static byte[] Encode(LauncherBootstrapState state) => JsonSerializer.SerializeToUtf8Bytes(new LauncherDocument(
        state.ManagedRootIdentity, Identity(state.Active), Identity(state.LastKnownGood),
        state.Pending is { } p ? new LauncherActivationDocument(Identity(p.Candidate)!, Identity(p.PreviousActive),
            Identity(p.PreviousLastKnownGood), (int)p.Phase) : null, Identity(state.Failed)), Options);

    internal static LauncherBootstrapState DecodeLauncher(byte[] bytes)
    {
        LauncherDocument value = JsonSerializer.Deserialize<LauncherDocument>(bytes, Options) ?? throw new JsonException();
        LauncherBootstrapState state = LauncherBootstrapState.Create(value.Root, Identity(value.Active), Identity(value.Fallback),
            value.Pending is { } p ? PendingLauncherActivation.Create(Identity(p.Candidate)!, Identity(p.PreviousActive),
                Identity(p.PreviousFallback), (LauncherActivationPhase)p.Phase) : null, Identity(value.Failed));
        if (!bytes.AsSpan().SequenceEqual(Encode(state))) { throw new JsonException("Noncanonical synthetic launcher state."); }
        return state;
    }

    private static ManagedAppVersion? Version(string? text) => text is null ? null : ManagedAppVersion.Parse(text);
    private static AdmissionDocument Admission(ManagedVersionAdmission a) => new(a.Version.ToString(), a.AdmissionIdentity, a.ReleaseManifestSha256);
    private static ManagedVersionAdmission Admission(AdmissionDocument a) => new(ManagedAppVersion.Parse(a.Version), a.Identity, a.Digest);
    private static IdentityDocument? Identity(ManagedLauncherIdentity? a) => a is null ? null : new(
        a.OwnerAppVersion.ToString(), a.OwnerAdmissionIdentity, a.OwnerReleaseManifestSha256, a.LauncherVersion.ToString(),
        a.ProtocolVersion, a.ExecutableRelativePath, a.Size, a.Sha256);
    private static ManagedLauncherIdentity? Identity(IdentityDocument? a) => a is null ? null : ManagedLauncherIdentity.Create(
        CoordinationFixture.Product, 200_000_000, ManagedAppVersion.Parse(a.Owner), a.OwnerIdentity, a.OwnerDigest,
        ManagedAppVersion.Parse(a.Version), a.Protocol, a.Path, a.Size, a.Digest);

    private sealed record AppDocument(
        [property: JsonRequired] string? Root, [property: JsonRequired] string? Source,
        [property: JsonRequired] string? Active, [property: JsonRequired] string? Fallback,
        [property: JsonRequired] AdmissionDocument[] Admissions, [property: JsonRequired] ActivationDocument? Activation,
        [property: JsonRequired] MutationDocument? Mutation, [property: JsonRequired] string? Failed,
        [property: JsonRequired] bool ReviewDue, [property: JsonRequired] RegistryDocument? Registry);
    private sealed record AdmissionDocument(
        [property: JsonRequired] string Version, [property: JsonRequired] string Identity, [property: JsonRequired] string Digest);
    private sealed record ActivationDocument(
        [property: JsonRequired] string Candidate, [property: JsonRequired] string Identity,
        [property: JsonRequired] string? PreviousActive, [property: JsonRequired] string? PreviousFallback, [property: JsonRequired] int Phase);
    private sealed record MutationDocument([property: JsonRequired] int Kind, [property: JsonRequired] AdmissionDocument Admission);
    private sealed record RegistryDocument(
        [property: JsonRequired] long Revision, [property: JsonRequired] string? Digest, [property: JsonRequired] bool Manual);
    private sealed record LauncherDocument(
        [property: JsonRequired] string Root, [property: JsonRequired] IdentityDocument? Active,
        [property: JsonRequired] IdentityDocument? Fallback, [property: JsonRequired] LauncherActivationDocument? Pending,
        [property: JsonRequired] IdentityDocument? Failed);
    private sealed record LauncherActivationDocument(
        [property: JsonRequired] IdentityDocument Candidate, [property: JsonRequired] IdentityDocument? PreviousActive,
        [property: JsonRequired] IdentityDocument? PreviousFallback, [property: JsonRequired] int Phase);
    private sealed record IdentityDocument(
        [property: JsonRequired] string Owner, [property: JsonRequired] string OwnerIdentity, [property: JsonRequired] string OwnerDigest,
        [property: JsonRequired] string Version, [property: JsonRequired] int Protocol, [property: JsonRequired] string Path,
        [property: JsonRequired] long Size, [property: JsonRequired] string Digest);
}
