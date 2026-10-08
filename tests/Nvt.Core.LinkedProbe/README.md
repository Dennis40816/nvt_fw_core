# Core-linked test probe

The linked probe runs Core code inside a test-only child process.
It references `src/Nvt.Core` and never ships or packs.
It uses a framework-dependent .NET 8 apphost with no package references.
It runs from any folder under any executable name.

## Ownership

| Files | Owner | Changes |
| --- | --- | --- |
| Project, lock file, and shared source files | NVT CORE | NVT CORE maintains these files. |
| `README.md` | NVT CORE | Lanes add their mode rows. |
| `Launcher/*.cs` | Launcher startup protocol work | Add new source files and README mode rows only. |
| `Recovery/*.cs` | Launcher recovery work | Add new source files and README mode rows only. |
| Solution entry, copy target, and Core friend assembly entry | NVT CORE | The skeleton supplies this wiring. |
| `tests/Nvt.Core.Tests/LinkedProbe/` | NVT CORE | Skeleton tests verify the shared contract. |

The shared source files are `Program.cs`, `ProbeInputs.cs`, `ModeAttribute.cs`, `ProbeContext.cs`, `ProbeExitCodes.cs`, `ProtocolNames.cs`, and `SelfCheck.cs`.
Lanes request shared-file changes from NVT CORE.
SDK globbing includes new mode files without project edits.
The skeleton contains no Launcher or Recovery mode.

## Inputs

Each input accepts `--<name> <value>` or `CORE_LINKED_PROBE_<NAME>`.
Environment names use uppercase letters and replace hyphens with underscores.
Arguments override environment values.
Recognized input pairs leave the payload.
Other arguments retain their order, including empty arguments and unknown options.
Missing required inputs write one standard-error line naming the input and exit 64.

| Name | Default | Use |
| --- | --- | --- |
| `mode` | Required | Selects the mode. |
| `protocol-prefix` | `CORE_TEST_` | Prefixes all 13 R05 protocol suffixes. |
| `marker` | Required by self-check | Receives mode evidence. |
| `state-path` | None | Reserved for lifetime and recovery modes. |
| `boundary` | None | Reserved for recovery modes. |
| `pause` | None | Reserved for recovery modes. |

The child creates no folders.
Marker files use UTF-8 without a byte order mark.
Their lines use the platform newline.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Normal end. |
| 24 | Lifetime capture failed. |
| 25 | A descendant or release handshake timed out. |
| 26 | START gate context or bytes were wrong. |
| 64 | Unknown mode, missing input, or unsupported operating system. |
| 70 | Duplicate mode name in the child assembly. |
| Core codec values | Future bootstrap modes return failure values from the Core codec. |

## Modes

Dispatch discovers static `Task<int> RunAsync(ProbeContext)` methods marked with `[LinkedProbeMode("<name>")]` in the child assembly.
`ProbeContext` supplies inputs, payload arguments, protocol names, and a cancellation token.
Mode names use ordinal comparison.
Duplicate names fail before any mode runs.
A mode method with another signature fails at run time. Run `ModeTableContainsSelfCheckWithoutDuplicateNames` after adding a mode.
Only `linked-self-check` runs outside Windows.
The dispatcher joins no Job.

| Mode | Inputs | Behavior | Exit |
| --- | --- | --- | --- |
| `linked-self-check` | `marker`, optional `protocol-prefix` | Writes the Core assembly name, informational version, 13 protocol names in record order, and the folder Core loaded from, as 16 lines. | 0 |
| `bootstrap-ready` | `state-path` and inherited START/lifetime context | Captures the Core START gate and requires `v1` plus the exact first six bytes `START\n`, then captures Bootstrap lifetime. | Core StateUnavailable (18) after capture; 24 on capture failure; 26 on invalid START |
| `bootstrap-identity-chain-root` | `state-path`, `marker`, inherited START/admission/lifetime context | Runs the exact START gate, starts `identity-context-child` through the contained gate with the exact lifetime handle, then captures its own lifetime and writes `ADMITTED\n`. | 0; child's failure code; 24 or 26 |
| `identity-context-child` | `marker`, optional `state-path`, inherited lifetime/identity context | Captures lifetime and calls Core `CaptureAndClear` only after Captured. Writes outcome, identity before/after, file name, length and SHA-256 as six lines (`<null>` for absent values). | 0, including failed capture |
| `launcher-identity-observation` | `state-path`, `marker`, inherited Launcher lifetime/READY/identity context | After Core capture and identity clearing, writes the six identity lines and emits the frozen synthetic launcher READY bytes through a BCL pipe. | 0; 24 on capture failure |
| `bootstrap-exit-22` | `state-path`, `marker`, inherited Bootstrap lifetime | Captures Core lifetime, writes its PID, then encodes InvalidInheritedContext with the Core codec. | 22; 24 on capture failure |
| `bootstrap-eof-before-exit-18` | `state-path`, `marker`, inherited Bootstrap lifetime | Captures Core lifetime, writes its PID and waits up to five seconds for `<marker>.release`. | Core StateUnavailable (18); 25 on timeout; 24 on capture failure |
| `lifetime-capture` | `marker`, optional `state-path` and `pause=dispose`, inherited lifetime | Chooses Application when expected application version is set, otherwise Bootstrap when the lifetime role says Bootstrap, otherwise Launcher. Capture is required for a state path or either expected READY value. Writes the Core outcome. The disposal handshake writes `.context` (inheritance flag and five cleared variables), waits up to five seconds for `.release`, disposes capture, writes handle openness to `.disposed`, then waits up to five seconds for `.exit`. | 0 after Captured; 24 otherwise; 25 on handshake timeout |
| `bootstrap-runtime` | `state-path`, `marker`, inherited Bootstrap START/admission/lifetime context | Calls the public Core runtime with synthetic fail-closed services. Writes `.waiting` after real capture; state composition is blocked until START. Writes composition/load events and `.closed` handle observations. | Core InvalidState (10), StartNotAuthorized (23), or InvalidInheritedContext (22) |

## Test location

`Nvt.Core.Tests` builds the child through a project reference with `ReferenceOutputAssembly="false"`.
`CopyLinkedProbe` copies the entire child output into `linked-probe/` beside the test assembly after Build.
The BCL probe keeps its separate `probe/` folder.
`LinkedProbeWorkspace` owns the child path:

```csharp
Path.Combine(AppContext.BaseDirectory, "linked-probe",
    OperatingSystem.IsWindows() ? "Nvt.Core.LinkedProbe.exe" : "Nvt.Core.LinkedProbe")
```

Rename tests first copy the entire output folder, including Core and runtime files.
They rename only the apphost and then run it from another working folder.
