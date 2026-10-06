# Copyright (c) 2026 Dennis Liu. All rights reserved.
param(
    [string]$RepositoryRoot = '.'
)

$ErrorActionPreference = 'Stop'
& python -B (Join-Path $PSScriptRoot 'path_guard.py') --repository $RepositoryRoot
exit $LASTEXITCODE
