[English](Time.md) | [中文](Time.zh-TW.md)

# Time: DelegateTimeProvider

[`DelegateTimeProvider`](../../../src/Nvt.Core/Time/DelegateTimeProvider.cs) adapts delegates for tests and legacy time sources in `Nvt.Core.Time`, targeting `net8.0` with BCL dependencies only. Production code injects the BCL `TimeProvider`. NFC's `ISystemClock` is not ported because `TimeProvider.GetUtcNow()` replaces it directly.

```csharp
using Nvt.Core.Time;

var fixedValue = DateTimeOffset.UnixEpoch;
TimeProvider fixedClock = new DelegateTimeProvider(() => fixedValue);
var queue = new Queue<DateTimeOffset>([fixedValue, fixedValue.AddSeconds(1)]);
TimeProvider queuedClock = new DelegateTimeProvider(queue.Dequeue);
```

Kept behavior: each UTC read invokes its delegate exactly once, returning the value and offset unchanged without caching or reading the real UTC clock. Delegate exceptions propagate, including an empty queue's `InvalidOperationException`. The first constructor uses `Stopwatch.GetTimestamp()` and `Stopwatch.Frequency`; the second uses a timestamp delegate and a positive frequency. Both constructors reject null delegates, and the second rejects zero or negative frequencies. The class supports derivation. `GetElapsedTime` and `GetLocalNow` remain inherited; timers and the local time zone follow the base class and therefore the system. For inherited `GetLocalNow()`, supply zero-offset UTC values, as expected by `TimeProvider`. This adapter does not provide controllable timers.

Frozen parent baseline: NFC (`nvt_fw_combiner`), ref `origin/1.2.x`, full commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`. These helpers are behavior references only; none is ported:

- `tests/NvtFwCombiner.TestSupport/FakeClock.cs`: queued timestamps.
- `tests/NvtFwCombiner.Bootstrap.Tests/CompositionRunExecutionMetricsTests.cs`, lines 415–428: `CountingClock`.
- `tests/NvtFwCombiner.Application.Tests/Diagnostics/SystemInformationServiceTests.cs`, lines 418–423: `StubClock`.
- `tests/NvtFwCombiner.Application.Tests/VersionManagement/ManagedFirstInstallationBootstrapOutcomeTests.cs`, lines 685–697: timestamp and frequency overrides.

[`DelegateTimeProviderTests`](../../../tests/Nvt.Core.Tests/Time/DelegateTimeProviderTests.cs) uses synthetic data to verify argument validation, exact values and offsets, call counts, queue and counter delegates, elapsed time, stopwatch timestamps, inherited local time and mutable state in a derived helper. NFC has no direct clock-helper tests to port.

Core verification after the existing package restore, without network access:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test Nvt.Core.sln --no-build --filter "FullyQualifiedName~Nvt.Core.Tests.Time"
```

NFC adoption is pending (T12 and T13).
