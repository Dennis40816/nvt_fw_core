# Test probe

The test probe is a synthetic child process for process lifetime tests.
It targets .NET 8 and uses only the base class library.
It does not reference the library under test.
It runs from any folder under any executable name.

| Mode | Inputs | Behavior | Exit |
| --- | --- | --- | --- |
| `ambient-pipe` | `marker`, `ambient-handle` | Writes `started` to the marker. Attempts to write `leaked` to the inherited pipe. | 0 |
| `contained-isolation` | `payload`; optional `allowed-handle`, `cross-handle` | Writes the payload to the allowed pipe. Writes `cross:` and the payload to the cross pipe. | 0 |
| `arguments-environment` | `marker`, `text`; payload arguments | Writes the text, current directory, and payload arguments as separate lines. | 0 |
| `ready` | READY inputs | Writes the identity and a literal newline. Holds the pipe open for 200 milliseconds. | 0 |
| `ready-wrong-identity` | READY inputs | Changes the last identity character to `0`, or to `1` when it is already `0`. Preserves suffix fields. | 0 |
| `ready-partial` | READY inputs; optional `partial-drop` | Drops the specified trailing characters. Writes no newline. Closes the pipe before the 200 millisecond wait. | 0 |
| `invalid-utf8` | READY inputs | Writes bytes `C3 28 0A`. Holds the pipe open for 200 milliseconds. | 0 |
| `oversized` | READY inputs; optional `oversize-chars` | Writes the specified number of `X` characters and a literal newline. Holds the pipe open for 200 milliseconds. | 0 |
| `ready-tree-root` | READY inputs, `tree-marker` | Writes its identifier to `.root`. Starts a pipe-holding child with a `.child` marker. Writes READY after the handshake. | 0 or 25 |
| `silent-wait` | None | Writes nothing. Waits 30 seconds. | 0 |
| `exit` | Optional `exit-code` | Writes nothing. Exits immediately. | Requested code |
| `tree-grandchild` | `tree-marker` | Writes its process identifier. Waits 30 seconds. | 0 |
| `tree-root-exit` | `tree-marker`; optional `stdout-text`, `exit-code` | Writes the optional output line. Starts a pipe-holding child. Exits after its marker appears. | Requested code or 25 |
| `tree-root-wait` | `tree-marker` | Starts a pipe-holding child. Waits 30 seconds after its marker appears. | 0 or 25 |
| `orphan-chain-root` | `tree-marker` | Starts a pipe-holding middle process. Writes `.middle` and `.ready` markers. Waits with an exited middle process and a live leaf. | 0 or 25 |
| `orphan-chain-exit` | `tree-marker`; optional `stdout-text` | Writes the optional output line. Runs the `orphan-chain-root` chain, then exits after the `.ready` marker. The live leaf keeps the inherited standard streams open after the root and middle processes exit. | 0 or 25 |
| `detached-descendant-root` | `tree-marker` | Starts a child without inherited standard streams. Exits after its marker appears. | 0 or 25 |
| `hold-lock` | `lock-path`, `lock-ready` | Writes `STARTED`. Opens an exclusive read/write file. Writes the ready marker and `LOCK_HELD`. Holds the file for 30 seconds. | 0 or 1 |
| `dual-output-exit` | Optional `out-char`, `out-count`, `out-suffix`, `err-char`, `err-count`, `err-suffix` | Writes the repeated character and the suffix to standard output, then to standard error. Writes no newline. Flushes each stream. Exits immediately. | 0 |
| `dual-output-wait` | The `dual-output-exit` inputs; optional `wait-ms` | Writes both streams like `dual-output-exit`, with other defaults. Then waits without more output. | 0 |

Every input has two forms:

- Argument: `--<name> <value>`.
- Environment: `CORE_TEST_PROBE_<NAME>`, with uppercase letters and underscores instead of hyphens.

Arguments override environment values.
The probe removes recognized input pairs from the payload arguments.
All other arguments remain in their original order, including empty arguments.
The probe requires `mode` and has no default mode.

Optional input defaults are:

- `exit-code`: 0.
- `oversize-chars`: 256, with a minimum of 1.
- `partial-drop`: 0, with a minimum of 0.
- `out-char`, `out-count`, `out-suffix`: `A`, 131072 and `OUT-END` for `dual-output-exit`. `O`, 131072 and `OUT-PARTIAL-END` for `dual-output-wait`.
- `err-char`, `err-count`, `err-suffix`: `B`, 131072 and `ERR-END` for `dual-output-exit`. `E`, 131072 and `ERR-PARTIAL-END` for `dual-output-wait`.
- `wait-ms`: 30000, with a minimum of 1.

A character input is one ASCII character.
A suffix input is ASCII text.
A count input has a minimum of 0.
With the default counts, each stream exceeds a pipe buffer.
A parent must drain both streams at the same time.

Handles use unsigned decimal text with invariant culture.
Unparseable ambient, allowed, and cross handles cause no pipe write.
The probe ignores the documented pipe access exceptions for these writes.
The probe creates no folders.
Each file input requires an existing parent folder.
Files and pipe bytes use UTF-8 without a byte order mark.
Process identifiers contain invariant decimal digits and no newline.
Standard output and standard error use UTF-8 without a byte order mark.
Standard output lines use the platform newline and are flushed.
The dual-output modes write no newline.
Pipe lines end with a literal `\n`.

The six READY modes share a prelude.
The prelude joins the optional Job before it selects the protocol form.
It then builds the identity and writes optional observation files in this order:

1. `process-marker`: the current process identifier.
2. `identity-marker`: the entry value of `CORE_TEST_BOOTSTRAP_IDENTITY`, or `<null>`, followed by a newline. The probe then clears this variable.
3. `args-path`: one payload argument per line.

The application form applies when `CORE_TEST_APP_EXPECTED_VERSION` is set.
It requires `CORE_TEST_APP_READY_HANDLE` and writes `READY:<version>`.

The launcher form requires these inputs:

- `CORE_TEST_LAUNCHER_EXPECTED_READY`.
- `CORE_TEST_LAUNCHER_READY_HANDLE`.
- `app-version`.
- `app-admission`.
- `app-manifest`.

Its identity contains the expected READY value and the three application fields, separated by colons.
The admission field contains Base64-encoded UTF-8 bytes.
The wrong-identity mode changes only the version or the expected launcher READY value.
The partial mode closes the pipe before its wait.
The tree READY mode exits immediately after its write.
All other READY modes keep the pipe open during their wait.

The optional Job join runs before each mode except these four:

- `ambient-pipe`.
- `contained-isolation`.
- `arguments-environment`.
- `tree-grandchild`.

Without `CORE_TEST_LIFETIME_JOB`, the probe runs unmanaged.
With a Job name, it clears inheritance on a decimal `CORE_TEST_LIFETIME_HANDLE` and keeps that handle open until exit.
It removes the lifetime variables before it opens and joins the named Job.
Those variables are `CONTEXT`, `HANDLE`, `JOB`, `STATE_PATH`, and `KIND`, with the `CORE_TEST_LIFETIME_` prefix.
It keeps its Job handle open until exit.
Descendants become Job members through process creation.
The probe reads no bootstrap admission or start variables.

Descendants run the same executable with `--mode` and `--tree-marker` arguments.
They use no shell and create no console window.
Pipe-holding descendants keep standard output and standard error open.
Detached descendants inherit neither stream.
Each handshake has a five-second limit and a ten-millisecond poll interval.
The orphan handshake requires both the leaf marker and the middle process exit.

| Exit code | Meaning |
| --- | --- |
| 0 | Normal completion. |
| 1 | The lock file could not open. |
| 24 | The optional Job join failed. |
| 25 | A descendant handshake exceeded five seconds. |
| 64 | The mode is unknown, an input is missing or invalid, or the operating system is unsupported. |
| Other | The requested exit code for `exit` or `tree-root-exit`. |

The probe builds on other operating systems.
Modes that require Windows handles or a Job return 64 there.
Windows-only tests use the xUnit skip API.

Tests find the built probe in `probe/` beside the test assembly.
The build copies the executable, assembly, runtime configuration, and dependency manifest there.
Tests that rename the executable first copy the whole folder to a temporary folder.
They rename only the executable and clean up the copy after the test.