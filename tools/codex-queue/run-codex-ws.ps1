# Copyright (c) 2026 Dennis Liu. All rights reserved.
param(
    [Parameter(Mandatory)][string]$PromptFile,
    [Parameter(Mandatory)][string]$Out,
    [Parameter(Mandatory)][string]$Log,
    [string]$Model = '',
    [string]$Effort = '',
    [string]$Sandbox = 'read-only', [string]$AddDirs = '', [Parameter(Mandatory)][string]$Dir
)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$prompt = Get-Content -Raw -Encoding utf8 -LiteralPath $PromptFile; if (-not $prompt) { throw "empty prompt: $PromptFile" }
# Owner decision (2026-10-03): no model or effort is hard-coded. Without -Model/-Effort Codex uses its own default
# (~/.codex/config.toml, adjusted by the owner/commander); a task that needs another model or effort passes it for that one run.
$modelArgs = @(); if ($Model) { $modelArgs += '-m'; $modelArgs += $Model }; if ($Effort) { $modelArgs += '-c'; $modelArgs += "model_reasoning_effort=`"$Effort`"" }
$modelLabel = if ($Model) { $Model } else { 'codex-default' }; $effortLabel = if ($Effort) { $Effort } else { 'codex-default' }
"start $(Get-Date -Format o) model=$modelLabel effort=$effortLabel out=$Out" | Out-File -Encoding utf8 $Log
$extra = @(); foreach ($d in $AddDirs.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) { $extra += '--add-dir'; $extra += $d }
# Owner request (2026-10-02): nothing the sandbox creates may be undeletable by the commander.
# The sandbox creates pytest's cache folder with an access list this account cannot remove, so pytest
# inside Codex never writes that cache, and Python writes no bytecode folders into the worktree.
$env:PYTEST_ADDOPTS = '-p no:cacheprovider'
$env:PYTHONDONTWRITEBYTECODE = '1'
$sandboxEnv = @(
    '-c', 'shell_environment_policy.set.PYTEST_ADDOPTS="-p no:cacheprovider"',
    '-c', 'shell_environment_policy.set.PYTHONDONTWRITEBYTECODE="1"',
    '-c', 'shell_environment_policy.set.AVALONIA_TELEMETRY_OPTOUT="1"',
    '-c', 'shell_environment_policy.set.DOTNET_CLI_UI_LANGUAGE="en"',
    '-c', 'shell_environment_policy.set.DOTNET_CLI_TELEMETRY_OPTOUT="1"',
    '-c', 'shell_environment_policy.set.DOTNET_NOLOGO="1"'
)
$prompt | codex exec @modelArgs @sandboxEnv -s $Sandbox @extra -C $Dir --skip-git-repo-check -o $Out *>> $Log
$code = $LASTEXITCODE
# The service sometimes ends a run with "Selected model is at capacity" (three times on 2026-10-02). The run's
# edits stay in the working tree, so the same prompt is sent again with a note to continue from the present state.
$resume = "NOTE FROM THE COMMANDER: an earlier attempt at this exact task was cut off by a service capacity error. Its edits may already be in the working tree. First inspect the present state (git status, git diff), keep what is right, and continue until the task's stop rules are met; do not start over.`n`n"
for ($attempt = 2; $attempt -le 4 -and $code -ne 0 -and -not (Test-Path -LiteralPath $Out); $attempt++) {
    if (-not (Select-String -LiteralPath $Log -Pattern 'at capacity' -Quiet)) { break }
    "retry attempt=$attempt after capacity error $(Get-Date -Format o)" | Out-File -Encoding utf8 -Append $Log
    Start-Sleep -Seconds 120
    ($resume + $prompt) | codex exec @modelArgs @sandboxEnv -s $Sandbox @extra -C $Dir --skip-git-repo-check -o $Out *>> $Log
    $code = $LASTEXITCODE
}
# Report existing caches without changing them, including in read-only tasks.
$left = @()
foreach ($name in '.pytest_cache') {
    Get-ChildItem -LiteralPath $Dir -Force -Recurse -Directory -Filter $name -ErrorAction SilentlyContinue | ForEach-Object {
        $left += $_.FullName
    }
}
if ($left.Count) { "cache-residue-count=$($left.Count)" | Out-File -Encoding utf8 -Append $Log }
"exit=$code end $(Get-Date -Format o)" | Out-File -Encoding utf8 -Append $Log
