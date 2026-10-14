# Copyright (c) 2026 Dennis Liu. All rights reserved.
#Requires -Version 7.0
<#
Runs repo-health Verify on a developer machine from a clean build state.

Why this exists: a compile server that another process started earlier (VBCSCompiler) keeps its own UI culture.
A build that reuses it can print a localized message, and a build that finds old output can print no analyzer
message at all. Both change the fingerprints that Verify compares, so Verify fails on code that did not change.
This script removes the two causes before it calls Verify:

1. It shuts down every build server of the current SDK (dotnet build-server shutdown) and stops any VBCSCompiler
   process. This also stops the compile server of another session, so a build that runs there can fail.
2. It deletes the bin and obj folders that sit directly next to a *.csproj file of this repository.
   It skips a project below a link, a bin or obj that is a link, and a project outside the repository root.

It does not change repo-health.ps1, so it is not part of the checker bundle and needs no pin synchronization.
CI runs on a fresh runner and does not need it.
#>
[CmdletBinding()]
param(
    [string]$Repo = 'core',
    # The repository root. It defaults to the root that contains this script (tools/repo-checks/..).
    [string]$Root = '',
    [string]$Solution = 'Nvt.Core.sln',
    [string]$BaseRef = 'origin/main'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Join-Path $PSScriptRoot '../..' }
$Root = (Resolve-Path -LiteralPath $Root).Path

# Delete nothing unless Root is the top of a Git working tree. A wrong Root must not reach other repositories.
$top = (& git -C $Root rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $top) { throw "Root is not inside a Git working tree: $Root" }
$topPath = (Resolve-Path -LiteralPath $top).Path
if (-not [string]::Equals($topPath.TrimEnd('\', '/'), $Root.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Root must be the top of the Git working tree. Root: $Root. Top: $topPath."
}

# The same fixed tool language that repo-health.ps1 sets for its own process. Set here too, so that restore prints
# its messages in English as well.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:VSLANG = '1033'
$env:PreferredUILang = 'en-US'

Write-Host 'Stopping build servers. A build that runs in another session can fail.'
& dotnet build-server shutdown | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet build-server shutdown exited $LASTEXITCODE" }
# A server of another SDK or a leftover process is not stopped by the command above.
foreach ($process in @(Get-Process -Name 'VBCSCompiler' -ErrorAction SilentlyContinue)) {
    Write-Host "Stopping VBCSCompiler $($process.Id)."
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
}

function Test-InsideRoot([string]$Path) {
    $relative = [IO.Path]::GetRelativePath($Root, $Path)
    return -not ($relative.StartsWith('..', [StringComparison]::Ordinal) -or [IO.Path]::IsPathRooted($relative))
}

# True when no directory from the given one up to the root (the root itself excluded) is a link.
# A link above a project would move the project, and its bin and obj, outside the repository.
function Test-LinkFree([IO.DirectoryInfo]$Directory) {
    $current = $Directory
    while ($null -ne $current -and -not [string]::Equals($current.FullName.TrimEnd('\', '/'), $Root.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
        if ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }
        $current = $current.Parent
    }
    return $null -ne $current
}

Write-Host 'Removing bin and obj folders next to project files.'
$removed = 0
foreach ($project in Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.csproj') {
    $relative = [IO.Path]::GetRelativePath($Root, $project.FullName)
    # Skip projects of nested build output and of a Git directory.
    if ($relative -match '(^|[\\/])(\.git|bin|obj|node_modules)[\\/]') { continue }
    $directory = Get-Item -LiteralPath $project.DirectoryName -Force
    # A project in a linked directory, or below one, belongs to another place. Skip it.
    if (-not (Test-LinkFree $directory)) { Write-Warning "Skipped a project below a link: $($directory.FullName)"; continue }
    if (-not (Test-InsideRoot $directory.FullName)) { Write-Warning "Skipped a project outside the root: $($directory.FullName)"; continue }
    foreach ($name in @('bin', 'obj')) {
        $folder = Join-Path $directory.FullName $name
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
        $item = Get-Item -LiteralPath $folder -Force
        # A link named bin or obj is not build output. Leave it, and never delete through it.
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { Write-Warning "Skipped a link: $folder"; continue }
        Remove-Item -LiteralPath $folder -Recurse -Force
        $removed++
    }
}
Write-Host "Removed $removed folders."

$code = 1
Push-Location $Root
try {
    & dotnet restore $Solution --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore exited $LASTEXITCODE" }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'repo-health.ps1') -Mode Verify -Repo $Repo -Root . -Solution $Solution -BaseRef $BaseRef
    $code = $LASTEXITCODE
}
finally { Pop-Location }
exit $code
