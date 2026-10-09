// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nvt.Core.Threading;

/// <summary>Observes UI event operations and reports their failures through host-owned callbacks.</summary>
/// <remarks>
/// Work begins on the calling thread and synchronization context. The operation owns its busy state,
/// cancellation source, cleanup, and success status; observing a failure does not make the operation successful.
/// Define NVT_CORE_SOURCE_CONSUMPTION to compile this canonical source as internal, keeping the namespace
/// unchanged. Consumer CI must reject compiling the source copy while also referencing the Core package;
/// remove the copy and symbol when adopting the package to avoid a type clash.
/// </remarks>
#if NVT_CORE_SOURCE_CONSUMPTION
internal sealed class UiEventRunner
#else
public sealed class UiEventRunner
#endif
{
    private readonly Action<string, Exception> report;
    private readonly Action<string, Exception> fallbackReport;

    /// <summary>Creates a runner with primary and emergency failure reporters.</summary>
    /// <param name="report">Reports the operation name and original operation exception once.</param>
    /// <param name="fallbackReport">
    /// Reports the same name and original exception if the primary reporter throws. Hosts should supply
    /// a nonthrowing emergency sink; if it also throws, its failure is swallowed.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="report" /> or <paramref name="fallbackReport" /> is null.
    /// </exception>
    public UiEventRunner(Action<string, Exception> report, Action<string, Exception> fallbackReport)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(fallbackReport);
        this.report = report;
        this.fallbackReport = fallbackReport;
    }

    /// <summary>Starts an observed fire-and-forget operation, delegating to <see cref="RunAsync" />.</summary>
    /// <param name="operation">The operation identifier passed unchanged to reporters. Empty names are allowed.</param>
    /// <param name="action">The operation, invoked inside the exception boundary on the calling context.</param>
    /// <param name="cancellationToken">The token supplied to the operation.</param>
    /// <remarks>
    /// Returns when the delegate yields an incomplete task. Synchronous and asynchronous operation failures,
    /// including failures in either reporter, are fully observed by <see cref="RunAsync" />.
    /// Argument validation still throws synchronously at this public boundary.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="operation" /> or <paramref name="action" /> is null.
    /// </exception>
    public void Run(string operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        _ = RunAsync(operation, action, cancellationToken);
    }

    /// <summary>Runs an operation and observes its completion, cancellation, and failures.</summary>
    /// <param name="operation">The operation identifier passed unchanged to reporters. Empty names are allowed.</param>
    /// <param name="action">The operation, invoked inside the exception boundary on the calling context.</param>
    /// <param name="cancellationToken">The token supplied to the operation.</param>
    /// <returns>A task that completes after the operation and any failure reporting have been observed.</returns>
    /// <remarks>
    /// Cancellation is silent only when the supplied token is cancelled and the exception carries that token.
    /// Cancellation with a different token is reported, even if the supplied token is also cancelled.
    /// All other operation exceptions reach the primary reporter once. Reporter failures cannot escape.
    /// Task completion indicates observation, not business success. Argument validation throws synchronously.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="operation" /> or <paramref name="action" /> is null.
    /// </exception>
    public Task RunAsync(string operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(action);
        return RunObservedAsync(operation, action, cancellationToken);
    }

    private async Task RunObservedAsync(string operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (OperationCanceledException exception) when (
            cancellationToken.IsCancellationRequested && exception.CancellationToken == cancellationToken)
        {
            // Cancellation requested by this operation is expected and needs no error report.
        }
        catch (Exception exception)
        {
            try
            {
                report(operation, exception);
            }
            catch (Exception)
            {
                try
                {
                    fallbackReport(operation, exception);
                }
                catch (Exception)
                {
                    // The emergency sink failed too; keep that failure out of the dispatcher.
                }
            }
        }
    }
}
