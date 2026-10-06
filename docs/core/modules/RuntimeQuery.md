[English](RuntimeQuery.md) | [中文](RuntimeQuery.zh-TW.md)

# RuntimeQuery

## Purpose

`Nvt.Core.RuntimeQuery` provides the JSON envelopes and local named-pipe transport extracted from FreeformHelper (NFH). It serves one request per connection, sequentially, using byte-mode asynchronous pipes. It has no Avalonia or NLog dependency and does not interpret product commands.

## Commands and arguments

Core now provides command routing, request checks, and five generic argument helpers.

| API | Contract |
| --- | --- |
| `RuntimeQueryCommandRouter(handlers)` | Uses the caller's delegate dictionary without adding commands or changing its key comparer. |
| `RegisteredCommands` | Read-only names in dictionary enumeration order at construction. Build the dictionary in registration order first. |
| `RouteAsync(commandText, args)` | Trims and lowercases the command with invariant culture. Passes the original argument dictionary to the handler. |
| `ExecuteAsync(request, expectedVersion)` | Checks null first, compares versions with ordinal equality second, then routes. The tool supplies its version. |
| `RuntimeQueryArgumentParser.TryGetIntArg` | Parses invariant integers and checks an inclusive range. |
| `TryGetIntListArg` | Splits on commas, trims items, removes empty items, and checks each integer in order. |
| `TryGetDoubleArg` | Parses invariant floats with thousands separators. Rejects NaN, infinity, and values outside the inclusive range. |
| `TryGetStringArg` | Reads a nonblank value and trims surrounding whitespace. |
| `TryGetBoolArg` | Accepts true/false, 1/0, on/off, and yes/no with ordinal case-insensitive matching. |

The handler type remains `Func<IReadOnlyDictionary<string, string>?, Task<RuntimeQueryResponseEnvelope>>`.
The router returns the handler's response and lets handler exceptions escape.
Core has no version constant.

Unknown names return `UNKNOWN_COMMAND` with `Unknown query command '{commandText}'.`.
The message retains the original command text, including whitespace. Null text appears as an empty string.
Null requests return `INVALID_REQUEST` with `Request is null.`.
Version mismatches return `UNSUPPORTED_VERSION` with `Unsupported request version '{request.Version}'. Expected '{expectedVersion}'.`.

Missing, null, or blank argument values return false with default outputs and no error.
Invalid supplied values return `INVALID_ARGUMENTS` with the frozen message.
Integer lists keep order and duplicates. A failed item clears the output list.
Double parsing uses invariant culture, but range messages format limits with the current culture.
For example, French culture still parses `1,5` as 15 and formats a 1.5 limit as `1,5`.

### Frozen command baseline

This command baseline is separate from the transport baseline below.

- Source repository: `Dennis40816/nvt-freeform-helper`.
- Source ref: `1.3.x`.
- Full frozen commit: `847cc4530ed098ceb56aa1bd8beda77bcd1ec227`.
- Extracted paths:
  - `src/FreeformHelper.UI/Services/RuntimeQueryCommandRouter.cs`: all 25 lines.
  - `src/FreeformHelper.UI/Services/RuntimeQueryUseCase.cs`: lines 51–92, for registration context and request checks.
  - `src/FreeformHelper.UI/Services/RuntimeQueryArgumentParser.cs`: generic helpers at lines 161–350.
- Envelope and version reference: `src/FreeformHelper.UI/Services/RuntimeQueryProtocol.cs` at the same commit.
- Test references: every `RuntimeQueryUseCaseTests*.cs` file and `RuntimeQueryIpcTests.cs` listed under NFH switch-over evidence.

The host commit message must include this repository, ref, full SHA, and extracted paths.

### Verification and switch-over

`RuntimeQueryCommandCases.cs` holds reusable inputs and literal expected outputs without Core envelope types.
`RuntimeQueryCommandRouterTests` and `RuntimeQueryArgumentParserTests` run those cases against Core.
`RuntimeQuerySourceContractTests` ports the routing boundaries of source request tests and their handler-exception contract.
Synthetic handlers replace product fixtures. Product output assertions stay in NFH.
The source has no direct tests for the router, request checks, or generic helpers.

To switch NFH to Core:

1. Replace its router with `RuntimeQueryCommandRouter`. Keep its handler table and each handler's check order.
2. Replace its request checks with `router.ExecuteAsync(request, RuntimeQueryProtocol.Version)` at the existing call position.
3. Replace its five generic helpers with `RuntimeQueryArgumentParser`. Keep product selection parsers from lines 7–159.
4. Reuse the case tables with adapters for the source types and Core types. Compare outcomes, output values, codes, and exact messages.
5. Run the full test list under NFH switch-over evidence before and after replacement.
6. Require equal test names, counts, pass/fail results, and skipped results.

The frozen files define 24 tests: 19 use-case tests and five IPC tests.
Record the complete test list in each adoption run. Do not remove or rename tests to accept a difference.
The source request-check adapter uses its protocol version. Other expected-version cases verify Core's caller-supplied version contract.

Command-line handling and the UI-thread step follow in later tasks.
This extraction does not adopt Core in the source tool.

## Command risk and confirmation

The confirmation guard is off unless the caller enables it.
This behavior was approved on 2026-10-06 and has no source tool baseline.

| Public API | Contract |
| --- | --- |
| `RuntimeQueryCommandRisk` | Defines `ReadOnly`, `ChangesState`, and `WritesData`. |
| `RuntimeQueryCommand(Name, Risk, Handler)` | A sealed record with the command name, risk, and existing handler type. |
| `RuntimeQueryCommandRouter(commands, requireConfirmation)` | Builds an ordinal handler table from a command list. `RegisteredCommands` preserves registration order. |

- `ReadOnly` commands only read state.
- `ChangesState` commands change UI state, such as the page or selection. They write no files and change no data.
- `WritesData` commands write files or change the tool's data.

The list constructor rejects null lists, commands, names, and handlers with `ArgumentNullException`.
It rejects duplicate names with `ArgumentException`.
It also rejects names that differ from their trimmed, lowercase invariant form.
The dictionary constructor keeps its existing behavior and never requires confirmation.

With `requireConfirmation: false`, handlers receive the original arguments, including any `confirm` key.
NFH, the FreeformHelper tool, first switches to Core with this flag set to false.
Turning the guard on is a separate, visible change.

With `requireConfirmation: true`, `RouteAsync` first finds the handler.
Unknown commands still return `UNKNOWN_COMMAND` before confirmation checks.
`ExecuteAsync` still checks null requests, then versions, then routing.

For `WritesData`, the router reads `confirm` with `RuntimeQueryArgumentParser.TryGetBoolArg`.
The helper accepts true/false, 1/0, on/off, and yes/no.
An invalid value returns the helper's exact `INVALID_ARGUMENTS` error.
A missing, blank, or false value returns `CONFIRMATION_REQUIRED` with this exact message:

```text
Command '{name}' writes files or changes data. Add --confirm to run it.
```

The message uses the normalized command name. The handler does not run after either error.
For every risk level, the enabled guard removes the ordinal key `confirm` before calling the handler.
It copies other keys and values unchanged into a new ordinal dictionary.
The handler receives null when no keys remain. Null arguments stay null.
Handlers no longer receive the `confirm` key when the guard is on.
The existing command-line grammar already maps `--confirm` to `"confirm": "true"`.

`RuntimeQueryConfirmationCases` holds the new inputs and literal expected outputs.
`RuntimeQueryCommandConfirmationTests` compares every existing `RuntimeQueryCommandCases` row through both constructors with the guard off.
It checks exact responses, handler calls, argument forwarding, registration checks, and error order.
Run the RuntimeQuery test command below to verify both constructors and the enabled guard.

For zero difference, NFH must keep the guard off and run the existing NFH switch-over checks below.
Compare test results, stdout and stderr bytes, pipe frames, error codes and messages, and process exit codes.
The separate guard change must expect `CONFIRMATION_REQUIRED` for unconfirmed `WritesData` commands and remove `confirm` from handler arguments.

## Public API

| API | Contract |
| --- | --- |
| `RuntimeQueryRequest(Version, Command, Args)` | Request fields in that order; arguments may be null. |
| `RuntimeQueryError(Code, Message)` | Caller-owned error code and message. |
| `RuntimeQueryResponseEnvelope(Ok, Data, Error)` | Response fields in that order; `Success(data)` and `Failure(code, message)` retain explicit null properties. |
| `RuntimeQueryProtocol.CompactJsonOptions` | Camel-case property names, explicit nulls, default JSON escaping, case-sensitive deserialization, no indentation. Dictionary keys retain their casing. |
| `RuntimeQueryProtocol.PrettyJsonOptions` | The same settings with two-space indentation and the serializer's default platform line endings. Used for caller output, not pipe messages. |
| `RuntimeQueryIpcServer(...)` | An instance configured with pipe name, protocol version, positive read and shutdown timeouts in milliseconds, error callback, diagnostic callback, and request handler. |
| `Start()` / `DisposeAsync()` | Start once; repeated starts while running and repeated disposal are harmless. Starting after disposal throws. Disposal closes the active pipe, cancels transport and handler waits, and bounds the wait for cancellation callbacks and the run loop. |
| `RuntimeQueryIpcClient.SendRequest(pipeName, request, timeoutMs, error)` | Synchronously sends one request with a positive total connection/write/read budget. Connection elapsed time is deducted before the write/read timer, retaining the source's integer millisecond rounding and minimum remaining budget of one millisecond. |

The server handler is `Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>>`. It receives the deserialized request, configured protocol version and shutdown token. Empty lines and malformed JSON are rejected by the transport; a JSON null literal and version mismatches reach the handler, exactly as in the frozen transport. Core now provides null/version checks and command routing. The tool keeps its handler table, product parsers, command line, and UI dispatch. Transport tests also pin the original null and version error envelopes.

The error callback is `Func<RuntimeQueryFailure, string?, RuntimeQueryError>`. The detail is the unchanged exception message for `InvalidJson`, `HandlerError`, `IoError` and `ClientError`, and null otherwise. Core wraps the resulting error in a failed envelope. `RuntimeQueryFailure` identifies these failures plus `RequestTimeout`, `EmptyRequest`, `EmptyResponse`, `InvalidResponse`, `ConnectionTimeout` and `ClientTimeout`; its enum names are not wire error codes.

The optional diagnostic callback is `Action<RuntimeQueryDiagnostic, Exception?>`. Events cover start, stop, pipe creation failure, connection failure, request failure, handler failure, shutdown failure and shutdown timeout. The caller supplies logging text and logging dependencies. Error and diagnostic callbacks must return promptly and must not throw.

The read limit, shutdown bound, client budget and protocol identity have no product defaults in Core. NFH supplies its existing 5,000 ms read timeout, 1,500 ms default CLI budget, protocol version, pipe name, error mappings and logging. The frozen shutdown tests use a 1,500 ms completion bound; the extracted server accepts that bound explicitly.

Pipe writers use compact JSON, UTF-8 without a BOM, and `StreamWriter.WriteLineAsync` with the platform newline (CRLF on Windows). Readers retain the source's `Encoding.UTF8` and disabled encoding detection. The UTF-8 encoding's own preamble is still consumed by `StreamReader`; other encodings are not detected. An open connection with an unterminated response times out. Closing a pipe without a complete frame can produce an IO error, including during reader/writer disposal, as in the source. Pretty output uses the serializer's default platform line endings (CRLF on the Windows baseline); the caller owns its final output newline.

Cancellation stops waiting for a handler that ignores its token; it cannot terminate that handler's own work. A shutdown timeout is reported through diagnostics. Callers remain responsible for cooperative handler cleanup. These bounded lifecycle changes do not alter request or response bytes.

## Pipe security

Only the same user on the same machine can use the transport. The transport opens no network port.

The client adds `PipeOptions.CurrentUserOnly` to `PipeOptions.Asynchronous`. It rejects a pipe that another user owns. Access denial maps to the existing `RuntimeQueryFailure.ClientError`, with the exception message as detail.

On Windows, every server instance uses the built-in `NamedPipeServerStreamAcl.Create` with explicit `PipeSecurity`:

- Allow the process token's user security identifier (SID) `PipeAccessRights.FullControl` for pipe creation and request handling.
- Deny the well-known NETWORK SID (`S-1-5-2`) `PipeAccessRights.FullControl`. Network logons cannot connect, including network logons by the same user.
- Protect the access rules from inheritance and add no other allow entries.

The pipe owner matches the token owner for the client's .NET ownership check. The allow entry uses the user SID.

`CurrentUserOnly` alone does not block remote clients on Windows. The runtime computes pipe modes without `PIPE_REJECT_REMOTE_CLIENTS` before calling `CreateNamedPipe`:

- [.NET 8.0.0, `NamedPipeServerStream.Windows.cs`, lines 114–125](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs#L114-L125).
- [.NET 10.0.0, the same file, lines 118–129](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs#L118-L129).
- [.NET 8.0.0 constructor, lines 33–36](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs#L33-L36) rejects explicit security with `CurrentUserOnly`. [.NET 10.0.0](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Windows.cs#L33-L36) retains this check.

`NamedPipeServerStreamAcl.Create` forwards to that constructor. Its [remarks](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstreamacl.create) describe ignored security, but the runtime rejects the combination.
Windows tests on .NET 8.0.31 confirm the rejected combination and access rules on two successive instances.
The Windows server therefore keeps explicit security and `Asynchronous`, without `CurrentUserOnly`. The NETWORK denial provides the local-only restriction without P/Invoke.
Other systems keep `Asynchronous | CurrentUserOnly` on the server. Remote named-pipe access does not apply there. Windows-only calls use `OperatingSystem.IsWindows()` guards.

If pipe creation fails, the server reports one `RuntimeQueryDiagnostic.PipeCreationFailed` event with the exception and ends the run loop.
This includes a second server with the same name. `Start()` keeps its signature and does not throw for this failure.
The first server continues to answer requests. Disposal retains the configured shutdown bound.

Callers of the same user see no change to request or response bytes. Another user's process can no longer connect.
A name conflict now produces one diagnostic event. Pipe names, discovery, options and envelope formats do not change.

## Frozen provenance

- Source repository: `Dennis40816/nvt-freeform-helper`.
- Source ref: `1.3.x`.
- Full frozen commit: `e01e07a361b8dc264a06b3741f40274feeeace2d`.
- Extracted source paths:
  - `src/FreeformHelper.UI/Services/RuntimeQueryProtocol.cs`: envelopes and JSON settings, excluding product identity constants.
  - `src/FreeformHelper.UI/Services/RuntimeQueryIpc.cs`: instance server transport and client `SendRequest` / remaining-time calculation, excluding the static host and CLI parser/output.
- Ported transport test source: `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryIpcTests.cs` at the same commit.

These details record the original extraction. This pipe security change has no source tool baseline and extracts no tool code.
The runtime evidence above supports the new security behavior. Adoption in NFH remains a separate task.

## Verification in Core

All fixtures are synthetic and use unique pipe names. Tests live in `tests/Nvt.Core.Tests/RuntimeQuery/`:

- `RuntimeQueryProtocolTests`: literal expected UTF-8 bytes for fixed compact and indented requests, success and failure envelopes; property order, casing, dictionary keys, nulls, escaping and case-sensitive deserialization.
- `RuntimeQueryIpcTests`: exact compact response frames, actual UTF-8 requests with and without a preamble, empty and malformed requests, null/version errors, configurable read timeout, recovery after a timed-out connection, diagnostics and bounded shutdown.
- `RuntimeQueryIpcClientTests`: literal request frame bytes without a BOM, null arguments, accepted-but-unanswered and unterminated-response timeout cases, one total timeout budget, empty/null/malformed responses and broken-pipe IO message forwarding.
- `RuntimeQueryTestValues`: caller configuration and the frozen NFH error codes/messages. Core production code contains none of those product strings.
- `RuntimeQueryIpcSecurityTests`: live Windows access rules, rejected security/option combination, access-denied client mapping, name-conflict diagnostics, continued responses and bounded disposal.

The Windows security tests use xUnit v3 `Assert.Skip` on other systems. Internals access exposes the active pipe without adding public API.
The access-rule test reads two successive server instances with `PipesAclExtensions.GetAccessControl`. New waits have explicit time bounds.

The source's five `RuntimeQueryIpcTests` cases are ported: stopping before start, stopping while waiting for connection, stopping with a connected idle client, accepted request without response, and a throwing request handler. Static-host/UI fixtures are replaced by an instance server and a synthetic throwing delegate; the original handler error code and message are retained. Additional cancellation-ignoring and synchronously blocked handler cases check the configured shutdown behavior and pipe release.

Literal expectations pin serialization and pipe framing rather than relying only on round trips. Malformed JSON and IO errors retain the serializer/OS exception message, as the source does; tests compare forwarded details on the same runtime. Their text can vary with runtime or operating-system localization.

Run with `AVALONIA_TELEMETRY_OPTOUT=1`, using the already-restored packages:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.RuntimeQuery"
```

## NFH switch-over evidence

Adoption is a separate NFH change. Preserve the frozen behavior and run all of these before and after replacing its duplicate transport:

- `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryIpcTests.cs` (`RuntimeQueryIpcTests`), including its headless UI handler-failure case and all shutdown/timeout cases.
- Every `RuntimeQueryUseCaseTests` partial file:
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryUseCaseTests.cs`
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryUseCaseTests.ExportAndStatus.cs`
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryUseCaseTests.NotchAndPad.cs`
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryUseCaseTests.Simulation.cs`
- CLI output comparisons using the same synthetic app state and arguments: default pretty output, `--json-pretty`, `--json-compact`, help, representative successful commands, invalid/missing commands and options, explicit `--timeout-ms`, no running instance, an unanswered request and handler failure. Compare stdout and stderr bytes, property order, escaping, nulls, internal and final newlines, exact error codes/messages and process exit codes. Keep dynamic state fixed in both runs.
- Compare raw request and response frames for the same synthetic envelopes against the frozen transport, including malformed/empty requests and idle read timeout. Supply the existing NFH configuration and error mappings to Core.
- Known difference: when a request or response is valid JSON of the wrong shape, such as `42` or `[]`, the serializer message names the Core type, for example `Nvt.Core.RuntimeQuery.RuntimeQueryRequest`. The frozen source named `FreeformHelper.UI.Services.RuntimeQueryRequest`. The error code is unchanged. To keep the frozen text, NFH's error mapper replaces `Nvt.Core.RuntimeQuery.` with `FreeformHelper.UI.Services.` in `InvalidJson` and `ClientError` details. Otherwise, the adoption PR records the difference.

For this version, run the Core transport tests and the tool's existing request, response, timeout and shutdown tests under the same user.
Compare request and response bytes, CLI output, error text and exit codes before and after adoption.
Also check the intended changes: another user cannot connect, and a name conflict produces one `PipeCreationFailed` event.
These security changes have no source tool baseline. This task does not perform NFH adoption checks.

## What stays in NFH

NFH retains `freeformhelper.runtime.v1`, its protocol version value, static host ownership, `ShellViewModel`, `RuntimeQueryUseCase`, and `Dispatcher.InvokeAsync` integration. NFH also retains its handler table, product parsers, option parsing, CLI usage and output, exit codes, and timeout defaults. NFH keeps shutdown policy, NLog, product error text, and diagnostic text. Product data and application workflows are not part of this module.

## UI thread and host

Core now provides UI dispatch and an instance host in `Nvt.Core.Avalonia.RuntimeQuery`.
The tool keeps its command handlers, error text, startup scheduling, and exit events.
The new public types are `RuntimeQueryUiThread` and `RuntimeQueryHost`.

| API | Contract |
| --- | --- |
| `RuntimeQueryUiThread.Wrap(handler, error)` | Returns a server handler with the same delegate signature. |
| `RuntimeQueryHost(factory)` | Creates a sealed instance host from `Func<RuntimeQueryIpcServer>`. |
| `RuntimeQueryHost.Start()` | Creates and starts one server under a lock. Repeated starts do nothing while the host owns a server. |
| `RuntimeQueryHost.StopAsync()` | Removes the current server under the lock, then disposes it. With no server, it completes immediately. |
| `RuntimeQueryFailure.DispatcherUnavailable` | Appends one failure value in `Nvt.Core.RuntimeQuery`. Existing failure values and diagnostic values stay unchanged. |

`Wrap` gets the dispatcher through `UiThread.TryGetRunningDispatcher`.
It uses the task-returning `Dispatcher.InvokeAsync` overload at the source's default priority.
It passes the request, version, and cancellation token unchanged, including a null request or an already canceled token.
It returns the inner response and lets inner exceptions escape. The server maps those exceptions.
The tool registers its running dispatcher through `UiThread.RegisterRunningDispatcher`.

Without a running dispatcher, `Wrap` maps `DispatcherUnavailable` with a null detail and returns a failed envelope.
NFH maps this failure to `IPC_ERROR` with `The UI dispatcher is unavailable.`
NFH maps handler failures to `IPC_ERROR` with the unchanged exception message.
These mappings belong to the tool. Core supplies no product error text for these failures.

`StopAsync` clears host ownership before disposal. A later `Start` creates a new server, as in the frozen host.
The host adds no scheduling, window events, lifetime events, static instance, or options.
The tool decides when to start it and calls `StopAsync` on exit.

### Tool startup and exit

NFH means FreeformHelper. NFC means NVT FW Combiner.
NFH starts its host after the main window's `Opened` event, posted at Background priority.
Its ApplicationIdle fallback checks window visibility and posts the same start at Background priority.
NFH stops the host on the desktop `Exit` event without waiting.
The existing `StartRuntimeIpcIfNeeded` function keeps its shell check and start guard.
This short lifecycle example stays in NFH:

```csharp
mainWindow.Opened += (_, _) => Dispatcher.UIThread.Post(
    StartRuntimeIpcIfNeeded, DispatcherPriority.Background);
Dispatcher.UIThread.Post(() =>
{
    if (mainWindow.IsVisible)
    {
        Dispatcher.UIThread.Post(StartRuntimeIpcIfNeeded, DispatcherPriority.Background);
    }
}, DispatcherPriority.ApplicationIdle);
desktop.Exit += (_, _) => { _ = RuntimeQueryIpcHost.StopAsync(); };
```

NFC starts its host only after it writes READY. This keeps the READY time unchanged.
Screenshot mode, `--help`, and internal probe runs do not start the host.
NFC also calls `StopAsync` on exit. This short example shows the startup order:

```csharp
WriteReady();
if (!screenshotMode && !helpRequested && !internalProbe)
{
    host.Start();
}
```

### Frozen UI and host baseline

- Source repository: `nvt-freeform-helper`.
- Source ref: `origin/1.3.x`.
- Full frozen commit: `847cc4530ed098ceb56aa1bd8beda77bcd1ec227`.
- Extracted path: `src/FreeformHelper.UI/Services/RuntimeQueryIpc.cs`, lines 11–48 for the host and 205–214 for UI dispatch.
- Documentation reference: `src/FreeformHelper.UI/App.axaml.cs`, lines 82–137 for NFH's startup and exit timing.
- Test source: `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryIpcTests.cs`, all five tests.

The host commit message must record this repository, ref, full commit, and all three file paths.
This extraction does not change NFH or NFC.

### Tests and zero-difference switch

Core ports three host shutdown tests and the throwing-handler pipe test through real UI dispatch.
The shutdown tests retain the 1500 ms completion bound.
The accepted-but-unanswered case already exists as `Nvt.Core.Tests.RuntimeQuery.RuntimeQueryIpcClientTests.SendRequestWhenServerAcceptsButDoesNotRespondReturnsTimeout`.
Core does not duplicate that transport test here.

The new tests also check UI access, unchanged inputs, dispatcher unavailability, exception identity, repeated starts, restart, and concurrent starts.
Every new pipe test uses its own synthetic pipe name.
All new tests use the `RuntimeQuery` collection with parallel runs disabled.
Tests restore the static `UiThread` dispatcher registry in `finally` and use bounded waits.

NFH switches with these steps:

1. Keep its static host as a wrapper around an instance of `RuntimeQueryHost`.
2. Build each server with its existing configuration and handler. Wrap that handler with `RuntimeQueryUiThread.Wrap` and NFH's error mapping.
3. Keep the existing startup scheduling and desktop exit call.
4. Run all five `RuntimeQueryIpcTests` before and after replacement.
5. Run NFH's complete test suite and save its full test list before and after replacement.
6. Require equal test names, counts, pass/fail results, and skipped results. Keep each existing test name.

The five frozen pipe tests are:

- `StopAsync_WhenHostNotStarted_Completes`
- `StopAsync_WhenHostStarted_CompletesWithinTimeout`
- `StopAsync_WhenClientConnectedWithoutRequest_CompletesWithinTimeout`
- `SendRequest_WhenServerAcceptsButDoesNotRespond_ReturnsTimeout`
- `SendRequest_WhenRuntimeQueryThrows_ReturnsIpcErrorEnvelope`

Core's tests establish the extracted contracts. NFH's before-and-after runs establish zero difference when it adopts Core.
Those adoption runs remain in NFH.

Use the already-restored packages for Core verification:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```
