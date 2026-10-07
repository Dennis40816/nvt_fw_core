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

## Startup entry

A tool defines each command once for startup arguments and RuntimeQuery requests.
The owner approved this new behavior on 2026-10-06.
It has no source tool baseline or extracted source paths.

| Public API | Contract |
| --- | --- |
| `RuntimeQueryStartupPhase` | `None` adds no startup option. `BeforeFirstFrame` is startup-only. `AfterStartup` also permits RuntimeQuery requests. |
| `RuntimeQueryCommand.StartupPhase` | Optional metadata. The default is `None`, so existing registrations keep their behavior. |
| `RuntimeQueryCommand.StartupValueKey` | Optional argument key for one startup value. The default is null, which defines a flag with null handler arguments. |
| `RuntimeQueryCommand.StartupValidator` | Optional validator with type `Func<IReadOnlyDictionary<string, string>?, RuntimeQueryResponseEnvelope?>?`. Returns null for valid arguments or a failure. |
| `RuntimeQueryStartupCall(Command, Args)` | One recognized occurrence. `Phase` comes from its command definition. Calls retain command-line order, including occurrences with issues. |
| `RuntimeQueryStartupIssue(Option, Message)` | The option name and exact issue message. |
| `RuntimeQueryStartupParseResult(Calls, RemainingArguments, Issues)` | All calls, remaining tool arguments, and issues from one parse. |
| `RuntimeQueryStartupCallResult(Call, Response)` | The executed call and its unchanged response. |
| `RuntimeQueryCommandRouter.ParseStartupArguments(arguments)` | Parses raw arguments against the router's registered commands and confirmation setting. Runs no handlers. |
| `RuntimeQueryCommandRouter.ExecuteStartupPhaseAsync(calls, phase)` | Runs one phase through the router in command-line order. Includes the first failed response, then stops. |

For example, register `theme` with phase `AfterStartup` and value key `value`:

```csharp
var command = new RuntimeQueryCommand(
    "theme",
    RuntimeQueryCommandRisk.ChangesState,
    ApplyThemeAsync,
    StartupPhase: RuntimeQueryStartupPhase.AfterStartup,
    StartupValueKey: "value",
    StartupValidator: ValidateTheme);
var router = new RuntimeQueryCommandRouter([command], requireConfirmation: false);
var startup = router.ParseStartupArguments(args);
```

The tool can call `ValidateTheme` from `ApplyThemeAsync` to keep value rules in one function.
The validator must have no side effects and must not run the command.

Both entry points pass `"dark"` under the ordinal key `"value"` to the same handler:

```text
--theme dark
query theme --value dark
```

Core accepts these startup forms:

- `--name value` and `--name=value` for value options.
- `--name` for flags.
- Values containing `=`, with their text unchanged.

Core takes only `--` followed by a registered command name whose phase differs from `None`.
It compares names with ordinal equality and does not change case.
A tool keeps its own parser for its existing options.
Core passes every other argument through unchanged, in its original order.
For example, `--page home --theme dark --load-report a.json` leaves `--page home --load-report a.json` for the tool.
Core adds no startup phases to generic commands such as `page` or `help`.
A tool that registers no startup phase sees no change.
The parser passes every argument through, including `--confirm`.

The parser reports these exact grammar messages:

- `--{name} requires a value.` for missing, empty, or blank values in either form.
- `--{name} does not take a value.` for flags with an equals sign.
- `--{name} is given more than once.` for repeated options.

A next token starting with `--` is not a value and remains available for parsing.
For each call without a grammar issue, Core calls its startup validator once.
A validator failure becomes an issue with its message unchanged.
The parser returns all calls and issues together for tool checks across options.

With startup commands registered, the enabled confirmation guard reserves `--confirm` as a startup flag.
It confirms every `WritesData` call, including calls before the flag.
Without confirmation, each such call adds `--{name} writes files or changes data. Add --confirm to use it.`.
With the guard off, `--confirm` passes through to the tool.
Handlers never receive the reserved startup confirmation flag.

The tool runs `BeforeFirstFrame` before the main window shows.
Use this phase for settings that affect the first frame.
The tool runs `AfterStartup` after its startup flow ends.
Core supplies no window events or UI dispatch.
Phase execution bypasses the runtime startup-only check and retains the enabled confirmation guard.
The tool checks parse issues and rules across options before it calls either phase.

Runtime requests check null, version, unknown command, startup-only status, then confirmation.
Both `RouteAsync` and `ExecuteAsync` reject `BeforeFirstFrame` with `STARTUP_ONLY` and this exact message:

```text
Command '{name}' can be used only at startup.
```

Strict mode stays with the tool.
An automation run rejects startup issues with exit code 64 and opens no UI.
Startup validators let the tool reject invalid values before any window opens.
An interactive run shows issues through the tool's own UI.
Core returns issues and responses and defines no exit-code constant.

`RuntimeQueryStartupCases` holds the input and literal expected-output rows.
`RuntimeQueryStartupParserTests` and `RuntimeQueryStartupTests` check parsing, runtime error order, equal handler arguments, and phase execution.
Run the RuntimeQuery test command below with the already-restored packages.
For zero difference, register no startup phase and compare every passed-through argument with the original list.
Run the tool's existing startup parser tests on both lists.
Compare parse results and exact error messages, including existing `page`, `help`, and report options.
Registering startup phases adds behavior and requires separate tool tests before adoption.
This task changes no tool repository.

## Public API

| API | Contract |
| --- | --- |
| `RuntimeQueryRequest(Version, Command, Args)` | Request fields in that order; arguments may be null. |
| `RuntimeQueryError(Code, Message)` | Caller-owned error code and message. |
| `RuntimeQueryResponseEnvelope(Ok, Data, Error)` | Response fields in that order; `Success(data)` and `Failure(code, message)` retain explicit null properties. |
| `RuntimeQueryProtocol.CompactJsonOptions` | Camel-case property names, explicit nulls, default JSON escaping, case-sensitive deserialization, no indentation. Dictionary keys retain their casing. |
| `RuntimeQueryProtocol.PrettyJsonOptions` | The same settings with two-space indentation and the serializer's default platform line endings. Used for caller output, not pipe messages. |
| `RuntimeQueryIpcServer(...)` | An instance configured with pipe name, protocol version, positive read and shutdown timeouts in milliseconds, error callback, diagnostic callback, and request handler. |
| `Start()` / `DisposeAsync()` | Start once; repeated starts while running and repeated disposal are harmless. Starting after disposal throws. Disposal stops new connections and cancels a connection that has no request line. A request that was already read can finish and write its response within the shutdown bound. After the bound, disposal cancels the handler wait and closes the pipe. |
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
A name conflict now produces one diagnostic event. See [Per-window pipes](#per-window-pipes) to run more than one copy.

## Command line

Core now provides the query command-line front end with the frozen source behavior.
The tool keeps its pipe name, protocol version, ordered command list, and client error texts.
Core handles parsing, JSON output, exit codes, and the default client timeout.

The new public type is `RuntimeQueryCommandLine`.
Its only entry method is `TryHandleQueryCommand(args, pipeName, protocolVersion, supportedCommands, error, output, out exitCode)`.
The error mapping uses the existing `Func<RuntimeQueryFailure, string?, RuntimeQueryError>` contract.
Tools pass `Console.Out` as the output writer.

- Only a first argument equal to `query`, ignoring case, returns handled.
- Unhandled arguments produce no output and return exit code 0.
- The parser trims the command and converts it to lowercase with the invariant culture.
- Usage and unsupported-command errors list the supplied commands in their original order.
- Pretty JSON is the default. The last `--json-pretty` or `--json-compact` switch selects the response format.
- Parse errors always print pretty `INVALID_ARGUMENTS` JSON and return exit code 2.
- Responses return exit code 0 when `ok` is true and 1 when it is false.
- The front end calls `WriteLine` once with the serialized JSON.

Options split at the first `=` only when its position is greater than 2.
Otherwise, the complete token after `--` becomes the key.
A missing value becomes `"true"`, including when the next token starts with `--`.
Keys ignore case. Repeated keys keep their first spelling and their last value.
The client-only `--timeout-ms` defaults to 1500 and accepts integers from 1 through 120000.
The request carries null arguments when no command arguments remain.

### Command-line baseline

- Source repository: `nvt-freeform-helper` (NFH).
- Production ref: `origin/1.3.x`.
- Frozen production commit: `847cc4530ed098ceb56aa1bd8beda77bcd1ec227`.
- Extracted path: `src/FreeformHelper.UI/Services/RuntimeQueryIpc.cs`, lines 239–289 and 376–463.
- Caller reference: `src/FreeformHelper.UI/Program.cs`, near line 37, at the same commit.
- Characterization ref: `test/1.3.x/runtimequery-characterization`, NFH pull request 47.
- Frozen characterization commit: `464ecf4d98095ac26b195046bfb50ef66679286f`.
- Characterization paths:
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryCharacterizationCommandLineTests.cs`
  - `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryCharacterizationSubject.cs`

### Command-line verification

`RuntimeQueryCommandLineCases` holds all 40 frozen input and expected-output rows in one reusable test table.
Four added rows cover `--=x`, `--a=b=c`, an empty command, and a command with surrounding spaces.
The frozen query-only row covers an empty argument list after `query`.
Tests compare exact stdout strings and request frames with `Environment.NewLine`.
The three timeout rows inspect the private parser, as the source tests do.
The Program row reproduces the caller's exit-code assignment without starting a UI.
All pipe tests share one collection with parallel execution disabled.
Tests use unique pipe names for connections and bounded waits.
The product pipe name remains test data only for cases that cannot connect.

Set `AVALONIA_TELEMETRY_OPTOUT=1` and use the restored packages:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.RuntimeQuery"
```

### Zero-difference switch

NFH keeps its command list in the frozen order and keeps its existing client error texts.
In `Program.cs`, replace the front-end call with the Core entry method:

```csharp
if (RuntimeQueryCommandLine.TryHandleQueryCommand(
    args, PipeName, ProtocolVersion, SupportedCommands, ClientError, Console.Out, out var cliExitCode))
{
    Environment.ExitCode = cliExitCode;
    return;
}
```

The setting names in this example represent the tool's existing values and client error mapping.
Run all 40 `RuntimeQueryCharacterizationCommandLineTests` cases before and after the switch.
Use `RuntimeQueryCommandLineCases` to run the same rows against both front ends.
Compare stdout bytes and exit codes. Both must be equal.
Keep the tool's `INSTANCE_NOT_RUNNING` text in its client error mapping.
Core continues to use the existing client transport and JSON options.
This task does not change the source tool.

## Per-window pipes

Per-window mode gives each running tool process its own pipe.
The owner approved this behavior on 2026-10-06.
Use `new behavior, no source baseline` in the host commit message.

| Public member | Contract |
| --- | --- |
| `RuntimeQueryWindowPipes.BuildName(baseName, processId)` | Returns `{baseName}.{processId}` with invariant decimal digits. Rejects null or blank base names and nonpositive process IDs. |
| `RuntimeQueryCommandLine.TryHandlePerWindowQueryCommand(args, baseName, protocolVersion, supportedCommands, error, output, out exitCode)` | Resolves a running window before calling the existing client. Other parameters match `TryHandleQueryCommand`. |
| `RuntimeQueryFailure.ServerNotFound` | Maps a missing window through the tool's error callback with null detail. The command line returns exit code 1. |

For example, two windows use `sample.runtime.v1.123` and `sample.runtime.v1.456`.
If process 456 started later, `sample query help` selects its pipe.
Use `sample query help --pid 123` to select the other pipe.

Discovery runs only on Windows and enumerates `\\.\pipe\`.
Each candidate name starts with `{baseName}.` and ends with a positive decimal process ID, with nothing after it.
Discovery ignores names such as `{baseName}.12x`, `{baseName}.`, and `{baseName}x.1`.
It skips processes that have exited or whose start time cannot be read.
Other systems return no candidates.

The selection rule uses only process IDs and start times:

- Without `--pid`, select the latest process start time.
- On equal start times, select the higher process ID.
- With `--pid`, select that process ID. A missing ID returns `ServerNotFound` without selecting another window.
- With no candidates, return `ServerNotFound`.

The new entry accepts `--pid 123` and `--pid=123` as client-only options, like `--timeout-ms`.
The value must be a positive integer.
A missing or invalid value prints pretty `INVALID_ARGUMENTS` JSON and returns exit code 2.
The exact message is `--pid must be a positive process ID.`.
The request omits `--pid` and `--timeout-ms`.
All other parsing, output formats, response handling, and exit codes retain the fixed-name behavior.

Fixed-name mode does not change.
`TryHandleQueryCommand` still sends to its supplied pipe and treats `--pid` as an ordinary command argument.
A second server with the same fixed name still reports `PipeCreationFailed`, while the first server continues to answer.
NFH first adopts Core in fixed-name mode with zero difference.
It moves to per-window mode in a later pull request with these changes:

1. Build each server name with `RuntimeQueryWindowPipes.BuildName(baseName, Environment.ProcessId)` and pass it to the existing server constructor.
2. Replace the command-line call with `TryHandlePerWindowQueryCommand` and supply the same base name.
3. Add the tool's `ServerNotFound` error mapping and test the new window selection behavior.

For `focus`, both client entry methods use the connected pipe's server process ID on Windows.
The client reads that ID with [GetNamedPipeServerProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid).
Before writing the request, it calls [AllowSetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow) for that process.
A failed Windows call does not stop the request.
Request bytes and response handling stay unchanged.

Tests check the pure selection rule, exact command-line output, real Windows pipes, and permission calls through an internal seam.
The old entry method still runs all 40 frozen command-line rows and the existing boundary rows.
Pipe tests use unique names, bounded waits, and the existing collection with parallel execution disabled.
The two-process discovery test uses the shared test probe in `silent-wait` mode and kills it in `finally`.

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

`StopAsync` stops new connections immediately and cancels connections that have no request line.
An already-read request can finish its handler and flush its response within the server's shutdown bound.
After that bound, the server cancels the handler wait, closes the pipe, and reports `ShutdownTimedOut`.

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

## Generic commands

Tools can register six generic commands beside their product commands.
This is new behavior, no source baseline.
Core returns command records and starts no server.
All six records have startup phase `None` and define no startup option.
The tool keeps `--page`, `--help`, and all other startup arguments.

The public types are in `Nvt.Core.Avalonia.RuntimeQuery`:

| Public type | Contract |
| --- | --- |
| `RuntimeQueryGenericCommands` | `Create(options)` returns a read-only list in the order below. The tool selects which records to register. |
| `RuntimeQueryGenericCommandOptions` | An immutable record with tool identity, command lookup, window lookup, navigation, exit decision, and close action. |
| `IRuntimeQueryNavigation` | Supplies `Pages`, `CurrentPage`, and synchronous `SwitchPage(name)`. |
| `RuntimeQueryPageResult` | Defines `Switched`, `NeedsConfirmation`, and `Rejected`. |
| `RuntimeQueryExitResult` | Defines `Closing`, `NeedsConfirmation`, and `Rejected`. |
| `RuntimeQueryScreenshotResult` | `Success(pixelWidth, pixelHeight, fileSize)` or `Failure(code, message)` for replacement capture. File size uses bytes. |
| `RuntimeQueryGenericFailureCodes` | Constants for the failure codes below, including the shared `INVALID_ARGUMENTS` code. `USER_CONFIRMATION_REQUIRED` differs from the router's `CONFIRMATION_REQUIRED`: adding `--confirm` does not help, because the tool needs a person to confirm. |

| Name | Risk | Arguments | Success data | Failure codes |
| --- | --- | --- | --- | --- |
| `help` | `ReadOnly` | None | `{ commands: [{ name, risk }] }`, or the tool's unchanged text as data | None |
| `ping` | `ReadOnly` | None | `{ toolName, version, processId }` | None |
| `focus` | `ChangesState` | None | `{ focused: true }` | `NO_MAIN_WINDOW` |
| `page` | `ChangesState` | None, or `--name <page>` | List: `{ pages, currentPage }`. Switch: `{ currentPage }` | `INVALID_ARGUMENTS`, `UNKNOWN_PAGE`, `USER_CONFIRMATION_REQUIRED`, `PAGE_REJECTED` |
| `screenshot` | `ChangesState` | `--path <file.png>` | `{ path, pixelWidth, pixelHeight, fileSize }` | `INVALID_ARGUMENTS`, `FILE_EXISTS`, `NO_MAIN_WINDOW`, or the replacement's unchanged failure |
| `exit` | `ChangesState` | None | `{ closing: true }` | `USER_CONFIRMATION_REQUIRED`, `EXIT_REJECTED` |

Help returns risk names as strings and keeps registration order.
The tool supplies `GetCommands` because registration finishes after factory creation.
Set `HelpText` to return that string unchanged, including whitespace or empty text.
FreeformHelper can keep its own help text this way.
Ping uses the supplied tool name and version, plus `Environment.ProcessId`.

The tool supplies `GetMainWindow` for focus and default capture.
Focus restores a minimized window to `Normal`, then calls `Activate()`.
The client grants foreground permission before sending the request.
A missing window returns `NO_MAIN_WINDOW` with `The main window is not available.`.

The tool supplies page names in its own order and owns the current page.
Core compares names with `StringComparer.Ordinal` and asks `SwitchPage` to perform navigation.
Core assumes no page names or startup targets.
For example, NVT FW Combiner can omit settings because settings opens as a dialog.
The tool returns these decisions:

- `Switched`: return the new current page. A request for the current page succeeds and changes nothing.
- `NeedsConfirmation`: leave the page unchanged and return `USER_CONFIRMATION_REQUIRED`. The tool must not open a dialog.
- `Rejected`: return `PAGE_REJECTED`, for example when a dialog prevents navigation.

A blank or null supplied name returns `INVALID_ARGUMENTS` with `Argument '--name' requires a page name.`.
An unknown name returns `UNKNOWN_PAGE` with `Unknown page '{name}'. Valid pages: {names}.`.
The valid names use the tool's order, separated by a comma and a space.
The existing query parser sends a valueless option as the string "true".
The handler cannot distinguish a valueless --name from an explicit page name of true.
`query page --name` returns `UNKNOWN_PAGE` unless the tool has a page named `true`.
Confirmation uses `Page switching requires confirmation.`. Rejection uses `The page switch was rejected.`.

Screenshot needs no `--confirm`, even when the router enables confirmation.
The owner assigned `ChangesState` because capture accepts only an absolute path and never overwrites a file.
This explicit exception keeps screenshot out of `WritesData`.
Core requires a fully qualified path ending in `.png`, ignoring extension case.
Core normalizes the absolute path and checks file existence before capture or window lookup.
An invalid or missing path returns `INVALID_ARGUMENTS` with `Argument '--path' must be an absolute path ending in '.png'.`.
An existing file returns `FILE_EXISTS` with `The screenshot file already exists.` and remains unchanged.

Default capture updates window layout and renders a `RenderTargetBitmap` at the current window size and scaling.
It writes PNG data to a temporary file in the destination folder.
It moves that file to the final name without replacement and removes the temporary file on failure.
If the destination appears before the move, Core returns the same `FILE_EXISTS` failure.
Other capture exceptions escape the handler.

Set `CaptureScreenshot` to use the tool's own asynchronous capture.
NVT FW Combiner can use its production capture this way.
The delegate receives the normalized absolute path and `CancellationToken.None` because command handlers currently have no token.
The delegate owns frame waits, layout, capture, and writing without replacement.
Core does not update layout or obtain the main window before this delegate runs.
Return `RuntimeQueryScreenshotResult.Success` with the saved image dimensions and file size.
Return `Failure` to preserve the tool's exact code and message, such as `PROTECTED_PATH` or `FILE_EXISTS`.
The delegate's no-replace move must handle a destination that appears after Core's existence check.
Delegate exceptions escape unchanged.

The tool supplies synchronous `DecideExit` and its normal `Close` action.
The decision must not open a dialog.
Core handles the three results:

- `Closing`: produce `{ closing: true }` and post `Close` to the UI dispatcher at Background priority.
- `NeedsConfirmation`: return `USER_CONFIRMATION_REQUIRED` with `Exit requires confirmation.`. Do not close or open a dialog.
- `Rejected`: return `EXIT_REJECTED` with `Exit was rejected.`. Do not close.

Posting defers closing so the handler produces its response first.
The tool must await `RuntimeQueryHost.StopAsync` in its close path before the process ends.
The server lets the response write and flush finish within its shutdown bound.
The tool must commit to closing after `Closing`; its close action must not veto the approved decision.

The constants are `NO_MAIN_WINDOW`, `USER_CONFIRMATION_REQUIRED`, `PAGE_REJECTED`, `UNKNOWN_PAGE`, `INVALID_ARGUMENTS`, `FILE_EXISTS`, and `EXIT_REJECTED`.
Tool capture failures can add their own codes without Core mappings.

For example, register selected generic commands and product commands together:

```csharp
IReadOnlyList<RuntimeQueryCommand> registered = [];
var options = new RuntimeQueryGenericCommandOptions(
    "Example tool", "1.0", () => registered, () => mainWindow,
    navigation, DecideExit, CloseNormally)
{
    CaptureScreenshot = CaptureProductionAsync
};
var generic = RuntimeQueryGenericCommands.Create(options);
registered = [.. generic.Where(command => command.Name != "help"), .. productCommands];
var router = new RuntimeQueryCommandRouter(registered, requireConfirmation: true);
var handler = RuntimeQueryUiThread.Wrap(
    (request, version, _) => router.ExecuteAsync(request, version), MapTransportError);
```

This example selects five generic commands and leaves help with the tool.
To register generic help, include its record and optionally set `HelpText`.
The completed `registered` list includes every product command and its risk.
Use the wrapped handler in the tool's existing server setup.

Headless tests check exact response data, messages, navigation decisions, and deferred closing through a confirmation-enabled router.
Fixed window content verifies PNG dimensions at two scaling values and temporary-file cleanup after success and failure.
Tests also verify path checks before replacement capture, delegate-owned layout, and unchanged tool failures and exceptions.

Use the already-restored packages with `AVALONIA_TELEMETRY_OPTOUT=1`:

```text
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build
```
