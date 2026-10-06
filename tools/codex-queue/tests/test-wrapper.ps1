# Copyright (c) 2026 Dennis Liu. All rights reserved.
param([string]$Wrapper, [string]$Fixture)
$ErrorActionPreference = 'Stop'
$root = Join-Path $Fixture 'wrapper'
New-Item -ItemType Directory -Path $root | Out-Null
$prompt = Join-Path $root 'prompt.md'
Set-Content -LiteralPath $prompt -Value 'Synthetic read-only fixture' -Encoding utf8
function global:codex {
    # Consume input without emitting it; no installed CLI is invoked.
    $null = $input | Out-String
    $global:LASTEXITCODE = 0
}
foreach ($mode in 'read-only', 'workspace-write') {
    $dir = Join-Path $root $mode
    $cache = Join-Path $dir '.pytest_cache'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $sentinel = Join-Path $cache 'sentinel.txt'
    Set-Content -LiteralPath $sentinel -Value 'preserve existing cache' -Encoding utf8
    $before = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
    $log = Join-Path $dir 'wrapper.log'
    & $Wrapper -PromptFile $prompt -Out (Join-Path $dir 'out.md') -Log $log -Sandbox $mode -Dir $dir
    if ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -ne $before) { throw 'cache changed' }
    if (-not (Select-String -LiteralPath $log -Pattern '^cache-residue-count=1$' -Quiet)) { throw 'missing cache report' }
}
Remove-Item Function:\codex
Write-Output 'PASS wrapper cache hashes unchanged in both sandbox modes'
