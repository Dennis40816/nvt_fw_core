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
$verifiedFiles = @('repo-health.ps1')
$healthFiles = $bundleFiles + $verifiedFiles
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$lockPath = Join-Path $repoRoot 'eng/core-health.lock.json'
$copyLines = @()
$verifiedLines = @()
$fileLines = @()
$hashes = [ordered]@{}
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    for ($index = 0; $index -lt $healthFiles.Count; $index++) {
        $name = $healthFiles[$index]
        $path = if ($name -in $verifiedFiles) { Join-Path $PSScriptRoot "../$name" } else { Join-Path $PSScriptRoot $name }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing health file: $name ($path)." }
        $bytes = [System.IO.File]::ReadAllBytes($path)
        if ($bytes -contains 13 -or
            ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and
             $bytes[1] -eq 187 -and $bytes[2] -eq 191)) {
            throw "$name must use LF line endings and UTF-8 without a BOM."
        }
        $hash = [System.BitConverter]::ToString($sha256.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
        $hashes[$name] = $hash
        if ($index -lt $bundleFiles.Count) {
            $copyComma = if ($index -lt $bundleFiles.Count - 1) { ',' } else { '' }
            $copyLines += '    "' + $name + '"' + $copyComma
        }
        else {
            $verifiedComma = if ($index -lt $healthFiles.Count - 1) { ',' } else { '' }
            $verifiedLines += '    "' + $name + '"' + $verifiedComma
        }
        $comma = if ($index -lt $healthFiles.Count - 1) { ',' } else { '' }
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
    @('  ],', '  "verifiedFiles": [') + $verifiedLines +
    @('  ],', '  "files": [') + $fileLines + @('  ]', '}')
$utf8 = [System.Text.UTF8Encoding]::new($false)
$expected = $utf8.GetBytes(($manifestLines -join "`n") + "`n")
$manifestPath = Join-Path $PSScriptRoot 'bundle-manifest.json'
if ($Check) {
    $actual = [System.IO.File]::ReadAllBytes($manifestPath)
    if ([System.Convert]::ToBase64String($actual) -cne [System.Convert]::ToBase64String($expected)) {
        throw 'Bundle manifest disagrees with the policy files. Run New-HealthBundle.ps1 after reviewing the change.'
    }
    $lock = ConvertFrom-Json ([IO.File]::ReadAllText($lockPath))
    foreach ($name in $verifiedFiles) {
        $entry = $lock.files.PSObject.Properties[$name]
        if ($null -eq $entry -or $entry.Value -cne $hashes[$name]) { throw "Core lock hash of $name disagrees with the verified file. Run New-HealthBundle.ps1 -Distribute after reviewing the change." }
    }
    Write-Output "Bundle manifest verified: $($bundleFiles.Count) files to distribute and $($verifiedFiles.Count) verified file."
}
else {
    [System.IO.File]::WriteAllBytes($manifestPath, $expected)
    Write-Output "Bundle manifest written: $($bundleFiles.Count) files to distribute and $($verifiedFiles.Count) verified file."
}

if ($Distribute) {
    if ($Check) { throw '-Check cannot be combined with -Distribute' }
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
    }
    else { $commit = (ConvertFrom-Json ([IO.File]::ReadAllText($lockPath))).coreCommit }
    # Explicit lines keep the bytes the same in Windows PowerShell 5.1 and PowerShell 7 (ConvertTo-Json differs).
    $lockLines = @('{', '  "schemaVersion": 1,', ('  "coreCommit": "' + $commit + '",'), '  "files": {')
    $hashNames = @($hashes.Keys)
    for ($index = 0; $index -lt $hashNames.Count; $index++) {
        $lockComma = if ($index -lt $hashNames.Count - 1) { ',' } else { '' }
        $lockLines += ('    "' + $hashNames[$index] + '": "' + $hashes[$hashNames[$index]] + '"' + $lockComma)
    }
    $lockLines += @('  }', '}')
    [IO.File]::WriteAllBytes($lockPath, $utf8.GetBytes(($lockLines -join "`n") + "`n"))
    Write-Output 'Distributed six files and the managed EditorConfig block. Verify provenance before committing the pin.'
}
