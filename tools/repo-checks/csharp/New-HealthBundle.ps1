# Copyright (c) 2026 Dennis Liu. All rights reserved.
[CmdletBinding()]
param([switch]$Check, [switch]$Distribute, [string]$CoreCommit = '')

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

if ($Distribute) {
    if ($Check) { throw '-Check cannot be combined with -Distribute' }
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
    $destination = Join-Path $repoRoot 'eng/core-health'
    [void][IO.Directory]::CreateDirectory($destination)
    foreach ($name in $bundleFiles) {
        [IO.File]::WriteAllBytes((Join-Path $destination $name), [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $name)))
    }
    $editorPath = Join-Path $repoRoot '.editorconfig'
    $common = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '.editorconfig'))
    $begin = '# BEGIN CORE HEALTH MANAGED BLOCK'
    $end = '# END CORE HEALTH MANAGED BLOCK'
    $editor = [IO.File]::ReadAllText($editorPath)
    if ($editor.Contains($begin)) {
        $pattern = '(?s)' + [regex]::Escape($begin) + '\n.*?' + [regex]::Escape($end)
        $editor = [regex]::Replace($editor, $pattern, $begin + "`n" + $common + $end)
    }
    elseif ($editor.Replace("`r`n", "`n") -ceq $common) { $editor = $begin + "`n" + $common + $end + "`n" }
    else { throw 'Root .editorconfig needs a reviewed managed-block integration' }
    [IO.File]::WriteAllText($editorPath, $editor, $utf8)
    if ($CoreCommit) {
        $commit = (& git -C $repoRoot rev-parse "$CoreCommit^{commit}").Trim()
        if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid CoreCommit' }
        $hashes = [ordered]@{}
        foreach ($name in $bundleFiles) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $destination $name) -Algorithm SHA256).Hash.ToLowerInvariant() }
        $lock = [ordered]@{ schemaVersion = 1; coreCommit = $commit; files = $hashes }
        [IO.File]::WriteAllText((Join-Path $repoRoot 'eng/core-health.lock.json'), ($lock | ConvertTo-Json -Depth 10).Replace("`r`n", "`n") + "`n", $utf8)
    }
    Write-Output 'Distributed six files and the managed EditorConfig block. Verify provenance before committing the pin.'
}
