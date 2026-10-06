[English](Persistence.md) | [中文](Persistence.zh-TW.md)

# Persistence: LocalJsonDocument and LatestSnapshotPersistenceCoordinator

[`LocalJsonDocument`](../../../src/Nvt.Core/Persistence/LocalJsonDocument.cs) supplies the existing local-state JSON options, host-supplied directory/file-name composition, and streamed deserialization of UTF-8 and BOM-marked UTF-16/UTF-32. It requires a seekable stream and leaves it open. File writing, atomic promotion, size limits, schemas, and fallback policy remain with the host. The extraction changes only the namespace, public visibility, copyright header, and API documentation.

[`LatestSnapshotPersistenceCoordinator<TSnapshot>`](../../../src/Nvt.Core/Persistence/LatestSnapshotPersistenceCoordinator.cs) captures host-supplied snapshots synchronously, serializes saves, cancels superseded work, and reports terminal outcomes with request generations. Retry reuses the latest captured value. `CompleteAsync` seals admission and waits for the current save and its observer without cancelling them; `Reopen` preserves the serial tail after a failed close. Save failures are recorded without faulting the tail, and later success does not clear `LastFailure`. The coordinator has no dispose API; hosts await completion before disposing their persistence resources. Besides the namespace, public visibility, copyright header, and API documentation, the sole implementation change is replacing `private readonly Lock _gate` with `private readonly object _gate`. All existing `lock` statements and mutual-exclusion boundaries are unchanged for net8.0 compatibility.

Frozen module baseline: NFC (`nvt_fw_combiner`), `origin/1.2.x`, commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. Source was read with `git show` at that commit:

- Extracted: `src/NvtFwCombiner.Presentation.Avalonia/LocalJsonDocument.cs`.
- Codec scenarios ported from `tests/NvtFwCombiner.UiSmoke.Tests/ShellNavigationSystemTests.Preferences.cs`: the codec portion of `ShellPreferenceFileStoreRoundTripsAndInvalidValuesFallBack` and `ShellPreferenceFileStoreLoadsBomDocuments`.
- Codec scenario ported from `tests/NvtFwCombiner.UiSmoke.Tests/ReportHistoryPersistenceTests.cs`: `LoadLargeHistoryAvoidsWholeFileTextAllocation`, retaining its 4 MiB payload and three-bytes-per-character allocation bound for UTF-8 and legacy UTF-16. Core uses synthetic in-memory documents; product stores and UI assertions remain in NFC.
- Extracted: `src/NvtFwCombiner.Presentation.Avalonia/LatestSnapshotPersistenceCoordinator.cs` from the same frozen commit as the codec, with the object lock replacement that net8.0 requires.
- All four direct coordinator tests ported from `tests/NvtFwCombiner.UiSmoke.Tests/ReportHistoryPersistenceTests.cs`: `CoordinatorKeepsLatestQueuedSnapshot`, `CoordinatorCompletesLatestSaveBeforeShutdown`, `CoordinatorReopenSerializesNewSnapshotAfterDelayedOldSave`, and `CoordinatorRecoversAfterSaveFault`. Synthetic snapshots and an in-memory save replace product report types and file stores while preserving the scheduling and shutdown assertions.
- All four direct coordinator tests ported from `tests/NvtFwCombiner.UiSmoke.Tests/LocalStateSaveNoticeTests.cs`: `CoordinatorReportsTerminalSavesButNotSupersededOnes`, `GenerationStaysCurrentOnlyUntilANewerSnapshotIsQueued`, `CoordinatorRetryRequeuesLatestCapturedSnapshot`, and `CoordinatorObserverFailureDoesNotPoisonLaterSaves`. Product UI and host store tests remain in NFC.

[`LocalJsonDocumentTests`](../../../tests/Nvt.Core.Tests/Persistence/LocalJsonDocumentTests.cs) also characterize exact serialization bytes, Unicode, short/null JSON, invalid JSON, property matching, cancellation, caller stream ownership, absolute-position reset, partial prefix reads, non-seekable input, and path argument behavior.

[`LatestSnapshotPersistenceCoordinatorTests`](../../../tests/Nvt.Core.Tests/Persistence/LatestSnapshotPersistenceCoordinatorTests.cs) adds characterization of mutable input capture, silent supersession cancellation, unrelated cancellation failures, retry generations and retained failures, empty completion/reopen, and null argument validation. Two deterministic close races cover shutdown while another request is still capturing and shutdown while a terminal observer is running after the save's cancellation source was disposed. They pin rejection of late writes, safe retry after cancellation-source disposal, serial ordering, and completion waiting for observers.

Run Core verification with the existing restored packages:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build
```

Zero-difference verification for a later NFC adoption: first run Core's persistence tests and NFC's original tests at the frozen commit, then repeat after NFC references Core and removes the duplicated codec and coordinator. In each NFC checkout, after its own build with `--no-restore`, run:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build --filter "FullyQualifiedName~ShellNavigationSystemTests|FullyQualifiedName~ReportHistoryPersistenceTests|FullyQualifiedName~LocalStateSaveNoticeTests|FullyQualifiedName~WindowLifetimeTests"
```

Use identical synthetic documents and serializer calls to compare saved UTF-8 bytes byte-for-byte, loaded values and exceptions for every encoding, stream/cancellation behavior, and the existing allocation bound. With the same gated save schedules, compare admitted/saved snapshot order, cancellation, terminal observer outcomes and generations, retained failure identity, retry capture counts, rejection after completion, reopen ordering, and whether close waits for the save and its observer. Keep the same OS/runtime for each NFC comparison and retain its host policies, including detaching UI observers on disposal. NFC's source-text architecture checks must later recognize the shared codec/coordinator references while keeping host-boundary checks. No NFC adoption or baseline refresh is performed here.
