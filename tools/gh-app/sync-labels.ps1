# Copyright (c) 2026 Dennis Liu. All rights reserved.
#Requires -Version 7.4
<#
.SYNOPSIS
Creates or updates the labels of labels.json in one repository. It never deletes a label.
.DESCRIPTION
Without -Apply the script reads the repository labels and prints what it would create or change.
With -Apply it needs -IssuesLogPath outside the repository and requests issues: write for the labels it changes.
The owner must agree before each -Apply run.
.EXAMPLE
pwsh -NoProfile -File .\tools\gh-app\sync-labels.ps1 -Owner Dennis40816 -Repo nvt_fw_core
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Owner, [Parameter(Mandatory)][string]$Repo,
    [string]$DefinitionPath = (Join-Path $PSScriptRoot 'labels.json'),
    [switch]$Apply, [string]$IssuesLogPath)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NvtGhApp.psd1') -Force
$null = Import-GhAppConfig -Owner $Owner -Repo $Repo
Sync-GhAppLabels -DefinitionPath $DefinitionPath -Apply:$Apply -IssuesLogPath $IssuesLogPath |
    Format-Table Action, Name, Color, Changes, Description -AutoSize | Out-String -Width 200
