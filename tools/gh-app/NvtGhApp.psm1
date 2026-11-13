# Copyright (c) 2026 Dennis Liu. All rights reserved.
#Requires -Version 7.4
Set-StrictMode -Version Latest
$script:Configuration = $null
$script:WriteTokenAttempts = 3

function Get-GhAppConfigPath {
    param([string]$Owner, [string]$Repo)
    Join-Path $HOME ".nvt/gh-app/$Owner-$Repo.json"
}

# The owner default is the per-repository config without its repo field. A repository with no file of its own uses it, so a new repository needs no hand-made file.
function Get-GhAppOwnerConfigPath {
    param([string]$Owner)
    Join-Path $HOME ".nvt/gh-app/$Owner.owner-default.json"
}

function Test-GhAppRepositoryPath {
    param([string]$Path, [Collections.Generic.HashSet[string]]$Visited)
    if ($null -eq $Visited) { $Visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase) }
    $Path = [IO.Path]::GetFullPath($Path)
    if (-not $Visited.Add($Path)) { return $false }
    $file = [IO.FileInfo]::new($Path)
    if ($file.LinkTarget) {
        $target = $file.ResolveLinkTarget($true)
        if ($null -eq $target -or (Test-GhAppRepositoryPath $target.FullName $Visited)) { return $true }
    }
    $directory = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path)))
    while ($null -ne $directory) {
        $marker = Join-Path $directory.FullName '.git'
        if ([IO.File]::Exists($marker) -or [IO.Directory]::Exists($marker)) { return $true }
        if ([IO.File]::Exists((Join-Path $directory.FullName 'HEAD')) -and
            [IO.File]::Exists((Join-Path $directory.FullName 'config')) -and
            [IO.Directory]::Exists((Join-Path $directory.FullName 'objects'))) { return $true }
        if ($directory.LinkTarget) {
            $target = $directory.ResolveLinkTarget($true)
            if ($null -eq $target -or (Test-GhAppRepositoryPath (Join-Path $target.FullName '.gh-app-path-check') $Visited)) { return $true }
        }
        $directory = $directory.Parent
    }
    return $false
}

function Import-GhAppConfig {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Owner, [Parameter(Mandatory)][string]$Repo)
    $script:Configuration = $null
    if ($Owner -cnotmatch '^[A-Za-z0-9-]+\z' -or $Repo -cnotmatch '^[A-Za-z0-9_.-]+\z' -or $Repo -in @('.', '..')) {
        throw 'Owner and Repo must name one GitHub repository.'
    }
    $path = Get-GhAppConfigPath $Owner $Repo
    if (Test-GhAppRepositoryPath $path) { throw 'The config file must be outside every repository.' }
    # The repository file wins. Only a repository with no file of its own falls back to the owner default, under the same checks.
    $usesOwnerDefault = $false
    if (-not [IO.File]::Exists($path)) {
        $ownerPath = Get-GhAppOwnerConfigPath $Owner
        if (Test-GhAppRepositoryPath $ownerPath) { throw 'The config file must be outside every repository.' }
        if (-not [IO.File]::Exists($ownerPath)) { throw "The repository config file is missing. Expected $path or the owner default $ownerPath." }
        $path = $ownerPath
        $usesOwnerDefault = $true
    }
    try { $config = [IO.File]::ReadAllText($path) | ConvertFrom-Json -AsHashtable -ErrorAction Stop }
    catch { throw 'The repository config file must contain a JSON object.' }
    if ($config -isnot [System.Collections.IDictionary]) { throw 'The repository config file must contain a JSON object.' }
    if ($usesOwnerDefault) {
        # The token helper still narrows each token to the one repository named here, so the default never widens access.
        if ($config.Contains('repo')) { throw "The owner default config file must not contain a 'repo' field." }
        $config['repo'] = $Repo
    }
    $fields = @('owner', 'repo', 'clientId', 'installationId', 'tokenHelperPath', 'dpapiPath', 'botLogin')
    foreach ($field in $fields) {
        if (-not $config.Contains($field) -or [string]::IsNullOrWhiteSpace([string]$config[$field])) {
            throw "Config field '$field' is required."
        }
    }
    if ($config.owner -cne $Owner -or $config.repo -cne $Repo) { throw "Repository mismatch. Expected $Owner/$Repo." }
    if ([string]$config.installationId -cnotmatch '^[1-9][0-9]{0,17}\z') { throw "Config field 'installationId' must be a positive integer." }
    foreach ($field in @('owner', 'repo', 'clientId', 'tokenHelperPath', 'dpapiPath', 'botLogin')) {
        if ($config[$field] -isnot [string] -or $config[$field] -match '[\x00-\x1f\x7f]') {
            throw "Config field '$field' must be a string without control characters."
        }
    }
    foreach ($field in @('tokenHelperPath', 'dpapiPath')) {
        if (-not [IO.Path]::IsPathFullyQualified($config[$field])) { throw "Config field '$field' must be an absolute path." }
    }
    if ([IO.Path]::GetExtension($config.tokenHelperPath) -ine '.ps1' -or -not [IO.File]::Exists($config.tokenHelperPath)) {
        throw "Config field 'tokenHelperPath' must name an installed PowerShell script."
    }
    # A helper or key inside a repository could be changed by repository work, then run with App rights.
    foreach ($field in @('tokenHelperPath', 'dpapiPath')) {
        if (Test-GhAppRepositoryPath $config[$field]) { throw "Config field '$field' must be outside every repository." }
    }
    # Copy only the defined fields. The returned object cannot change the active configuration.
    $script:Configuration = @{}
    foreach ($field in $fields) { $script:Configuration[$field] = $config[$field] }
    [pscustomobject]$script:Configuration.Clone()
}

function Get-GhAppContext {
    param([string]$Repository)
    if ($null -eq $script:Configuration) { throw 'Import-GhAppConfig must succeed before using this module.' }
    $expected = "$($script:Configuration.owner)/$($script:Configuration.repo)"
    if (($Repository -and $Repository -cne $expected) -or ($env:GH_REPO -and $env:GH_REPO -cne $expected)) {
        throw "Repository mismatch. Expected $expected."
    }
    $script:Configuration
}

function New-GhAppProcessInfo {
    param([string]$Executable, [string[]]$Arguments)
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    $info.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($name in @($info.Environment.Keys)) {
        if ($name -match '^(GH_|GITHUB_|GCM_|BW_|GIT_CONFIG_)' -or $name -in @('GIT_ASKPASS', 'SSH_ASKPASS', 'GIT_TERMINAL_PROMPT')) {
            [void]$info.Environment.Remove($name)
        }
    }
    foreach ($argument in $Arguments) { [void]$info.ArgumentList.Add($argument) }
    $info
}

function Get-GhAppToken {
    param([hashtable]$Context, [string]$ApiPath)
    $process = $null
    $token = $null
    $helperError = $null
    try {
        $executable = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
        $arguments = @('-NoProfile', '-NonInteractive', '-File', $Context.tokenHelperPath,
            '-Mode', 'token', '-Owner', $Context.owner, '-Repo', $Context.repo,
            '-ClientId', $Context.clientId, '-InstallationId', [string]$Context.installationId,
            '-DpapiPath', $Context.dpapiPath)
        # Only a push that changes workflow files sets this flag, and only for that one call chain.
        if ($Context.ContainsKey('includeWorkflowsWrite') -and $Context.includeWorkflowsWrite) { $arguments += '-IncludeWorkflowsWrite' }
        if ($Context.ContainsKey('includeIssuesWrite') -and $Context.includeIssuesWrite) { $arguments += '-IncludeIssuesWrite' }
        $process = [Diagnostics.Process]::Start((New-GhAppProcessInfo $executable $arguments))
        $process.StandardInput.Close()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $token = $outputTask.GetAwaiter().GetResult()
        $helperError = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or $token -cnotmatch '^[\x21-\x7e]+\z') { throw 'Invalid helper result.' }
        return $token
    } catch {
        # Report only the helper's exit code. Its output and error text may hold secrets.
        # Only fixed permission messages are safe to show.
        $code = $(if ($null -ne $process -and $process.HasExited) { $process.ExitCode } else { 'none' })
        $known = $(if ($helperError -clike '*Installation token lacks the requested workflows permission.*') { ': Installation token lacks the requested workflows permission.' }
            elseif ($helperError -clike '*Installation token lacks the requested issues permission.*') { ': Installation token lacks the requested issues permission.' } else { '' })
        throw "HTTP unknown $ApiPath (token helper exit $code)$known"
    }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        $token = $null
    }
}

# A token failure happens before gh starts, so nothing reached GitHub and a write may retry it. The helper can take
# seconds and the waits add more, so a caller that needs no gap before its write fetches the token first.
function Get-GhAppTokenWithRetry {
    param([hashtable]$Context, [string]$ApiPath, [int]$TokenAttempts = 1)
    for ($attempt = 1; ; $attempt++) {
        try { return Get-GhAppToken $Context $ApiPath }
        catch {
            if ($attempt -lt $TokenAttempts) { Start-Sleep -Seconds (2 * $attempt); continue }
            $failure = $_.Exception.Message
            if ($TokenAttempts -gt 1) { $failure = "$failure (after $TokenAttempts attempts)" }
            throw $failure
        }
    }
}

# The token for one write to a repository path, fetched ahead of the write. Pass it to Invoke-GhAppApi as PreparedToken.
function Get-GhAppWriteToken {
    param([hashtable]$Context, [string]$Suffix)
    Get-GhAppTokenWithRetry $Context "/repos/$($Context.owner)/$($Context.repo)/$Suffix" $script:WriteTokenAttempts
}

function Invoke-GhAppGh {
    param([hashtable]$Context, [string[]]$Arguments, [string]$ApiPath,
        [AllowNull()][string]$InputText, [switch]$AsApp, [int[]]$AllowedExitCodes = @(0), [switch]$AllowNotFound,
        [switch]$Read, [string]$PreparedToken)
    # Only reads retry. A write that fails after GitHub applied it must never repeat, for example a comment or a merge.
    $attempts = $(if ($Read) { 3 } else { 1 })
    [void]$PSBoundParameters.Remove('Read')
    # A token failure happens before gh starts, so nothing reached GitHub and a write may retry it too.
    # A read already retries the whole call, so its token step runs once per attempt.
    $PSBoundParameters['TokenAttempts'] = $(if ($Read) { 1 } else { $script:WriteTokenAttempts })
    for ($attempt = 1; ; $attempt++) {
        try { return Invoke-GhAppGhOnce @PSBoundParameters }
        catch {
            $message = $_.Exception.Message
            # Retry unknown failures, rate limits and server errors. Other 4xx answers are final.
            if ($message -cnotmatch '^HTTP (unknown|429|5\d\d) ') { throw }
            if ($attempt -ge $attempts) {
                if ($attempts -gt 1) { throw "$message (after $attempts attempts)" }
                throw
            }
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
}

function Invoke-GhAppGhOnce {
    param([hashtable]$Context, [string[]]$Arguments, [string]$ApiPath,
        [AllowNull()][string]$InputText, [switch]$AsApp, [int[]]$AllowedExitCodes = @(0), [switch]$AllowNotFound,
        [int]$TokenAttempts = 1, [string]$PreparedToken)
    $process = $null
    $token = $null
    $info = $null
    $failure = "HTTP unknown $ApiPath"
    try {
        $executable = (Get-Command gh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        # gh receives the App token, so a copy that repository work could change is refused.
        if (Test-GhAppRepositoryPath $executable) { throw 'gh inside a repository.' }
        $info = New-GhAppProcessInfo $executable $Arguments
        $info.Environment['GH_HOST'] = 'github.com'
        $info.Environment['GH_REPO'] = "$($Context.owner)/$($Context.repo)"
        $info.Environment['GH_PROMPT_DISABLED'] = '1'
        if ($AsApp) {
            if ($PreparedToken) { $token = $PreparedToken }
            else {
                try { $token = Get-GhAppTokenWithRetry $Context $ApiPath $TokenAttempts }
                catch { $failure = $_.Exception.Message; throw }
            }
            $info.Environment['GH_TOKEN'] = $token
        }
        $process = [Diagnostics.Process]::Start($info)
        # Remove the parent's reference to the child's environment immediately after launch.
        [void]$info.Environment.Remove('GH_TOKEN')
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if ($null -ne $InputText) { $process.StandardInput.Write($InputText) }
        $process.StandardInput.Close()
        $process.WaitForExit()
        $output = $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($token) {
            $output = $output.Replace($token, '[redacted]')
            $errorText = $errorText.Replace($token, '[redacted]')
        }
        $status = 'unknown'
        if ($errorText -match '\bHTTP\s+(\d{3})\b') { $status = $Matches[1] }
        if ($process.ExitCode -notin $AllowedExitCodes) {
            if ($AllowNotFound -and $status -eq '404') { return $null }
            $failure = "HTTP $status $ApiPath"
            throw 'Child failed.'
        }
        [pscustomobject]@{ Output = $output; ExitCode = $process.ExitCode; ErrorText = $errorText }
    } catch { throw $failure }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        if ($null -ne $info) { [void]$info.Environment.Remove('GH_TOKEN') }
        $token = $null
    }
}

function Invoke-GhAppApi {
    param([hashtable]$Context, [string]$Method, [string]$Suffix, [hashtable]$Body,
        [switch]$AsApp, [switch]$AllowNotFound, [string]$PreparedToken)
    $decoded = [Uri]::UnescapeDataString($Suffix.Split('?')[0])
    if ($decoded -match '(^|/)\.\.?(/|$)|\\|//|[\x00-\x20\x7f]' -or $decoded.Contains('%') -or $decoded.StartsWith('/')) {
        throw "Repository mismatch. Expected $($Context.owner)/$($Context.repo)."
    }
    $path = "/repos/$($Context.owner)/$($Context.repo)/$Suffix"
    $arguments = @('api', $path, '--hostname', 'github.com', '--method', $Method)
    $inputText = $null
    if ($null -ne $Body) {
        $arguments += @('--input', '-')
        $inputText = ConvertTo-Json -InputObject $Body -Depth 30 -Compress
    }
    $result = Invoke-GhAppGh -Context $Context -Arguments $arguments -ApiPath $path -InputText $inputText -Read:($Method -ceq 'GET') `
        -AsApp:$AsApp -AllowNotFound:$AllowNotFound -PreparedToken $PreparedToken
    if ($null -eq $result -or [string]::IsNullOrWhiteSpace($result.Output)) { return $null }
    try { ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop }
    catch { throw "HTTP unknown $path" }
}

function Get-GhAppItems {
    param([hashtable]$Context, [string]$Suffix)
    $separator = $(if ($Suffix.Contains('?')) { '&' } else { '?' })
    for ($page = 1; $page -le 1000; $page++) {
        $items = @(Invoke-GhAppApi $Context GET "${Suffix}${separator}per_page=100&page=$page")
        $items
        if ($items.Count -lt 100) { return }
    }
    throw 'Repository pagination exceeded 1000 pages.'
}

function Assert-GhAppSha {
    param([string]$Name, $Value)
    if (@($Value).Count -ne 1 -or [string]$Value -cnotmatch '^[0-9a-f]{40}\z') { throw "$Name is not a 40-character SHA." }
    [string]$Value
}

function Assert-GhAppBranch {
    param([string]$Branch)
    if ([string]::IsNullOrWhiteSpace($Branch) -or $Branch -match '[\x00-\x20\x7f~^:?*\[\\]' -or
        $Branch -match '\.\.|@\{|//|^[-/.]|[/.]$|(^|/)\.|\.lock($|/)' -or $Branch -eq '@') {
        throw 'Branch must be a valid local repository branch name.'
    }
}

function Read-GhAppBody {
    param([string]$Path)
    $Path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    if ($null -ne $script:Configuration -and [string]::Equals([IO.Path]::GetFullPath($Path),
        [IO.Path]::GetFullPath($script:Configuration.dpapiPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The body or message file must not name the configured DPAPI file.'
    }
    if (-not [IO.File]::Exists($Path)) { throw 'The body or message file is missing.' }
    [IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
}

function Get-GhAppPullRequest {
    param([hashtable]$Context, [int]$Number)
    $path = "/repos/$($Context.owner)/$($Context.repo)/pulls/$Number"
    $arguments = @('pr', 'view', [string]$Number, '--repo', "$($Context.owner)/$($Context.repo)",
        '--json', 'state,headRefOid,headRefName,baseRefName,baseRefOid,mergeStateStatus,isCrossRepository,mergeCommit,isDraft,author')
    $result = Invoke-GhAppGh -Context $Context -Arguments $arguments -ApiPath $path -Read
    try { $pr = ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop }
    catch { throw "HTTP unknown $path" }
    if ($pr.isCrossRepository) { throw "Repository mismatch. Expected $($Context.owner)/$($Context.repo)." }
    Assert-GhAppBranch $pr.headRefName
    $null = Assert-GhAppSha "head of #$Number" $pr.headRefOid
    $pr
}

function Test-GhAppBotAuthor {
    param([hashtable]$Context, $Author)
    # REST reports the App as "<name>[bot]". gh pr view reports it as "app/<name>" with is_bot set.
    if ($null -eq $Author) { return $false }
    if ([string]$Author.login -ceq $Context.botLogin) { return $true }
    $appLogin = 'app/' + ($Context.botLogin -creplace '\[bot\]\z', '')
    return ($Author.is_bot -eq $true -and [string]$Author.login -ceq $appLogin)
}

function Get-GhAppLedgerPath {
    param([hashtable]$Context, [string]$LedgerPath)
    if (-not $LedgerPath) {
        $LedgerPath = Join-Path (Split-Path (Get-GhAppConfigPath $Context.owner $Context.repo)) 'review-ledger.jsonl'
    }
    $LedgerPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LedgerPath)
    if (Test-GhAppRepositoryPath $LedgerPath) { throw 'The review ledger must be outside every repository.' }
    foreach ($protected in @((Get-GhAppConfigPath $Context.owner $Context.repo), (Get-GhAppOwnerConfigPath $Context.owner), $Context.tokenHelperPath, $Context.dpapiPath)) {
        if ([string]::Equals($LedgerPath,[IO.Path]::GetFullPath($protected), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The review ledger must not name a config, helper, or DPAPI file.'
        }
    }
    $LedgerPath
}

# The default review record: the first line names the head and the verdict. A repository with another format passes its own pattern.
$script:DefaultReviewRecordPattern = '^Review record: {HEAD} accept\b'

# Tier B scope: the repositories where a pull request can merge without an owner approval, and the bases it may target.
# Core is listed for main only (owner 2026-11-05 22:19, tiered approval for Core; narrowed by the owner on 2026-11-06 08:24: "anything that is not UI
# or a new feature does not need my approval"). The tier check decides what counts as B: for Core that is bug fixes and refactors in src, tests,
# documents and debt files that only decrease. A is UI, new features, the approval mechanism, and a looser guard, ratchet or baseline.
# A repository that is not listed is refused.
# ProtectedPathPattern is a floor that the tier check cannot lower: a pull request that changes (or renames away from) a matching path is refused here
# whatever the tier check said, because the tier check is outside this repository. It mirrors the A paths of the tier check for Core.
# The groups: UI files (axaml, views, themes, icons, images and fonts); the public API lists (a new feature adds to them); the approval mechanism and
# the guard code (gh-app, repo-checks, the shared CI actions, workflows, agent configuration, the pack script); eng except the named debt ledgers;
# project and build files; repository configuration, version and release files; the test support library (it is packed) and the approval-check
# self-test; tests whose path names a guard, a baseline, a ratchet, a contract, a public API or a public surface (this holds the Theme contract
# tests and their baseline XML, and the tests that pin the exported API); Architecture folders; governance and ADR documents at any depth.
# Ordinary src code, other tools, tests, documents (also a docs/**/Architecture folder) and CHANGELOG.md are not in the floor. The tier check judges a release by its title.
$script:CoreProtectedPathGroups = [ordered]@{
    Ui       = '\.(axaml|xaml)(\.cs)?$|^(src|examples)/.*\.(png|jpe?g|gif|svg|ico|icns|webp|bmp|ttf|otf|woff2?)$|^(src|examples)/(.+/)?(Views?|Themes?|Icons?)/|^src/Nvt\.Core\.Fonts/'
    PublicApi = '(^|/)PublicAPI[^/]*\.txt$'
    Mechanism = '^(tools/(gh-app|repo-checks)|actions|\.github|\.agents|\.claude|\.codex|scripts)/'
    Eng      = '^eng/(?!code-health/(baseline|test-debt)\.json$)'
    Build    = '\.(sln|slnx|csproj|props|targets|ruleset|globalconfig|rsp|slnf)$'
    Config   = '(^|/)(Directory\.[A-Za-z.]+|global\.json|nuget\.config|packages\.lock\.json|\.editorconfig|\.gitattributes|\.gitignore|\.gitmodules|CODEOWNERS|VERSION|AGENTS\.md|CLAUDE\.md|CONTRIBUTING\.md|SECURITY\.md|LICENSE[^/]*)$'
    TestInfra = '^tests/approval-check/|^tests/Nvt\.Core\.TestSupport/'
    Contracts = '^tests/.*(guard|baseline|ratchet|contract|publicapi|publicsurface)'
    Architecture = '^(?!docs/)(.+/)?Architecture/'
    Documents = '^docs/governance/|^docs/(.+/)?adr/'
}
$script:CoreProtectedPaths = '(?i)' + (($script:CoreProtectedPathGroups.Values | ForEach-Object { "(?:$_)" }) -join '|')
# The debt ledgers: a change may only remove lines from them.
$script:CoreDebtLedgers = @('eng/code-health/baseline.json', 'eng/code-health/test-debt.json')
$script:TierBScopes = @(
    @{ Repository = 'Dennis40816/nvt_fw_core'; BasePattern = '^main$'; ProtectedPathPattern = $script:CoreProtectedPaths; DebtLedgers = $script:CoreDebtLedgers },
    @{ Repository = 'Dennis40816/nvt-freeform-helper'; BasePattern = '^.+$' },
    @{ Repository = 'Dennis40816/nvt-event-buffer-replay'; BasePattern = '^.+$' },
    @{ Repository = 'Dennis40816/nvt_fw_combiner'; BasePattern = '^\d+\.\d+\.x$' }
)

function New-GhAppCheckRow {
    param([string]$Name, [bool]$Pass, [string]$Detail)
    [pscustomobject]@{ Name = $Name; Result = $(if ($Pass) { 'PASS' } else { 'FAIL' }); Detail = $Detail }
}

function Assert-GhAppReviewRecordPattern {
    param([string]$Pattern)
    if ([string]::IsNullOrEmpty($Pattern)) { return }
    try { $null = [regex]::new($Pattern.Replace('{HEAD}', 'a' * 40)) }
    catch { throw 'ReviewRecordPattern is not a valid regular expression.' }
}

function Get-GhAppLatestRuns {
    param([hashtable]$Context, [string]$Head)
    # The runs of a head can fill several pages, and the latest run of a workflow can sit on a later page.
    # The loop reads until it holds total_count runs, or stops at 1000 runs. A result that is not complete says so.
    $runs = @()
    $total = 0
    for ($page = 1; $page -le 10; $page++) {
        $response = Invoke-GhAppApi $Context GET "actions/runs?head_sha=$Head&per_page=100&page=$page"
        $batch = @()
        if ($null -ne $response -and $null -ne $response.PSObject.Properties['workflow_runs']) {
            $batch = @($response.workflow_runs)
            $total = $(if ($null -ne $response.PSObject.Properties['total_count']) { [int]$response.total_count } else { $batch.Count })
        }
        $runs += $batch
        if ($batch.Count -lt 100 -or $runs.Count -ge $total) { break }
    }
    # The key is the workflow ID, because two workflows can share a name. The name is the key only when the ID is missing.
    # StrictMode makes a missing property an error, so the property is looked up by name.
    $groups = @($runs | Group-Object {
        $workflowId = $_.PSObject.Properties['workflow_id']
        if ($null -ne $workflowId -and $null -ne $workflowId.Value) { [string]$workflowId.Value } else { [string]$_.name }
    })
    # @(foreach ...) is empty for no group, where @($null) would hold one null item.
    $latest = @(foreach ($group in $groups) {
        $ordered = @($group.Group | Sort-Object -Property @{ Expression = { [DateTimeOffset]$_.created_at }; Descending = $true },
            @{ Expression = { [long]$_.id }; Descending = $true })
        [pscustomobject]@{ Run = $ordered[0]; Older = @($ordered | Select-Object -Skip 1) }
    })
    [pscustomobject]@{ Latest = $latest; Read = $runs.Count; Total = $total; Complete = ($runs.Count -ge $total) }
}

# A repository has no CI only when it has no workflow at all, and the head has no commit status and no check run.
# A head with no check yet is not enough: a repository with a workflow may not have started the check. Each call must read a total_count of 0.
# Any other answer, including a missing count, means CI may exist. A call that fails stops the caller, never counts as "no CI".
function Test-GhAppNoCiRepository {
    param([hashtable]$Context, [string]$Head)
    $null = Assert-GhAppSha 'head for the CI proof' $Head
    foreach ($suffix in @('actions/workflows?per_page=1', "commits/$Head/status?per_page=1", "commits/$Head/check-runs?per_page=1")) {
        $response = Invoke-GhAppApi $Context GET $suffix
        $count = $null
        if ($null -ne $response -and $null -ne $response.PSObject.Properties['total_count']) { $count = $response.total_count }
        if ($count -isnot [int] -and $count -isnot [long]) { return $false }
        if ($count -ne 0) { return $false }
    }
    return $true
}

function Get-GhAppReadiness {
    param([hashtable]$Context, [int]$Number, $Pr, [string]$ExpectedHeadSha, [string]$ReviewRecordPattern)
    $head = Assert-GhAppSha "head of #$Number" $Pr.headRefOid
    $custom = -not [string]::IsNullOrEmpty($ReviewRecordPattern)
    if (-not $custom) { $ReviewRecordPattern = $script:DefaultReviewRecordPattern }
    $pattern = $ReviewRecordPattern.Replace('{HEAD}', [regex]::Escape($head))
    $checks = [Collections.Generic.List[object]]::new()
    $checks.Add((New-GhAppCheckRow 'State' ($Pr.state -ceq 'OPEN') "state is $($Pr.state)"))
    $checks.Add((New-GhAppCheckRow 'Draft' (-not $Pr.isDraft) $(if ($Pr.isDraft) { 'the PR is still a draft' } else { 'not a draft' })))
    $author = $(if ($null -ne $Pr.author) { [string]$Pr.author.login } else { 'none' })
    $checks.Add((New-GhAppCheckRow 'Author' (Test-GhAppBotAuthor $Context $Pr.author) "author is $author; botLogin is $($Context.botLogin)"))
    if ($ExpectedHeadSha) {
        $checks.Add((New-GhAppCheckRow 'Head' ($head -ceq $ExpectedHeadSha) $(if ($head -ceq $ExpectedHeadSha) { "head is $head" } else { "head is $head; expected $ExpectedHeadSha" })))
    } else {
        $checks.Add((New-GhAppCheckRow 'Head' $true "head is $head; no expected head given"))
    }
    # BLOCKED is the normal state before the owner approves. BEHIND would change the head at merge time and void the review record.
    $stateDetail = "merge state is $($Pr.mergeStateStatus)"
    if ($Pr.mergeStateStatus -ceq 'BEHIND') { $stateDetail += "; update the branch first, or the head changes at merge time" }
    elseif ($Pr.mergeStateStatus -ceq 'DIRTY') { $stateDetail += '; the branch conflicts with its base' }
    elseif ($Pr.mergeStateStatus -ceq 'UNKNOWN') { $stateDetail += '; GitHub has not computed it yet, try again' }
    $checks.Add((New-GhAppCheckRow 'MergeState' ($Pr.mergeStateStatus -cin @('CLEAN', 'BLOCKED', 'UNSTABLE', 'HAS_HOOKS')) $stateDetail))

    $reviews = @(Get-GhAppItems $Context "pulls/$Number/reviews")
    # Only the first non-empty line of a review counts (the NFU record rule), so a rejection that quotes an acceptance cannot pass.
    # A review counts as an acceptance in state COMMENTED or APPROVED only. A later verdict on the same head replaces an earlier one:
    # a rejection and a dismissed record both cancel the acceptance before them.
    # What a rejection looks like is known for the default format only (`Review record: <head> <verdict>`). A custom pattern names
    # the acceptance and nothing else, so with a custom pattern every review on the head that has a body, a dismissed or a
    # CHANGES_REQUESTED state is a verdict, and the latest one decides. A review without a body (the container of inline comments)
    # is not a verdict.
    $accept = [regex]::new($pattern, [Text.RegularExpressions.RegexOptions]::None, [TimeSpan]::FromSeconds(2))
    $verdict = [regex]::new('^Review record: ' + [regex]::Escape($head) + ' \S+', [Text.RegularExpressions.RegexOptions]::None, [TimeSpan]::FromSeconds(2))
    $verdicts = @(foreach ($review in @($reviews | Where-Object { $_.state -cne 'PENDING' -and [string]$_.commit_id -ceq $head })) {
        $lines = @([string]$review.body -split "\r?\n" | Where-Object { $_.Trim().Length -gt 0 })
        $firstLine = $(if ($lines.Count -gt 0) { [string]$lines[0] } else { '' })
        $isAccept = ($review.state -cin @('COMMENTED', 'APPROVED')) -and $accept.IsMatch($firstLine)
        $isOther = $verdict.IsMatch($firstLine) -or ($custom -and ($firstLine.Length -gt 0 -or $review.state -ceq 'DISMISSED'))
        if ($isAccept -or $review.state -ceq 'CHANGES_REQUESTED' -or $isOther) {
            $stamp = $(if ($null -ne $review.PSObject.Properties['submitted_at'] -and $review.submitted_at) { [DateTimeOffset]$review.submitted_at } else { [DateTimeOffset]::MinValue })
            [pscustomobject]@{ Review = $review; Accept = $isAccept; Time = $stamp; Id = [long]$review.id }
        }
    })
    $latestVerdict = $(if ($verdicts.Count -gt 0) { @($verdicts | Sort-Object -Property Time, Id)[-1] } else { $null })
    if ($null -ne $latestVerdict -and $latestVerdict.Accept) {
        $checks.Add((New-GhAppCheckRow 'ReviewRecord' $true "the latest verdict on this head accepts it; review $($latestVerdict.Review.id) by $($latestVerdict.Review.user.login)"))
    } elseif ($null -ne $latestVerdict) {
        $checks.Add((New-GhAppCheckRow 'ReviewRecord' $false "the latest verdict on this head is review $($latestVerdict.Review.id), which does not accept it (state $($latestVerdict.Review.state))"))
    } else {
        $checks.Add((New-GhAppCheckRow 'ReviewRecord' $false "no review on this head matches the pattern $pattern"))
    }

    # The latest run of each workflow decides. An older failed run stays in the PR rollup for good, because the App cannot re-run it.
    $runs = Get-GhAppLatestRuns $Context $head
    if (-not $runs.Complete) {
        # A workflow whose latest run was not read could hide a failure, so an incomplete list never passes.
        $checks.Add((New-GhAppCheckRow 'Runs' $false "only $($runs.Read) of $($runs.Total) runs of this head were read"))
    }
    elseif ($runs.Latest.Count -eq 0) {
        # No run is a failure when CI may exist. A repository with no workflow, status or check run has no CI to wait for.
        if (Test-GhAppNoCiRepository $Context $head) {
            $checks.Add((New-GhAppCheckRow 'Runs' $true 'no CI in repository: no workflow, no commit status and no check run on this head'))
        } else {
            $checks.Add((New-GhAppCheckRow 'Runs' $false 'no workflow run found for this head'))
        }
    }
    foreach ($item in @($runs.Latest | Sort-Object -Property { [string]$_.Run.name })) {
        $run = $item.Run
        $pass = ($run.status -ceq 'completed' -and $run.conclusion -ceq 'success')
        $detail = "$($run.event) run $($run.id) is $($run.status)"
        if ($run.status -ceq 'completed') { $detail += "/$($run.conclusion)" }
        $ignored = @($item.Older | Where-Object { $_.conclusion -cne 'success' }).Count
        if ($ignored -gt 0) { $detail += "; $ignored older run(s) of this workflow ignored" }
        $checks.Add((New-GhAppCheckRow "Run: $($run.name)" $pass "$detail $($run.html_url)"))
    }

    $files = @(Get-GhAppItems $Context "pulls/$Number/files" | ForEach-Object {
        [pscustomobject]@{ Path = [string]$_.filename; Added = [int]$_.additions; Deleted = [int]$_.deletions
            Previous = $(if ($null -ne $_.PSObject.Properties['previous_filename']) { [string]$_.previous_filename } else { '' })
            Status = $(if ($null -ne $_.PSObject.Properties['status']) { [string]$_.status } else { '' }) } })
    [pscustomobject]@{ Number = $Number; Head = $head; Ready = (@($checks | Where-Object { $_.Result -ceq 'FAIL' }).Count -eq 0);
        Checks = $checks.ToArray(); Files = $files }
}

function Get-GhAppPullRequestReadiness {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [string]$ExpectedHeadSha, [string]$ReviewRecordPattern, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    if ($PSBoundParameters.ContainsKey('ExpectedHeadSha')) { $null = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha }
    Assert-GhAppReviewRecordPattern $ReviewRecordPattern
    $pr = Get-GhAppPullRequest $context $Number
    Get-GhAppReadiness $context $Number $pr $ExpectedHeadSha $ReviewRecordPattern
}

# The content of a pull request as git patch-id: the diff from the merge base of the head and the base, to the head.
# A rebase or a base change that leaves the content alone keeps the value. Options are fixed so that local Git
# configuration cannot change the diff text.
function Get-GhAppPatchId {
    param([string]$Worktree, [string]$Head, [string]$BaseSha)
    $null = Assert-GhAppSha 'patch-id head' $Head
    $null = Assert-GhAppSha 'patch-id base' $BaseSha
    $mergeBase = Assert-GhAppSha 'merge base' (Invoke-GhAppGit $Worktree @('merge-base', $BaseSha, $Head)).Trim()
    $diff = Invoke-GhAppGit -Worktree $Worktree -Bytes -Arguments @('diff', '--binary', '--no-color', '--no-ext-diff',
        '--no-textconv', '--no-renames', '--diff-algorithm=myers', '--unified=3', '--inter-hunk-context=0',
        '--src-prefix=a/', '--dst-prefix=b/', $mergeBase, $Head, '--')
    if ($diff.Length -eq 0) { throw 'The pull request has no diff to identify.' }
    $line = (Invoke-GhAppGit -Worktree $Worktree -InputBytes $diff -Arguments @('patch-id', '--stable')).Trim()
    Assert-GhAppSha 'patch-id' $line.Split(' ')[0]
}

function Add-GhAppLedgerEntry {
    param([string]$LedgerPath, $Entry)
    try {
        $line = ConvertTo-Json -InputObject $Entry -Compress
        [IO.File]::AppendAllText($LedgerPath, $line + "`n", [Text.UTF8Encoding]::new($false))
    } catch { throw 'The review ledger could not be appended.' }
}

function Request-GhAppOwnerReview {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [string]$LedgerPath, [Alias('Repo')][string]$Repository,
        [string]$ExpectedHeadSha, [switch]$CheckReadiness, [string]$ReviewRecordPattern, [string]$Worktree)
    if ($PSBoundParameters.ContainsKey('ReviewRecordPattern') -and -not $CheckReadiness) { throw 'ReviewRecordPattern requires CheckReadiness.' }
    $context = Get-GhAppContext $Repository
    if ($PSBoundParameters.ContainsKey('ExpectedHeadSha')) { $null = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha }
    Assert-GhAppReviewRecordPattern $ReviewRecordPattern
    $LedgerPath = Get-GhAppLedgerPath $context $LedgerPath
    $pr = Get-GhAppPullRequest $context $Number
    if ($pr.state -cne 'OPEN' -or $pr.isDraft) { throw "#$Number must be open and not a draft before requesting owner review." }
    if (-not (Test-GhAppBotAuthor $context $pr.author)) { throw "#$Number PR author does not match botLogin." }
    Assert-GhAppBranch $pr.baseRefName
    $head = Assert-GhAppSha "head of #$Number" $pr.headRefOid
    if ($ExpectedHeadSha -and $head -cne $ExpectedHeadSha) { throw "#$Number head changed. Expected $ExpectedHeadSha; found $head." }
    if ($CheckReadiness) {
        # The checks cover the head that the ledger records, so a head that moves later fails the merge proof instead.
        $readiness = Get-GhAppReadiness $context $Number $pr $ExpectedHeadSha $ReviewRecordPattern
        if (-not $readiness.Ready) {
            $failed = @($readiness.Checks | Where-Object { $_.Result -ceq 'FAIL' } | ForEach-Object { "$($_.Name): $($_.Detail)" })
            throw "#$Number is not ready for owner review. $($failed -join '; ')"
        }
    }
    $entry = [pscustomobject][ordered]@{ owner = $context.owner; repo = $context.repo; number = $Number;
        head = $head;
        requestedAt = (Get-GhAppUtcNow).ToUniversalTime().ToString('o'); base = $pr.baseRefName }
    if ($PSBoundParameters.ContainsKey('Worktree')) {
        # The patch-id binds the request to the content, so a later base change can be proven harmless.
        $baseSha = Assert-GhAppSha "base of #$Number" $pr.baseRefOid
        Sync-GhAppReviewCommits $context $Worktree @($head, $baseSha)
        $entry | Add-Member -NotePropertyName patchId -NotePropertyValue (Get-GhAppPatchId $Worktree $head $baseSha)
    }
    Add-GhAppLedgerEntry $LedgerPath $entry
    $entry
}

# Only a missing file means "no ledger". File.Exists also returns false when the file cannot be reached, for example
# when the path is a directory, so opening the file is the test. Any other failure must stop the caller.
function Open-GhAppLedgerReader {
    param([string]$LedgerPath)
    try { return [IO.StreamReader]::new($LedgerPath, [Text.UTF8Encoding]::new($false, $true)) }
    catch {
        $inner = $(if ($null -ne $_.Exception.InnerException) { $_.Exception.InnerException } else { $_.Exception })
        if ($inner -is [IO.FileNotFoundException] -or $inner -is [IO.DirectoryNotFoundException]) { return $null }
        throw
    }
}

function Get-GhAppReviewRequest {
    param([hashtable]$Context, [int]$Number, [string]$LedgerPath)
    $latest = $null
    $reuses = [Collections.Generic.List[object]]::new()
    $reader = $null
    try {
        $reader = Open-GhAppLedgerReader $LedgerPath
        if ($null -ne $reader) {
            while ($null -ne ($line = $reader.ReadLine())) {
                # Keep timestamps as JSON strings on every supported PowerShell version.
                $document = [Text.Json.JsonDocument]::Parse($line)
                try {
                    $entry = $document.RootElement
                    if ($entry.GetProperty('owner').GetString() -cne $Context.owner -or
                        $entry.GetProperty('repo').GetString() -cne $Context.repo -or $entry.GetProperty('number').GetInt32() -ne $Number) { continue }
                    $kind = $null
                    foreach ($property in $entry.EnumerateObject()) {
                        if ($property.Name -ceq 'kind') { $kind = $property.Value.GetString() }
                    }
                    if ($kind -ceq 'reuse') {
                        # A reuse record says that a merge spent one owner approval. It only restricts: it never lets a head pass.
                        # It binds to one request by the request head and time, so a record cannot move to a newer request.
                        $recordStamp = $entry.GetProperty('requestedAt').GetString()
                        if ($recordStamp -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$') { throw 'Expected UTC ISO 8601.' }
                        $reuses.Add([pscustomobject]@{ ApprovalId = [string]$entry.GetProperty('approvalId').GetInt64(); Mode = $entry.GetProperty('mode').GetString();
                            ApprovalCommit = Assert-GhAppSha 'reuse approval commit' $entry.GetProperty('approvalCommit').GetString();
                            OldHead = Assert-GhAppSha 'reuse old head' $entry.GetProperty('oldHead').GetString();
                            NewHead = Assert-GhAppSha 'reuse new head' $entry.GetProperty('newHead').GetString();
                            RequestedAt = [DateTimeOffset]::Parse($recordStamp, [Globalization.CultureInfo]::InvariantCulture) })
                        continue
                    }
                    if ($null -ne $kind) { throw 'Unknown ledger entry kind.' }
                    $head = Assert-GhAppSha 'review request head' $entry.GetProperty('head').GetString()
                    $base = $entry.GetProperty('base').GetString()
                    Assert-GhAppBranch $base
                    $stamp = $entry.GetProperty('requestedAt').GetString()
                    if ($stamp -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$') { throw 'Expected UTC ISO 8601.' }
                    $time = [DateTimeOffset]::Parse($stamp, [Globalization.CultureInfo]::InvariantCulture)
                    $patchId = $null
                    foreach ($property in $entry.EnumerateObject()) {
                        if ($property.Name -ceq 'patchId') { $patchId = Assert-GhAppSha 'review request patch-id' $property.Value.GetString() }
                    }
                    $latest = [pscustomobject]@{ Head = $head; RequestedAt = $time; Base = $base; PatchId = $patchId; Reuses = $null }
                } finally { $document.Dispose() }
            }
        }
    } catch { throw 'The review ledger contains an invalid entry or could not be read.' }
    finally { if ($null -ne $reader) { $reader.Dispose() } }
    if ($null -eq $latest) { throw "#$Number stop: no review ledger entry; send the head to the owner and record a review request." }
    $latest.Reuses = @($reuses | Where-Object { $_.OldHead -ceq $latest.Head -and $_.RequestedAt -eq $latest.RequestedAt })
    $latest
}

function Get-GhAppOwnerApproval {
    param([hashtable]$Context, [int]$Number, [DateTimeOffset]$RequestedAt)
    # A COMMENTED review does not change approval on GitHub, so only state-changing reviews count.
    $reviews = @(Get-GhAppItems $Context "pulls/$Number/reviews" | Where-Object {
        $_.user.login -ieq $Context.owner -and $_.state -cin @('APPROVED', 'CHANGES_REQUESTED', 'DISMISSED', 'PENDING') })
    if ($reviews.Count -eq 0) { return $null }
    $ranked = foreach ($review in $reviews) {
        try {
            $id = [long]$review.id
            if ($review.state -ceq 'PENDING') { $time = [DateTimeOffset]::MaxValue }
            elseif ($review.submitted_at) { $time = [DateTimeOffset]$review.submitted_at }
            else { throw 'A submitted review needs a timestamp.' }
        } catch { throw "HTTP unknown /repos/$($Context.owner)/$($Context.repo)/pulls/$Number/reviews" }
        [pscustomobject]@{ Review = $review; Time = $time; Id = $id }
    }
    # A draft can receive its ID before a later review, then be submitted last.
    $latest = $ranked | Sort-Object -Property Time, Id | Select-Object -Last 1
    if ($latest.Review.state -ceq 'APPROVED' -and $latest.Time -gt $RequestedAt) { return $latest.Review }
    return $null
}

function Invoke-GhAppGit {
    param([string]$Worktree, [string[]]$Arguments, [switch]$Bytes, [switch]$OwnerCredentials,
        [int[]]$AllowedExitCodes = @(0), [switch]$Result, [byte[]]$InputBytes)
    $process = $null
    $buffer = [IO.MemoryStream]::new()
    try {
        $Worktree = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Worktree)
        $executable = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $info = New-GhAppProcessInfo $executable (@('--no-replace-objects', '-C', $Worktree) + $Arguments)
        # Fetch uses the owner's normal Git credential setup, never an App token.
        if ($OwnerCredentials) {
            foreach ($item in Get-ChildItem Env:) {
                if ($item.Name -match '^(GCM_|GIT_CONFIG_)' -or $item.Name -in @('GIT_ASKPASS', 'SSH_ASKPASS', 'GIT_TERMINAL_PROMPT')) {
                    $info.Environment[$item.Name] = $item.Value
                }
            }
        }
        foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_COMMON_DIR', 'GIT_INDEX_FILE', 'GIT_OBJECT_DIRECTORY', 'GIT_ALTERNATE_OBJECT_DIRECTORIES')) {
            [void]$info.Environment.Remove($name)
        }
        $process = [Diagnostics.Process]::Start($info)
        # Output is read before the input is written, so a large input cannot block on a full output pipe.
        $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($buffer)
        $errorTask = $process.StandardError.ReadToEndAsync()
        if ($null -ne $InputBytes -and $InputBytes.Length -gt 0) {
            $process.StandardInput.BaseStream.Write($InputBytes, 0, $InputBytes.Length)
            $process.StandardInput.BaseStream.Flush()
        }
        $process.StandardInput.Close()
        $process.WaitForExit()
        $null = $outputTask.GetAwaiter().GetResult()
        $null = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -notin $AllowedExitCodes) { throw 'Local git command failed.' }
        if ($Result) { return [pscustomobject]@{ Output = [Text.Encoding]::UTF8.GetString($buffer.ToArray()); ExitCode = $process.ExitCode } }
        if ($Bytes) { return ,$buffer.ToArray() }
        return [Text.Encoding]::UTF8.GetString($buffer.ToArray())
    } catch { throw 'Local git command failed.' }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        $buffer.Dispose()
    }
}

function Get-GhAppTreeChanges {
    param([string]$Worktree, [string]$LocalBase, [string]$Head = 'HEAD')
    $raw = Invoke-GhAppGit $Worktree @('diff', '--raw', '-z', '--no-renames', '--no-abbrev', $LocalBase, $Head, '--')
    if (-not $raw) { return }
    $parts = $raw.Split([char]0)
    for ($index = 0; $index -lt $parts.Length - 1; $index += 2) {
        $metadata = $parts[$index].Split(' ')
        $path = $parts[$index + 1]
        $deleted = $metadata[4] -ceq 'D'
        $mode = $(if ($deleted) { $metadata[0].Substring(1) } else { $metadata[1] })
        $type = $(if ($mode -eq '160000') { 'commit' } else { 'blob' })
        $sha = $(if ($deleted) { $null } else { Assert-GhAppSha 'local blob or gitlink' $metadata[3] })
        $content = $null
        if (-not $deleted -and $type -eq 'blob') {
            $bytes = Invoke-GhAppGit -Worktree $Worktree -Arguments @('cat-file', 'blob', $sha) -Bytes
            $content = [Convert]::ToBase64String($bytes)
        }
        [pscustomobject]@{ Path = $path; Mode = $mode; Type = $type; Sha = $sha; Content = $content }
    }
}

function Write-GhAppCommit {
    param([hashtable]$Context, [string]$Worktree, [string]$LocalBase, [string[]]$Parents, [string]$Message, [string]$Candidate = 'HEAD')
    $expectedTree = Assert-GhAppSha 'local tree' (Invoke-GhAppGit $Worktree @('rev-parse', '--verify', "$Candidate^{tree}")).Trim()
    $localBaseTree = Assert-GhAppSha 'local base tree' (Invoke-GhAppGit $Worktree @('rev-parse', '--verify', "$LocalBase^{tree}")).Trim()
    $changes = @(Get-GhAppTreeChanges $Worktree $LocalBase $Candidate)
    $parentCommit = Invoke-GhAppApi $Context GET "git/commits/$($Parents[0])"
    $baseTree = Assert-GhAppSha 'remote base tree' $parentCommit.tree.sha
    if ($baseTree -cne $localBaseTree) { throw "base tree mismatch: remote $baseTree local $localBaseTree" }
    $entries = @(foreach ($change in $changes) {
        if ($null -ne $change.Content) {
            $blob = Invoke-GhAppApi $Context POST 'git/blobs' @{ content = $change.Content; encoding = 'base64' } -AsApp
            $blobSha = Assert-GhAppSha 'new blob' $blob.sha
            if ($blobSha -cne $change.Sha) { throw "blob mismatch $($change.Path)" }
        }
        @{ path = $change.Path; mode = $change.Mode; type = $change.Type; sha = $change.Sha }
    })
    $tree = Invoke-GhAppApi $Context POST 'git/trees' @{ base_tree = $baseTree; tree = @($entries) } -AsApp
    $treeSha = Assert-GhAppSha 'new tree' $tree.sha
    if ($treeSha -cne $expectedTree) { throw "tree mismatch: remote $treeSha local $expectedTree" }
    $commit = Invoke-GhAppApi $Context POST 'git/commits' @{ tree = $treeSha; message = $Message; parents = $Parents } -AsApp
    $commitSha = Assert-GhAppSha 'new commit' $commit.sha
    [pscustomobject]@{ Files = $changes.Count; Tree = $treeSha; Commit = $commitSha }
}

# An owner review closes the branch to pushes. Two pushes keep the content that the owner saw, so they stay allowed:
#   base-merge  the pushed commit is the old head plus one clean merge of the base branch. This is the rule of
#               Assert-GhAppReviewHistory: the old head is the review request head (or a clean chain from it), the second
#               parent is contained in the base, and the pushed tree is the recomputed merge tree.
#   rebase      the pushed commit has the patch-id that the review request recorded, and it gives the same tree as the
#               request head once both are merged into the current base. The patch-id is only a screen, because it ignores
#               white space and line numbers. A request recorded without a patch-id cannot prove this.
# The pushed commit does not exist yet, so a probe commit with the same tree and parents stands in for it. The probe is
# a local object only. $Candidate is the one local commit that the caller verifies and publishes. The function returns
# the kind, or $null for every other push and for any failure to prove one.
function Get-GhAppReviewedPushKind {
    param([hashtable]$Context, $Pr, [string]$Worktree, [string]$LedgerPath, [string[]]$Parents, [string]$Candidate)
    try {
        $number = [int]$Pr.number
        $request = Get-GhAppReviewRequest $Context $number $LedgerPath
        $baseRef = [string]$Pr.base.ref
        $baseSha = Assert-GhAppSha "base of #$number" $Pr.base.sha
        if ($baseRef -cne $request.Base) { return $null }
        Sync-GhAppReviewCommits $Context $Worktree (@($Parents) + $baseSha + $request.Head)
        $tree = Assert-GhAppSha 'local tree' (Invoke-GhAppGit $Worktree @('rev-parse', '--verify', "$Candidate^{tree}")).Trim()
        if ($Parents.Count -eq 2) {
            try {
                $old = [pscustomobject]@{ headRefOid = $Parents[0]; baseRefName = $baseRef; baseRefOid = $baseSha }
                $null = Assert-GhAppReviewHistory $Context $number $Worktree $old $request
                $contained = Invoke-GhAppGit -Worktree $Worktree -Arguments @('merge-base', '--is-ancestor', $Parents[1], $baseSha) -AllowedExitCodes @(0, 1) -Result
                if ($contained.ExitCode -eq 0) {
                    $merge = Invoke-GhAppGit -Worktree $Worktree -Arguments @('merge-tree', '--write-tree', $Parents[0], $Parents[1]) -AllowedExitCodes @(0, 1) -Result
                    if ($merge.ExitCode -eq 0 -and (Assert-GhAppSha 'recomputed merge tree' ($merge.Output.Split("`n")[0].Trim())) -ceq $tree) { return 'base-merge' }
                }
            } catch { }
        }
        if ($request.PatchId) {
            $probeArguments = @('-c', 'user.name=gh-app', '-c', 'user.email=gh-app@example.invalid', '-c', 'commit.gpgSign=false', 'commit-tree', $tree)
            foreach ($parent in $Parents) { $probeArguments += @('-p', $parent) }
            $probeArguments += @('-m', 'probe')
            $probe = Assert-GhAppSha 'probe commit' (Invoke-GhAppGit $Worktree $probeArguments).Trim()
            if ((Get-GhAppPatchId $Worktree $probe $baseSha) -ceq $request.PatchId -and
                (Get-GhAppIntegratedTree $Worktree $baseSha $probe) -ceq (Get-GhAppIntegratedTree $Worktree $baseSha $request.Head)) { return 'rebase' }
        }
        return $null
    } catch { return $null }
}

function Push-GhAppBranch {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Worktree, [Parameter(Mandatory)][string]$LocalBase,
        [Parameter(Mandatory)][string]$RemoteParent, [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$MessageFile, [string]$ExtraParent, [switch]$AllowOpenPr,
        [switch]$IncludeWorkflowsWrite, [string]$WorkflowsLogPath, [string]$LedgerPath, [string]$PushLogPath,
        [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $parents = @(Assert-GhAppSha 'RemoteParent' $RemoteParent)
    $LocalBase = Assert-GhAppSha 'LocalBase' $LocalBase
    if ($ExtraParent) { $parents += Assert-GhAppSha 'ExtraParent' $ExtraParent }
    Assert-GhAppBranch $Branch
    $message = Read-GhAppBody $MessageFile
    # HEAD is read once. The guard, the diff and the published commit all use this commit, even if HEAD moves meanwhile.
    $candidate = Assert-GhAppSha 'HEAD' (Invoke-GhAppGit $Worktree @('rev-parse', '--verify', 'HEAD^{commit}')).Trim()
    # The diff from LocalBase to the candidate becomes the remote commit. A LocalBase that the candidate does not contain would turn
    # every newer base file into a deletion, so require the candidate to be built on it.
    $ancestor = Invoke-GhAppGit -Worktree $Worktree -Arguments @('merge-base', '--is-ancestor', $LocalBase, $candidate) -AllowedExitCodes @(0, 1) -Result
    if ($ancestor.ExitCode -ne 0) { throw "LocalBase $LocalBase is not an ancestor of HEAD; rebase onto the remote base first." }
    if ($IncludeWorkflowsWrite) {
        # The switch is off by default. It is allowed only for a push whose tree changes a workflow file.
        if ([string]::IsNullOrWhiteSpace($WorkflowsLogPath)) { throw 'IncludeWorkflowsWrite requires WorkflowsLogPath.' }
        $changes = @(Get-GhAppTreeChanges $Worktree $LocalBase $candidate)
        $workflowFiles = @($changes | Where-Object { $_.Path.StartsWith('.github/workflows/', [StringComparison]::Ordinal) })
        if ($workflowFiles.Count -eq 0) { throw 'IncludeWorkflowsWrite refused: the push changes no file under .github/workflows/.' }
        $logPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkflowsLogPath)
        $line = "$([DateTimeOffset]::UtcNow.ToString('o')) workflows-write push branch=$Branch files=$($changes.Count) workflowFiles=$($workflowFiles.Count)"
        try { [IO.File]::AppendAllText($logPath, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
        catch { throw 'The workflows write log could not be written.' }
        # A copy carries the flag, so the stored configuration and every other call stay unchanged.
        $context = $context.Clone()
        $context.includeWorkflowsWrite = $true
    }
    $headFilter = [Uri]::EscapeDataString("$($context.owner):$Branch")
    $open = @(Get-GhAppItems $context "pulls?state=open&head=$headFilter")
    $reviewedKind = $null
    $reviewedLog = $null
    foreach ($pr in $open) {
        $reviews = @(Get-GhAppItems $context "pulls/$($pr.number)/reviews" | Where-Object { $_.user.login -ieq $context.owner })
        if ($reviews.Count -gt 0) {
            # Only a push that keeps the reviewed content passes: a clean base merge or a rebase with the recorded patch-id.
            $kind = $null
            try {
                $ledger = Get-GhAppLedgerPath $context $LedgerPath
                $kind = Get-GhAppReviewedPushKind $context $pr $Worktree $ledger $parents $candidate
                if ($PushLogPath) { $reviewedLog = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PushLogPath) }
                else { $reviewedLog = Join-Path (Split-Path $ledger) 'push-after-review.log' }
            } catch { $kind = $null }
            if ($null -eq $kind) { throw "#$($pr.number) has an owner review; push refused. Open a follow-up PR instead." }
            $reviewedKind = $kind
        }
        if (-not $AllowOpenPr) { throw "#$($pr.number) is open; push refused. Pass -AllowOpenPr only if it has not been sent to the owner." }
        if ($null -ne $reviewedKind) {
            $line = "$([DateTimeOffset]::UtcNow.ToString('o')) push after owner review kind=$reviewedKind pr=#$($pr.number) branch=$Branch parents=$($parents -join ',')"
            try { [IO.File]::AppendAllText($reviewedLog, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
            catch { throw 'The push log could not be written.' }
        }
    }
    $result = Write-GhAppCommit $context $Worktree $LocalBase $parents $message $candidate
    $encodedBranch = [Uri]::EscapeDataString($Branch)
    $ref = Invoke-GhAppApi $context GET "git/ref/heads/$encodedBranch" -AllowNotFound
    if ($null -ne $ref) {
        $null = Assert-GhAppSha 'remote branch head' $ref.object.sha
        $null = Invoke-GhAppApi $context PATCH "git/refs/heads/$encodedBranch" @{ sha = $result.Commit; force = $false } -AsApp
    } else {
        $null = Invoke-GhAppApi $context POST 'git/refs' @{ ref = "refs/heads/$Branch"; sha = $result.Commit } -AsApp
    }
    [pscustomobject]@{ Files = $result.Files; Tree = $result.Tree; Commit = $result.Commit; Branch = $Branch; OwnerReviewPush = $reviewedKind }
}

function New-GhAppPullRequest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Head, [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$BodyFile, [string]$Base = 'main', [switch]$Draft,
        [Alias('Repo')][string]$Repository, [switch]$CloseOnWrongAuthor, [string]$LedgerPath, [string]$Worktree)
    $context = Get-GhAppContext $Repository
    Assert-GhAppBranch $Head
    Assert-GhAppBranch $Base
    $created = Invoke-GhAppApi $context POST 'pulls' @{ head = $Head; base = $Base; title = $Title;
        body = (Read-GhAppBody $BodyFile); draft = [bool]$Draft } -AsApp
    $pr = Invoke-GhAppApi $context GET "pulls/$($created.number)"
    $url = "https://github.com/$($context.owner)/$($context.repo)/pull/$($created.number)"
    if ($pr.user.login -cne $context.botLogin) {
        if ($CloseOnWrongAuthor) {
            $closed = Invoke-GhAppApi $context PATCH "pulls/$($created.number)" @{ state = 'closed' } -AsApp
            if ($closed.state -cne 'closed') { throw "#$($created.number) close was not confirmed." }
            throw "PR author does not match botLogin. #$($created.number) was closed. $url"
        }
        throw "PR author does not match botLogin. Inspect $url"
    }
    # A draft is not sent to the owner. A pull request that opens ready is registered at once.
    $request = $null
    if (-not $Draft) {
        $arguments = @{ Number = [int]$created.number; Repository = $Repository }
        if ($LedgerPath) { $arguments.LedgerPath = $LedgerPath }
        if ($Worktree) { $arguments.Worktree = $Worktree }
        $request = Request-GhAppOwnerReview @arguments
    }
    [pscustomobject]@{ Number = $created.number; Url = $url; ReviewRequest = $request }
}

# Marks a bot-authored draft ready and registers the owner review request for the head. A draft is never registered.
function Set-GhAppPullRequestReady {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][string]$ExpectedHeadSha, [Parameter(Mandatory)][string]$Worktree,
        [string]$LedgerPath, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $expected = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha
    $LedgerPath = Get-GhAppLedgerPath $context $LedgerPath
    $pr = Get-GhAppPullRequest $context $Number
    if ($pr.state -cne 'OPEN') { throw "#$Number must be open to be marked ready." }
    if (-not (Test-GhAppBotAuthor $context $pr.author)) { throw "#$Number PR author does not match botLogin." }
    if ($pr.headRefOid -cne $expected) { throw "#$Number head changed. Expected $expected; found $($pr.headRefOid)." }
    # A second registration would void an approval that the owner already gave.
    if (-not $pr.isDraft) { throw "#$Number is already ready. Use Request-GhAppOwnerReview to register it." }
    $node = Invoke-GhAppApi $context GET "pulls/$Number"
    $query = 'mutation($id:ID!){markPullRequestReadyForReview(input:{pullRequestId:$id}){pullRequest{number isDraft}}}'
    $body = ConvertTo-Json -InputObject @{ query = $query; variables = @{ id = [string]$node.node_id } } -Compress
    $null = Invoke-GhAppGh -Context $context -Arguments @('api', 'graphql', '--hostname', 'github.com', '--input', '-') `
        -ApiPath '/graphql' -InputText $body -AsApp
    Request-GhAppOwnerReview -Number $Number -Repository $Repository -LedgerPath $LedgerPath -ExpectedHeadSha $expected -Worktree $Worktree
}

# Changes the base of a pull request. A pull request with a review request keeps it when the content is the same:
# the new entry carries the original request time, so an approval that the owner gave stays valid.
function Set-GhAppPullRequestBase {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][string]$NewBase, [Parameter(Mandatory)][string]$ExpectedHeadSha,
        [Parameter(Mandatory)][string]$Worktree, [Parameter(Mandatory)][Alias('Log')][string]$LogPath,
        [string]$LedgerPath, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    Assert-GhAppBranch $NewBase
    $expected = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha
    $LedgerPath = Get-GhAppLedgerPath $context $LedgerPath
    $LogPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogPath)
    $pr = Get-GhAppPullRequest $context $Number
    if ($pr.state -cne 'OPEN') { throw "#$Number must be open to change its base." }
    if (-not (Test-GhAppBotAuthor $context $pr.author)) { throw "#$Number PR author does not match botLogin." }
    if ($pr.headRefOid -cne $expected) { throw "#$Number head changed. Expected $expected; found $($pr.headRefOid)." }
    $oldBase = $pr.baseRefName
    if ($oldBase -ceq $NewBase) { throw "#$Number is already based on $NewBase." }
    $request = $null
    if (Test-GhAppReviewRequested $context $Number $LedgerPath) {
        $request = Get-GhAppReviewRequest $context $Number $LedgerPath
        if ($request.Head -cne $expected) {
            throw "#$Number stop: the review request is for $($request.Head), not for the current head; send the head for review again."
        }
        if (-not $request.PatchId) {
            throw "#$Number stop: the review request has no patch-id; register it again with a worktree before the base change."
        }
        $ref = Invoke-GhAppApi $context GET "git/ref/heads/$NewBase"
        $newBaseSha = Assert-GhAppSha "tip of $NewBase" $ref.object.sha
        Sync-GhAppReviewCommits $context $Worktree @($expected, $newBaseSha)
        $patchId = Get-GhAppPatchId $Worktree $expected $newBaseSha
        if ($patchId -cne $request.PatchId) {
            Write-GhAppMergeLog $LogPath "#$Number base change $oldBase -> $NewBase refused: patch-id $($request.PatchId) would become $patchId; base not changed"
            throw "#$Number stop: base $NewBase changes the patch-id from $($request.PatchId) to $patchId; the base was not changed. Owner approval and a new review request required."
        }
    }
    $null = Invoke-GhAppApi $context PATCH "pulls/$Number" @{ base = $NewBase } -AsApp
    $fresh = Get-GhAppPullRequest $context $Number
    if ($fresh.baseRefName -cne $NewBase -or $fresh.headRefOid -cne $expected) {
        throw "#$Number base change was not confirmed. Check the pull request."
    }
    if ($null -eq $request) {
        Write-GhAppMergeLog $LogPath "#$Number base changed $oldBase -> $NewBase; no review request on file"
        return [pscustomobject]@{ Number = $Number; OldBase = $oldBase; NewBase = $NewBase; ReviewRequest = $null }
    }
    if ($fresh.baseRefOid -cne $newBaseSha) {
        # The base moved between the check and the change. The kept request must still describe the content.
        Sync-GhAppReviewCommits $context $Worktree @($expected, $fresh.baseRefOid)
        if ((Get-GhAppPatchId $Worktree $expected $fresh.baseRefOid) -cne $request.PatchId) {
            Write-GhAppMergeLog $LogPath "#$Number base changed $oldBase -> $NewBase; the base moved during the change and the patch-id differs; no review request kept"
            throw "#$Number stop: the base moved during the change and the patch-id differs; the review request was not kept. Owner approval and a new review request required."
        }
    }
    $now = (Get-GhAppUtcNow).ToUniversalTime().ToString('o')
    $entry = [pscustomobject][ordered]@{ owner = $context.owner; repo = $context.repo; number = $Number; head = $expected;
        requestedAt = $request.RequestedAt.ToUniversalTime().ToString('o'); base = $NewBase; patchId = $request.PatchId;
        registeredAt = $now; reason = 'base-change'; fromBase = $oldBase }
    Add-GhAppLedgerEntry $LedgerPath $entry
    Write-GhAppMergeLog $LogPath "#$Number base changed $oldBase -> $NewBase; patch-id $($request.PatchId) unchanged; review request kept with its original time $($entry.requestedAt) (registered again at $now)"
    [pscustomobject]@{ Number = $Number; OldBase = $oldBase; NewBase = $NewBase; ReviewRequest = $entry }
}

function Set-GhAppPullRequestBody {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][string]$BodyFile, [Alias('Repo')][string]$Repository, [string]$ExpectedHeadSha)
    $context = Get-GhAppContext $Repository
    $body = Read-GhAppBody $BodyFile
    if ($PSBoundParameters.ContainsKey('ExpectedHeadSha')) {
        # A body quotes the head, its CI run and its review. This is a check before the write, not a lock: the head
        # can still move between the check and the PATCH, because the PATCH cannot carry the expected head.
        $expected = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha
        $pr = Get-GhAppPullRequest $context $Number
        if ($pr.headRefOid -cne $expected) { throw "#$Number head changed. Expected $expected; found $($pr.headRefOid)." }
    }
    Invoke-GhAppApi $context PATCH "pulls/$Number" @{ body = $body } -AsApp
}

function Test-GhAppReviewRequested {
    param([hashtable]$Context, [int]$Number, [string]$LedgerPath)
    try { $null = Get-GhAppReviewRequest $Context $Number $LedgerPath; return $true }
    catch {
        if ($_.Exception.Message -clike "#$Number stop: no review ledger entry*") { return $false }
        throw
    }
}

function Update-GhAppPullRequestBranch {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][string]$ExpectedHeadSha, [string]$LedgerPath,
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = 120, [ValidateRange(1, 60)][int]$PollSeconds = 5,
        [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $expected = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha
    $LedgerPath = Get-GhAppLedgerPath $context $LedgerPath
    $pr = Get-GhAppPullRequest $context $Number
    if ($pr.state -cne 'OPEN') { throw "#$Number must be open to update its branch." }
    if (-not (Test-GhAppBotAuthor $context $pr.author)) { throw "#$Number PR author does not match botLogin." }
    if ($pr.headRefOid -cne $expected) { throw "#$Number head changed. Expected $expected; found $($pr.headRefOid)." }
    # A new head voids a review request and its review record. Merge-GhAppApprovedPullRequest updates a behind branch
    # after approval and proves the result, so a PR that was sent to the owner is refused here.
    if (Test-GhAppReviewRequested $context $Number $LedgerPath) {
        throw "#$Number was sent for owner review; update refused. Merge-GhAppApprovedPullRequest updates the branch after approval."
    }
    $reviews = @(Get-GhAppItems $context "pulls/$Number/reviews" | Where-Object { $_.user.login -ieq $context.owner })
    if ($reviews.Count -gt 0) { throw "#$Number has an owner review; update refused. Open a follow-up PR instead." }
    $deadline = (Get-GhAppUtcNow).AddSeconds($TimeoutSeconds)
    $null = Invoke-GhAppApi $context PUT "pulls/$Number/update-branch" @{ expected_head_sha = $expected } -AsApp
    # The update is asynchronous. The new head appears when the merge commit exists.
    while ($true) {
        Start-Sleep -Seconds $PollSeconds
        $fresh = Get-GhAppPullRequest $context $Number
        if ($fresh.headRefOid -cne $expected) {
            return [pscustomobject]@{ Number = $Number; OldHead = $expected; NewHead = (Assert-GhAppSha "head of #$Number" $fresh.headRefOid) }
        }
        if ((Get-GhAppUtcNow) -ge $deadline) {
            throw "#$Number update was accepted but the head did not change within $TimeoutSeconds seconds. Check the PR before trying again."
        }
    }
}

function Add-GhAppComment {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][Alias('CommentFile')][string]$BodyFile, [Alias('Repo')][string]$Repository,
        [switch]$IncludeIssuesWrite, [string]$IssuesLogPath)
    $context = Get-GhAppContext $Repository
    if ($IncludeIssuesWrite) {
        if ([string]::IsNullOrWhiteSpace($IssuesLogPath)) { throw 'IncludeIssuesWrite requires IssuesLogPath.' }
        $target = Invoke-GhAppApi $context GET "issues/$Number"
        if ($null -ne $target.PSObject.Properties['pull_request']) { throw "IncludeIssuesWrite refused: #$Number is a pull request." }
        $line = "$([DateTimeOffset]::UtcNow.ToString('o')) issues-write comment number=$Number"
        try {
            $logPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($IssuesLogPath)
            [IO.File]::AppendAllText($logPath, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
        } catch { throw 'The issues write log could not be written.' }
        # A copy carries the flag, so the stored configuration and every other call stay unchanged.
        $context = $context.Clone()
        $context.includeIssuesWrite = $true
    }
    Invoke-GhAppApi $context POST "issues/$Number/comments" @{ body = (Read-GhAppBody $BodyFile) } -AsApp
}

function Read-GhAppLabelDefinition {
    param([string]$Path)
    if (-not [string]::IsNullOrWhiteSpace($Path)) { $Path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path) }
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.File]::Exists($Path)) { throw 'The label definition file is missing.' }
    try { $document = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($Path)) -ErrorAction Stop }
    catch { throw 'The label definition file is not valid JSON.' }
    if ($null -eq $document -or $null -eq $document.PSObject.Properties['labels']) { throw 'The label definition file lists no labels.' }
    $labels = @($document.labels)
    if ($labels.Count -eq 0) { throw 'The label definition file lists no labels.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($label in $labels) {
        if ($null -eq $label) { throw 'Each label has exactly the fields name, color and description.' }
        $names = @($label.PSObject.Properties.Name)
        if ($names.Count -ne 3 -or @($names | Where-Object { $_ -cnotin @('name', 'color', 'description') }).Count -gt 0) {
            throw 'Each label has exactly the fields name, color and description.'
        }
        # A name without spaces or percent signs is the same in every URL that the module builds.
        # The anchor is \z, because $ also matches before a final newline.
        if ($label.name -isnot [string] -or $label.name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,49}\z') { throw "Invalid label name: $($label.name)" }
        if ($label.color -isnot [string] -or $label.color -cnotmatch '^[0-9A-Fa-f]{6}\z') { throw "Invalid label color for $($label.name)." }
        if ($label.description -isnot [string] -or $label.description.Length -gt 100 -or $label.description -cmatch '[\x00-\x1f\x7f]') {
            throw "Invalid label description for $($label.name)."
        }
        if (-not $seen.Add($label.name)) { throw "Duplicate label name: $($label.name)" }
    }
    $labels
}

function Get-GhAppIssuesLogPath {
    param([hashtable]$Context, [string]$IssuesLogPath)
    $path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($IssuesLogPath)
    if (Test-GhAppRepositoryPath $path) { throw 'The issues write log must be outside every repository.' }
    foreach ($protected in @((Get-GhAppConfigPath $Context.owner $Context.repo), (Get-GhAppOwnerConfigPath $Context.owner), $Context.tokenHelperPath, $Context.dpapiPath)) {
        if ([string]::Equals($path,[IO.Path]::GetFullPath($protected), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The issues write log must not name a config, helper, or DPAPI file.'
        }
    }
    $path
}

function Sync-GhAppLabels {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$DefinitionPath, [Alias('Repo')][string]$Repository,
        [switch]$Apply, [string]$IssuesLogPath)
    $context = Get-GhAppContext $Repository
    $definition = @(Read-GhAppLabelDefinition $DefinitionPath)
    if ($Apply -and [string]::IsNullOrWhiteSpace($IssuesLogPath)) { throw 'Apply requires IssuesLogPath.' }
    $logPath = $(if ($Apply) { Get-GhAppIssuesLogPath $context $IssuesLogPath } else { $null })
    # GitHub compares label names without case. An ordinal comparison keeps a character that only folds to ASCII from matching.
    $existing = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in @(Get-GhAppItems $context 'labels')) { $existing[[string]$item.name] = $item }
    $plan = @()
    foreach ($label in $definition) {
        $current = $null
        $null = $existing.TryGetValue($label.name, [ref]$current)
        $action = 'create'
        $changes = @()
        if ($null -ne $current) {
            if ($current.name -cne $label.name) { $changes += 'name' }
            if ($current.color -ine $label.color) { $changes += 'color' }
            if ([string]$current.description -cne $label.description) { $changes += 'description' }
            $action = $(if ($changes.Count -gt 0) { 'update' } else { 'unchanged' })
        }
        $plan += [pscustomobject]@{ Action = $action; Name = $label.name; Color = $label.color.ToLowerInvariant();
            Description = $label.description; Changes = ($changes -join ','); Existing = $(if ($null -ne $current) { [string]$current.name } else { $null }) }
    }
    # A dry run reads only. It never lists a label that the definition file does not name, and no run deletes a label.
    if (-not $Apply) { return $plan }
    $writeContext = $null
    foreach ($row in $plan) {
        if ($row.Action -ceq 'unchanged') { continue }
        $line = "$([DateTimeOffset]::UtcNow.ToString('o')) issues-write label $($row.Action) name=$($row.Name)"
        try { [IO.File]::AppendAllText($logPath, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
        catch { throw 'The issues write log could not be written.' }
        if ($null -eq $writeContext) {
            # A copy carries the flag, so the stored configuration and every other call stay unchanged.
            $writeContext = $context.Clone()
            $writeContext.includeIssuesWrite = $true
        }
        if ($row.Action -ceq 'create') {
            $null = Invoke-GhAppApi $writeContext POST 'labels' @{ name = $row.Name; color = $row.Color; description = $row.Description } -AsApp
        } else {
            # The validated definition name goes into the path. GitHub finds the label without regard to case.
            $null = Invoke-GhAppApi $writeContext PATCH "labels/$($row.Name)" @{ new_name = $row.Name; color = $row.Color; description = $row.Description } -AsApp
        }
    }
    $plan
}

function Add-GhAppReviewRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][string]$ExpectedHeadSha, [Parameter(Mandatory)][string]$BodyFile,
        [string]$Event = 'COMMENT', [Alias('Repo')][string]$Repository)
    if ($Event -cne 'COMMENT') { throw 'Review event must be COMMENT. Only the owner approves.' }
    $context = Get-GhAppContext $Repository
    $expected = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha
    $body = Read-GhAppBody $BodyFile
    $pr = Get-GhAppPullRequest $context $Number
    $head = Assert-GhAppSha "head of #$Number" $pr.headRefOid
    if ($head -cne $expected) { throw "#$Number head changed. Expected $expected; found $head." }
    Invoke-GhAppApi $context POST "pulls/$Number/reviews" @{ event = 'COMMENT'; commit_id = $head; body = $body } -AsApp
}

function Write-GhAppMergeLog {
    param([string]$LogPath, [string]$Message)
    $line = "$([DateTimeOffset]::UtcNow.ToString('o')) $Message"
    try { [IO.File]::AppendAllText($LogPath, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
    catch { throw 'The merge log could not be written.' }
    Write-Verbose $line
}

# The one clock read by the merge wait. Tests replace it to control time.
function Get-GhAppUtcNow { [DateTimeOffset]::UtcNow }

function Sync-GhAppReviewCommits {
    param([hashtable]$Context, [string]$Worktree, [string[]]$Commits)
    if ((Invoke-GhAppGit $Worktree @('rev-parse', '--is-inside-work-tree')).Trim() -cne 'true') {
        throw 'Worktree must name a local Git clone.'
    }
    if ((Invoke-GhAppGit $Worktree @('rev-parse', '--is-shallow-repository')).Trim() -cne 'false') {
        throw 'Review proof requires a complete clone; shallow history is refused.'
    }
    $url = "https://github.com/$($Context.owner)/$($Context.repo).git"
    $null = Invoke-GhAppGit -Worktree $Worktree -OwnerCredentials -Arguments (@('-c', 'fetch.writeCommitGraph=false',
        'fetch', '--no-tags', '--no-prune', '--refmap=', '--no-write-fetch-head', '--no-auto-maintenance',
        '--recurse-submodules=no', $url) + @($Commits | Select-Object -Unique))
}

# A property that a caller-built object may not have. StrictMode makes a missing property an error.
function Get-GhAppProperty {
    param($Object, [string]$Name)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { $property.Value }
}

# The tree that a merge of $Head into $BaseSha gives. A conflict is an error. The tree id covers every byte, path and mode.
function Get-GhAppIntegratedTree {
    param([string]$Worktree, [string]$BaseSha, [string]$Head)
    $result = Invoke-GhAppGit -Worktree $Worktree -Arguments @('merge-tree', '--write-tree', $BaseSha, $Head) -AllowedExitCodes @(0, 1) -Result
    if ($result.ExitCode -ne 0) { throw "$Head does not merge cleanly into $BaseSha." }
    Assert-GhAppSha 'integrated tree' ($result.Output.Split("`n")[0].Trim())
}

function Assert-GhAppReviewHistory {
    # Outcome is optional. Given with an Approval, it lets a rebased head stand in for the approved commit, and it tells the
    # caller so: PatchReuse is set when only the exact content proof held. The patch-id is a screen. The decision is the
    # integrated tree: the approved commit and the head must give the same tree once each is merged into the current base.
    param([hashtable]$Context, [int]$Number, [string]$Worktree, $Pr, $Request, [hashtable]$Outcome)
    $head = Assert-GhAppSha "head of #$Number" $Pr.headRefOid
    if ($Pr.baseRefName -cne $Request.Base) {
        # A base change is harmless when the head is the requested head and the content is the same.
        if ($Request.PatchId -and $head -ceq $Request.Head) {
            $baseSha = Assert-GhAppSha "base of #$Number" $Pr.baseRefOid
            Sync-GhAppReviewCommits $Context $Worktree @($head, $baseSha)
            if ((Get-GhAppPatchId $Worktree $head $baseSha) -ceq $Request.PatchId) { return ,@($head) }
            throw "#$Number stop: review request base changed and the patch-id differs; owner approval and a new review request required."
        }
        throw "#$Number stop: review request base changed; owner approval and a new review request required."
    }
    # Returns the proven chain from the current head back to the review request head, both included.
    if ($head -ceq $Request.Head) { return ,@($head) }
    $base = Assert-GhAppSha "base of #$Number" $Pr.baseRefOid
    $failure = $null
    try {
        Sync-GhAppReviewCommits $Context $Worktree @($head, $Request.Head, $base)
        $version = Invoke-GhAppGit $Worktree @('--version')
        if ($version -notmatch '^git version (\d+)\.(\d+)' -or [version]"$($Matches[1]).$($Matches[2])" -lt [version]'2.38') {
            throw "#$Number stop: clean merge proof needs Git 2.38 or later for merge-tree --write-tree."
        }
        $chain = [Collections.Generic.List[string]]::new()
        $current = $head
        for ($count = 0; $current -cne $Request.Head; $count++) {
            $chain.Add($current)
            if ($count -ge 20) { throw "#$Number stop: review request $($Request.Head) was not reached within 20 first-parent commits from $head." }
            $parents = (Invoke-GhAppGit $Worktree @('rev-list', '--parents', '-n', '1', $current)).Trim().Split(' ')
            if ($parents.Length -ne 3) {
                throw "#$Number stop: head $head adds commits after review request $($Request.Head); owner approval and a new review request required."
            }
            $first = Assert-GhAppSha 'first parent' $parents[1]
            $second = Assert-GhAppSha 'second parent' $parents[2]
            $contained = Invoke-GhAppGit -Worktree $Worktree -Arguments @('merge-base', '--is-ancestor', $second, $base) -AllowedExitCodes @(0, 1) -Result
            if ($contained.ExitCode -ne 0) { throw "#$Number stop: second parent $second is not contained in $($Pr.baseRefName); owner approval and a new review request required." }
            # Every merge is recomputed. A blob comparison alone would accept a conflict resolved
            # wholly to either parent, so no shortcut is taken.
            # merge-tree writes only objects: it leaves the index, worktree, and refs untouched.
            $result = Invoke-GhAppGit -Worktree $Worktree -Arguments @('merge-tree', '--write-tree', $first, $second) -AllowedExitCodes @(0, 1) -Result
            if ($result.ExitCode -ne 0) { throw "#$Number stop: recomputed merge conflicts; manual resolution requires owner approval." }
            $tree = Assert-GhAppSha 'recomputed merge tree' ($result.Output.Split("`n")[0].Trim())
            $actual = (Invoke-GhAppGit $Worktree @('rev-parse', '--verify', "$current^{tree}")).Trim()
            if ($tree -cne $actual) { throw "#$Number stop: merge tree differs from the clean automatic merge; manual resolution requires owner approval." }
            $current = $first
        }
        $chain.Add($Request.Head)
        return ,$chain.ToArray()
    } catch { $failure = $_.Exception.Message }
    # The head is not a clean merge chain from the request. A rebase gives the same content a new history.
    $patchId = Get-GhAppProperty $Request 'PatchId'
    $approval = $(if ($null -ne $Outcome -and $Outcome.ContainsKey('Approval')) { $Outcome.Approval } else { $null })
    if (-not $patchId -or $null -eq $approval) { throw $failure }
    $approved = Assert-GhAppSha 'approval commit' $approval.commit_id
    Sync-GhAppReviewCommits $Context $Worktree @($head, $base, $approved)
    if ((Get-GhAppPatchId $Worktree $head $base) -cne $patchId) {
        throw "#$Number stop: head $head is not review request $($Request.Head) or a clean base merge after it, and its patch-id differs from the request; owner approval and a new review request required."
    }
    # The approval must sit on the request head or on a clean base merge after it. That is the history the owner saw.
    if ($approved -cne $Request.Head) {
        $onApproved = [pscustomobject]@{ headRefOid = $approved; baseRefName = $Pr.baseRefName; baseRefOid = $base }
        try { $null = Assert-GhAppReviewHistory $Context $Number $Worktree $onApproved $Request }
        catch { throw "#$Number stop: owner approval is on $approved, not on review request $($Request.Head) or a clean base merge after it." }
    }
    try { $same = (Get-GhAppIntegratedTree $Worktree $base $approved) -ceq (Get-GhAppIntegratedTree $Worktree $base $head) }
    catch { throw "#$Number stop: head $head or approved commit $approved does not merge cleanly into $($Pr.baseRefName); owner approval and a new review request required." }
    if (-not $same) {
        throw "#$Number stop: head $head does not have the content of approved commit $approved once both are merged into $($Pr.baseRefName); owner approval and a new review request required."
    }
    $Outcome.PatchReuse = $true
    return ,@($head, $Request.Head, $approved)
}

function Get-GhAppTimelineObservation {
    param([hashtable]$Context, [int]$Number, $Approval)
    # Timeline order is an observation only. Commit timestamps can predate a push.
    $query = 'query($owner:String!,$repo:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$repo){pullRequest(number:$number){timelineItems(first:100,after:$cursor,itemTypes:[PULL_REQUEST_COMMIT,PULL_REQUEST_REVIEW]){nodes{__typename ... on PullRequestCommit{commit{oid}} ... on PullRequestReview{fullDatabaseId}} pageInfo{hasNextPage endCursor}}}}}'
    $cursor = $null
    $lastCommit = 'none'
    try {
        for ($page = 0; $page -lt 1000; $page++) {
            $body = @{ query = $query; variables = @{ owner = $Context.owner; repo = $Context.repo; number = $Number; cursor = $cursor } }
            $result = Invoke-GhAppGh -Context $Context -Read -Arguments @('api', 'graphql', '--hostname', 'github.com', '--method', 'POST', '--input', '-') `
                -ApiPath '/graphql' -InputText (ConvertTo-Json $body -Depth 10 -Compress)
            $data = ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop
            if ($data.PSObject.Properties['errors']) { return 'unavailable' }
            $timeline = $data.data.repository.pullRequest.timelineItems
            foreach ($node in $timeline.nodes) {
                if ($node.__typename -ceq 'PullRequestCommit') { $lastCommit = Assert-GhAppSha 'timeline commit' $node.commit.oid }
                elseif ($node.__typename -ceq 'PullRequestReview' -and [string]$node.fullDatabaseId -ceq [string]$Approval.id) { return $lastCommit }
            }
            if (-not $timeline.pageInfo.hasNextPage) { return 'unavailable' }
            $cursor = $timeline.pageInfo.endCursor
        }
    } catch { return 'unavailable' }
    return 'unavailable'
}

function Get-GhAppApprovedReviewRequest {
    param([hashtable]$Context, [int]$Number, [string]$Worktree, $Pr, [string]$LedgerPath)
    $request = Get-GhAppReviewRequest $Context $Number $LedgerPath
    $approval = Get-GhAppOwnerApproval $Context $Number $request.RequestedAt
    if ($null -eq $approval) { throw "#$Number stop: no owner approval after review request $($request.Head)." }
    $outcome = @{ Approval = $approval }
    $chain = Assert-GhAppReviewHistory $Context $Number $Worktree $Pr $request $outcome
    # The ledger time comes from this machine's clock. Binding the approval to the proven chain means a
    # skewed clock can never let an approval of an older head count for the request.
    if ([string]$approval.commit_id -cnotin $chain) {
        throw "#$Number stop: owner approval is on $($approval.commit_id), not on review request $($request.Head) or a clean base merge after it."
    }
    # After a rebase the approval counts for a head that the exact content proof accepted. It counts once: a merge spends it,
    # and the caller writes the record only after the merge is confirmed. A record of the same approval for another head
    # means that it was spent already. This function writes nothing.
    $reuse = $null
    if ($outcome.ContainsKey('PatchReuse')) {
        $head = Assert-GhAppSha "head of #$Number" $Pr.headRefOid
        $spent = @(@(Get-GhAppProperty $request 'Reuses') | Where-Object { $null -ne $_ -and $_.ApprovalId -ceq [string]$approval.id -and $_.ApprovalCommit -ceq [string]$approval.commit_id })
        if ($spent.Count -gt 0 -and $head -cnotin @($spent | ForEach-Object { $_.NewHead })) {
            throw "#$Number stop: owner approval $($approval.id) was already reused for $($spent[0].NewHead); a new owner approval is required."
        }
        $reuse = [pscustomobject]@{ Mode = 'tree-equal'; From = $request.Head; ApprovalId = [string]$approval.id; ApprovalCommit = [string]$approval.commit_id
            NewHead = $head; RequestedAt = $request.RequestedAt; PatchId = (Get-GhAppProperty $request 'PatchId') }
    }
    [pscustomobject]@{ Request = $request; Approval = $approval; Reuse = $reuse }
}

# Appends the record of a spent approval. The caller calls it after the merge is confirmed.
function Add-GhAppReuseRecord {
    param([hashtable]$Context, [string]$LedgerPath, [int]$Number, $Reuse)
    $entry = [ordered]@{ kind = 'reuse'; owner = $Context.owner; repo = $Context.repo; number = $Number; mode = $Reuse.Mode;
        requestedAt = $Reuse.RequestedAt.ToUniversalTime().ToString('o'); oldHead = $Reuse.From; newHead = $Reuse.NewHead;
        approvalId = [long]$Reuse.ApprovalId; approvalCommit = $Reuse.ApprovalCommit }
    if ($Reuse.PatchId) { $entry.patchId = $Reuse.PatchId }
    $entry.reusedAt = (Get-GhAppUtcNow).ToUniversalTime().ToString('o')
    Add-GhAppLedgerEntry $LedgerPath ([pscustomobject]$entry)
}

# One merge of a pull request at a time. The lock file lives next to the ledger and goes away with the handle.
function Enter-GhAppPullRequestLock {
    param([hashtable]$Context, [string]$LedgerPath, [int]$Number)
    $path = "$LedgerPath.$($Context.owner)_$($Context.repo)_$Number.lock"
    try { [IO.FileStream]::new($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None, 1, [IO.FileOptions]::DeleteOnClose) }
    catch { throw "#$Number stop: another merge of this pull request is running." }
}

# The checks of a pull request as gh reports them. An empty list is "no check" only when gh says so (exit 0, or its
# own "no checks reported on the '<branch>' branch" message, exit code 1). Any other failure with no list is an error: a failed login or a lost network
# must never look like a repository without checks.
function Read-GhAppChecks {
    param([hashtable]$Context, [int]$Number, [string]$ApiPath, [switch]$Required)
    $arguments = @('pr', 'checks', [string]$Number, '--repo', "$($Context.owner)/$($Context.repo)")
    if ($Required) { $arguments += '--required' }
    $arguments += @('--json', 'name,bucket,state')
    $result = Invoke-GhAppGh -Context $Context -Arguments $arguments -ApiPath $ApiPath -AllowedExitCodes @(0, 1, 8) -Read
    $checks = @()
    if (-not [string]::IsNullOrWhiteSpace($result.Output)) {
        try { $checks = @(ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop) }
        catch { throw "HTTP unknown $ApiPath" }
    }
    $checks = @($checks | Where-Object { $null -ne $_ })
    if ($checks.Count -eq 0 -and $result.ExitCode -ne 0) {
        # The whole message, case-sensitive, with exit code 1. gh 2.92.0 prints "no checks reported on the '<branch>' branch"
        # for both calls; the "no required checks reported ..." wording is accepted too.
        $noChecks = "^no (required )?checks reported on the '[^'\r\n]+' branch$"
        if ($result.ExitCode -ne 1 -or ([string]$result.ErrorText).Trim() -cnotmatch $noChecks) { throw "HTTP unknown $ApiPath" }
    }
    [pscustomobject]@{ Checks = $checks; ExitCode = $result.ExitCode }
}

function Wait-GhAppApprovedHead {
    param([hashtable]$Context, [int]$Number, $Pr, [string]$Worktree, [string]$LedgerPath, [int]$TimeoutSeconds, [int]$PollSeconds)
    $deadline = (Get-GhAppUtcNow).AddSeconds($TimeoutSeconds)
    $updates = 0
    $updatingHead = $null
    while ($true) {
        if ($Pr.state -cne 'OPEN') { throw "#$Number stop: state $($Pr.state)." }
        if ($Pr.isDraft -or -not (Test-GhAppBotAuthor $Context $Pr.author)) { throw "#$Number stop: PR must be a non-draft authored by botLogin." }
        $current = Assert-GhAppSha "head of #$Number" $Pr.headRefOid
        if ($Pr.mergeStateStatus -ceq 'DIRTY') { throw "#$Number stop: merge conflict with $($Pr.baseRefName); manual resolution requires owner approval." }
        if ((Get-GhAppUtcNow) -ge $deadline) { throw "#$Number stop: timed out in state $($Pr.mergeStateStatus)." }
        # While an update is pending the head has not moved, so skip the proof until it does.
        # A PR that is no longer behind needs no new head, so stop waiting for one.
        if ($updatingHead -ceq $current -and $Pr.mergeStateStatus -ceq 'BEHIND') {
            Start-Sleep -Seconds $PollSeconds
            $Pr = Get-GhAppPullRequest $Context $Number
            continue
        }
        $updatingHead = $null
        $null = Get-GhAppApprovedReviewRequest $Context $Number $Worktree $Pr $LedgerPath
        if ($updates -ge 3 -and $Pr.mergeStateStatus -ceq 'BEHIND') { throw "#$Number stop: still behind $($Pr.baseRefName) after three updates." }
        if ($Pr.mergeStateStatus -ceq 'BEHIND') {
            try { $null = Invoke-GhAppApi $Context PUT "pulls/$Number/update-branch" @{ expected_head_sha = $current } -AsApp }
            catch { throw "#$Number stop: branch update failed; manual resolution requires owner approval." }
            $updates++
            $updatingHead = $current
            # The update is asynchronous. Poll readiness and prove any new head before checking it.
            Start-Sleep -Seconds $PollSeconds
            $Pr = Get-GhAppPullRequest $Context $Number
            continue
        }
        $path = "/repos/$($Context.owner)/$($Context.repo)/pulls/$Number/checks"
        $required = Read-GhAppChecks $Context $Number $path -Required
        $checks = $required.Checks
        $checksExit = $required.ExitCode
        $checkLabel = 'required checks'
        $noCi = $false
        if ($checks.Count -eq 0) {
            # A repository with no required checks makes gh print nothing and exit 1. That is a state, not a failure:
            # judge the head by all of its checks.
            $all = Read-GhAppChecks $Context $Number $path
            $checks = $all.Checks
            $checksExit = $(if ($checks.Count -eq 0) { 0 } else { $all.ExitCode })
            $checkLabel = 'checks'
            # With no check at all, the head is released only for a repository with no CI. A repository with a workflow may not have
            # started its check yet, so the loop waits and reads again. The ruleset and CLEAN still have to agree.
            if ($checks.Count -eq 0) { $noCi = Test-GhAppNoCiRepository $Context $current }
        }
        if (@($checks | Where-Object { $_.bucket -in @('fail', 'cancel') }).Count -gt 0) { throw "#$Number stop: $checkLabel failed." }
        $pending = (@($checks | Where-Object { $_.bucket -notin @('pass', 'skipping') }).Count -gt 0) -or ($checks.Count -eq 0 -and -not $noCi)
        if (-not $pending -and $checksExit -eq 0 -and $Pr.mergeStateStatus -ceq 'CLEAN') {
            # Recheck both the head and the latest owner review after checking CI.
            $fresh = Get-GhAppPullRequest $Context $Number
            if ($fresh.headRefOid -ceq $current -and $fresh.baseRefOid -ceq $Pr.baseRefOid -and
                $fresh.baseRefName -ceq $Pr.baseRefName -and $fresh.state -ceq 'OPEN' -and $fresh.mergeStateStatus -ceq 'CLEAN') {
                if ($fresh.isDraft -or -not (Test-GhAppBotAuthor $Context $fresh.author)) { throw "#$Number stop: PR must be a non-draft authored by botLogin." }
                $evidence = Get-GhAppApprovedReviewRequest $Context $Number $Worktree $fresh $LedgerPath
                return [pscustomobject]@{ Pr = $fresh; Request = $evidence.Request; Approval = $evidence.Approval; Reuse = $evidence.Reuse; NoCi = $noCi }
            }
            $Pr = $fresh
            continue
        }
        Start-Sleep -Seconds $PollSeconds
        $Pr = Get-GhAppPullRequest $Context $Number
    }
}

function New-GhAppBranchOutcome {
    param([int]$Number, [string]$Branch, [bool]$Deleted, [string]$Reason)
    [pscustomobject]@{ Number = $Number; Branch = $Branch; Deleted = $Deleted; Reason = $Reason }
}

function Remove-GhAppBranchAfterMerge {
    param([hashtable]$Context, [int]$Number, $Pr, [string]$Head, [string]$LogPath, [string]$Prefix)
    $branch = $Pr.headRefName
    $base = $Pr.baseRefName
    $encoded = [Uri]::EscapeDataString($branch)
    # Deletion is opt-in by prefix, so release and other long-lived branches are never deleted.
    if (-not $Prefix -or -not $branch.StartsWith($Prefix, [StringComparison]::Ordinal)) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; no matching delete prefix"
        if (-not $Prefix) {
            Write-Warning "#$Number kept branch $branch because no delete prefix was given. Delete it with Remove-GhAppMergedBranch, or pass -DeleteBranchPrefix to the merge."
        }
        return New-GhAppBranchOutcome $Number $branch $false 'no matching delete prefix'
    }
    if ($branch -ceq 'main' -or $branch -ceq $base) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; base branch"
        return New-GhAppBranchOutcome $Number $branch $false 'base branch'
    }
    $ref = Invoke-GhAppApi $Context GET "git/ref/heads/$encoded" -AllowNotFound
    if ($null -eq $ref -or $ref.object.sha -cne $Head) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; ref changed or absent"
        return New-GhAppBranchOutcome $Number $branch $false 'ref changed or absent'
    }
    $comparison = Invoke-GhAppApi $Context GET "compare/$([Uri]::EscapeDataString($base))...$Head"
    if ($comparison.status -notin @('behind', 'identical') -or $comparison.ahead_by -ne 0) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; $base does not contain $Head"
        return New-GhAppBranchOutcome $Number $branch $false "$base does not contain $Head"
    }
    # GitHub has no delete that names the expected SHA. So the App token comes first: the token helper can take
    # seconds and retry. Then the ref is read once more, compared, and deleted. A delete is never retried, so no
    # token request, wait or retry sits between that read and the delete. Only the comparison and the start of gh do.
    $token = $null
    try {
        $token = Get-GhAppWriteToken $Context "git/refs/heads/$encoded"
        $last = Invoke-GhAppApi $Context GET "git/ref/heads/$encoded" -AllowNotFound
        if ($null -eq $last -or $last.object.sha -cne $Head) {
            Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; ref changed before delete"
            return New-GhAppBranchOutcome $Number $branch $false 'ref changed before delete'
        }
        $null = Invoke-GhAppApi $Context DELETE "git/refs/heads/$encoded" -AsApp -PreparedToken $token
    } finally { $token = $null }
    Write-GhAppMergeLog $LogPath "#$Number deleted branch $branch at $Head"
    New-GhAppBranchOutcome $Number $branch $true 'deleted'
}

function Remove-GhAppMergedBranch {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][string]$ExpectedHeadSha, [Parameter(Mandatory)][Alias('Log')][string]$LogPath,
        [Parameter(Mandatory)][Alias('DeleteBranchPrefix')][string]$BranchPrefix, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $head = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha
    if ([string]::IsNullOrWhiteSpace($BranchPrefix)) { throw 'BranchPrefix must be nonempty.' }
    $LogPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogPath)
    $pr = Get-GhAppPullRequest $context $Number
    if ($pr.state -cne 'MERGED') { throw "#$Number is $($pr.state), not merged; branch deletion refused." }
    if ($pr.headRefOid -cne $head) { throw "#$Number head changed. Expected $head; found $($pr.headRefOid)." }
    Remove-GhAppBranchAfterMerge $context $Number $pr $head $LogPath $BranchPrefix
}

function Merge-GhAppApprovedPullRequest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int[]]$Numbers,
        [Parameter(Mandatory)][string]$Worktree, [Parameter(Mandatory)][Alias('Log')][string]$LogPath,
        [string]$LedgerPath, [ValidateRange(1, 3600)][int]$TimeoutSeconds = 1200,
        [ValidateRange(1, 60)][int]$PollSeconds = 15, [string]$DeleteBranchPrefix, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    if ($PSBoundParameters.ContainsKey('DeleteBranchPrefix') -and [string]::IsNullOrWhiteSpace($DeleteBranchPrefix)) {
        throw 'DeleteBranchPrefix must be nonempty when given.'
    }
    $LedgerPath = Get-GhAppLedgerPath $context $LedgerPath
    $LogPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogPath)
    foreach ($number in $Numbers) {
        $lock = $null
        try {
            Write-GhAppMergeLog $LogPath "#$number start"
            $lock = Enter-GhAppPullRequestLock $context $LedgerPath $number
            $pr = Get-GhAppPullRequest $context $number
            if ($pr.state -cne 'OPEN') { Write-GhAppMergeLog $LogPath "#$number skip: state $($pr.state)"; continue }
            $evidence = Wait-GhAppApprovedHead $context $number $pr $Worktree $LedgerPath $TimeoutSeconds $PollSeconds
            $pr = $evidence.Pr
            $head = Assert-GhAppSha "head of #$number" $pr.headRefOid
            if ($evidence.NoCi) { Write-GhAppMergeLog $LogPath "#$number no CI in repository: no workflow, no commit status and no check run on $head" }
            $provenBase = $null
            if ($null -ne $evidence.Reuse) {
                # The reuse proof holds for the base it was made against. The merge API binds only the head, so read the base
                # once more right before the merge and refuse when it moved. A smaller window than before, not a closed one.
                $provenBase = Assert-GhAppSha "base of #$number" $pr.baseRefOid
                $now = Get-GhAppPullRequest $context $number
                if ($now.headRefOid -cne $head -or $now.baseRefOid -cne $provenBase) {
                    throw "#$number stop: the base branch moved after the reuse proof ($provenBase -> $($now.baseRefOid)); a new owner approval or a new proof is required."
                }
            }
            $merged = Invoke-GhAppApi $context PUT "pulls/$number/merge" @{ merge_method = 'merge'; sha = $head } -AsApp
            if (-not $merged.merged) { throw "#$number stop: merge failed." }
            $mergeSha = Assert-GhAppSha "merge commit of #$number" $merged.sha
            $pr = Get-GhAppPullRequest $context $number
            if ($pr.state -cne 'MERGED') { throw "#$number stop: merge was not confirmed." }
            Write-GhAppMergeLog $LogPath "#$number merged $head -> $mergeSha"
            $approvedAt = ([DateTimeOffset]$evidence.Approval.submitted_at).ToUniversalTime().ToString('o')
            Write-GhAppMergeLog $LogPath "#$number review requested at $($evidence.Request.Head); approved $approvedAt; merged head $head; differences only from $($evidence.Request.Base)"
            $treeMismatch = $false
            if ($null -ne $evidence.Reuse) {
                Write-GhAppMergeLog $LogPath "#$number owner approval $($evidence.Reuse.ApprovalId) reused from $($evidence.Reuse.From) ($($evidence.Reuse.Mode)); merged head $head"
                # The merge is done and the approval is spent. Compare what GitHub merged with the tree that was proven, and
                # flag a difference. It cannot be undone here, so the flag is for the people who read the log and the result.
                $mergedTree = $null
                $provenTree = $null
                try {
                    $mergedTree = Assert-GhAppSha 'merge commit tree' (Invoke-GhAppApi $context GET "git/commits/$mergeSha").tree.sha
                    $provenTree = Get-GhAppIntegratedTree $Worktree $provenBase $head
                } catch { Write-GhAppMergeLog $LogPath "#$number reuse tree check could not run: $($_.Exception.Message)" }
                if ($mergedTree -cne $provenTree) {
                    $treeMismatch = $true
                    Write-GhAppMergeLog $LogPath "#$number TREE MISMATCH after reuse: merge commit $mergeSha has tree $mergedTree; proven base $provenBase + head $head gives $provenTree. Review this merge."
                    Write-Warning "#$number tree mismatch after approval reuse: merge commit $mergeSha differs from the proven result. Tell PC-Commander."
                }
                Add-GhAppReuseRecord $context $LedgerPath $number $evidence.Reuse
            }
            $observation = Get-GhAppTimelineObservation $context $number $evidence.Approval
            Write-GhAppMergeLog $LogPath "#$number timeline observation only: last PullRequestCommit before approval $observation"
            $null = Remove-GhAppBranchAfterMerge $context $number $pr $head $LogPath $DeleteBranchPrefix
            [pscustomobject]@{ Number = $number; Head = $head; MergeCommit = $mergeSha; Reused = ($null -ne $evidence.Reuse); TreeMismatch = $treeMismatch; NoCi = [bool]$evidence.NoCi }
        } catch {
            Write-GhAppMergeLog $LogPath "#$number error: $($_.Exception.Message)"
            throw
        } finally { if ($null -ne $lock) { $lock.Dispose() } }
    }
    Write-GhAppMergeLog $LogPath "done: $($Numbers -join ',')"
}

function Get-GhAppUnresolvedThreadCount {
    param([hashtable]$Context, [int]$Number)
    # A thread count that cannot be read is an error, never zero: the caller must refuse to merge.
    $query = 'query($owner:String!,$repo:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$repo){pullRequest(number:$number){reviewThreads(first:100,after:$cursor){nodes{isResolved} pageInfo{hasNextPage endCursor}}}}}'
    $cursor = $null
    $unresolved = 0
    for ($page = 0; $page -lt 100; $page++) {
        $body = @{ query = $query; variables = @{ owner = $Context.owner; repo = $Context.repo; number = $Number; cursor = $cursor } }
        $result = Invoke-GhAppGh -Context $Context -Read -Arguments @('api', 'graphql', '--hostname', 'github.com', '--method', 'POST', '--input', '-') `
            -ApiPath '/graphql' -InputText (ConvertTo-Json $body -Depth 10 -Compress)
        try { $data = ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop }
        catch { throw "The review threads of #$Number could not be read." }
        if ($data.PSObject.Properties['errors'] -or $null -eq $data.data -or $null -eq $data.data.repository.pullRequest) {
            throw "The review threads of #$Number could not be read."
        }
        $threads = $data.data.repository.pullRequest.reviewThreads
        $unresolved += @($threads.nodes | Where-Object { $_.isResolved -ne $true }).Count
        if (-not $threads.pageInfo.hasNextPage) { return $unresolved }
        $cursor = $threads.pageInfo.endCursor
    }
    throw "The review threads of #$Number fill more than 100 pages."
}

# Merges a pull request of tier B. The owner does not approve a tier B pull request, so the tool proves each condition itself
# and refuses when one fails. The conditions: the repository and base are in $script:TierBScopes, the PR is ready and written
# by the App, the head is the expected head, a review record accepts that head, the latest run of every workflow succeeded,
# no review thread is open, and the merge state is CLEAN. The merge names the head as its boundary, and each merge adds one
# line to tier-b-merges.jsonl next to the review ledger.
function Merge-GhAppTierBPullRequest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][string]$ExpectedHeadSha, [Parameter(Mandatory)][Alias('Log')][string]$LogPath,
        [string]$LedgerPath, [string]$ReviewRecordPattern, [string]$DeleteBranchPrefix, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $head = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha
    Assert-GhAppReviewRecordPattern $ReviewRecordPattern
    if ($PSBoundParameters.ContainsKey('DeleteBranchPrefix') -and [string]::IsNullOrWhiteSpace($DeleteBranchPrefix)) {
        throw 'DeleteBranchPrefix must be nonempty when given.'
    }
    $LedgerPath = Get-GhAppLedgerPath $context $LedgerPath
    $LogPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogPath)
    $name = "$($context.owner)/$($context.repo)"
    $scope = @($script:TierBScopes | Where-Object { [string]::Equals($_.Repository, $name, [StringComparison]::OrdinalIgnoreCase) })
    if ($scope.Count -eq 0) { throw "#$Number tier B merge refused: $name is not a tier B repository. Only the owner approves there." }
    try {
        Write-GhAppMergeLog $LogPath "#$Number tier B start"
        $pr = Get-GhAppPullRequest $context $Number
        $refusals = [Collections.Generic.List[string]]::new()
        if (-not [regex]::IsMatch([string]$pr.baseRefName, $scope[0].BasePattern, [Text.RegularExpressions.RegexOptions]::None, [TimeSpan]::FromSeconds(2))) {
            $refusals.Add("Base: $($pr.baseRefName) is not a tier B base of $name")
        }
        $readiness = Get-GhAppReadiness $context $Number $pr $head $ReviewRecordPattern
        foreach ($row in @($readiness.Checks | Where-Object { $_.Result -ceq 'FAIL' })) { $refusals.Add("$($row.Name): $($row.Detail)") }
        # Readiness also accepts BLOCKED, the state before an owner approves. A tier B merge has no owner approval, so only CLEAN counts.
        if ($pr.mergeStateStatus -cne 'CLEAN') { $refusals.Add("MergeState: must be CLEAN, is $($pr.mergeStateStatus)") }
        $unresolved = Get-GhAppUnresolvedThreadCount $context $Number
        if ($unresolved -gt 0) { $refusals.Add("Threads: $unresolved review thread(s) are not resolved") }
        if ($scope[0].ContainsKey('ProtectedPathPattern')) {
            # The file list must be complete, or a protected file could sit outside it. GitHub returns at most 3,000 files.
            $meta = Invoke-GhAppApi $context GET "pulls/$Number"
            $changed = $(if ($null -ne $meta -and $null -ne $meta.PSObject.Properties['changed_files']) { [int]$meta.changed_files } else { -1 })
            $listedCount = @($readiness.Files).Count
            if ($changed -lt 0 -or $changed -gt 3000 -or $changed -ne $listedCount) {
                $refusals.Add("Files: the pull request changes $changed file(s) and $listedCount were listed; the path floor cannot be proven")
            }
            $protected = @(foreach ($file in $readiness.Files) {
                foreach ($path in @($file.Path, $file.Previous)) {
                    if ($path -and [regex]::IsMatch($path, $scope[0].ProtectedPathPattern, [Text.RegularExpressions.RegexOptions]::None, [TimeSpan]::FromSeconds(2))) { $path }
                }
            })
            if ($protected.Count -gt 0) {
                $listed = @($protected | Select-Object -Unique -First 3) -join ', '
                $refusals.Add("Paths: $listed is a protected path of $name; only the owner approves a change there")
            }
            # A debt ledger may only lose lines. A new, removed or renamed ledger, or a ledger that gains a line, is an increase or a policy change.
            $ledgers = @($scope[0].DebtLedgers)
            $grown = @(foreach ($file in $readiness.Files) {
                $touchesLedger = ($file.Path -iin $ledgers) -or ($file.Previous -and $file.Previous -iin $ledgers)
                if ($touchesLedger -and ($file.Added -gt 0 -or $file.Previous -or $file.Status -cin @('added', 'removed', 'renamed'))) { $file.Path }
            })
            if ($grown.Count -gt 0) {
                $refusals.Add("Paths: $(@($grown | Select-Object -Unique -First 3) -join ', ') is a debt ledger and gains lines or is added, removed or renamed; only the owner approves that")
            }
        }
        if ($refusals.Count -gt 0) { throw "#$Number tier B merge refused: $($refusals -join '; ')." }
        # Check the pull request once more just before the merge: the head, the base and the state must not have moved.
        $fresh = Get-GhAppPullRequest $context $Number
        if ($fresh.state -cne 'OPEN' -or $fresh.isDraft -or $fresh.headRefOid -cne $head -or $fresh.baseRefOid -cne $pr.baseRefOid -or
            $fresh.baseRefName -cne $pr.baseRefName -or $fresh.mergeStateStatus -cne 'CLEAN') {
            throw "#$Number tier B merge refused: the pull request changed during the checks."
        }
        $merged = Invoke-GhAppApi $context PUT "pulls/$Number/merge" @{ merge_method = 'merge'; sha = $head } -AsApp
        if (-not $merged.merged) { throw "#$Number stop: merge failed." }
        $mergeSha = Assert-GhAppSha "merge commit of #$Number" $merged.sha
        $after = Get-GhAppPullRequest $context $Number
        if ($after.state -cne 'MERGED') { throw "#$Number stop: merge was not confirmed." }
        Write-GhAppMergeLog $LogPath "#$Number tier B merged $head -> $mergeSha"
    } catch {
        Write-GhAppMergeLog $LogPath "#$Number tier B error: $($_.Exception.Message)"
        throw
    }
    $runNames = @($readiness.Checks | Where-Object { $_.Name.StartsWith('Run: ', [StringComparison]::Ordinal) } | ForEach-Object { $_.Name.Substring(5) })
    $entry = [ordered]@{ owner = $context.owner; repo = $context.repo; number = $Number; head = $head; base = [string]$pr.baseRefName
        mergeSha = $mergeSha; mergedAt = (Get-GhAppUtcNow).ToString('o'); mergedBy = $context.botLogin; operator = [Environment]::UserName
        basis = 'tier B: review record accepts this head; latest run of every workflow succeeded; no open review thread; merge state CLEAN'
        workflows = $runNames }
    $auditPath = Join-Path (Split-Path $LedgerPath) 'tier-b-merges.jsonl'
    try { [IO.File]::AppendAllText($auditPath, (ConvertTo-Json $entry -Compress) + "`n", [Text.UTF8Encoding]::new($false)) }
    catch { throw "#$Number was merged as $mergeSha, but the audit entry could not be written to $auditPath." }
    $null = Remove-GhAppBranchAfterMerge $context $Number $after $head $LogPath $DeleteBranchPrefix
}

function Close-GhAppPullRequest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][Alias('BodyFile')][string]$CommentFile, [switch]$DeleteBranch,
        [Alias('DeleteBranchPrefix')][string]$BranchPrefix, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    if ($DeleteBranch -and [string]::IsNullOrWhiteSpace($BranchPrefix)) { throw 'DeleteBranch requires a nonempty BranchPrefix.' }
    $body = Read-GhAppBody $CommentFile
    $pr = Get-GhAppPullRequest $context $Number
    if ($DeleteBranch -and (-not $pr.headRefName.StartsWith($BranchPrefix, [StringComparison]::Ordinal) -or
        $pr.headRefName -ceq 'main' -or $pr.headRefName -ceq $pr.baseRefName)) { throw 'Head branch does not satisfy BranchPrefix; deletion refused.' }
    $null = Invoke-GhAppApi $context POST "issues/$Number/comments" @{ body = $body } -AsApp
    $closed = Invoke-GhAppApi $context PATCH "pulls/$Number" @{ state = 'closed' } -AsApp
    if ($closed.state -cne 'closed') { throw "#$Number close was not confirmed." }
    if ($DeleteBranch) {
        $encoded = [Uri]::EscapeDataString($pr.headRefName)
        $ref = Invoke-GhAppApi $context GET "git/ref/heads/$encoded"
        if ($ref.object.sha -cne $pr.headRefOid) { throw 'Head branch changed; deletion refused.' }
        $null = Invoke-GhAppApi $context DELETE "git/refs/heads/$encoded" -AsApp
    }
}

function Assert-GhAppReadArguments {
    param([hashtable]$Context, [string[]]$Arguments)
    $expected = "$($Context.owner)/$($Context.repo)"
    $refusal = 'Invoke-GhAppRead accepts only repository-scoped read commands.'
    if ($Arguments.Count -lt 2) { throw $refusal }
    $command = $Arguments[0]
    $subcommand = $Arguments[1]
    $api = $command -ceq 'api'
    if ($api) {
        $path = $subcommand.TrimStart('/')
        $route = $path.Split('?')[0]
        $decoded = [Uri]::UnescapeDataString($route)
        if (($route -cne "repos/$expected" -and -not $route.StartsWith("repos/$expected/", [StringComparison]::Ordinal)) -or
            $decoded -match '(^|/)\.\.?(/|$)|\\|//|[\x00-\x20\x7f]' -or $decoded.Contains('%')) {
            throw "Repository mismatch. Expected $expected."
        }
        $valueFlags = @('--method', '-X', '--jq', '-q')
        $switchFlags = @('--paginate', '--slurp')
    } else {
        if (($command -cnotin @('pr', 'issue') -or $subcommand -cnotin @('view', 'list', 'checks')) -and
            -not ($command -ceq 'repo' -and $subcommand -ceq 'view')) { throw $refusal }
        if ($command -ceq 'issue' -and $subcommand -ceq 'checks') { throw $refusal }
        $valueFlags = @('--repo', '-R', '--json', '--jq', '-q', '--limit', '-L', '--state', '--head', '--base',
            '--author', '--assignee', '--label')
        $switchFlags = @('--required', '--draft')
    }
    $positionals = 0
    for ($index = 2; $index -lt $Arguments.Count; $index++) {
        $argument = $Arguments[$index]
        $flag = $argument
        $value = $null
        if ($argument.Contains('=')) { $flag, $value = $argument.Split('=', 2) }
        if ($argument -cmatch '^-R(.+)\z') { $flag = '-R'; $value = $Matches[1].TrimStart('=') }
        if ($flag -cin $valueFlags) {
            if ($null -eq $value) {
                if (++$index -ge $Arguments.Count) { throw $refusal }
                $value = $Arguments[$index]
            }
            if ($flag -cin @('--repo', '-R') -and $value -cne $expected) { throw "Repository mismatch. Expected $expected." }
            if ($flag -cin @('--method', '-X') -and $value -cne 'GET') { throw $refusal }
        } elseif ($argument -cin $switchFlags) { continue }
        elseif (-not $api -and -not $argument.StartsWith('-')) {
            $positionals++
            if ($positionals -gt 1 -or $subcommand -ceq 'list') { throw $refusal }
            if ($command -ceq 'repo') {
                if ($argument -cne $expected) { throw "Repository mismatch. Expected $expected." }
            } elseif ($argument -cnotmatch '^[1-9][0-9]*\z') { throw $refusal }
        } else { throw $refusal }
    }
    if ($api) { return ,$Arguments }
    if ($command -ceq 'repo') {
        if ($positionals -eq 0) { return ,($Arguments + @($expected)) }
        return ,$Arguments
    }
    return ,($Arguments + @('--repo', $expected))
}

function Invoke-GhAppRead {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Arguments, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $safeArguments = Assert-GhAppReadArguments $context $Arguments
    $path = "/repos/$($context.owner)/$($context.repo)"
    if ($safeArguments[0] -ceq 'api') { $path = '/' + $safeArguments[1].TrimStart('/') }
    $result = Invoke-GhAppGh -Context $context -Arguments $safeArguments -ApiPath $path -Read
    $result.Output.TrimEnd("`r", "`n")
}

Export-ModuleMember -Function Import-GhAppConfig, Push-GhAppBranch, New-GhAppPullRequest,
    Set-GhAppPullRequestBody, Update-GhAppPullRequestBranch, Add-GhAppComment, Sync-GhAppLabels, Add-GhAppReviewRecord,
    Get-GhAppPullRequestReadiness, Request-GhAppOwnerReview, Merge-GhAppApprovedPullRequest, Merge-GhAppTierBPullRequest, Remove-GhAppMergedBranch,
    Close-GhAppPullRequest, Invoke-GhAppRead, Set-GhAppPullRequestReady, Set-GhAppPullRequestBase
