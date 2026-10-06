# Copyright (c) 2026 Dennis Liu. All rights reserved.
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param([Parameter(Mandatory)][ValidateSet('commander-tick')][string]$Id,
    [ValidateSet('\', '\NVT\')][string]$TaskPath = '\',
    [string]$CommanderDir = $env:COMMANDER_DIR)

$ErrorActionPreference = 'Stop'
if (-not $CommanderDir) {
    [Console]::Error.WriteLine('nvt-sched-runner: Set COMMANDER_DIR or pass -CommanderDir (commander folder).')
    exit 2
}
. (Join-Path $PSScriptRoot 'nvt-sched.ps1') -CommanderDir $CommanderDir
exit (Invoke-NvtRunner $Id -Legacy:($TaskPath -eq '\'))
