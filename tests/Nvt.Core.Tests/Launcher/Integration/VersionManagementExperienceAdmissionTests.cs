// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Nvt.Core.Launcher.Persistence;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;
using TransactionRepository = Nvt.Core.Tests.Launcher.Coordination.ManagedMutationTestSupport.TransactionRepository;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Complete source readmission and exact custody boundary cases.</summary>
public sealed class VersionManagementExperienceAdmissionTests
{
    /// <summary>Readmission retains every await and fence position, including idempotent zero-save commits.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistryCommitPreservesTheCompleteFrozenTrace(bool idempotent)
    {
        string latest = SourcePath("complete-trace");
        VersionManagerState initial = Initial(latest);
        using var store = new MemoryStateStore(initial);
        var trace = new List<string>();
        store.BeforeAcquire = () => trace.Add("writer");
        store.BeforeLoad = () => trace.Add("reload");
        store.BeforeSave = () => trace.Add("save");
        var repository = new HealthyRepository { BeforeVerification = () => trace.Add("verify") };
        var registry = IntegrationTestData.Stub(Registry(idempotent ? 1 : 2,
            idempotent ? FirstRegistryDigest : SecondRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest)));
        var tracedRegistry = new IntegrationTestData.DelegateRegistry(token =>
        {
            trace.Add("registry");
            return registry.LoadAsync(token);
        });
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new TraceCatalog(trace, Catalog("0.10.6")), repository, tracedRegistry, new TraceFence(trace, store));
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken, isReadOnly: true);
        trace.Clear();

        VersionManagementSnapshot observed = await experience.CheckAsync(false, TestContext.Current.CancellationToken);

        string[] admission = ["registry", "catalog", "verify", "writer", "reload", "fence", "registry", "catalog", "verify", "fence"];
        Assert.Equal(idempotent ? admission : [.. admission, "save"], trace);
        Assert.Equal(idempotent ? 0 : 1, store.SaveCount);
        Assert.Equal(UpdateSourceRegistryIssue.None, observed.RegistryIssue);
        if (idempotent) { Assert.Same(initial, observed.State); }
    }

    /// <summary>Every durable field participates in the lease-held snapshot comparison.</summary>
    [Theory]
    [InlineData("root")]
    [InlineData("source")]
    [InlineData("active")]
    [InlineData("fallback")]
    [InlineData("admissions")]
    [InlineData("admissionIdentity")]
    [InlineData("admissionManifest")]
    [InlineData("activation")]
    [InlineData("installJournal")]
    [InlineData("deleteJournal")]
    [InlineData("failed")]
    [InlineData("retention")]
    [InlineData("revision")]
    [InlineData("digest")]
    [InlineData("pin")]
    public async Task EveryDurableFieldDriftRejectsRegistryReadmission(string field)
    {
        string latest = SourcePath("durable-drift");
        VersionManagerState initial = Initial(latest);
        VersionManagerState changed = ChangeState(initial, field);
        using var store = new ReloadChangedStateStore(initial, changed);
        var repository = new TransactionRepository(initial.Admissions, "0.10.7")
        {
            DeleteIssue = ManagedVersionDeleteIssue.DeleteFailed,
        };
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new PathCatalogSource((latest, new(Catalog("0.10.8"), UpdateCatalogLoadIssue.None))), repository,
            new SequenceRegistrySource(RegistryWithPublication("0.10.8", 1, CatalogContentDigest,
                3, SecondRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot observed = await experience.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, observed.RegistryIssue);
        Assert.Same(changed, store.State);
        Assert.Equal(0, store.SaveCount);
        Assert.Null(observed.Catalog);
        Assert.Null(observed.VerifiedCandidate);
    }

    /// <summary>Every row fact, including historical metadata, belongs to publication equality.</summary>
    [Theory]
    [InlineData("version", false)]
    [InlineData("published", false)]
    [InlineData("path", false)]
    [InlineData("size", false)]
    [InlineData("packageHash", false)]
    [InlineData("manifestHash", false)]
    [InlineData("notes", false)]
    [InlineData("policy", false)]
    [InlineData("version", true)]
    [InlineData("published", true)]
    [InlineData("path", true)]
    [InlineData("size", true)]
    [InlineData("packageHash", true)]
    [InlineData("manifestHash", true)]
    [InlineData("notes", true)]
    [InlineData("policy", true)]
    [InlineData("count", true)]
    public async Task EveryCatalogFieldDriftRejectsRegistryReadmission(string field, bool older)
    {
        string latest = SourcePath("catalog-drift");
        VersionManagerState initial = Initial(latest);
        using var store = new MemoryStateStore(initial);
        UpdateCatalogVersionSnapshot newest = Row("0.10.8");
        UpdateCatalogVersionSnapshot historical = Row("0.10.4");
        UpdateCatalogSnapshot before = UpdateCatalogSnapshot.Create(IntegrationTestData.Limits, [newest, historical]);
        UpdateCatalogSnapshot after = UpdateCatalogSnapshot.Create(IntegrationTestData.Limits, field == "count"
            ? [newest] : older ? [newest, ChangeRow(historical, field)] : [ChangeRow(newest, field), historical]);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new SequencePathCatalogSource(latest, before, after), new HealthyRepository(),
            new SequenceRegistrySource(RegistryWithPublication("0.10.8", 1, CatalogContentDigest,
                3, SecondRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot observed = await experience.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateSourceRegistryIssue.RegistryChanged, observed.RegistryIssue);
        Assert.Same(initial, store.State);
        Assert.Equal(0, store.SaveCount);
        Assert.Null(observed.VerifiedCandidate);
    }

    /// <summary>Both launcher fence checks run at their original readmission positions.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task EachFenceRecheckRejectsWithoutSaving(int denyAt, int verifications)
    {
        string latest = SourcePath("fence");
        VersionManagerState initial = Initial(latest);
        using var store = new MemoryStateStore(initial);
        var repository = new CountingRepository();
        var fence = new SequenceFence(denyAt);
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))), repository,
            new SequenceRegistrySource(Registry(3, SecondRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))), fence);

        VersionManagementSnapshot observed = await experience.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, observed.RegistryIssue);
        Assert.Equal(denyAt, fence.LoadCount);
        Assert.Equal(verifications, repository.VerifiedVersions.Count);
        Assert.Equal(0, store.SaveCount);
        Assert.Same(initial, store.State);
    }

    /// <summary>Historical acquired flags on forged or stale receipts grant no source commit authority.</summary>
    [Theory]
    [InlineData("arbitrary")]
    [InlineData("foreign")]
    [InlineData("disposed")]
    public async Task ExperienceRejectsInvalidWriterBeforeDurableReadmission(string kind)
    {
        string latest = SourcePath("writer");
        VersionManagerState initial = Initial(latest);
        using var store = new WriterOverrideStore(initial);
        using var foreign = new MemoryStateStore(initial);
        using var arbitrary = new CancellationTokenSource();
        using VersionManagerWriteLeaseResult writer = await WriterForAsync(kind, arbitrary, kind == "foreign" ? foreign : store);
        if (kind == "disposed") { writer.Dispose(); }
        store.Writer = writer;
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new PathCatalogSource((latest, new(Catalog("0.10.6"), UpdateCatalogLoadIssue.None))), repository,
            new SequenceRegistrySource(Registry(3, SecondRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))));
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken, isReadOnly: true);
        int reads = store.LoadCount;

        VersionManagementSnapshot observed = await experience.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, observed.RegistryIssue);
        Assert.Equal(reads, store.LoadCount);
        _ = Assert.Single(repository.VerifiedVersions);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Catalogs admitted under wider producer limits cannot bypass consumer ceilings.</summary>
    [Theory]
    [InlineData("rows")]
    [InlineData("bytes")]
    [InlineData("notes")]
    [InlineData("path")]
    public async Task ConsumerRechecksCatalogLimitsBeforeVerification(string field)
    {
        using var store = new MemoryStateStore(State([Admission("0.10.5")], "0.10.5", "0.10.5"));
        var limits = new VersionManagementLimits(1, 42, 3, 12, 16, 128);
        UpdateCatalogVersionSnapshot row = IntegrationTestData.Package(6,
            bytes: field == "bytes" ? 43 : 42, notes: field == "notes" ? "界a" : "界",
            pathCharacters: field == "path" ? 13 : 12);
        UpdateCatalogSnapshot catalog = UpdateCatalogSnapshot.Create(IntegrationTestData.Limits,
            field == "rows" ? [row, IntegrationTestData.Package(7)] : [row]);
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store, new FixedCatalogSource(catalog), repository, limits: limits);

        VersionManagementSnapshot observed = await experience.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCatalogLoadIssue.InvalidManifest, observed.CatalogIssue);
        Assert.Null(observed.Catalog);
        Assert.Empty(repository.VerifiedVersions);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Normalized registry values must also pass the consumer's descriptor and ceilings.</summary>
    [Theory]
    [InlineData("entries")]
    [InlineData("identityLength")]
    [InlineData("identity")]
    public async Task ConsumerRechecksRegistryBeforeCatalogContact(string field)
    {
        using var store = new MemoryStateStore(State([Admission("0.10.5")], "0.10.5", "0.10.5"));
        var limits = new VersionManagementLimits(128, 134_217_728, 65_536, 512, 1,
            field == "identityLength" ? 1 : 128);
        UpdateSourceRegistrySnapshot snapshot = IntegrationTestData.Snapshot(registryId:
            field == "identity" ? "foreign-fixture" : "fixture-registry", entries: field == "entries"
                ? [IntegrationTestData.Entry(), IntegrationTestData.Entry(1, UpdateSourceRegistryEntryStatus.Available)] : null);
        var catalogs = new PathCatalogSource();
        var repository = new CountingRepository();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store, catalogs, repository,
            IntegrationTestData.Stub(IntegrationTestData.Success(snapshot)), limits: limits);

        VersionManagementSnapshot observed = await experience.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateSourceRegistryIssue.Invalid, observed.RegistryIssue);
        Assert.Empty(catalogs.LoadedRoots);
        Assert.Empty(repository.VerifiedVersions);
        Assert.Equal(0, store.SaveCount);
    }

    private static VersionManagerState Initial(string source) => State(
        [Admission("0.10.5"), Admission("0.10.4"), Admission("0.10.6")], "0.10.5", "0.10.4", source,
        new(1, FirstRegistryDigest, false));

    /// <summary>Every nested activation and filesystem journal value belongs to durable identity.</summary>
    [Theory]
    [InlineData("candidate")]
    [InlineData("candidateIdentity")]
    [InlineData("priorActive")]
    [InlineData("priorFallback")]
    [InlineData("phase")]
    [InlineData("mutationKind")]
    [InlineData("mutationVersion")]
    [InlineData("mutationIdentity")]
    [InlineData("mutationManifest")]
    public async Task EveryJournalFieldDriftRejectsRegistryReadmission(string field)
    {
        string latest = SourcePath("journal-drift");
        VersionManagerState basis = Initial(latest);
        bool mutation = field.StartsWith("mutation", StringComparison.Ordinal);
        var activation = new PendingVersionActivation(ManagedAppVersion.Parse("0.10.6"),
            Admission("0.10.6").AdmissionIdentity, basis.ActiveVersion, basis.LastKnownGoodVersion);
        var pending = new PendingManagedVersionMutation(ManagedVersionMutationKind.Install, Admission("0.10.7"));
        VersionManagerState initial = WithJournals(basis, mutation ? null : activation, mutation ? pending : null);
        PendingVersionActivation changedActivation = field switch
        {
            "candidate" => activation with { CandidateVersion = ManagedAppVersion.Parse("0.10.4"), CandidateAdmissionIdentity = Admission("0.10.4").AdmissionIdentity },
            "candidateIdentity" => activation with { CandidateAdmissionIdentity = "changed" },
            "priorActive" => activation with { PreviousActiveVersion = basis.LastKnownGoodVersion },
            "priorFallback" => activation with { PreviousLastKnownGoodVersion = basis.ActiveVersion },
            "phase" => activation with { Phase = VersionActivationPhase.CandidateLaunchRecorded },
            _ => activation,
        };
        PendingManagedVersionMutation changedMutation = field switch
        {
            "mutationKind" => new(ManagedVersionMutationKind.Delete, Admission("0.10.4")),
            "mutationVersion" => pending with { Admission = Admission("0.10.9") },
            "mutationIdentity" => pending with { Admission = pending.Admission with { AdmissionIdentity = "changed" } },
            "mutationManifest" => pending with { Admission = pending.Admission with { ReleaseManifestSha256 = SecondRegistryDigest } },
            _ => pending,
        };
        VersionManagerState changed = WithJournals(basis, mutation ? null : changedActivation, mutation ? changedMutation : null);
        using var store = new ReloadChangedStateStore(initial, changed);
        var repository = new TransactionRepository(basis.Admissions, "0.10.7")
        {
            DeleteIssue = ManagedVersionDeleteIssue.DeleteFailed,
            InventoryResultOverride = JournalInventory(basis),
        };
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store,
            new PathCatalogSource((latest, new(Catalog("0.10.8"), UpdateCatalogLoadIssue.None))), repository,
            new SequenceRegistrySource(RegistryWithPublication("0.10.8", 1, CatalogContentDigest,
                3, SecondRegistryDigest, (latest, UpdateSourceRegistryEntryStatus.Latest))));

        VersionManagementSnapshot observed = await experience.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateSourceRegistryIssue.StateUnavailable, observed.RegistryIssue);
        Assert.Same(changed, store.State);
        Assert.Equal(0, store.SaveCount);
    }

    private static VersionManagerState WithJournals(VersionManagerState basis,
        PendingVersionActivation? activation, PendingManagedVersionMutation? mutation) => VersionManagerState.Create(
            basis.UpdateSource, basis.ActiveVersion, basis.LastKnownGoodVersion, basis.Admissions.Select(admission =>
                activation is not null && activation.CandidateVersion == admission.Version
                    ? admission with { AdmissionIdentity = activation.CandidateAdmissionIdentity } : admission), activation,
            basis.FailedActivationVersion, basis.RetentionReviewDue, mutation, basis.ManagedRootIdentity, basis.SourceRegistryState);

    private static readonly string[] _unadmittedVersions = ["0.10.7", "0.10.9"];

    private static async Task<VersionManagerWriteLeaseResult> WriterForAsync(
        string kind, CancellationTokenSource arbitrary, MemoryStateStore source)
    {
        if (kind == "arbitrary") { return new(VersionManagerWriteLeaseIssue.None, arbitrary); }
        return await source.TryAcquireWriteLeaseAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    private static ManagedVersionInventoryReadResult JournalInventory(VersionManagerState basis) =>
        ManagedVersionInventoryReadResult.Success(ManagedVersionInventory.Create(
            basis.Admissions.Select(admission => new InstalledVersionSnapshot(admission.Version,
                admission.AdmissionIdentity, ManagedVersionIntegrity.Healthy, null,
                admission.Version == basis.ActiveVersion, admission.Version == basis.LastKnownGoodVersion))
            .Concat(_unadmittedVersions.Select(version => new InstalledVersionSnapshot(
                ManagedAppVersion.Parse(version), "unadmitted", ManagedVersionIntegrity.Damaged,
                ManagedVersionDamageReason.UnexpectedPath, false, false, ManagedVersionAdmissionState.Unadmitted)))));

    private static VersionManagerState ChangeState(VersionManagerState state, string field)
    {
        ManagedVersionAdmission[] admissions = [.. state.Admissions];
        ManagedVersionAdmission target = admissions[0];
        if (field == "admissionIdentity") { admissions[0] = target with { AdmissionIdentity = "changed" }; }
        if (field == "admissionManifest") { admissions[0] = target with { ReleaseManifestSha256 = SecondRegistryDigest }; }
        if (field == "admissions") { admissions = [.. admissions, Admission("0.10.3")]; }
        return VersionManagerState.Create(field == "source" ? SourcePath("changed") : state.UpdateSource,
            field == "active" ? ManagedAppVersion.Parse("0.10.4") : state.ActiveVersion,
            field == "fallback" ? state.ActiveVersion : state.LastKnownGoodVersion, admissions,
            field == "activation" ? new(ManagedAppVersion.Parse("0.10.6"), Admission("0.10.6").AdmissionIdentity,
                state.ActiveVersion, state.LastKnownGoodVersion) : null,
            field == "failed" ? ManagedAppVersion.Parse("0.10.4") : null, field == "retention",
            field == "installJournal" ? new(ManagedVersionMutationKind.Install, Admission("0.10.7"))
                : field == "deleteJournal" ? new(ManagedVersionMutationKind.Delete, Admission("0.10.4")) : null,
            field == "root" ? "other-managed-root" : state.ManagedRootIdentity,
            new(field == "revision" ? 2 : 1, field == "digest" ? SecondRegistryDigest : FirstRegistryDigest, field == "pin"));
    }

    private static UpdateCatalogVersionSnapshot ChangeRow(UpdateCatalogVersionSnapshot row, string field) =>
        UpdateCatalogVersionSnapshot.Create(134_217_728, 65_536,
            field == "version" ? ManagedAppVersion.Parse("0.10.7") : row.Version,
            field == "published" ? row.PublishedAt.AddTicks(1) : row.PublishedAt,
            field == "path" ? new("packages/changed.zip") : row.PackagePath,
            field == "size" ? row.PackageSize + 1 : row.PackageSize,
            field == "packageHash" ? SecondRegistryDigest : row.PackageSha256,
            field == "manifestHash" ? SecondRegistryDigest : row.ReleaseManifestSha256,
            field == "notes" ? "changed" : row.ReleaseNotes,
            field == "policy" ? UpdateNotificationPolicy.ManualOnly : row.NotificationPolicy);

    private sealed class SequenceFence(int denyAt) : ILauncherMutationFence
    {
        internal int LoadCount { get; private set; }
        public ValueTask<LauncherMutationProtection> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new LauncherMutationProtection(LauncherMutationFenceIssue.None,
                ++LoadCount == denyAt, null, null, []));
        }
        public ValueTask<LauncherMutationFenceIssue> RetireLastKnownGoodOwnerAsync(
            ManagedVersionAdmission expectedOwner, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class TraceCatalog(List<string> trace, UpdateCatalogSnapshot catalog) : IRootCatalogSourceTestDouble
    {
        public ValueTask<UpdateCatalogLoadResult> LoadAsync(string sourceRoot, CancellationToken cancellationToken)
        {
            trace.Add("catalog");
            return ValueTask.FromResult(new UpdateCatalogLoadResult(catalog, UpdateCatalogLoadIssue.None, new(1, CatalogContentDigest)));
        }
    }

    private sealed class TraceFence(List<string> trace, MemoryStateStore store) : ILauncherMutationFence
    {
        public ValueTask<LauncherMutationProtection> LoadAsync(CancellationToken cancellationToken)
        {
            Assert.True(store.HoldsWriter);
            trace.Add("fence");
            return ValueTask.FromResult(new LauncherMutationProtection(LauncherMutationFenceIssue.None, false, null, null, []));
        }
        public ValueTask<LauncherMutationFenceIssue> RetireLastKnownGoodOwnerAsync(
            ManagedVersionAdmission expectedOwner, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class WriterOverrideStore(VersionManagerState state) : MemoryStateStore(state)
    {
        internal VersionManagerWriteLeaseResult? Writer { get; set; }
        public override ValueTask<VersionManagerWriteLeaseResult> TryAcquireWriteLeaseAsync(
            TimeSpan waitTimeout, CancellationToken cancellationToken) => Writer is { } writer
                ? ValueTask.FromResult(writer) : base.TryAcquireWriteLeaseAsync(waitTimeout, cancellationToken);
    }
}
