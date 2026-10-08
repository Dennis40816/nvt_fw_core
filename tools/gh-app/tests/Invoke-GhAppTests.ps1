# Copyright (c) 2026 Dennis Liu. All rights reserved.
#Requires -Version 7.4
[CmdletBinding()]
param([string]$PesterVersion = '3.4.0')
$ErrorActionPreference = 'Stop'
$testRoot = $null
try {
    if ($PesterVersion -cne '3.4.0') { throw 'Only Pester 3.4.0 is supported.' }
    $testRoot = Join-Path ([IO.Path]::GetTempPath()) ('nvt-ghapp-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $testRoot
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'pwsh.exe'))
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($name in @($info.Environment.Keys)) {
        if ($name -match '^(GH_|GITHUB_|GCM_|BW_|GIT_CONFIG_)' -or $name -in @('GIT_ASKPASS', 'SSH_ASKPASS', 'GIT_TERMINAL_PROMPT')) {
            [void]$info.Environment.Remove($name)
        }
    }
    foreach ($name in @('TEMP', 'TMP', 'TMPDIR')) { $info.Environment[$name] = $testRoot }
    $info.Environment['NVT_GHAPP_TEST_ROOT'] = $testRoot
    $info.Environment['NVT_GHAPP_TEST_SUITE'] = Join-Path $PSScriptRoot 'NvtGhApp.Tests.ps1'
    $command = @'
$ErrorActionPreference = 'Stop'
try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    Import-Module Pester -RequiredVersion 3.4.0 -Force
    $loaded = @(Get-Module Pester)
    if ($loaded.Count -ne 1 -or $loaded[0].Version -ne [version]'3.4.0') { throw 'Pester 3.4.0 was not loaded.' }
    $result = Invoke-Pester -Script $env:NVT_GHAPP_TEST_SUITE -PassThru
    if ($null -eq $result -or ($result.PassedCount + $result.FailedCount) -lt 1 -or
        ($result.PassedCount + $result.FailedCount) -gt $result.TotalCount) { throw 'Pester returned no valid executed tests.' }
    Write-Output "GhApp tests: $($result.PassedCount) passed, $($result.FailedCount) failed, $($result.TotalCount) total."
    if ($result.FailedCount -gt 0) { exit 1 }
    exit 0
} catch {
    [Console]::Error.WriteLine("GhApp test runner failed: $($_.Exception.Message)")
    exit 2
}
'@
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-Command', $command)) { [void]$info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        Write-Output $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($errorText) { Write-Output $errorText }
        $exitCode = $process.ExitCode
        if ($exitCode -notin @(0, 1)) { $exitCode = 2 }
    } finally { $process.Dispose() }
} catch {
    [Console]::Error.WriteLine("GhApp test runner failed: $($_.Exception.Message)")
    $exitCode = 2
} finally {
    if ($testRoot -and [IO.Directory]::Exists($testRoot)) {
        $resolved = [IO.Path]::GetFullPath($testRoot)
        $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolved) -notmatch '^nvt-ghapp-[0-9a-f]{32}$') { throw 'Test cleanup path is outside its temporary directory.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
exit $exitCode
