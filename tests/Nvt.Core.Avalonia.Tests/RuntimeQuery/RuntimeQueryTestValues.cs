// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Nvt.Core.Avalonia.Threading;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.RuntimeQuery;

/// <summary>Serializes pipe tests and changes to the static UI dispatcher registry.</summary>
[CollectionDefinition("RuntimeQuery", DisableParallelization = true)]
public sealed class RuntimeQueryCollectionDefinition;

internal static class RuntimeQueryTestValues
{
    internal const string Version = "synthetic-v1";
    internal static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan ShutdownBound = TimeSpan.FromMilliseconds(1500);
    internal static readonly FieldInfo DispatcherField = typeof(UiThread).GetField(
        "s_runningDispatcher", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("The UI dispatcher registry field was not found.");

    internal static string NewPipeName() => $"nvt-core-runtime-query-ui-test-{Guid.NewGuid():N}";

    internal static RuntimeQueryError NfhError(RuntimeQueryFailure failure, string? detail) => failure switch
    {
        RuntimeQueryFailure.DispatcherUnavailable => new("IPC_ERROR", "The UI dispatcher is unavailable."),
        RuntimeQueryFailure.HandlerError or RuntimeQueryFailure.ClientError => new("IPC_ERROR", detail!),
        RuntimeQueryFailure.InvalidJson => new("INVALID_JSON", detail!),
        _ => new(failure.ToString(), detail ?? "Unexpected transport failure.")
    };

    internal static RuntimeQueryIpcServer CreateServer(
        string pipeName,
        Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>>? handler = null,
        Action<RuntimeQueryDiagnostic, Exception?>? diagnostic = null) => new(
            pipeName, Version, 5000, 1500, NfhError, diagnostic,
            handler ?? ((_, _, _) => Task.FromResult(RuntimeQueryResponseEnvelope.Success(null))));
}
