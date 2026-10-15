// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Setup;

internal sealed record FirstInstallationAdmission : IDisposable
{
    private FirstInstallationAdmission(TimeSpan operationCutoff, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Started = timeProvider.GetTimestamp();
        Deadline = new CancellationTokenSource(operationCutoff, timeProvider);
        try
        {
            Linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Deadline.Token);
        }
        catch
        {
            // A failed token link must release the timer already owned by this invocation.
            Deadline.Dispose();
            throw;
        }
    }

    internal long Started { get; }
    internal CancellationTokenSource Deadline { get; }
    internal CancellationTokenSource Linked { get; }

    internal static FirstInstallationAdmission Create(TimeSpan operationCutoff,
        TimeProvider timeProvider, CancellationToken cancellationToken) =>
        new(operationCutoff, timeProvider, cancellationToken);

    public void Dispose()
    {
        Linked.Dispose();
        Deadline.Dispose();
    }
}
