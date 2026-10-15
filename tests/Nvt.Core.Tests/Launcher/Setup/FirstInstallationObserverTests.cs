// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Setup;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Setup;

/// <summary>Checks the non-authoritative progress diagnostics boundary.</summary>
public sealed class FirstInstallationObserverTests : FirstInstallationTestSupport
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProgressDiagnosticsReceivesExceptionWithoutChangingInstallation(bool throwFromDiagnostics)
    {
        var failure = new InvalidOperationException("Progress observer failure.");
        var observed = new List<Exception>();
        Action<Exception> diagnostics = exception =>
        {
            observed.Add(exception);
            if (throwFromDiagnostics)
            {
                throw new IOException("Diagnostics failure.");
            }
        };
        using InstallHarness harness = await CreateInstallHarnessAsync(observerDiagnostics: diagnostics);
        var progress = new FailingProgress(failure);

        ManagedFirstInstallationResult result = await harness.Experience.InstallAndLaunchAsync(
            harness.Plan, progress, TestContext.Current.CancellationToken);

        Assert.Equal(ManagedFirstInstallationOutcome.Completed, result.Outcome);
        Assert.Equal(3, progress.ReportCount);
        Assert.Equal(3, observed.Count);
        Assert.All(observed, exception => Assert.Same(failure, exception));
        Assert.Equal(1, harness.Materializer.Transaction.CompleteCount);
    }

    // Guard: the single install invocation serializes progress callbacks and the final assertion.
    private sealed class FailingProgress(Exception failure) : IProgress<ManagedFirstInstallationProgress>
    {
        internal int ReportCount { get; private set; }

        public void Report(ManagedFirstInstallationProgress value)
        {
            ReportCount++;
            throw failure;
        }
    }
}
