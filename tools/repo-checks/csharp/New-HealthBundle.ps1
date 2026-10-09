# Copyright (c) 2026 Dennis Liu. All rights reserved.
[CmdletBinding()]
param([switch]$Check)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# These six policy files are the distribution contract. The manifest cannot hash itself.
$bundleFiles = @(
    'Directory.Build.props',
    '.editorconfig',
    'BannedSymbols.Common.txt',
    'BannedSymbols.Files.txt',
    'BannedSymbols.Tests.txt',
    'schema.json'
)
$copyLines = @()
$fileLines = @()
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    for ($index = 0; $index -lt $bundleFiles.Count; $index++) {
        $name = $bundleFiles[$index]
        $path = Join-Path $PSScriptRoot $name
        $bytes = [System.IO.File]::ReadAllBytes($path)
        if ($bytes -contains 13 -or
            ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and
             $bytes[1] -eq 187 -and $bytes[2] -eq 191)) {
            throw "$name must use LF line endings and UTF-8 without a BOM."
        }
        $hash = [System.BitConverter]::ToString($sha256.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
        $comma = if ($index -lt $bundleFiles.Count - 1) { ',' } else { '' }
        $copyLines += '    "' + $name + '"' + $comma
        $fileLines += @(
            '    {',
            ('      "path": "' + $name + '",'),
            ('      "sha256": "' + $hash + '",'),
            ('      "byteLength": ' + $bytes.Length),
            ('    }' + $comma)
        )
    }
}
finally {
    $sha256.Dispose()
}

# Explicit formatting keeps output identical in Windows PowerShell 5.1 and PowerShell 7.
$manifestLines = @('{', '  "version": 1,', '  "copyFiles": [') + $copyLines +
    @('  ],', '  "files": [') + $fileLines + @('  ]', '}')
$utf8 = [System.Text.UTF8Encoding]::new($false)
$expected = $utf8.GetBytes(($manifestLines -join "`n") + "`n")
$manifestPath = Join-Path $PSScriptRoot 'bundle-manifest.json'
if ($Check) {
    $actual = [System.IO.File]::ReadAllBytes($manifestPath)
    if ([System.Convert]::ToBase64String($actual) -cne [System.Convert]::ToBase64String($expected)) {
        throw 'Bundle manifest disagrees with the policy files. Run New-HealthBundle.ps1 after reviewing the change.'
    }
    Write-Output "Bundle manifest verified: $($bundleFiles.Count) files."
}
else {
    [System.IO.File]::WriteAllBytes($manifestPath, $expected)
    Write-Output "Bundle manifest written: $($bundleFiles.Count) files."
}
