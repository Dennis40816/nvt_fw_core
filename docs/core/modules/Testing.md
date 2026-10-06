[English](Testing.md) | [中文](Testing.zh-TW.md)

# Testing: NFC rendering parity

[`AvaloniaTestHost`](../../../src/Nvt.Core.Avalonia/Testing/AvaloniaTestHost.cs) matches NVT FW Combiner's (NFC) frozen Inter, Skia, and headless builder chain. Only the application type differs.

The namespace is `Nvt.Core.Avalonia.Testing`. Test projects compile the shared source directly. The runtime library excludes this file, so headless and xUnit dependencies remain in tests. The module supports dispatcher, layout, input, text measurement, and frame capture tests.

## Frozen parent baseline

The source repository is NFC (`nvt_fw_combiner`). The frozen ref is `origin/1.2.x` at commit `60e3f28e9c9f9926097e642e22e59d2a92ebc00e`.

The extraction used `git show` at that commit for these files:

- `tests/NvtFwCombiner.UiSmoke.Tests/AvaloniaHeadlessTestApplication.cs`: builder source.
- `tests/NvtFwCombiner.UiSmoke.Tests/AvaloniaApplicationResourceTests.cs`: existing UI-thread assertion.
- `tests/NvtFwCombiner.UiSmoke.Tests/packages.lock.json`: resolved rendering package evidence.

The Core tests retain the UI-thread assertion. NFC keeps its product resource checks. The source has no dedicated bootstrap test.

| Builder step | NFC frozen host | Core host |
| --- | --- | --- |
| Application | `AppBuilder.Configure<App>()` | `AppBuilder.Configure<TApplication>()` |
| Font | `.WithInterFont()` | `.WithInterFont()` |
| Renderer | `.UseSkia()` | `.UseSkia()` |
| Platform | `.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })` | `.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })` |

The Core lock file is [`tests/Nvt.Core.Avalonia.Tests/packages.lock.json`](../../../tests/Nvt.Core.Avalonia.Tests/packages.lock.json). Both lock files resolve the following versions. All four package content hashes also match.

| Package | NFC frozen lock | Core lock |
| --- | --- | --- |
| Avalonia.Skia | 12.0.5 | 12.0.5 |
| SkiaSharp | 3.119.4 | 3.119.4 |
| HarfBuzzSharp | 8.3.1.3 | 8.3.1.3 |
| Avalonia.Fonts.Inter | 12.0.5 | 12.0.5 |

## Zero-difference evidence

[`AvaloniaRenderingTests`](../../../tests/Nvt.Core.Avalonia.Tests/Testing/AvaloniaRenderingTests.cs) compares exact text measurements and raw frame pixels. Both builders used the same `ThemeTestApplication` and synthetic scene.

Two separate test processes first used NFC's frozen chain, with only its application type replaced. Both runs produced identical values. The Core builder then reproduced those values in two separate test processes. No measured value was unstable.

The baseline uses Windows NT 10.0.26300.0 and 96 DPI. Keep the same OS, fallback fonts, DPI, and theme when comparing. Only Inter text is pinned. Inter is embedded, so the values do not depend on system fonts. Traditional Chinese falls back to system fonts, which differ between machines. It will be pinned after the font set embeds Noto Sans TC.

The measurement test sets `FontFamily` to `avares://Avalonia.Fonts.Inter/Assets#Inter`. It disables layout rounding and measures each `TextBlock` against infinite available size. The exact `DesiredSize` values use device-independent pixels:

| Text | Font size | Width | Height |
| --- | --- | --- | --- |
| Core 123 | 11 | 46.734375 | 13.3125 |
| Core 123 | 13 | 55.23153409090909 | 15.732954545454545 |
| Core 123 | 16 | 67.97727272727272 | 19.363636363636363 |
| Core 123 | 24 | 101.96590909090907 | 29.045454545454547 |

The captured frame has a white background and four black Inter text rows. Each row contains `Core 123`. Font sizes are 11, 13, 16, and 24. The stack margin is 12 pixels, and row spacing is 6 pixels.

- Frame size: 320 × 240 pixels.
- Frame DPI: 96 × 96.
- Pixel format: RGBA8888.
- Hash input: raw rows from `Bitmap.CopyPixels`, with a 1,280-byte stride and 307,200 bytes total.
- SHA-256: `19F40303C54B8F27E07EE66C7B8A312EF02BD4A73387F6D9ABB0291929DEE4E6`.

[`AvaloniaTestHostTests`](../../../tests/Nvt.Core.Avalonia.Tests/Testing/AvaloniaTestHostTests.cs) also checks startup, dispatcher ownership, fixed control bounds, window closing, and keyboard/text routing. Input cases cover ASCII, Traditional Chinese, and emoji. Theme and Focus assertions remain unchanged.

## Adoption and verification

A test project needs `Avalonia.Headless.XUnit` and `Avalonia.Skia`, with the matching versions above. Keep the linked-source layout:

```xml
<Compile Include="..\..\src\Nvt.Core.Avalonia\Testing\AvaloniaTestHost.cs"
         Link="Testing\AvaloniaTestHost.cs" />
```

Register the host exactly once in each test assembly:

```csharp
[assembly: Avalonia.Headless.AvaloniaTestApplication(
    typeof(Nvt.Core.Avalonia.Testing.AvaloniaTestHost))]
```

An assembly that needs its own application registers that class instead. Its parameterless builder calls the generic host:

```csharp
public static AppBuilder BuildAvaloniaApp() =>
    AvaloniaTestHost.Build<ThemeTestApplication>();
```

Core keeps only `ThemeTestApplication`'s assembly registration. That application loads the extracted theme resources through the shared builder.

Use `[AvaloniaFact]` or `[AvaloniaTheory]` for Avalonia objects and `Dispatcher.UIThread`. Tests load their required resources and close windows in `finally`. The host adds no product styles, services, preload, persistence, or desktop lifetime.

Run the Core checks with telemetry disabled:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Nvt.Core.sln --no-restore
dotnet test tests/Nvt.Core.Avalonia.Tests/Nvt.Core.Avalonia.Tests.csproj --no-build --filter FullyQualifiedName~Testing --logger 'console;verbosity=detailed'
dotnet test Nvt.Core.sln --no-build
```

Before NFC switches hosts, run its unchanged UI smoke suite against the frozen parent:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet test tests/NvtFwCombiner.UiSmoke.Tests/NvtFwCombiner.UiSmoke.Tests.csproj --no-build
```

Keep NFC's product application when its adapter calls `AvaloniaTestHost.Build<App>()`. Run the same suite after switching. Compare exact measurements, control bounds, dispatcher ownership, focus, keyboard modifiers, delivered text, capture dimensions, and pixel hashes.

Include `AvaloniaApplicationResourceTests`, `StartupFocusTests`, `NavigationFocusIndicatorTests`, and the existing layout and capture tests. Compare existing screenshots against the unchanged frozen baseline. Any difference blocks adoption. Do not refresh the baseline to accept a difference. NFC adoption remains outside this change.
