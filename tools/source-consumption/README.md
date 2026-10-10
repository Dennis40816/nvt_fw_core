# Verified Core source consumption

NFC uses the canonical [UiEventRunner.cs](../../src/Nvt.Core/Threading/UiEventRunner.cs) without a Core runtime dependency. NFH and NFU use the public type from the Core package. Keep the copied source byte-for-byte identical; retain its copyright header and comply with the repository [license](../../LICENSE).

[manifest.json](manifest.json) has schema version 1 and a `files` array. Each entry contains only the repository-relative `path`, `compileSymbol`, lowercase `sha256`, and `byteLength` of the exact UTF-8 source bytes with LF line endings. The version describes the manifest schema.

## Copy and compile

Choose an accepted Core commit and retain that commit identifier with the copied manifest in the consumer. Copy the source and manifest from that same checkout into the consumer; do not keep a separately edited implementation.

~~~powershell
# Set these to your existing checkouts.
$canonicalSource = Join-Path $coreCheckout 'src/Nvt.Core/Threading/UiEventRunner.cs'
$vendorDirectory = Join-Path $consumerCheckout 'Vendor/Core'
New-Item -ItemType Directory -Path $vendorDirectory -Force | Out-Null
Copy-Item -LiteralPath $canonicalSource -Destination (Join-Path $vendorDirectory 'UiEventRunner.cs')
Copy-Item -LiteralPath (Join-Path $coreCheckout 'tools/source-consumption/manifest.json') -Destination $vendorDirectory
~~~

Add `Vendor/Core/UiEventRunner.cs text eol=lf` to the consumer's `.gitattributes`. Avoid reformatting, newline conversion, or BOM insertion. This repository already forces LF for the canonical file.

Define `NVT_CORE_SOURCE_CONSUMPTION` in the consumer project that compiles the copy:

~~~xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);NVT_CORE_SOURCE_CONSUMPTION</DefineConstants>
</PropertyGroup>
~~~

The type becomes `internal sealed` and retains namespace `Nvt.Core.Threading`. Its members keep the package signatures. It uses only BCL types and starts work on the calling context. The consumer must provide nullable-enabled C# 10 or newer and a .NET runtime supporting `ArgumentNullException.ThrowIfNull` (the Core/test projects target `net10.0`). No implicit usings are required by the source file.

Consumer CI must reject a project compiling the source copy while also referencing the Core package, directly or transitively. Internal visibility alone does not prevent a same-name type clash. This is a consumer-side rule.

## Verify before building

In the Core checkout, generate the manifest after an intentional source change, then review both changes together:

~~~powershell
./tools/source-consumption/New-SourceConsumptionManifest.ps1
./tools/source-consumption/New-SourceConsumptionManifest.ps1 -Check
~~~

`-Check` throws (nonzero exit when run as a script process) for missing, stale, or malformed manifests and for CR line endings. It does not modify either file. Consumers verify the raw copied bytes against their pinned copy of the manifest; do not normalize text before verification or replace the expected hash with a hash calculated from the consumer copy.

~~~powershell
$manifest = Get-Content -LiteralPath (Join-Path $vendorDirectory 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne 1) { throw 'Unsupported source manifest version.' }
$entries = @($manifest.files | Where-Object { $_.path -eq 'src/Nvt.Core/Threading/UiEventRunner.cs' })
if ($entries.Count -ne 1 -or $entries[0].compileSymbol -cne 'NVT_CORE_SOURCE_CONSUMPTION') {
    throw 'Unexpected source manifest entry.'
}
$entry = $entries[0]
$copy = Join-Path $vendorDirectory 'UiEventRunner.cs'
$hash = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -cne $entry.sha256 -or (Get-Item -LiteralPath $copy).Length -ne $entry.byteLength) {
    throw 'Core source copy differs from the accepted canonical source.'
}
~~~

For updates, copy the source and manifest together from a newly accepted Core commit, review them, and rerun consumer behavior tests. The standalone [source-consumption tests](../../tests/Nvt.Core.SourceConsumption.Tests/) link the same file with the symbol defined, verify internal visibility and absence of a Core reference, and validate the manifest's hash and byte length.

## Adopt the package later

Remove the copied source from compilation and disk, remove `NVT_CORE_SOURCE_CONSUMPTION`, and remove the copy-specific manifest, hash check, and LF rule. Add the accepted Core package reference and keep the same `Nvt.Core.Threading` namespace and calling code. Replace the copy-exclusion CI rule as appropriate and rerun the consumer's event-handler behavior tests.
