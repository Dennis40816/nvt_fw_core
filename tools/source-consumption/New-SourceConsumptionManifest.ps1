# Copyright (c) 2026 Dennis Liu. All rights reserved.

[CmdletBinding()]
param([switch] $Check)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$manifestPath = Join-Path $PSScriptRoot 'manifest.json'
$sourcePath = 'src/Nvt.Core/Threading/UiEventRunner.cs'
$sourceBytes = [IO.File]::ReadAllBytes((Join-Path $repoRoot $sourcePath))
if ($sourceBytes -contains 13) {
    throw "$sourcePath must use LF line endings (see .gitattributes)."
}

$hasher = [Security.Cryptography.SHA256]::Create()
try {
    $sourceHash = [BitConverter]::ToString($hasher.ComputeHash($sourceBytes)).Replace('-', '').ToLowerInvariant()
}
finally {
    $hasher.Dispose()
}

$manifest = [ordered]@{
    version = 1
    files = @(
        [ordered]@{
            path = $sourcePath
            compileSymbol = 'NVT_CORE_SOURCE_CONSUMPTION'
            sha256 = $sourceHash
            byteLength = $sourceBytes.Length
        }
    )
}
$json = ($manifest | ConvertTo-Json -Depth 4).Replace("`r`n", "`n") + "`n"
if ($Check) {
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw 'Source-consumption manifest is missing. Run this script without -Check to generate it.'
    }
    $recorded = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($recorded.version -ne 1 -or @($recorded.files).Count -ne 1 -or
        $recorded.files[0].path -cne $sourcePath -or
        $recorded.files[0].compileSymbol -cne $manifest.files[0].compileSymbol -or
        $recorded.files[0].sha256 -cne $sourceHash -or
        $recorded.files[0].byteLength -ne $sourceBytes.Length) {
        throw 'Source-consumption manifest differs from the canonical source. Regenerate and review it.'
    }
    Write-Output 'Source-consumption manifest matches (1 file).'
}
else {
    [IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))
    Write-Output 'Wrote source-consumption manifest (1 file).'
}
