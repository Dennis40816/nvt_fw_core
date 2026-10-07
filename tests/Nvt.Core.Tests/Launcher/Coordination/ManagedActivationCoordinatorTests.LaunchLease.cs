// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Activation;
using Nvt.Core.Launcher.Contracts;
using Nvt.Core.Launcher.Persistence;

using Nvt.Core.Launcher.Coordination;

using Xunit;

namespace Nvt.Core.Tests.Launcher.Coordination;

public sealed partial class ManagedActivationCoordinatorTests
{
    /// <summary>An executable lease failure starts nothing and does not create a durable launch tombstone.</summary>
    [Fact]
    public async Task ExecutableLeaseFailureOccursBeforeLaunchJournalWrite()
    {
        VersionManagerState original = State();
        var store = new FakeStateStore(original);
        var process = new FakeProcess(ManagedProcessStartOutcome.Ready);
        var repository = new HealthyRepository
        {
            LaunchLeaseIssue = ManagedExecutableLaunchIssue.Unavailable,
        };

        ManagedLauncherResult result = await new ManagedActivationCoordinator(
            "managed",
            store,
            repository,
            process).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManagedLauncherOutcome.StateUnavailable, result.Outcome);
        Assert.Empty(process.Starts);
        Assert.Same(original, store.State);
    }
}
