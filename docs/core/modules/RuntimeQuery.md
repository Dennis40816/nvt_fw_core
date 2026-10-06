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

The optional diagnostic callback is `Action<RuntimeQueryDiagnostic, Exception?>`. Events cover start, stop, connection failure, request failure, handler failure, shutdown failure and shutdown timeout. The caller supplies logging text and logging dependencies. Error and diagnostic callbacks must return promptly and must not throw.

The read limit, shutdown bound, client budget and protocol identity have no product defaults in Core. NFH supplies its existing 5,000 ms read timeout, 1,500 ms default CLI budget, protocol version, pipe name, error mappings and logging. The frozen shutdown tests use a 1,500 ms completion bound; the extracted server accepts that bound explicitly.

Pipe writers use compact JSON, UTF-8 without a BOM, and `StreamWriter.WriteLineAsync` with the platform newline (CRLF on Windows). Readers retain the source's `Encoding.UTF8` and disabled encoding detection. The UTF-8 encoding's own preamble is still consumed by `StreamReader`; other encodings are not detected. An open connection with an unterminated response times out. Closing a pipe without a complete frame can produce an IO error, including during reader/writer disposal, as in the source. Pretty output uses the serializer's default platform line endings (CRLF on the Windows baseline); the caller owns its final output newline.

Cancellation stops waiting for a handler that ignores its token; it cannot terminate that handler's own work. A shutdown timeout is reported through diagnostics. Callers remain responsible for cooperative handler cleanup. These bounded lifecycle changes do not alter request or response bytes.

## Frozen provenance

- Source repository: `Dennis40816/nvt-freeform-helper`.
- Source ref: `1.3.x`.
- Full frozen commit: `e01e07a361b8dc264a06b3741f40274feeeace2d`.
- Extracted source paths:
  - `src/FreeformHelper.UI/Services/RuntimeQueryProtocol.cs`: envelopes and JSON settings, excluding product identity constants.
  - `src/FreeformHelper.UI/Services/RuntimeQueryIpc.cs`: instance server transport and client `SendRequest` / remaining-time calculation, excluding the static host and CLI parser/output.
- Ported transport test source: `tests/FreeformHelper.Tests/UI/Services/RuntimeQueryIpcTests.cs` at the same commit.

The host commit message must include this repository, ref, full SHA and these source paths. The extraction does not update the frozen baseline or adopt Core in NFH.

## Verification in Core

All fixtures are synthetic and use unique pipe names. Tests live in `tests/Nvt.Core.Tests/RuntimeQuery/`:

- `RuntimeQueryProtocolTests`: literal expected UTF-8 bytes for fixed compact and indented requests, success and failure envelopes; property order, casing, dictionary keys, nulls, escaping and case-sensitive deserialization.
- `RuntimeQueryIpcTests`: exact compact response frames, actual UTF-8 requests with and without a preamble, empty and malformed requests, null/version errors, configurable read timeout, recovery after a timed-out connection, diagnostics and bounded shutdown.
- `RuntimeQueryIpcClientTests`: literal request frame bytes without a BOM, null arguments, accepted-but-unanswered and unterminated-response timeout cases, one total timeout budget, empty/null/malformed responses and broken-pipe IO message forwarding.
- `RuntimeQueryTestValues`: caller configuration and the frozen NFH error codes/messages. Core production code contains none of those product strings.

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

Passing Core characterization tests establishes the extracted transport contracts; NFH's product tests and CLI/frame comparisons establish zero difference at adoption. Those NFH adoption checks are not performed by this extraction.

## What stays in NFH

NFH retains `freeformhelper.runtime.v1`, its protocol version value, static host ownership, `ShellViewModel`, `RuntimeQueryUseCase`, and `Dispatcher.InvokeAsync` integration. NFH also retains its handler table, product parsers, option parsing, CLI usage and output, exit codes, and timeout defaults. NFH keeps shutdown policy, NLog, product error text, and diagnostic text. Product data and application workflows are not part of this module.
