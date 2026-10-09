// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Threading;
using Nvt.Core.RuntimeQuery;

namespace Nvt.Core.Avalonia.RuntimeQuery;

/// <summary>Creates optional generic commands for tools to register beside their product commands.</summary>
public static partial class RuntimeQueryGenericCommands
{
    /// <summary>Returns help, ping, focus, page, screenshot and exit, in that order, with no startup options.</summary>
    /// <remarks>Run handlers on the UI thread through RuntimeQueryUiThread.Wrap.</remarks>
    public static IReadOnlyList<RuntimeQueryCommand> Create(RuntimeQueryGenericCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Array.AsReadOnly<RuntimeQueryCommand>(
        [
            new("help", RuntimeQueryCommandRisk.ReadOnly, (_, _, cancellationToken) => Run(() => Help(options), cancellationToken)),
            new("ping", RuntimeQueryCommandRisk.ReadOnly, (_, _, cancellationToken) => Run(() => RuntimeQueryResponseEnvelope.Success(new
            {
                toolName = options.ToolName, version = options.ToolVersion, processId = Environment.ProcessId
            }), cancellationToken)),
            new("focus", RuntimeQueryCommandRisk.ChangesState, (_, _, cancellationToken) => Run(() => Focus(options), cancellationToken)),
            new("page", RuntimeQueryCommandRisk.ChangesState, (_, args, cancellationToken) => Run(() => Page(options.Navigation, args), cancellationToken)),
            new("screenshot", RuntimeQueryCommandRisk.ChangesState, (_, args, cancellationToken) => ScreenshotAsync(options, args, cancellationToken)),
            new("exit", RuntimeQueryCommandRisk.ChangesState, (_, args, cancellationToken) => Run(() => Exit(options, args), cancellationToken))
            {
                ReceivesConfirmation = true
            }
        ]);
    }

    private static Task<RuntimeQueryResponseEnvelope> Run(
        Func<RuntimeQueryResponseEnvelope> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(action());
    }

    private static RuntimeQueryResponseEnvelope Help(RuntimeQueryGenericCommandOptions options) =>
        RuntimeQueryResponseEnvelope.Success(options.HelpText is { } text ? text : new
        {
            commands = options.GetCommands().Select(command => new { name = command.Name, risk = command.Risk.ToString() }).ToArray()
        });

    private static RuntimeQueryResponseEnvelope Focus(RuntimeQueryGenericCommandOptions options)
    {
        var window = options.GetMainWindow();
        if (window is null)
        {
            return NoMainWindow();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        return RuntimeQueryResponseEnvelope.Success(new { focused = true });
    }

    private static RuntimeQueryResponseEnvelope Page(
        IRuntimeQueryNavigation navigation, IReadOnlyDictionary<string, string>? args)
    {
        if (args is null || !args.TryGetValue("name", out var name))
        {
            return RuntimeQueryResponseEnvelope.Success(new { pages = navigation.Pages.ToArray(), currentPage = navigation.CurrentPage });
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return RuntimeQueryResponseEnvelope.Failure(RuntimeQueryGenericFailureCodes.InvalidArguments,
                "Argument '--name' requires a page name.");
        }

        if (!navigation.Pages.Contains(name, StringComparer.Ordinal))
        {
            return RuntimeQueryResponseEnvelope.Failure(RuntimeQueryGenericFailureCodes.UnknownPage,
                $"Unknown page '{name}'. Valid pages: {string.Join(", ", navigation.Pages)}.");
        }

        return navigation.SwitchPage(name) switch
        {
            RuntimeQueryPageResult.Switched => RuntimeQueryResponseEnvelope.Success(new { currentPage = navigation.CurrentPage }),
            RuntimeQueryPageResult.NeedsConfirmation => RuntimeQueryResponseEnvelope.Failure(
                RuntimeQueryGenericFailureCodes.UserConfirmationRequired, "Page switching requires confirmation."),
            RuntimeQueryPageResult.Rejected => RuntimeQueryResponseEnvelope.Failure(
                RuntimeQueryGenericFailureCodes.PageRejected, "The page switch was rejected."),
            _ => throw new InvalidOperationException("The tool returned an unknown page result.")
        };
    }

    private static RuntimeQueryResponseEnvelope Exit(
        RuntimeQueryGenericCommandOptions options, IReadOnlyDictionary<string, string>? args)
    {
        RuntimeQueryExitResult result;
        if (options.DecideExitRequest is { } decideExitRequest)
        {
            _ = RuntimeQueryArgumentParser.TryGetBoolArg(args, "confirm", out var confirmed, out var error);
            if (error is not null)
            {
                return error;
            }

            result = decideExitRequest(new RuntimeQueryExitRequest(confirmed));
        }
        else
        {
            result = options.DecideExit();
        }

        switch (result)
        {
            case RuntimeQueryExitResult.Closing:
                var response = RuntimeQueryResponseEnvelope.Success(new { closing = true });
                Dispatcher.UIThread.Post(options.Close, DispatcherPriority.Background);
                return response;
            case RuntimeQueryExitResult.NeedsConfirmation:
                return RuntimeQueryResponseEnvelope.Failure(
                    RuntimeQueryGenericFailureCodes.UserConfirmationRequired, "Exit requires confirmation.");
            case RuntimeQueryExitResult.Rejected:
                return RuntimeQueryResponseEnvelope.Failure(RuntimeQueryGenericFailureCodes.ExitRejected, "Exit was rejected.");
            default:
                throw new InvalidOperationException("The tool returned an unknown exit result.");
        }
    }

    private static RuntimeQueryResponseEnvelope NoMainWindow() => RuntimeQueryResponseEnvelope.Failure(
        RuntimeQueryGenericFailureCodes.NoMainWindow, "The main window is not available.");
}
