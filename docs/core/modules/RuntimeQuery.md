[English](RuntimeQuery.md) | [中文](RuntimeQuery.zh-TW.md)

# RuntimeQuery

## Purpose

`Nvt.Core.RuntimeQuery` provides the JSON envelopes and local named-pipe transport extracted from FreeformHelper (NFH). It serves one request per connection, sequentially, using byte-mode asynchronous pipes. It has no Avalonia or NLog dependency and does not interpret product commands.

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

The server handler is `Func<RuntimeQueryRequest?, string, CancellationToken, Task<RuntimeQueryResponseEnvelope>>`. It receives the deserialized request, configured protocol version and shutdown token. Empty lines and malformed JSON are rejected by the transport; a JSON null literal and version mismatches reach the handler, exactly as in the frozen transport. Null/version and command validation remain in NFH's `RuntimeQueryUseCase`, after its UI dispatch. The synthetic test handler reproduces those existing validation envelopes without moving product validation into Core.

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

NFH retains `freeformhelper.runtime.v1`, its protocol version value, static host ownership, `ShellViewModel`, `RuntimeQueryUseCase`, `Dispatcher.InvokeAsync` integration, null/version validation, command registry and command validation, option parsing, CLI usage and output, exit codes, timeout defaults, shutdown policy, NLog and all product error/diagnostic text. Product data and application workflows are not part of this module.
