// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.IntegrationTestData;

namespace Nvt.Core.Tests.Launcher.Integration;

/// <summary>Frozen self-test consistency predicates, derived outcomes and collection ownership.</summary>
public sealed class EnvironmentSelfTestContractTests
{
    /// <summary>A complete successful attempt preserves every observation.</summary>
    [Fact]
    public void VerifiedAttemptPreservesValues()
    {
        VersionEnvironmentSelfTestAttempt attempt = Attempt();
        Assert.Equal(Entry().SourceRoot, attempt.SourceRoot);
        Assert.Equal(UpdateSourceRegistryEntryStatus.Latest, attempt.Status);
        Assert.Equal(UpdateCatalogLoadIssue.None, attempt.CatalogIssue);
        Assert.Equal(ManagedVersionInstallIssue.None, attempt.PackageIssue);
        Assert.Equal(ManagedAppVersion.Parse("1.0.1"), attempt.NewestVersion);
        Assert.True(attempt.IsVerified);
    }

    /// <summary>Catalog failures and package failures remain distinct legal observations.</summary>
    [Fact]
    public void FailedAttemptsPreserveFrozenOutcomeShape()
    {
        var catalogFailure = new VersionEnvironmentSelfTestAttempt(Entry().SourceRoot,
            UpdateSourceRegistryEntryStatus.Available, UpdateCatalogLoadIssue.SourceMissing, null, null, false);
        var packageFailure = new VersionEnvironmentSelfTestAttempt(Entry().SourceRoot,
            UpdateSourceRegistryEntryStatus.Latest, UpdateCatalogLoadIssue.None,
            ManagedVersionInstallIssue.PackageMismatch, ManagedAppVersion.Parse("1.0.1"), false);
        Assert.False(catalogFailure.IsVerified);
        Assert.Null(catalogFailure.PackageIssue);
        Assert.Null(catalogFailure.NewestVersion);
        Assert.False(packageFailure.IsVerified);
        Assert.Equal(ManagedVersionInstallIssue.PackageMismatch, packageFailure.PackageIssue);
    }

    /// <summary>The frozen predicate permits one optional package fact after a catalog failure.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CatalogFailureRetainsFrozenOptionalFactPredicate(bool hasPackageIssue)
    {
        var attempt = new VersionEnvironmentSelfTestAttempt(Entry().SourceRoot, UpdateSourceRegistryEntryStatus.Latest,
            UpdateCatalogLoadIssue.SourceMissing, hasPackageIssue ? ManagedVersionInstallIssue.None : null,
            hasPackageIssue ? null : ManagedAppVersion.Parse("1.0.1"), false);
        Assert.False(attempt.IsVerified);
    }

    /// <summary>Each invalid attempt fact retains the exact frozen consistency message.</summary>
    [Theory]
    [InlineData("status")]
    [InlineData("catalog-issue")]
    [InlineData("package-issue")]
    [InlineData("missing-version")]
    [InlineData("missing-package")]
    [InlineData("false-verification")]
    [InlineData("failed-verification")]
    [InlineData("catalog-failure-with-package")]
    public void InconsistentAttemptsAreRejected(string fact)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new VersionEnvironmentSelfTestAttempt(
            Entry().SourceRoot,
            fact == "status" ? UpdateSourceRegistryEntryStatus.Deprecated : UpdateSourceRegistryEntryStatus.Latest,
            fact switch
            {
                "catalog-issue" => (UpdateCatalogLoadIssue)999,
                "catalog-failure-with-package" => UpdateCatalogLoadIssue.SourceMissing,
                _ => UpdateCatalogLoadIssue.None,
            },
            fact switch
            {
                "package-issue" => (ManagedVersionInstallIssue)999,
                "missing-package" => null,
                "failed-verification" => ManagedVersionInstallIssue.InvalidPayload,
                _ => ManagedVersionInstallIssue.None,
            },
            fact == "missing-version" ? null : ManagedAppVersion.Parse("1.0.1"),
            fact != "false-verification"));
        Assert.Equal("Environment self-test attempt is inconsistent.", exception.Message);
    }

    /// <summary>Source validation precedes the attempt consistency checks.</summary>
    [Fact]
    public void AttemptChecksSourceBeforeConsistency()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new VersionEnvironmentSelfTestAttempt(
            " ", UpdateSourceRegistryEntryStatus.Deprecated, (UpdateCatalogLoadIssue)999, null, null, true));
        Assert.Equal("sourceRoot", exception.ParamName);
    }

    /// <summary>The attempt ceiling includes its preceding and exact values.</summary>
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void AttemptCountCeilingIsInclusive(int count)
    {
        var outcome = new VersionEnvironmentSelfTestResult(Limits, UpdateSourceRegistryLoadIssue.None,
            Enumerable.Range(0, count).Select(_ => Attempt()).ToArray());
        Assert.True(outcome.IsSuccess);
        Assert.Equal(count, outcome.Attempts.Count);
    }

    /// <summary>All inconsistent result shapes retain the frozen message.</summary>
    [Theory]
    [InlineData("registry-issue")]
    [InlineData("authority-issue")]
    [InlineData("revision")]
    [InlineData("both-issues")]
    [InlineData("empty-success")]
    [InlineData("attempts-on-failure")]
    [InlineData("attempt-ceiling")]
    public void InconsistentResultsAreRejected(string fact)
    {
        UpdateSourceRegistryLoadIssue registryIssue = fact switch
        {
            "registry-issue" => (UpdateSourceRegistryLoadIssue)999,
            "both-issues" or "attempts-on-failure" => UpdateSourceRegistryLoadIssue.RegistryUnavailable,
            _ => UpdateSourceRegistryLoadIssue.None,
        };
        UpdateSourceRegistryIssue authorityIssue = fact switch
        {
            "authority-issue" => (UpdateSourceRegistryIssue)999,
            "both-issues" => UpdateSourceRegistryIssue.RevisionConflict,
            _ => UpdateSourceRegistryIssue.None,
        };
        int count = fact switch { "empty-success" => 0, "attempt-ceiling" => 17, _ => 1 };
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new VersionEnvironmentSelfTestResult(
            Limits, registryIssue, Enumerable.Range(0, count).Select(_ => Attempt()).ToArray(),
            authorityIssue: authorityIssue, acceptedRegistryRevision: fact == "revision" ? -1 : null));
        Assert.Equal("Environment self-test result is inconsistent.", exception.Message);
    }

    /// <summary>Load and authority failures permit no attempts and retain optional revision zero.</summary>
    [Fact]
    public void FailedResultsRemainTypedAndReadOnly()
    {
        var loadFailure = new VersionEnvironmentSelfTestResult(Limits, UpdateSourceRegistryLoadIssue.RegistryMissing, [],
            acceptedRegistryRevision: 0);
        var authorityFailure = new VersionEnvironmentSelfTestResult(Limits, UpdateSourceRegistryLoadIssue.None, [],
            authorityIssue: UpdateSourceRegistryIssue.RevisionRollback, acceptedRegistryRevision: 7);
        Assert.False(loadFailure.IsSuccess);
        Assert.Equal(0, loadFailure.AcceptedRegistryRevision);
        Assert.Empty(loadFailure.Attempts);
        Assert.Empty(loadFailure.Replicas);
        Assert.False(authorityFailure.IsSuccess);
        Assert.Equal(UpdateSourceRegistryIssue.RevisionRollback, authorityFailure.AuthorityIssue);
        Assert.Equal(7, authorityFailure.AcceptedRegistryRevision);
    }

    /// <summary>Read-only results require one verified source rather than merely successful catalog reads.</summary>
    [Fact]
    public void SuccessRequiresVerifiedAttempt()
    {
        var failed = new VersionEnvironmentSelfTestAttempt(Entry().SourceRoot, UpdateSourceRegistryEntryStatus.Latest,
            UpdateCatalogLoadIssue.SourceMissing, null, null, false);
        Assert.False(new VersionEnvironmentSelfTestResult(Limits, UpdateSourceRegistryLoadIssue.None, [failed]).IsSuccess);
        Assert.True(new VersionEnvironmentSelfTestResult(Limits, UpdateSourceRegistryLoadIssue.None, [failed, Attempt()]).IsSuccess);
    }

    /// <summary>Each published collection owns a read-only copy of the supplied array.</summary>
    [Fact]
    public void SelfTestOwnsCollectionsAndRechecksLimits()
    {
        VersionEnvironmentSelfTestAttempt original = Attempt();
        VersionEnvironmentSelfTestAttempt[] attempts = [original, original];
        UpdateSourceRegistryReplicaObservation first = new(1, UpdateSourceRegistryLoadIssue.None, 1, true);
        UpdateSourceRegistryReplicaObservation[] observations = [first];
        var outcome = new VersionEnvironmentSelfTestResult(Limits, UpdateSourceRegistryLoadIssue.None, attempts, observations);
        attempts[0] = new(Entry().SourceRoot, UpdateSourceRegistryEntryStatus.Available,
            UpdateCatalogLoadIssue.SourceMissing, null, null, false);
        observations[0] = new(2, UpdateSourceRegistryLoadIssue.RegistryMissing, null, false);
        Assert.Same(original, outcome.Attempts[0]);
        Assert.Same(first, outcome.Replicas[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<VersionEnvironmentSelfTestAttempt>)outcome.Attempts)[0] = attempts[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<UpdateSourceRegistryReplicaObservation>)outcome.Replicas)[0] = observations[0]);
        Assert.Throws<ArgumentException>(() => new VersionEnvironmentSelfTestResult(
            new VersionManagementLimits(128, 134_217_728, 65_536, 512, 1, 128),
            UpdateSourceRegistryLoadIssue.None, outcome.Attempts));
        Assert.Throws<ArgumentNullException>(() => new VersionEnvironmentSelfTestResult(null!, UpdateSourceRegistryLoadIssue.None, []));
        Assert.Throws<ArgumentNullException>(() => new VersionEnvironmentSelfTestResult(Limits, UpdateSourceRegistryLoadIssue.None, null!));
    }

    private static VersionEnvironmentSelfTestAttempt Attempt() => new(Entry().SourceRoot,
        UpdateSourceRegistryEntryStatus.Latest, UpdateCatalogLoadIssue.None, ManagedVersionInstallIssue.None,
        ManagedAppVersion.Parse("1.0.1"), true);
}
