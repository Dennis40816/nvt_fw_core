[English](Csv.md) | [中文](Csv.zh-TW.md)

# Csv: CsvQuoting

[`CsvQuoting.Quote`](../../../src/Nvt.Core/Csv/CsvQuoting.cs) quotes one CSV field. It is in `Nvt.Core.Csv`, targets `net10.0` and depends only on the BCL. Tool adoption is a separate task.

Frozen parent baseline: NFU (`Dennis40816/nvt-event-buffer-replay`), `origin/0.2.0`, commit `915d0c1b571a2c4a95c8c6d2d3cc6421079ff99b`. Extracted from two identical private helpers:

- `src/Nvt.Replay.Rendering/AnalysisOutputWriter.cs:164-165`
- `src/Nvt.Replay.Rendering/ReadableCommunicationLogWriter.cs:123-124`

## Behavior

- The method quotes a field only when it contains a comma, a double quote, CR or LF.
- A quoted field gets outer double quotes, and each embedded double quote is doubled.
- All other text stays unchanged. This includes empty strings, Unicode, tabs, semicolons and leading or trailing spaces.
- Line endings inside a field stay exactly as they are.
- Null throws `ArgumentNullException` with the parameter name `value`. The old private helpers threw `NullReferenceException` instead.

No NFU caller passes null. Analysis fields are non-nullable or fall back to empty strings. Communication rows replace nullable fields with empty strings before quoting. The field declarations are in `src/Nvt.Replay.Analysis/CaptureAnalysis.cs` and `src/Nvt.Replay.Core/CaptureModels.cs`.

## Verification and adoption

[`CsvQuotingTests`](../../../tests/Nvt.Core.Tests/Csv/CsvQuotingTests.cs) compares a fixed corpus with literal expected strings from the frozen rule. It also checks the null rejection.

Run after the existing package restore:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj --no-build
```

NFU adoption replaces the two private helpers with this method. It is a separate task that uses the versioned Core package. The adoption evidence is that the analysis CSV files and the communication log CSV keep the same bytes before and after the change.
