// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Integration;
using Nvt.Core.Launcher.Setup;
using Nvt.Core.TestSupport;
using Xunit;
using static Nvt.Core.Tests.Launcher.Integration.VersionManagementExperienceTestSupport;

namespace Nvt.Core.Tests.Launcher.Integration;

public sealed partial class VersionManagementExperienceFreshInstallationTests
{
    /// <summary>
    /// A candidate query takes no experience lock. Disposal cancels the running check while it holds the generation lock,
    /// and the cancellation callback waits for a query on another thread, which must complete.
    /// </summary>
    [Fact]
    public async Task FreshQueryNeverWaitsForTheGenerationLock()
    {
        using var store = new MemoryStateStore(FreshState());
        var catalog = new QueryOnCancellationCatalog();
        using VersionManagementExperience experience = VersionManagementExperienceTestFactory.Create(
            ManagedAppVersion.Parse("0.10.5"), "managed-root", store, catalog, new HealthyRepository());
        _ = await experience.InitializeAsync(TestContext.Current.CancellationToken, isReadOnly: true);
        catalog.Query = () => experience.InspectFreshInstallationAsync(CancellationToken.None).AsTask();
        Task<VersionManagementSnapshot> running = experience.CheckAsync(false, TestContext.Current.CancellationToken).AsTask();
        await catalog.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        experience.Dispose();

        Assert.True(catalog.QueryRan);
        Assert.Null(catalog.Deadlock);
        Assert.Equal(FreshInstallationCandidateIssue.RegistryNotConfigured, catalog.QueryResult?.Issue);
        try
        {
            _ = await running;
        }
        catch (OperationCanceledException)
        {
            // The disposed experience cancels its running check; either outcome ends the check.
        }
    }

    private sealed class QueryOnCancellationCatalog : IRootCatalogSourceTestDouble
    {
        // Guard: never completed; only the check's cancellation ends the wait on it.
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Guard: TaskCompletionSource orders the check's start with the test body.
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Guard: the owning test sets the query before the check starts.
        internal Func<Task<FreshInstallationCandidateResult>>? Query { get; set; }
        // Guard: only the cancellation callback on the disposing thread writes these; the test reads them after Dispose returns.
        internal bool QueryRan { get; private set; }
        internal FreshInstallationCandidateResult? QueryResult { get; private set; }
        internal TimeoutException? Deadlock { get; private set; }

        public async ValueTask<UpdateCatalogLoadResult> LoadAsync(string sourceRoot, CancellationToken cancellationToken)
        {
            // Cancellation runs callbacks in reverse order: registering after the wait runs the query first,
            // while the disposing thread still holds the experience's generation lock.
            Task wait = _never.Task.WaitAsync(cancellationToken);
            using CancellationTokenRegistration registration = cancellationToken.Register(RunQuery);
            _ = Started.TrySetResult();
            await wait;
            throw new InvalidOperationException("The check was not cancelled.");
        }

        private void RunQuery()
        {
            QueryRan = true;
            try
            {
                QueryResult = TaskBlock.UntilComplete(Task.Run(Query!).WaitAsync(TimeSpan.FromSeconds(10)));
            }
            catch (TimeoutException timeout)
            {
                Deadlock = timeout;
            }
        }
    }
}
